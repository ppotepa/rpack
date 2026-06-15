using System.IO.Compression;
using System.Text;
using Rpack.Core.Patches;
using Rpack.Core.State;
using Rpack.Core.Issues;
using Rpack.Core.Results;

namespace Rpack.Core.Packages;

public sealed class RpackApplyPlanBuilder
{
    private readonly IGitRepositoryInspector _gitRepositoryInspector;
    private readonly IGitWorkingTreeStatus _gitWorkingTreeStatus;
    private readonly IGitPatchOperations _gitPatchOperations;
    private readonly RpackPackageReader _packageReader;
    private readonly RpackManifestValidator _manifestValidator;
    private readonly RpackChecksumVerifier _checksumVerifier;
    private readonly RpackPatchPreparer _patchPreparer;
    private readonly RpackRepositoryPolicyService _repositoryPolicyService;

    public RpackApplyPlanBuilder(
        IGitRepositoryInspector gitRepositoryInspector,
        IGitWorkingTreeStatus gitWorkingTreeStatus,
        IGitPatchOperations gitPatchOperations,
        RpackPackageReader packageReader,
        RpackManifestValidator manifestValidator,
        RpackChecksumVerifier checksumVerifier,
        RpackPatchPreparer patchPreparer,
        RpackRepositoryPolicyService repositoryPolicyService)
    {
        _gitRepositoryInspector = gitRepositoryInspector;
        _gitWorkingTreeStatus = gitWorkingTreeStatus;
        _gitPatchOperations = gitPatchOperations;
        _packageReader = packageReader;
        _manifestValidator = manifestValidator;
        _checksumVerifier = checksumVerifier;
        _patchPreparer = patchPreparer;
        _repositoryPolicyService = repositoryPolicyService;
    }

    public bool TryBuild(CheckPackageOptions options, out RpackApplyPlan? plan, out RpackResult failure)
    {
        plan = null;

        var pathPrefixResult = ValidatePathPrefix(options.PathPrefix);
        if (!pathPrefixResult.Success)
        {
            failure = pathPrefixResult;
            return false;
        }

        if (!_patchPreparer.TryParseAddedFileConflictResolution(options.AddedFileConflictResolution, out var addedFileConflictResolution, out var parseFailure))
        {
            failure = parseFailure;
            return false;
        }

        var pathPrefix = NormalizePathPrefix(options.PathPrefix);
        using var archive = _packageReader.OpenRead(options.PackagePath);
        var manifest = _packageReader.ReadManifest(archive);
        var manifestResult = _manifestValidator.Validate(manifest);
        if (!manifestResult.Success)
        {
            failure = manifestResult;
            return false;
        }

        var checksumResult = _checksumVerifier.VerifyChecksums(archive, manifest);
        if (!checksumResult.Success)
        {
            failure = checksumResult;
            return false;
        }

        var repository = _gitRepositoryInspector.InspectRepository(options.RepositoryPath);
        if (manifest.RequiresCleanTree && !options.AllowDirty)
        {
            var clean = options.AllowedDirtyPaths.Count > 0
                ? _gitWorkingTreeStatus.EnsureCleanWorkingTreeExcept(repository.RootPath, options.AllowedDirtyPaths)
                : _gitWorkingTreeStatus.EnsureCleanWorkingTree(repository.RootPath);
            if (!clean.Success)
            {
                failure = clean;
                return false;
            }
        }

        var baseWarning = GetBaseMismatchMessage(repository.RootPath, manifest);
        if (options.StrictBase && !string.IsNullOrWhiteSpace(baseWarning))
        {
            failure = RpackResult.Fail(baseWarning);
            return false;
        }

        var tempPatchSet = _patchPreparer.ExtractPatchesToTempDirectory(archive, manifest.Patches, pathPrefix);
        var checkResult = BuildPatchCheckResult(
            repository.RootPath,
            manifest,
            tempPatchSet,
            options.IgnoreSpaceChange,
            addedFileConflictResolution);
        if (!checkResult.Success)
        {
            tempPatchSet.Dispose();
            failure = checkResult;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(baseWarning))
        {
            checkResult = RpackResult.Ok($"{checkResult.Message}{Environment.NewLine}Warning: {baseWarning}");
        }

        plan = new RpackApplyPlan
        {
            Manifest = manifest,
            Repository = repository,
            TempPatchSet = tempPatchSet,
            CheckResult = checkResult,
            CheckReport = ToStructuredCheckReport(checkResult, baseWarning)
        };
        failure = RpackResult.Ok("Apply plan ready.");
        return true;
    }

