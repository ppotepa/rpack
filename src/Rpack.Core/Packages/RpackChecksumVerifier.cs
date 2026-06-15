using System.IO.Compression;
using Rpack.Core;
using Rpack.Core.Issues;
using Rpack.Core.Results;

namespace Rpack.Core.Packages;

public sealed class RpackChecksumVerifier
{
    public RpackResult VerifyChecksums(ZipArchive archive, RpackManifest manifest)
    {
        var detailed = VerifyChecksumsDetailed(archive, manifest);
        return detailed.Success
            ? RpackResult.Ok(detailed.Summary)
            : RpackResult.Fail(detailed.Issues.FirstOrDefault()?.Message ?? detailed.Summary);
    }

    public RpackOperationResult VerifyChecksumsDetailed(ZipArchive archive, RpackManifest manifest)
    {
        var issues = new List<RpackIssue>();
        foreach (var patch in manifest.Patches)
        {
            var result = VerifyArchiveEntryChecksum(archive, patch.Path, patch.Sha256, "Patch");
            if (!result.Success)
            {
                issues.Add(new RpackIssue(
                    "checksum.mismatch",
                    RpackSeverity.Error,
                    RpackStage.ChecksumVerification,
                    result.Message,
                    "Repack the archive or verify the payload bytes.",
                    RawDetails: result.Message));
            }
        }

        foreach (var action in manifest.PreActions.Concat(manifest.PostActions))
        {
            if (string.IsNullOrWhiteSpace(action.Path))
            {
                continue;
            }

            var result = VerifyArchiveEntryChecksum(archive, action.Path, action.Sha256, "Action");
            if (!result.Success)
            {
                var issue = result.Message.StartsWith("Action is missing:", StringComparison.OrdinalIgnoreCase)
                    ? new RpackIssue(
                        "action.script-missing",
                        RpackSeverity.Error,
                        RpackStage.ChecksumVerification,
                        result.Message,
                        "Add the script entry to the package archive or update the manifest path.",
                        PackagePath: action.Path,
                        RawDetails: result.Message)
                    : new RpackIssue(
                        "action.script-checksum-mismatch",
                        RpackSeverity.Error,
                        RpackStage.ChecksumVerification,
                        result.Message,
                        "Regenerate the script entry or update the declared action checksum.",
                        PackagePath: action.Path,
                        RawDetails: result.Message);
                issues.Add(issue);
            }
        }

        return issues.Count == 0
            ? RpackOperationResult.Ok("Checksums are valid.")
            : RpackOperationResult.Fail("Checksum verification failed.", issues);
    }

    public static RpackResult VerifyArchiveEntryChecksum(ZipArchive archive, string path, string sha256, string kind)
    {
        if (!RpackPackageReader.IsSafeArchivePath(path))
        {
            return RpackResult.Fail($"Unsafe archive path: {path}");
        }

        var entry = archive.GetEntry(path);
        if (entry is null)
        {
            return RpackResult.Fail($"{kind} is missing: {path}");
        }

        using var memory = new MemoryStream();
        using (var stream = entry.Open())
        {
            stream.CopyTo(memory);
        }

        var actual = Sha256.ForBytes(memory.ToArray());
        return string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase)
            ? RpackResult.Ok($"{kind} checksum is valid.")
            : RpackResult.Fail($"Checksum mismatch for {path}.");
    }
}
