using System.IO.Compression;
using System.Text;

namespace Rpack.Core.Patches;

public sealed class RpackPatchPreparer
{
    private readonly RpackPatchPathTransformer _pathTransformer;

    public RpackPatchPreparer()
        : this(new RpackPatchPathTransformer())
    {
    }

    public RpackPatchPreparer(RpackPatchPathTransformer pathTransformer)
    {
        _pathTransformer = pathTransformer;
    }

    public TempPatchSet ExtractPatchesToTempDirectory(ZipArchive archive, IReadOnlyList<RpackPatch> patches, string pathPrefix)
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
                var transformedPatch = _pathTransformer.RewritePatchPaths(patchContent, pathPrefix);
                File.WriteAllText(tempPath, transformedPatch, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            tempPatches.Add(new TempPatch(patch.Path, tempPath));
        }

        return new TempPatchSet(tempDirectory, tempPatches);
    }

    public RpackResult PrepareExistingAddedFiles(
        string repositoryPath,
        RpackManifest manifest,
        TempPatchSet tempPatchSet,
        AddedFileConflictResolution addedFileConflictResolution)
    {
        var sameContents = new List<string>();
        var skipped = new List<string>();
        var converted = new List<string>();
        var conflicts = new List<string>();

        foreach (var patch in tempPatchSet.Patches)
        {
            var patchText = File.ReadAllText(patch.TempPath, Encoding.UTF8);
            var rewritten = RewriteAlreadyPresentAddedFiles(
                repositoryPath,
                manifest,
                patch.ManifestPath,
                patchText,
                sameContents,
                skipped,
                converted,
                conflicts,
                addedFileConflictResolution);
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

        if (sameContents.Count == 0 && skipped.Count == 0 && converted.Count == 0)
        {
            return RpackResult.Ok("No existing added files.");
        }

        var summaries = new List<string>();
        if (sameContents.Count > 0)
        {
            summaries.Add($"Already-present file(s) skipped because target content matches the package:{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", sameContents)}");
        }

        if (skipped.Count > 0)
        {
            summaries.Add($"Already-present file(s) skipped due to conflict resolution:{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", skipped)}");
        }

        if (converted.Count > 0)
        {
            summaries.Add($"Already-present file(s) rewritten from add to modify:{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", converted)}");
        }

        return RpackResult.Ok(string.Join(Environment.NewLine + Environment.NewLine, summaries));
    }

    public bool TryParseAddedFileConflictResolution(
        string? value,
        out AddedFileConflictResolution resolution,
        out RpackResult failure)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? "abort"
            : value.Trim().ToLowerInvariant();

        resolution = normalized switch
        {
            "abort" => AddedFileConflictResolution.Abort,
            "skip" => AddedFileConflictResolution.Skip,
            "modify" => AddedFileConflictResolution.Modify,
            "as-modify" => AddedFileConflictResolution.Modify,
            "overwrite" => AddedFileConflictResolution.Overwrite,
            _ => AddedFileConflictResolution.Abort
        };

        if (normalized is "abort" or "skip" or "modify" or "as-modify" or "overwrite")
        {
            failure = RpackResult.Ok("ok");
            return true;
        }

        failure = RpackResult.Fail($"Unknown added-file conflict resolution '{value}'. Expected: abort, skip, modify, overwrite, as-modify.");
        return false;
    }

    private static string RewriteAlreadyPresentAddedFiles(
        string repositoryPath,
        RpackManifest manifest,
        string manifestPatchPath,
        string patchText,
        List<string> sameContents,
        List<string> skipped,
        List<string> converted,
        List<string> conflicts,
        AddedFileConflictResolution addedFileConflictResolution)
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
                        sameContents.Add($"{manifestPatchPath}:{addedFile.Path}");
                        block.Clear();
                        return;
                    }

                    if (addedFileConflictResolution == AddedFileConflictResolution.Skip)
                    {
                        skipped.Add($"{manifestPatchPath}:{addedFile.Path} (content differs)");
                        block.Clear();
                        return;
                    }

                    if (addedFileConflictResolution == AddedFileConflictResolution.Abort)
                    {
                        conflicts.Add(BuildAddedFileConflict(manifest, manifestPatchPath, addedFile, targetPath));
                        block.Clear();
                        return;
                    }

                    var convertedBlock = BuildModifyPatchForAddedFileBlock(addedFile, targetText);
                    if (convertedBlock is null)
                    {
                        conflicts.Add(BuildAddedFileConflict(manifest, manifestPatchPath, addedFile, targetPath));
                        block.Clear();
                        return;
                    }

                    converted.Add($"{manifestPatchPath}:{addedFile.Path}");
                    output.AddRange(convertedBlock.Split('\n'));
                    block.Clear();
                    return;
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

        var path = RpackPatchParser.ParseDiffGitPath(block[0]);
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

    private static string? BuildModifyPatchForAddedFileBlock(AddedTextFile addedFile, string targetText)
    {
        var oldLines = SplitLinesForPatch(targetText, out var oldHasTrailingNewline);
        var newLines = SplitLinesForPatch(addedFile.Content, out var newHasTrailingNewline);

        var builder = new List<string>
        {
            $"diff --git a/{addedFile.Path} b/{addedFile.Path}",
            "index 0000000..0000000",
            $"--- a/{addedFile.Path}",
            $"+++ b/{addedFile.Path}",
            $"@@ -1,{oldLines.Length} +1,{newLines.Length} @@"
        };

        foreach (var line in oldLines)
        {
            builder.Add($"-{line}");
        }

        if (!oldHasTrailingNewline)
        {
            builder.Add(@"\ No newline at end of file");
        }

        foreach (var line in newLines)
        {
            builder.Add($"+{line}");
        }

        if (!newHasTrailingNewline)
        {
            builder.Add(@"\ No newline at end of file");
        }

        return string.Join("\n", builder);
    }

    private static string[] SplitLinesForPatch(string content, out bool hasTrailingNewline)
    {
        var normalized = NormalizeLineEndings(content);
        hasTrailingNewline = normalized.EndsWith("\n", StringComparison.Ordinal);
        if (hasTrailingNewline)
        {
            normalized = normalized[..^1];
        }

        return string.IsNullOrEmpty(normalized)
            ? []
            : normalized.Split('\n');
    }

    private static bool IsSafeArchivePath(string path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && !Path.IsPathRooted(path)
            && !path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Contains("..");
    }

    private static string NormalizeGitPath(string path)
    {
        return path.Replace('\\', '/').Trim();
    }

    private sealed record AddedTextFile(string Path, string Content);
}

public enum AddedFileConflictResolution
{
    Abort,
    Skip,
    Modify,
    Overwrite
}

public sealed record TempPatch(string ManifestPath, string TempPath);

public sealed class TempPatchSet : IDisposable
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
