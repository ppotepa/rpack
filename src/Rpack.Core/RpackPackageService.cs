using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rpack.Core;

public sealed class RpackPackageService
{
    private const string ManifestPath = "manifest.json";
    private const string PatchPath = "patches/change.patch";
    private const string ApplyLogPath = "apply-log.json";
    private static readonly string[] ForbiddenPathPatterns =
    [
        "logs/",
        "artifacts/",
        "release/",
        "bin/",
        "obj/",
        ".env",
        ".key",
        ".pem",
        ".pdb",
        ".exe",
        ".dll",
        ".rpack",
        ".user",
        ".suo"
    ];
    private static readonly Regex SecretAssignmentPattern = new(
        @"(?i)\b(api[_-]?key|secret|token|password)\b\s*[:=]\s*[""']?[A-Za-z0-9_\-./+=]{6,}",
        RegexOptions.Compiled);
    private static readonly string[] LocalPathMarkers =
    [
        "D:\\Git\\",
        "C:\\Users\\",
        "/home/"
    ];
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".md",
        ".json",
        ".ps1",
        ".csproj",
        ".sln",
        ".slnx",
        ".xml",
        ".txt",
        ".yml",
        ".yaml"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        TypeInfoResolver = RpackJsonContext.Default,
        WriteIndented = true
    };

    private readonly GitClient _gitClient;

    public RpackPackageService(GitClient gitClient)
    {
        _gitClient = gitClient;
    }

    public RpackResult Create(CreatePackageOptions options)
    {
        var repository = _gitClient.InspectRepository(options.RepositoryPath);

        if (options.Staged && (!string.IsNullOrWhiteSpace(options.FromRevision) || !string.IsNullOrWhiteSpace(options.ToRevision)))
        {
            return RpackResult.Fail("--staged cannot be combined with --from/--to.");
        }

        string patch;
        string baseCommit;
        string headCommit;
        if (!string.IsNullOrWhiteSpace(options.FromRevision) || !string.IsNullOrWhiteSpace(options.ToRevision))
        {
            if (string.IsNullOrWhiteSpace(options.FromRevision) || string.IsNullOrWhiteSpace(options.ToRevision))
            {
                return RpackResult.Fail("Both --from and --to are required when creating a revision range package.");
            }

            baseCommit = _gitClient.ResolveCommit(repository.RootPath, options.FromRevision);
            headCommit = _gitClient.ResolveCommit(repository.RootPath, options.ToRevision);
            patch = _gitClient.CreateDiff(repository.RootPath, options.FromRevision, options.ToRevision);
        }
        else if (options.Staged)
        {
            baseCommit = _gitClient.ResolveCommit(repository.RootPath, "HEAD");
            headCommit = baseCommit;
            patch = _gitClient.CreateStagedDiff(repository.RootPath);
        }
        else
        {
            baseCommit = _gitClient.ResolveCommit(repository.RootPath, "HEAD");
            headCommit = baseCommit;
            patch = _gitClient.CreateWorkingTreeDiff(repository.RootPath);
        }

        if (string.IsNullOrWhiteSpace(patch))
        {
            return RpackResult.Fail("No changes found.");
        }

        var patchBytes = Encoding.UTF8.GetBytes(patch);
        var patchHash = Sha256.ForBytes(patchBytes);
        var manifest = new RpackManifest
        {
            Id = string.IsNullOrWhiteSpace(options.Id) ? $"rpack-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}" : options.Id,
            Title = string.IsNullOrWhiteSpace(options.Title) ? "Repository patch package" : options.Title,
            Description = options.Description ?? "",
            CreatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            BaseCommit = baseCommit,
            Source = new RpackSourceInfo
            {
                Repository = GetRepositoryName(repository.RootPath),
                ProjectPath = repository.RootPath,
                BaseCommit = baseCommit,
                HeadCommit = headCommit
            },
            Patches =
            [
                new RpackPatch
                {
                    Path = PatchPath,
                    Kind = "git-diff",
                    Sha256 = patchHash
                }
            ]
        };

        var fullOutputPath = Path.GetFullPath(options.OutputPath);
        var outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        if (File.Exists(fullOutputPath))
        {
            File.Delete(fullOutputPath);
        }

        using var archive = ZipFile.Open(fullOutputPath, ZipArchiveMode.Create);
        WriteEntry(archive, ManifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
        WriteEntry(archive, PatchPath, patchBytes);
        WriteEntry(archive, "checksums.sha256", $"{patchHash}  {PatchPath}{Environment.NewLine}");
        WriteEntry(archive, "README.md", $"# {manifest.Title}{Environment.NewLine}{Environment.NewLine}{manifest.Description}{Environment.NewLine}");

        return RpackResult.Ok($"Created {fullOutputPath}");
    }

    public PackageInspection Inspect(string packagePath)
    {
        return Inspect(new InspectPackageOptions
        {
            PackagePath = packagePath
        });
    }

    public PackageInspection Inspect(InspectPackageOptions options)
    {
        var pathPrefix = NormalizePathPrefix(options.PathPrefix);
        using var archive = ZipFile.OpenRead(options.PackagePath);
        var manifest = ReadManifest(archive);
        var patchStats = new List<RpackPatchDiffStats>();
        var changedFiles = new List<RpackFileDiffStats>();
        for (var index = 0; index < manifest.Patches.Count; index++)
        {
            var patch = manifest.Patches[index];
            var patchContent = RewritePatchPaths(ReadEntryText(archive, patch.Path), pathPrefix);
            var files = AnalyzePatch(patchContent);
            changedFiles.AddRange(files);
            patchStats.Add(BuildPatchDiffStats(index + 1, patch, files));
        }

        return new PackageInspection
        {
            Manifest = manifest,
            Entries = archive.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ToArray(),
            ChangedFiles = changedFiles,
            DiffStats = new RpackDiffStats
            {
                PatchCount = patchStats.Count,
                FileCount = changedFiles.Count,
                AddedLines = changedFiles.Sum(file => file.AddedLines),
                RemovedLines = changedFiles.Sum(file => file.RemovedLines),
                HunkCount = changedFiles.Sum(file => file.HunkCount),
                BinaryFileCount = changedFiles.Count(file => file.IsBinary),
                Patches = patchStats
            }
        };
    }

    public RpackResult Check(CheckPackageOptions options)
    {
        var pathPrefixResult = ValidatePathPrefix(options.PathPrefix);
        if (!pathPrefixResult.Success)
        {
            return pathPrefixResult;
        }

        var pathPrefix = NormalizePathPrefix(options.PathPrefix);
        using var archive = ZipFile.OpenRead(options.PackagePath);
        var manifest = ReadManifest(archive);
        var manifestResult = ValidateManifest(manifest);
        if (!manifestResult.Success)
        {
            return manifestResult;
        }

        var checksumResult = VerifyChecksums(archive, manifest);
        if (!checksumResult.Success)
        {
            return checksumResult;
        }

        var repository = _gitClient.InspectRepository(options.RepositoryPath);
        if (manifest.RequiresCleanTree && !options.AllowDirty)
        {
            var clean = options.AllowedDirtyPaths.Count > 0
                ? _gitClient.EnsureCleanWorkingTreeExcept(repository.RootPath, options.AllowedDirtyPaths)
                : _gitClient.EnsureCleanWorkingTree(repository.RootPath);
            if (!clean.Success)
            {
                return clean;
            }
        }

        var baseWarning = GetBaseMismatchMessage(repository.RootPath, manifest);
        if (options.StrictBase && !string.IsNullOrWhiteSpace(baseWarning))
        {
            return RpackResult.Fail(baseWarning);
        }

        var applyCheck = CheckPatchesInOrder(archive, manifest, repository.RootPath, pathPrefix, options.IgnoreSpaceChange);
        if (!applyCheck.Success)
        {
            return applyCheck;
        }

        return string.IsNullOrWhiteSpace(baseWarning)
            ? applyCheck
            : RpackResult.Ok($"{applyCheck.Message}{Environment.NewLine}Warning: {baseWarning}");
    }

    public RpackResult Diagnose(DiagnosePackageOptions options)
    {
        PackageInspection inspection;
        try
        {
            inspection = Inspect(new InspectPackageOptions
            {
                PackagePath = options.PackagePath,
                PathPrefix = options.PathPrefix
            });
        }
        catch (Exception ex)
        {
            return RpackResult.Fail($"Diagnosis failed while reading package:{Environment.NewLine}{ex.Message}");
        }

        var check = Check(new CheckPackageOptions
        {
            PackagePath = options.PackagePath,
            RepositoryPath = options.RepositoryPath,
            AllowDirty = options.AllowDirty,
            StrictBase = options.StrictBase,
            PathPrefix = options.PathPrefix,
            IgnoreSpaceChange = options.IgnoreSpaceChange
        });

        var builder = new StringBuilder();
        builder.AppendLine($"Package: {options.PackagePath}");
        builder.AppendLine($"Repository: {options.RepositoryPath}");
        builder.AppendLine($"Id: {inspection.Manifest.Id}");
        builder.AppendLine($"Title: {inspection.Manifest.Title}");
        builder.AppendLine($"Files: {inspection.ChangedFiles.Count}");
        builder.AppendLine($"Patches: {inspection.Manifest.Patches.Count}");
        builder.AppendLine();
        builder.AppendLine(check.Success ? "Status: Ready" : "Status: Failed");
        builder.AppendLine();

        if (check.Success)
        {
            builder.AppendLine("Check:");
            builder.AppendLine(check.Message);
            return RpackResult.Ok(builder.ToString().TrimEnd());
        }

        builder.AppendLine("Problem:");
        builder.AppendLine(ClassifyCheckFailure(check.Message));
        builder.AppendLine();
        builder.AppendLine("Raw details:");
        builder.AppendLine(check.Message);
        return RpackResult.Fail(builder.ToString().TrimEnd());
    }

    public RpackResult Lint(LintPackageOptions options)
    {
        var pathPrefix = NormalizePathPrefix(options.PathPrefix);
        using var archive = ZipFile.OpenRead(options.PackagePath);
        var manifest = ReadManifest(archive);
        var manifestResult = ValidateManifest(manifest);
        if (!manifestResult.Success)
        {
            return manifestResult;
        }

        var checksumResult = VerifyChecksums(archive, manifest);
        if (!checksumResult.Success)
        {
            return checksumResult;
        }

        var issues = new List<LintIssue>();
        foreach (var patch in manifest.Patches)
        {
            var patchContent = RewritePatchPaths(ReadEntryText(archive, patch.Path), pathPrefix);
            var changedFiles = AnalyzePatch(patchContent);
            foreach (var file in changedFiles)
            {
                AddPathLintIssues(issues, patch.Path, file.Path);
            }

            AddContentLintIssues(issues, patch.Path, patchContent);
            AddQualityLintIssues(issues, patch.Path, patchContent, changedFiles);
        }

        if (issues.Count == 0)
        {
            return RpackResult.Ok("Lint passed.");
        }

        var hasErrors = issues.Any(issue => issue.Severity == "error");
        var builder = new StringBuilder();
        builder.AppendLine($"Lint found {issues.Count} issue(s):");
        foreach (var issue in issues)
        {
            builder.AppendLine($"[{issue.Severity}] {issue.Code}: {issue.Message}");
        }

        return hasErrors
            ? RpackResult.Fail(builder.ToString().TrimEnd())
            : RpackResult.Ok(builder.ToString().TrimEnd());
    }

    public RpackResult Apply(ApplyPackageOptions options)
    {
        var check = Check(new CheckPackageOptions
        {
            PackagePath = options.PackagePath,
            RepositoryPath = options.RepositoryPath,
            AllowDirty = options.AllowDirty,
            StrictBase = options.StrictBase,
            PathPrefix = options.PathPrefix,
            AllowedDirtyPaths = options.AllowedDirtyPaths,
            IgnoreSpaceChange = options.IgnoreSpaceChange
        });

        if (!check.Success)
        {
            return check;
        }

        using var archive = ZipFile.OpenRead(options.PackagePath);
        var manifest = ReadManifest(archive);
        var repository = _gitClient.InspectRepository(options.RepositoryPath);
        using var tempPatchSet = ExtractPatchesToTempDirectory(archive, manifest.Patches, NormalizePathPrefix(options.PathPrefix));
        var existingAddedFiles = PrepareExistingAddedFiles(repository.RootPath, manifest, tempPatchSet);
        if (!existingAddedFiles.Success)
        {
            return existingAddedFiles;
        }

        var appliedPatches = new List<string>();
        foreach (var patch in tempPatchSet.Patches)
        {
            var apply = _gitClient.Apply(repository.RootPath, patch.TempPath, options.IgnoreSpaceChange);
            if (!apply.Success)
            {
                foreach (var appliedPatch in appliedPatches.AsEnumerable().Reverse())
                {
                    _gitClient.ReverseApply(repository.RootPath, appliedPatch, options.IgnoreSpaceChange);
                }

                return RpackResult.Fail($"Patch apply failed for {patch.ManifestPath}:{Environment.NewLine}{apply.Message}");
            }

            appliedPatches.Add(patch.TempPath);
        }

        var applyId = CreateApplyId();
        var storedPackagePath = StoreAppliedPackage(repository, applyId, manifest, tempPatchSet.Patches);
        AppendApplyLog(repository, new RpackApplyLog
        {
            ApplyId = applyId,
            PackageId = manifest.Id,
            Title = manifest.Title,
            AppliedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            BaseCommit = GetManifestBaseCommit(manifest),
            TargetHeadAtApply = _gitClient.ResolveCommit(repository.RootPath, "HEAD"),
            PatchPath = tempPatchSet.Patches.Count == 1 ? $"{storedPackagePath}/{tempPatchSet.Patches[0].ManifestPath}" : "",
            PackagePath = storedPackagePath
        });

        return RpackResult.Ok($"Package applied. Apply id: {applyId}");
    }

    public RpackResult UndoLastApply(string repositoryPath, bool allowDirty = false)
    {
        var repository = _gitClient.InspectRepository(repositoryPath);

        var logs = ReadApplyLogs(repository);
        var index = logs.FindLastIndex(log => string.IsNullOrWhiteSpace(log.UndoneAtUtc));
        if (index < 0)
        {
            return RpackResult.Fail("No applied package to undo.");
        }

        var log = logs[index];
        var storedPackage = ReadStoredPackage(repository, log);
        if (!storedPackage.Exists)
        {
            return RpackResult.Fail(storedPackage.ErrorMessage);
        }

        if (!allowDirty)
        {
            var dirtyCheck = EnsureNoChangesOutsidePatches(repository.RootPath, storedPackage.PatchPaths);
            if (!dirtyCheck.Success)
            {
                return dirtyCheck;
            }
        }

        foreach (var patchPath in storedPackage.PatchPaths.AsEnumerable().Reverse())
        {
            var check = _gitClient.CheckReverseApply(repository.RootPath, patchPath);
            if (!check.Success)
            {
                return check;
            }
        }

        var reversedPatches = new List<string>();
        foreach (var patchPath in storedPackage.PatchPaths.AsEnumerable().Reverse())
        {
            var undo = _gitClient.ReverseApply(repository.RootPath, patchPath);
            if (!undo.Success)
            {
                foreach (var reversedPatch in reversedPatches.AsEnumerable().Reverse())
                {
                    _gitClient.Apply(repository.RootPath, reversedPatch);
                }

                return undo;
            }

            reversedPatches.Add(patchPath);
        }

        logs[index] = new RpackApplyLog
        {
            ApplyId = log.ApplyId,
            PackageId = log.PackageId,
            Title = log.Title,
            AppliedAtUtc = log.AppliedAtUtc,
            UndoneAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            BaseCommit = log.BaseCommit,
            TargetHeadAtApply = log.TargetHeadAtApply,
            PatchPath = log.PatchPath,
            PackagePath = log.PackagePath
        };
        WriteApplyLogs(repository, logs);

        return RpackResult.Ok($"Undone apply id: {log.ApplyId}");
    }

    public IReadOnlyList<RpackApplyLog> ReadHistory(string repositoryPath)
    {
        var repository = _gitClient.InspectRepository(repositoryPath);
        return ReadApplyLogs(repository);
    }

    private static RpackManifest ReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry(ManifestPath) ?? throw new InvalidOperationException("manifest.json is missing.");
        using var stream = entry.Open();
        var manifest = JsonSerializer.Deserialize(stream, RpackJsonContext.Default.RpackManifest);
        return manifest ?? throw new InvalidOperationException("manifest.json is invalid.");
    }

    private static RpackResult ValidateManifest(RpackManifest manifest)
    {
        if (manifest.Format != "rpack-v1")
        {
            return RpackResult.Fail($"Unsupported package format: {manifest.Format}");
        }

        if (manifest.Mode != "working-tree-patch")
        {
            return RpackResult.Fail($"Unsupported package mode: {manifest.Mode}");
        }

        if (manifest.Patches.Count < 1)
        {
            return RpackResult.Fail("Packages must contain at least one patch in rpack-v1.");
        }

        foreach (var patch in manifest.Patches)
        {
            if (string.IsNullOrWhiteSpace(patch.Path) || string.IsNullOrWhiteSpace(patch.Sha256))
            {
                return RpackResult.Fail("Patch path or checksum is missing.");
            }

            if (patch.Kind != "git-diff")
            {
                return RpackResult.Fail($"Unsupported patch kind: {patch.Kind}");
            }
        }

        return RpackResult.Ok("Manifest is valid.");
    }

    private static RpackResult VerifyChecksums(ZipArchive archive, RpackManifest manifest)
    {
        foreach (var patch in manifest.Patches)
        {
            if (!IsSafeArchivePath(patch.Path))
            {
                return RpackResult.Fail($"Unsafe archive path: {patch.Path}");
            }

            var entry = archive.GetEntry(patch.Path);
            if (entry is null)
            {
                return RpackResult.Fail($"Patch is missing: {patch.Path}");
            }

            using var memory = new MemoryStream();
            using (var stream = entry.Open())
            {
                stream.CopyTo(memory);
            }

            var actual = Sha256.ForBytes(memory.ToArray());
            if (!string.Equals(actual, patch.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return RpackResult.Fail($"Checksum mismatch for {patch.Path}.");
            }
        }

        return RpackResult.Ok("Checksums are valid.");
    }

    private string GetBaseMismatchMessage(string repositoryPath, RpackManifest manifest)
    {
        var baseCommit = GetManifestBaseCommit(manifest);
        if (string.IsNullOrWhiteSpace(baseCommit))
        {
            return "";
        }

        var head = _gitClient.ResolveCommit(repositoryPath, "HEAD");
        return string.Equals(head, baseCommit, StringComparison.OrdinalIgnoreCase)
            ? ""
            : $"Package base commit differs from repository HEAD. Package base: {baseCommit}; repository HEAD: {head}.";
    }

    private static string GetManifestBaseCommit(RpackManifest manifest)
    {
        return !string.IsNullOrWhiteSpace(manifest.Source?.BaseCommit)
            ? manifest.Source.BaseCommit
            : manifest.BaseCommit;
    }

    private RpackResult CheckPatchesInOrder(ZipArchive archive, RpackManifest manifest, string repositoryPath, string pathPrefix, bool ignoreSpaceChange)
    {
        using var tempPatchSet = ExtractPatchesToTempDirectory(archive, manifest.Patches, pathPrefix);
        var existingAddedFiles = PrepareExistingAddedFiles(repositoryPath, manifest, tempPatchSet);
        if (!existingAddedFiles.Success)
        {
            return existingAddedFiles;
        }

        var patchPaths = tempPatchSet.Patches.Select(patch => patch.TempPath).ToArray();
        var check = _gitClient.CheckApply(repositoryPath, patchPaths, ignoreSpaceChange);
        if (!check.Success)
        {
            var manifestPatch = FindManifestPatchForFailure(check.Message, tempPatchSet);
            if (manifestPatch == "unknown patch")
            {
                manifestPatch = FindFirstIndividuallyFailingPatch(repositoryPath, tempPatchSet, ignoreSpaceChange);
            }

            var diagnostics = new List<string>();
            if (check.Message.Contains("already exists in working directory", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add("Already-present diagnostic: the target repository already contains file(s) this patch wants to add. The package may already be partially applied, or the target repository may be ahead of the package.");
            }

            var whitespaceDiagnostic = ignoreSpaceChange
                ? null
                : _gitClient.CheckApply(repositoryPath, patchPaths, ignoreSpaceChange: true);
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

    private RpackResult PrepareExistingAddedFiles(string repositoryPath, RpackManifest manifest, TempPatchSet tempPatchSet)
    {
        var skipped = new List<string>();
        var conflicts = new List<string>();

        foreach (var patch in tempPatchSet.Patches)
        {
            var patchText = File.ReadAllText(patch.TempPath, Encoding.UTF8);
            var rewritten = RewriteAlreadyPresentAddedFiles(repositoryPath, manifest, patch.ManifestPath, patchText, skipped, conflicts);
            if (!string.Equals(rewritten, patchText, StringComparison.Ordinal))
            {
                File.WriteAllText(patch.TempPath, rewritten, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }

        if (conflicts.Count > 0)
        {
            return RpackResult.Fail($"""
                Added-file conflict: the target repository already contains file(s) this package wants to add, but their contents differ.
                rpack will not choose by file size or modification time because that could overwrite local work.

                {string.Join(Environment.NewLine + Environment.NewLine, conflicts)}

                Suggested action: regenerate the package against the current target repository, or resolve these files manually and create a new package with the remaining changes.
                """);
        }

        return skipped.Count == 0
            ? RpackResult.Ok("No existing added files.")
            : RpackResult.Ok($"Already-present file(s) skipped because target content matches the package:{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", skipped)}");
    }

    private static string RewriteAlreadyPresentAddedFiles(
        string repositoryPath,
        RpackManifest manifest,
        string manifestPatchPath,
        string patchText,
        List<string> skipped,
        List<string> conflicts)
    {
        var lines = patchText.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var output = new List<string>();
        var prefix = new List<string>();
        var block = new List<string>();
        var hasBlock = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                FlushBlock();
                hasBlock = true;
                block.Add(line);
                continue;
            }

            if (hasBlock)
            {
                block.Add(line);
            }
            else
            {
                prefix.Add(line);
            }
        }

        FlushBlock();
        return string.Join('\n', output);

        void FlushBlock()
        {
            if (!hasBlock)
            {
                output.AddRange(prefix);
                prefix.Clear();
                return;
            }

            var addedFile = TryParseAddedTextFile(block);
            if (addedFile is not null)
            {
                var targetPath = ResolveRepositoryFilePath(repositoryPath, addedFile.Path);
                if (targetPath is null)
                {
                    conflicts.Add($"{manifestPatchPath}:{addedFile.Path}{Environment.NewLine}  Unsafe target path.");
                }
                else if (File.Exists(targetPath))
                {
                    var targetText = File.ReadAllText(targetPath);
                    if (NormalizeLineEndings(targetText) == NormalizeLineEndings(addedFile.Content))
                    {
                        skipped.Add($"{manifestPatchPath}:{addedFile.Path}");
                        block.Clear();
                        return;
                    }

                    conflicts.Add(BuildAddedFileConflict(manifest, manifestPatchPath, addedFile, targetPath));
                }
            }

            output.AddRange(block);
            block.Clear();
        }
    }

    private static AddedTextFile? TryParseAddedTextFile(IReadOnlyList<string> block)
    {
        if (block.Count == 0
            || !block.Any(line => line.StartsWith("new file mode ", StringComparison.Ordinal))
            || !block.Any(line => line == "--- /dev/null")
            || block.Any(line => line is "GIT binary patch" || line.StartsWith("Binary files ", StringComparison.Ordinal)))
        {
            return null;
        }

        var path = ParseDiffGitPath(block[0]);
        var lines = new List<string>();
        var inHunk = false;
        var finalNewline = false;
        var lastLineWasAdded = false;

        foreach (var line in block)
        {
            if (line.StartsWith("@@ ", StringComparison.Ordinal))
            {
                inHunk = true;
                lastLineWasAdded = false;
                continue;
            }

            if (!inHunk)
            {
                continue;
            }

            if (line.StartsWith(@"\ No newline", StringComparison.Ordinal) && lastLineWasAdded)
            {
                finalNewline = false;
                continue;
            }

            lastLineWasAdded = false;
            if (line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
            {
                lines.Add(line[1..]);
                finalNewline = true;
                lastLineWasAdded = true;
            }
        }

        var content = lines.Count == 0
            ? ""
            : string.Join('\n', lines) + (finalNewline ? "\n" : "");
        return new AddedTextFile(path, content);
    }

    private static string BuildAddedFileConflict(RpackManifest manifest, string manifestPatchPath, AddedTextFile addedFile, string targetPath)
    {
        var info = new FileInfo(targetPath);
        var packageBytes = Encoding.UTF8.GetByteCount(addedFile.Content);
        var sizeRelation = info.Length == packageBytes
            ? "same size"
            : info.Length > packageBytes ? "target is larger" : "package is larger";
        var packageCreated = string.IsNullOrWhiteSpace(manifest.CreatedAtUtc)
            ? "unknown"
            : manifest.CreatedAtUtc;

        return $"""
            {manifestPatchPath}:{addedFile.Path}
              target bytes: {info.Length}
              package bytes: {packageBytes}
              size comparison: {sizeRelation}
              target last write UTC: {info.LastWriteTimeUtc:O}
              package created UTC: {packageCreated}
            """;
    }

    private static string? ResolveRepositoryFilePath(string repositoryPath, string gitPath)
    {
        var normalized = NormalizeGitPath(gitPath);
        if (!IsSafeArchivePath(normalized))
        {
            return null;
        }

        var repositoryRoot = Path.GetFullPath(repositoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return fullPath.StartsWith(repositoryRoot + Path.DirectorySeparatorChar, comparison)
            ? fullPath
            : null;
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
    }

    private static string ClassifyCheckFailure(string message)
    {
        if (message.Contains("Added-file conflict", StringComparison.OrdinalIgnoreCase))
        {
            return "A file the package wants to add already exists in the target repository with different content. Regenerate the package against the current checkout or resolve the file manually.";
        }

        if (message.Contains("Already-present diagnostic", StringComparison.OrdinalIgnoreCase)
            || message.Contains("already exists in working directory", StringComparison.OrdinalIgnoreCase))
        {
            return "The target repository already contains file(s) the package wants to add. The package may be partially applied or based on an older target.";
        }

        if (message.Contains("Whitespace diagnostic", StringComparison.OrdinalIgnoreCase))
        {
            return "Strict patch context failed, but whitespace-compatible context would pass. Run without --strict or normalize line endings/final newlines.";
        }

        if (message.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase)
            || message.Contains("does not exist in index", StringComparison.OrdinalIgnoreCase))
        {
            return "The patch references a path that does not exist in the target repository. Check repository version or try --path-prefix when the package was made from a subdirectory snapshot.";
        }

        if (message.Contains("Package base commit differs", StringComparison.OrdinalIgnoreCase))
        {
            return "The package was created from a different base commit than the target repository HEAD.";
        }

        if (message.Contains("patch does not apply", StringComparison.OrdinalIgnoreCase)
            || message.Contains("patch failed", StringComparison.OrdinalIgnoreCase))
        {
            return "Patch context does not match the target file. Regenerate the package against the current checkout or run diagnose with path-prefix/strict options adjusted.";
        }

        return "Package check failed. Review raw details.";
    }

    private static void AddPathLintIssues(List<LintIssue> issues, string patchPath, string filePath)
    {
        var normalized = NormalizeGitPath(filePath);
        var lower = normalized.ToLowerInvariant();
        foreach (var pattern in ForbiddenPathPatterns)
        {
            if (pattern.EndsWith("/", StringComparison.Ordinal))
            {
                if (lower.StartsWith(pattern, StringComparison.Ordinal) || lower.Contains($"/{pattern}", StringComparison.Ordinal))
                {
                    issues.Add(new LintIssue("error", "forbidden-path", $"{patchPath}: {filePath} matches forbidden path pattern `{pattern}**`."));
                }

                continue;
            }

            if (lower.EndsWith(pattern, StringComparison.Ordinal))
            {
                issues.Add(new LintIssue("error", "forbidden-path", $"{patchPath}: {filePath} matches forbidden file pattern `*{pattern}`."));
            }
        }
    }

    private static void AddContentLintIssues(List<LintIssue> issues, string patchPath, string patchContent)
    {
        var addedLines = patchContent
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
            .Select(line => line[1..])
            .ToArray();

        var secretMatch = addedLines.FirstOrDefault(line => line.Contains("BEGIN PRIVATE KEY", StringComparison.OrdinalIgnoreCase)
            || SecretAssignmentPattern.IsMatch(line));
        if (secretMatch is not null)
        {
            issues.Add(new LintIssue("error", "secret-marker", $"{patchPath}: patch content contains a likely secret assignment or private key marker."));
        }

        foreach (var marker in LocalPathMarkers)
        {
            if (addedLines.Any(line => line.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new LintIssue("warning", "local-path", $"{patchPath}: patch content contains local path marker `{marker}`."));
            }
        }
    }

    private static void AddQualityLintIssues(List<LintIssue> issues, string patchPath, string patchContent, IReadOnlyList<RpackFileDiffStats> changedFiles)
    {
        foreach (var file in changedFiles)
        {
            var extension = Path.GetExtension(file.Path);
            if (file.Status == "binary" && TextExtensions.Contains(extension))
            {
                issues.Add(new LintIssue("warning", "binary-text-file", $"{patchPath}: {file.Path} is a text-like file represented as a binary patch."));
            }

            if (file.RemovedLines > 0 && file.AddedLines > 0)
            {
                var bigger = Math.Max(file.AddedLines, file.RemovedLines);
                var smaller = Math.Min(file.AddedLines, file.RemovedLines);
                if (bigger >= 200 && smaller > 0 && (double)bigger / smaller > 4)
                {
                    issues.Add(new LintIssue("warning", "possible-whole-file-rewrite", $"{patchPath}: {file.Path} looks like a possible whole-file rewrite (+{file.AddedLines}/-{file.RemovedLines})."));
                }
            }
        }

        var hunkAdded = 0;
        var hunkRemoved = 0;
        foreach (var rawLine in patchContent.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("@@ ", StringComparison.Ordinal))
            {
                FlushHunk();
                continue;
            }

            if (line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
            {
                hunkAdded++;
                if (line.Length > 1 && (line.EndsWith(' ') || line.EndsWith('\t')))
                {
                    issues.Add(new LintIssue("warning", "trailing-whitespace", $"{patchPath}: added line contains trailing whitespace."));
                }
            }
            else if (line.StartsWith('-') && !line.StartsWith("---", StringComparison.Ordinal))
            {
                hunkRemoved++;
            }
            else if (line.StartsWith(@"\ No newline", StringComparison.Ordinal))
            {
                issues.Add(new LintIssue("warning", "no-final-newline", $"{patchPath}: patch contains no-final-newline marker."));
            }
        }

        FlushHunk();

        void FlushHunk()
        {
            if (hunkAdded + hunkRemoved > 300)
            {
                issues.Add(new LintIssue("warning", "large-hunk", $"{patchPath}: hunk changes {hunkAdded + hunkRemoved} line(s)."));
            }

            hunkAdded = 0;
            hunkRemoved = 0;
        }
    }

    private string FindFirstIndividuallyFailingPatch(string repositoryPath, TempPatchSet tempPatchSet, bool ignoreSpaceChange)
    {
        foreach (var patch in tempPatchSet.Patches)
        {
            var check = _gitClient.CheckApply(repositoryPath, patch.TempPath, ignoreSpaceChange);
            if (!check.Success)
            {
                return patch.ManifestPath;
            }
        }

        return "unknown patch";
    }

    private static string FindManifestPatchForFailure(string message, TempPatchSet tempPatchSet)
    {
        var failedPaths = ExtractGitFailurePaths(message)
            .Select(NormalizeGitPath)
            .ToArray();
        if (failedPaths.Length > 0)
        {
            foreach (var failedPath in failedPaths)
            {
                foreach (var patch in tempPatchSet.Patches)
                {
                    var files = AnalyzePatch(File.ReadAllText(patch.TempPath, Encoding.UTF8));
                    if (files.Any(file => string.Equals(NormalizeGitPath(file.Path), failedPath, StringComparison.Ordinal)))
                    {
                        return patch.ManifestPath;
                    }
                }
            }
        }

        return tempPatchSet.Patches.Count == 1
            ? tempPatchSet.Patches[0].ManifestPath
            : "unknown patch";
    }

    private static IReadOnlyList<string> ExtractGitFailurePaths(string message)
    {
        var paths = new List<string>();
        foreach (var rawLine in message.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            const string patchFailedPrefix = "error: patch failed: ";
            if (line.StartsWith(patchFailedPrefix, StringComparison.Ordinal))
            {
                var content = line[patchFailedPrefix.Length..];
                var lineNumberSeparator = content.LastIndexOf(':');
                if (lineNumberSeparator > 0)
                {
                    paths.Add(content[..lineNumberSeparator]);
                }

                continue;
            }

            const string errorPrefix = "error: ";
            if (line.StartsWith(errorPrefix, StringComparison.Ordinal))
            {
                var content = line[errorPrefix.Length..];
                var separator = content.IndexOf(':');
                if (separator > 0)
                {
                    paths.Add(content[..separator]);
                }
            }
        }

        return paths;
    }

    private static TempPatchSet ExtractPatchesToTempDirectory(ZipArchive archive, IReadOnlyList<RpackPatch> patches, string pathPrefix)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"rpack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var tempPatches = new List<TempPatch>();

        foreach (var patch in patches)
        {
            if (!IsSafeArchivePath(patch.Path))
            {
                throw new InvalidOperationException($"Unsafe archive path: {patch.Path}");
            }

            var entry = archive.GetEntry(patch.Path) ?? throw new InvalidOperationException($"Patch is missing: {patch.Path}");
            var tempPath = Path.Combine(tempDirectory, patch.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

            using (var source = entry.Open())
            using (var memory = new MemoryStream())
            {
                source.CopyTo(memory);
                var patchContent = Encoding.UTF8.GetString(memory.ToArray());
                var transformedPatch = RewritePatchPaths(patchContent, pathPrefix);
                File.WriteAllText(tempPath, transformedPatch, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            tempPatches.Add(new TempPatch(patch.Path, tempPath));
        }

        return new TempPatchSet(tempDirectory, tempPatches);
    }

    private static bool IsSafeArchivePath(string path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && !Path.IsPathRooted(path)
            && !path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Contains("..");
    }

    private static string ReadEntryText(ZipArchive archive, string path)
    {
        if (!IsSafeArchivePath(path))
        {
            throw new InvalidOperationException($"Unsafe archive path: {path}");
        }

        var entry = archive.GetEntry(path) ?? throw new InvalidOperationException($"Archive entry is missing: {path}");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static IReadOnlyList<RpackFileDiffStats> AnalyzePatch(string patch)
    {
        var files = new List<PatchFileSummaryBuilder>();
        PatchFileSummaryBuilder? current = null;

        foreach (var rawLine in patch.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                AddCurrent();
                current = new PatchFileSummaryBuilder
                {
                    Path = ParseDiffGitPath(line),
                    Status = "Modified",
                    Category = ClassifyPath(ParseDiffGitPath(line))
                };
                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (line.StartsWith("new file mode ", StringComparison.Ordinal))
            {
                current.Status = "Added";
            }
            else if (line.StartsWith("deleted file mode ", StringComparison.Ordinal))
            {
                current.Status = "Deleted";
            }
            else if (line.StartsWith("rename to ", StringComparison.Ordinal))
            {
                current.Status = "Renamed";
                current.Path = StripGitPath(line["rename to ".Length..]);
                current.Category = ClassifyPath(current.Path);
            }
            else if (line.StartsWith("@@ ", StringComparison.Ordinal))
            {
                current.HunkCount++;
            }
            else if (line is "GIT binary patch" || line.StartsWith("Binary files ", StringComparison.Ordinal))
            {
                current.IsBinary = true;
                if (current.Status is not ("Added" or "Deleted"))
                {
                    current.Status = "Modified";
                }
            }
            else if (line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
            {
                current.AddedLines++;
            }
            else if (line.StartsWith('-') && !line.StartsWith("---", StringComparison.Ordinal))
            {
                current.RemovedLines++;
            }
        }

        AddCurrent();
        return files
            .Select(file => new RpackFileDiffStats
            {
                Path = file.Path,
                Status = file.Status,
                AddedLines = file.AddedLines,
                RemovedLines = file.RemovedLines,
                HunkCount = file.HunkCount,
                IsBinary = file.IsBinary,
                Category = file.Category
            })
            .ToArray();

        void AddCurrent()
        {
            if (current is not null && !string.IsNullOrWhiteSpace(current.Path))
            {
                files.Add(current);
            }
        }
    }

    private static string ParseDiffGitPath(string line)
    {
        var marker = " b/";
        var index = line.LastIndexOf(marker, StringComparison.Ordinal);
        return index < 0
            ? line["diff --git ".Length..].Trim()
            : StripGitPath(line[(index + marker.Length)..]);
    }

    private static string StripGitPath(string path)
    {
        path = path.Trim().Trim('"');
        return path.StartsWith("b/", StringComparison.Ordinal) || path.StartsWith("a/", StringComparison.Ordinal)
            ? path[2..]
            : path;
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

    private static string NormalizePathPrefix(string? pathPrefix)
    {
        return string.IsNullOrWhiteSpace(pathPrefix)
            ? ""
            : pathPrefix.Replace('\\', '/').Trim().Trim('/');
    }

    private static string BuildPatchTitle(RpackPatch patch, int patchNumber)
    {
        var name = Path.GetFileNameWithoutExtension(patch.Path);
        if (string.IsNullOrWhiteSpace(name))
        {
            return $"PATCH {patchNumber:D3}";
        }

        var numberMatch = Regex.Match(name, @"^(?<number>\d+)");
        var title = name;
        if (numberMatch.Success)
        {
            title = name[numberMatch.Length..];
            if (title.StartsWith("-", StringComparison.Ordinal) || title.StartsWith("_", StringComparison.Ordinal))
            {
                title = title[1..];
            }
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return $"PATCH {patchNumber:D3}";
        }

        var normalized = char.ToUpperInvariant(title[0]) + title[1..];
        return $"PATCH {patchNumber:D3} — {normalized}";
    }

    private static RpackPatchDiffStats BuildPatchDiffStats(int index, RpackPatch patch, IReadOnlyList<RpackFileDiffStats> files)
    {
        return new RpackPatchDiffStats
        {
            PatchPath = patch.Path,
            Title = BuildPatchTitle(patch, index),
            FileCount = files.Count,
            AddedLines = files.Sum(file => file.AddedLines),
            RemovedLines = files.Sum(file => file.RemovedLines),
            HunkCount = files.Sum(file => file.HunkCount),
            Files = files
                .Select(file => new RpackFileDiffStats
                {
                    Path = file.Path,
                    Status = file.Status,
                    AddedLines = file.AddedLines,
                    RemovedLines = file.RemovedLines,
                    HunkCount = file.HunkCount,
                    IsBinary = file.IsBinary,
                    Category = file.Category
                })
                .ToArray()
        };
    }

    private static string ClassifyPath(string path)
    {
        var normalizedPath = path.Replace('\\', '/').ToLowerInvariant();

        if (normalizedPath.Contains("/test/", StringComparison.Ordinal) || normalizedPath.Contains("/tests/", StringComparison.Ordinal))
        {
            return "Tests";
        }

        var extension = Path.GetExtension(normalizedPath);
        return extension switch
        {
            ".cs" or ".csproj" or ".sln" or ".slnx" => "Code",
            ".md" or ".txt" or ".json" or ".yml" or ".yaml" or ".xml" => "Docs",
            ".ps1" or ".sh" or ".bash" or ".cmd" or ".bat" => "Scripts",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg" => "Assets",
            _ => normalizedPath.Contains("/assets/") || normalizedPath.Contains("/artifacts/") || normalizedPath.Contains("/release/") ? "Assets" : "Other"
        };
    }

    private static string RewritePatchPaths(string patch, string pathPrefix)
    {
        if (string.IsNullOrWhiteSpace(pathPrefix))
        {
            return patch;
        }

        var lines = patch.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = RewritePatchLine(lines[i].TrimEnd('\r'), pathPrefix);
        }

        return string.Join('\n', lines);
    }

    private static string RewritePatchLine(string line, string pathPrefix)
    {
        if (line.StartsWith("diff --git ", StringComparison.Ordinal))
        {
            var parts = line["diff --git ".Length..].Split(' ', 2);
            return parts.Length == 2
                ? $"diff --git {PrefixPatchPath(parts[0], pathPrefix)} {PrefixPatchPath(parts[1], pathPrefix)}"
                : line;
        }

        if (line.StartsWith("--- ", StringComparison.Ordinal) || line.StartsWith("+++ ", StringComparison.Ordinal))
        {
            return $"{line[..4]}{PrefixPatchPath(line[4..], pathPrefix)}";
        }

        if (line.StartsWith("rename from ", StringComparison.Ordinal))
        {
            return $"rename from {PrefixPlainPath(line["rename from ".Length..], pathPrefix)}";
        }

        if (line.StartsWith("rename to ", StringComparison.Ordinal))
        {
            return $"rename to {PrefixPlainPath(line["rename to ".Length..], pathPrefix)}";
        }

        if (line.StartsWith("copy from ", StringComparison.Ordinal))
        {
            return $"copy from {PrefixPlainPath(line["copy from ".Length..], pathPrefix)}";
        }

        if (line.StartsWith("copy to ", StringComparison.Ordinal))
        {
            return $"copy to {PrefixPlainPath(line["copy to ".Length..], pathPrefix)}";
        }

        if (line.StartsWith("Binary files ", StringComparison.Ordinal))
        {
            return RewriteBinaryFilesLine(line, pathPrefix);
        }

        return line;
    }

    private static string RewriteBinaryFilesLine(string line, string pathPrefix)
    {
        const string prefix = "Binary files ";
        const string separator = " and ";
        const string suffix = " differ";
        if (!line.EndsWith(suffix, StringComparison.Ordinal))
        {
            return line;
        }

        var content = line[prefix.Length..^suffix.Length];
        var parts = content.Split(separator, 2, StringSplitOptions.None);
        return parts.Length == 2
            ? $"{prefix}{PrefixPatchPath(parts[0], pathPrefix)}{separator}{PrefixPatchPath(parts[1], pathPrefix)}{suffix}"
            : line;
    }

    private static string PrefixPatchPath(string path, string pathPrefix)
    {
        path = path.Trim();
        if (path == "/dev/null")
        {
            return path;
        }

        if (path.StartsWith("a/", StringComparison.Ordinal) || path.StartsWith("b/", StringComparison.Ordinal))
        {
            return $"{path[..2]}{pathPrefix}/{path[2..]}";
        }

        return PrefixPlainPath(path, pathPrefix);
    }

    private static string PrefixPlainPath(string path, string pathPrefix)
    {
        path = path.Trim();
        return path == "/dev/null"
            ? path
            : $"{pathPrefix}/{path}";
    }

    private RpackResult EnsureNoChangesOutsidePatches(string repositoryPath, IReadOnlyList<string> patchPaths)
    {
        var allowedPaths = patchPaths
            .SelectMany(path => AnalyzePatch(File.ReadAllText(path, Encoding.UTF8)))
            .Select(file => NormalizeGitPath(file.Path))
            .ToHashSet(StringComparer.Ordinal);
        var dirtyPaths = _gitClient.GetChangedPaths(repositoryPath)
            .Select(NormalizeGitPath)
            .ToArray();
        var outsidePaths = dirtyPaths
            .Where(path => !allowedPaths.Contains(path))
            .ToArray();

        return outsidePaths.Length == 0
            ? RpackResult.Ok("Working tree changes are limited to the last applied package.")
            : RpackResult.Fail($"Working tree has changes outside the last applied package: {string.Join(", ", outsidePaths)}. Use --allow-dirty to undo anyway.");
    }

    private static string NormalizeGitPath(string path)
    {
        return path.Replace('\\', '/').Trim();
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

    private static string StoreAppliedPackage(GitRepository repository, string applyId, RpackManifest manifest, IReadOnlyList<TempPatch> patches)
    {
        var relativeDirectory = $"applied/{applyId}";
        var directory = ResolveStatePath(repository, relativeDirectory);
        Directory.CreateDirectory(directory);

        foreach (var patch in patches)
        {
            var relativePatchPath = $"{relativeDirectory}/{patch.ManifestPath}";
            var destinationPath = ResolveStatePath(repository, relativePatchPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(patch.TempPath, destinationPath, overwrite: true);
        }

        using var manifestFile = File.Create(ResolveStatePath(repository, $"{relativeDirectory}/manifest.json"));
        JsonSerializer.Serialize(manifestFile, manifest, RpackJsonContext.Default.RpackManifest);

        return relativeDirectory;
    }

    private static StoredPackage ReadStoredPackage(GitRepository repository, RpackApplyLog log)
    {
        if (!string.IsNullOrWhiteSpace(log.PackagePath))
        {
            var manifestPath = ResolveStatePath(repository, $"{log.PackagePath}/manifest.json");
            if (!File.Exists(manifestPath))
            {
                return StoredPackage.Missing($"Stored manifest is missing: {log.PackagePath}/manifest.json");
            }

            using var manifestFile = File.OpenRead(manifestPath);
            var manifest = JsonSerializer.Deserialize(manifestFile, RpackJsonContext.Default.RpackManifest)
                ?? throw new InvalidOperationException("Stored manifest is invalid.");
            var patchPaths = manifest.Patches
                .Select(patch => ResolveStatePath(repository, $"{log.PackagePath}/{patch.Path}"))
                .ToArray();
            var missingPatch = patchPaths.FirstOrDefault(path => !File.Exists(path));
            if (missingPatch is not null)
            {
                return StoredPackage.Missing($"Stored patch is missing: {missingPatch}");
            }

            return new StoredPackage(patchPaths);
        }

        if (!string.IsNullOrWhiteSpace(log.PatchPath))
        {
            var patchPath = ResolveStatePath(repository, log.PatchPath);
            return File.Exists(patchPath)
                ? new StoredPackage([patchPath])
                : StoredPackage.Missing($"Stored patch is missing: {log.PatchPath}");
        }

        return StoredPackage.Missing("Stored package path is missing from apply log.");
    }

    private static List<RpackApplyLog> ReadApplyLogs(GitRepository repository)
    {
        var logPath = ResolveStatePath(repository, ApplyLogPath);
        if (!File.Exists(logPath))
        {
            return [];
        }

        using var existing = File.OpenRead(logPath);
        return JsonSerializer.Deserialize(existing, RpackJsonContext.Default.ListRpackApplyLog) ?? [];
    }

    private static void AppendApplyLog(GitRepository repository, RpackApplyLog log)
    {
        var logs = ReadApplyLogs(repository);
        logs.Add(log);
        WriteApplyLogs(repository, logs);
    }

    private static void WriteApplyLogs(GitRepository repository, List<RpackApplyLog> logs)
    {
        Directory.CreateDirectory(repository.StatePath);
        using var output = File.Create(ResolveStatePath(repository, ApplyLogPath));
        JsonSerializer.Serialize(output, logs, RpackJsonContext.Default.ListRpackApplyLog);
    }

    private static string ResolveStatePath(GitRepository repository, string relativePath)
    {
        return Path.GetFullPath(Path.Combine(repository.StatePath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string CreateApplyId()
    {
        return $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
    }

    private static string GetRepositoryName(string rootPath)
    {
        return new DirectoryInfo(rootPath).Name;
    }

    private sealed class PatchFileSummaryBuilder
    {
        public string Path { get; set; } = "";
        public string Status { get; set; } = "modified";
        public int AddedLines { get; set; }
        public int RemovedLines { get; set; }
        public int HunkCount { get; set; }
        public bool IsBinary { get; set; }
        public string Category { get; set; } = "Other";
    }

    private sealed record AddedTextFile(string Path, string Content);

    private sealed record LintIssue(string Severity, string Code, string Message);

    private sealed record TempPatch(string ManifestPath, string TempPath);

    private sealed class TempPatchSet : IDisposable
    {
        public TempPatchSet(string directoryPath, IReadOnlyList<TempPatch> patches)
        {
            DirectoryPath = directoryPath;
            Patches = patches;
        }

        public string DirectoryPath { get; }
        public IReadOnlyList<TempPatch> Patches { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }

    private sealed class StoredPackage
    {
        public StoredPackage(IReadOnlyList<string> patchPaths)
        {
            PatchPaths = patchPaths;
        }

        public IReadOnlyList<string> PatchPaths { get; }
        public bool Exists => string.IsNullOrWhiteSpace(ErrorMessage);
        public string ErrorMessage { get; private init; } = "";

        public static StoredPackage Missing(string errorMessage)
        {
            return new StoredPackage([]) { ErrorMessage = errorMessage };
        }
    }

}

public sealed class CreatePackageOptions
{
    public required string RepositoryPath { get; init; }
    public required string OutputPath { get; init; }
    public string? FromRevision { get; init; }
    public string? ToRevision { get; init; }
    public bool Staged { get; init; }
    public string? Id { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
}

public sealed class CheckPackageOptions
{
    public required string PackagePath { get; init; }
    public required string RepositoryPath { get; init; }
    public bool AllowDirty { get; init; }
    public bool StrictBase { get; init; }
    public string? PathPrefix { get; init; }
    public IReadOnlyList<string> AllowedDirtyPaths { get; init; } = [];
    public bool IgnoreSpaceChange { get; init; } = true;
}

public sealed class ApplyPackageOptions
{
    public required string PackagePath { get; init; }
    public required string RepositoryPath { get; init; }
    public bool AllowDirty { get; init; }
    public bool StrictBase { get; init; }
    public string? PathPrefix { get; init; }
    public IReadOnlyList<string> AllowedDirtyPaths { get; init; } = [];
    public bool IgnoreSpaceChange { get; init; } = true;
}

public sealed class DiagnosePackageOptions
{
    public required string PackagePath { get; init; }
    public required string RepositoryPath { get; init; }
    public bool AllowDirty { get; init; }
    public bool StrictBase { get; init; }
    public string? PathPrefix { get; init; }
    public bool IgnoreSpaceChange { get; init; } = true;
}

public sealed class LintPackageOptions
{
    public required string PackagePath { get; init; }
    public string? PathPrefix { get; init; }
}

public sealed class InspectPackageOptions
{
    public required string PackagePath { get; init; }
    public string? PathPrefix { get; init; }
}