    private static RpackOperationResult ToStructuredCheckReport(RpackResult result, string? baseWarning)
    {
        if (result.Success)
        {
            var issues = new List<RpackIssue>();
            if (!string.IsNullOrWhiteSpace(baseWarning))
            {
                issues.Add(new RpackIssue(
                    "repo.base-mismatch",
                    RpackSeverity.Warning,
                    RpackStage.RepositoryPolicy,
                    baseWarning,
                    "Rebase the package or target the matching repository HEAD.",
                    RawDetails: baseWarning));
            }

            return new RpackOperationResult(true, result.Message, issues, [], []);
        }

        var issue = ClassifyFailureIssue(result.Message);
        return new RpackOperationResult(false, result.Message, issue is null ? [] : [issue], [], []);
    }

    private RpackResult BuildPatchCheckResult(
        string repositoryPath,
        RpackManifest manifest,
        TempPatchSet tempPatchSet,
        bool ignoreSpaceChange,
        AddedFileConflictResolution resolution)
    {
        var existingAddedFiles = _patchPreparer.PrepareExistingAddedFiles(repositoryPath, manifest, tempPatchSet, resolution);
        if (!existingAddedFiles.Success)
        {
            return existingAddedFiles;
        }

        var patchPaths = tempPatchSet.Patches.Select(patch => patch.TempPath).ToArray();
        var check = _gitPatchOperations.CheckApply(repositoryPath, patchPaths, ignoreSpaceChange);
        if (!check.Success)
        {
            var manifestPatch = _repositoryPolicyService.FindManifestPatchForFailure(check.Message, tempPatchSet);
            if (manifestPatch == "unknown patch")
            {
                manifestPatch = _repositoryPolicyService.FindFirstIndividuallyFailingPatch(repositoryPath, tempPatchSet, ignoreSpaceChange);
            }

            var diagnostics = new List<string>();
            if (check.Message.Contains("already exists in working directory", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add("Already-present diagnostic: the target repository already contains file(s) this patch wants to add. The package may already be partially applied, or the target repository may be ahead of the package.");
            }

            var whitespaceDiagnostic = ignoreSpaceChange
                ? null
                : _gitPatchOperations.CheckApply(repositoryPath, patchPaths, ignoreSpaceChange: true);
            if (whitespaceDiagnostic?.Success == true)
            {
                diagnostics.Add("Whitespace diagnostic: this patch set passes when whitespace-only context differences are ignored. The target file likely has CRLF/LF, whitespace-only context, or final-newline drift. Recheck without --strict only if that is intentional.");
            }

            var diagnosticMessage = diagnostics.Count == 0
                ? ""
                : $"{Environment.NewLine}{string.Join(Environment.NewLine, diagnostics)}";
            return RpackResult.Fail($"Patch dry-run failed for {manifestPatch}:{Environment.NewLine}{check.Message}{diagnosticMessage}");
        }

        var message = ignoreSpaceChange
            ? RpackResult.Ok($"All {tempPatchSet.Patches.Count} patch(es) can be applied with whitespace-compatible context matching.")
            : RpackResult.Ok($"All {tempPatchSet.Patches.Count} patch(es) can be applied.");
        return existingAddedFiles.Message == "No existing added files."
            ? message
            : RpackResult.Ok($"{message.Message}{Environment.NewLine}{existingAddedFiles.Message}");
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

    private string GetBaseMismatchMessage(string repositoryPath, RpackManifest manifest)
    {
        var baseCommit = GetManifestBaseCommit(manifest);
        if (string.IsNullOrWhiteSpace(baseCommit))
        {
            return "";
        }

        try
        {
            var head = _gitRepositoryInspector.ResolveCommit(repositoryPath, "HEAD");
            return string.Equals(head, baseCommit, StringComparison.OrdinalIgnoreCase)
                ? ""
                : $"Package base commit differs from repository HEAD. Package base: {baseCommit}; repository HEAD: {head}.";
        }
        catch
        {
            return "";
        }
    }

    private static string GetManifestBaseCommit(RpackManifest manifest)
    {
        return !string.IsNullOrWhiteSpace(manifest.Source?.BaseCommit)
            ? manifest.Source.BaseCommit
            : manifest.BaseCommit;
    }

    internal static RpackIssue? ClassifyFailureIssue(string message)
    {
        if (message.Contains("Unsafe path prefix", StringComparison.OrdinalIgnoreCase))
        {
            return new RpackIssue(
                "package.path-unsafe",
                RpackSeverity.Error,
                RpackStage.PackageOpen,
                message,
                "Remove the unsafe path prefix or use a repository-relative path.",
                RawDetails: message);
        }

        if (message.Contains("Unsupported package format", StringComparison.OrdinalIgnoreCase))
        {
            return new RpackIssue(
                "manifest.unsupported-format",
                RpackSeverity.Error,
                RpackStage.ManifestValidation,
                message,
                "Regenerate the package using a supported manifest format.",
                RawDetails: message);
        }

        if (message.Contains("checksum", StringComparison.OrdinalIgnoreCase))
        {
            return new RpackIssue(
                "checksum.mismatch",
                RpackSeverity.Error,
                RpackStage.ChecksumVerification,
                message,
                "Repack the archive or verify the payload bytes.",
                RawDetails: message);
        }

        if (message.Contains("Working tree is not clean", StringComparison.OrdinalIgnoreCase))
        {
            return new RpackIssue(
                "repo.dirty",
                RpackSeverity.Error,
                RpackStage.RepositoryPolicy,
                message,
                "Commit, stash, or allow dirty paths before applying.",
                RawDetails: message);
        }

        if (message.Contains("Package base commit differs", StringComparison.OrdinalIgnoreCase))
        {
            return new RpackIssue(
                "repo.base-mismatch",
                RpackSeverity.Error,
                RpackStage.RepositoryPolicy,
                message,
                "Regenerate the package from the target repository HEAD.",
                RawDetails: message);
        }

        if (message.Contains("Patch dry-run failed", StringComparison.OrdinalIgnoreCase))
        {
            return new RpackIssue(
                "patch.dry-run-failed",
                RpackSeverity.Error,
                RpackStage.PatchDryRun,
                message,
                "Inspect the conflicting patch path and rerun with adjusted inputs.",
                RawDetails: message);
        }

        if (message.Contains("Already-present diagnostic", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Added-file conflict", StringComparison.OrdinalIgnoreCase))
        {
            return new RpackIssue(
                "conflict.added-file-different-content",
                RpackSeverity.Error,
                RpackStage.ConflictDetection,
                message,
                "A file already exists in the target repository with different content. Regenerate against the current checkout or resolve the conflicting file manually.",
                RawDetails: message);
        }

        if (message.Contains("No changes found", StringComparison.OrdinalIgnoreCase))
        {
            return new RpackIssue(
                "package.no-changes",
                RpackSeverity.Error,
                RpackStage.PackageOpen,
                message,
                "Ensure the source repository has changes before creating a package.",
                RawDetails: message);
        }

        return new RpackIssue(
            "legacy.result",
            RpackSeverity.Error,
            RpackStage.PackageOpen,
            message,
            "Review the legacy message for details.",
            RawDetails: message);
    }
}
