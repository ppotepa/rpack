using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Rpack.Core.Issues;
using Rpack.Core.Patches;
using Rpack.Core.State;
using Rpack.Core.Results;

namespace Rpack.Core.Packages;

public sealed class RpackPackageRebaseService
{
    private const string ManifestPath = "manifest.json";
    private const string PatchPath = "patches/change.patch";

    private readonly IGitRepositoryInspector _gitRepositoryInspector;
    private readonly IGitPatchOperations _gitPatchOperations;
    private readonly IGitWorktreeOperations _gitWorktreeOperations;
    private readonly RpackPackageReader _packageReader;
    private readonly RpackManifestValidator _manifestValidator;
    private readonly RpackChecksumVerifier _checksumVerifier;
    private readonly RpackPatchPreparer _patchPreparer;
    private readonly RpackRepositoryPolicyService _repositoryPolicyService;

    public RpackPackageRebaseService(
        IGitRepositoryInspector gitRepositoryInspector,
        IGitPatchOperations gitPatchOperations,
        IGitWorktreeOperations gitWorktreeOperations,
        RpackPackageReader packageReader,
        RpackManifestValidator manifestValidator,
        RpackChecksumVerifier checksumVerifier,
        RpackPatchPreparer patchPreparer,
        RpackRepositoryPolicyService repositoryPolicyService)
    {
        _gitRepositoryInspector = gitRepositoryInspector;
        _gitPatchOperations = gitPatchOperations;
        _gitWorktreeOperations = gitWorktreeOperations;
        _packageReader = packageReader;
        _manifestValidator = manifestValidator;
        _checksumVerifier = checksumVerifier;
        _patchPreparer = patchPreparer;
        _repositoryPolicyService = repositoryPolicyService;
    }

    public RpackResult Execute(RebasePackageOptions options)
    {
        return ToLegacyResult(ExecuteDetailed(options));
    }

    public RpackOperationResult ExecuteDetailed(RebasePackageOptions options)
    {
        var pathPrefixResult = ValidatePathPrefix(options.PathPrefix);
        if (!pathPrefixResult.Success)
        {
            return RpackResultMapper.ToOperationResult(pathPrefixResult, failureIssue: new RpackIssue(
                "package.path-unsafe",
                RpackSeverity.Error,
                RpackStage.PackageOpen,
                pathPrefixResult.Message,
                "Remove the unsafe path prefix or use a repository-relative path.",
                RawDetails: pathPrefixResult.Message));
        }

        var pathPrefix = NormalizePathPrefix(options.PathPrefix);
        using var archive = _packageReader.OpenRead(options.PackagePath);
        var manifest = _packageReader.ReadManifest(archive);
        var manifestResult = _manifestValidator.Validate(manifest);
        if (!manifestResult.Success)
        {
            return RpackResultMapper.ToOperationResult(manifestResult, failureIssue: new RpackIssue(
                "manifest.validation-invalid",
                RpackSeverity.Error,
                RpackStage.ManifestValidation,
                manifestResult.Message,
                "Fix the manifest fields before rebasing.",
                RawDetails: manifestResult.Message));
        }

        var checksumResult = _checksumVerifier.VerifyChecksums(archive, manifest);
        if (!checksumResult.Success)
        {
            return RpackResultMapper.ToOperationResult(checksumResult, failureIssue: new RpackIssue(
                "checksum.mismatch",
                RpackSeverity.Error,
                RpackStage.ChecksumVerification,
                checksumResult.Message,
                "Repack the archive or verify the payload bytes.",
                RawDetails: checksumResult.Message));
        }

        if (!_patchPreparer.TryParseAddedFileConflictResolution(options.AddedFileConflictResolution, out var addedFileConflictResolution, out var parseFailure))
        {
            return RpackResultMapper.ToOperationResult(parseFailure, failureIssue: new RpackIssue(
                "conflict.added-file-different-content",
                RpackSeverity.Error,
                RpackStage.ConflictDetection,
                parseFailure.Message,
                "Use abort, skip, modify, overwrite, or as-modify.",
                RawDetails: parseFailure.Message));
        }

        var repository = _gitRepositoryInspector.InspectRepository(options.RepositoryPath);
        var targetHead = _gitRepositoryInspector.ResolveCommit(repository.RootPath, "HEAD");
        var worktreePath = _gitWorktreeOperations.CreateDetachedWorktree(repository.RootPath);
        try
        {
            using var tempPatchSet = _patchPreparer.ExtractPatchesToTempDirectory(archive, manifest.Patches, pathPrefix);
            var existingAddedFiles = _patchPreparer.PrepareExistingAddedFiles(
                worktreePath,
                manifest,
                tempPatchSet,
                addedFileConflictResolution);
            if (!existingAddedFiles.Success)
            {
                return RpackResultMapper.ToOperationResult(existingAddedFiles, failureIssue: new RpackIssue(
                    "conflict.added-file-different-content",
                    RpackSeverity.Error,
                    RpackStage.ConflictDetection,
                    existingAddedFiles.Message,
                    "Regenerate the package against the current checkout or resolve the conflicting file manually.",
                    RawDetails: existingAddedFiles.Message));
            }

            var patchPaths = tempPatchSet.Patches.Select(patch => patch.TempPath).ToArray();
            var check = _gitPatchOperations.CheckApply(worktreePath, patchPaths, options.IgnoreSpaceChange);
            if (!check.Success)
            {
                var message = $"Rebase patch dry-run failed for {_repositoryPolicyService.FindFirstIndividuallyFailingPatch(worktreePath, tempPatchSet, options.IgnoreSpaceChange)}:{Environment.NewLine}{check.Message}";
                return RpackOperationResult.Fail(message, [new RpackIssue(
                    "patch.dry-run-failed",
                    RpackSeverity.Error,
                    RpackStage.PatchDryRun,
                    message,
                    "Inspect the conflicting patch path and rerun with adjusted inputs.",
                    RawDetails: message)]);
            }

            foreach (var patch in tempPatchSet.Patches)
            {
                var apply = _gitPatchOperations.Apply(worktreePath, patch.TempPath, options.IgnoreSpaceChange);
                if (!apply.Success)
                {
                    var message = $"Rebase patch apply failed for {patch.ManifestPath}:{Environment.NewLine}{apply.Message}";
                    return RpackOperationResult.Fail(message, [new RpackIssue(
                        "patch.apply-failed",
                        RpackSeverity.Error,
                        RpackStage.PatchApply,
                        message,
                        "Inspect the conflicting patch and retry with a matching target checkout.",
                        PatchPath: patch.ManifestPath,
                        RawDetails: message)]);
                }
            }

            var rebasedPatch = _gitPatchOperations.CreateWorkingTreeDiff(worktreePath);
            if (string.IsNullOrWhiteSpace(rebasedPatch))
            {
                var message = "Rebase produced no changes.";
                return RpackOperationResult.Fail(message, [new RpackIssue(
                    "patch.no-output",
                    RpackSeverity.Error,
                    RpackStage.Rebase,
                    message,
                    "Ensure the rebased worktree actually contains changes.",
                    RawDetails: message)]);
            }

            var rebasedPatchBytes = Encoding.UTF8.GetBytes(rebasedPatch);
            var rebasedPatchHash = Sha256.ForBytes(rebasedPatchBytes);
            var outputPath = Path.GetFullPath(options.OutputPath);
            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            var source = manifest.Source;
            var rebasedManifest = new RpackManifest
            {
                Id = string.IsNullOrWhiteSpace(manifest.Id)
                    ? $"rpack-rebased-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}"
                    : $"{manifest.Id}-rebased",
                Title = manifest.Title,
                Description = manifest.Description,
                CreatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                BaseCommit = targetHead,
                Source = new RpackSourceInfo
                {
                    Repository = source?.Repository ?? GetRepositoryName(repository.RootPath),
                    ProjectPath = source?.ProjectPath ?? repository.RootPath,
                    BaseCommit = targetHead,
                    HeadCommit = targetHead
                },
                RequiresCleanTree = manifest.RequiresCleanTree,
                Mode = manifest.Mode,
                Patches =
                [
                    new RpackPatch
                    {
                        Path = PatchPath,
                        Kind = "git-diff",
                        Sha256 = rebasedPatchHash
                    }
                ],
                Validation = manifest.Validation
            };

            using var rebasedArchive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
            WriteEntry(rebasedArchive, ManifestPath, JsonSerializer.Serialize(rebasedManifest));
            WriteEntry(rebasedArchive, PatchPath, rebasedPatchBytes);
            WriteEntry(rebasedArchive, "checksums.sha256", $"{rebasedPatchHash}  {PatchPath}{Environment.NewLine}");
            WriteEntry(rebasedArchive, "README.md", $"# {rebasedManifest.Title}{Environment.NewLine}{Environment.NewLine}{rebasedManifest.Description}{Environment.NewLine}");

            return new RpackOperationResult(true, $"Rebased package written to {outputPath}", [], [new RpackArtifact("file", outputPath, "Rebased package archive")], []);
        }
        finally
        {
            _ = _gitWorktreeOperations.RemoveDetachedWorktree(repository.RootPath, worktreePath);
        }
    }

    private static RpackResult ToLegacyResult(RpackOperationResult result)
    {
        return result.Success
            ? RpackResult.Ok(result.Summary)
            : RpackResult.Fail(result.Summary);
    }

    private static RpackResult ValidatePathPrefix(string? pathPrefix)
    {
        if (string.IsNullOrWhiteSpace(pathPrefix))
        {
            return RpackResult.Ok("Path prefix is valid.");
        }

        return IsSafeArchivePath(NormalizePathPrefix(pathPrefix))
            ? RpackResult.Ok("Path prefix is valid.")
            : RpackResult.Fail($"Unsafe path prefix: {pathPrefix}");
    }

    private static bool IsSafeArchivePath(string path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && !Path.IsPathRooted(path)
            && !path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Contains("..");
    }

    private static string NormalizePathPrefix(string? pathPrefix)
    {
        return string.IsNullOrWhiteSpace(pathPrefix)
            ? ""
            : pathPrefix.Replace('\\', '/').Trim().Trim('/');
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(content);
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static string GetRepositoryName(string rootPath)
    {
        return new DirectoryInfo(rootPath).Name;
    }
}
