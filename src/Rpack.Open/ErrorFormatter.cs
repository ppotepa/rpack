using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Rpack.Open;

internal static class ErrorFormatter
{
    public static PackageProblem FromCheckFailure(string message, bool allowDirty, bool ignoreSpaceChange = false)
    {
        var normalized = Normalize(message);
        if (string.Equals(normalized, "Working tree is not clean.", StringComparison.Ordinal))
        {
            return new PackageProblem(
                "Dirty working tree",
                allowDirty
                    ? "The target repository still has local changes that block this package."
                    : "The target repository has local changes. Use Shift+double-click or the dirty-tree context action only when those changes are intentional.",
                "Check repository state",
                normalized);
        }

        if (normalized.Contains("Checksum mismatch", StringComparison.OrdinalIgnoreCase))
        {
            return new PackageProblem(
                "Checksum mismatch",
                "The package contents do not match the checksum stored in manifest.json. Do not apply this package.",
                "Verify package integrity",
                normalized);
        }

        if (normalized.Contains("Unsafe archive path", StringComparison.OrdinalIgnoreCase))
        {
            return new PackageProblem(
                "Unsafe package path",
                "The package contains an archive path that would escape the package or repository boundary.",
                "Reject package",
                normalized);
        }

        if (normalized.Contains("Unsupported package format", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Unsupported package mode", StringComparison.OrdinalIgnoreCase))
        {
            return new PackageProblem(
                "Unsupported package",
                "This rpack version does not support the package format or mode.",
                "Install a newer rpack or regenerate the package",
                normalized);
        }

        if (normalized.Contains("Package base commit differs", StringComparison.OrdinalIgnoreCase))
        {
            return new PackageProblem(
                "Base commit mismatch",
                "The package was created from a different base commit than the target repository HEAD.",
                "Apply without strict base only if the patch context is compatible",
                normalized);
        }

        if (normalized.Contains("Whitespace diagnostic", StringComparison.OrdinalIgnoreCase))
        {
            return new PackageProblem(
                "Whitespace mismatch",
                "Strict patch validation failed, but Git reports that the patch can be applied when whitespace changes in context lines are ignored.",
                "Fix final-newline/CRLF drift in the target file, or run without strict whitespace mode if that drift is intentional.",
                normalized);
        }

        if (normalized.Contains("Added-file conflict", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Already-present diagnostic", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("already exists in working directory", StringComparison.OrdinalIgnoreCase))
        {
            return new PackageProblem(
                normalized.Contains("Added-file conflict", StringComparison.OrdinalIgnoreCase) ? "Added-file conflict" : "Patch files already exist",
                "The target repository already contains file(s) that this package wants to add.",
                "If the existing file differs, regenerate the package against this repository or resolve the file manually.",
                normalized);
        }

        if (normalized.Contains("PreAction failed", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("PostAction failed", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Unsupported action kind", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Action script is missing", StringComparison.OrdinalIgnoreCase))
        {
            return new PackageProblem(
                "Package action failed",
                "The package patch may be valid, but one of its manifest actions failed or could not be prepared.",
                "Review the action details. Use --no-actions only when you intentionally want to apply the patch without lifecycle scripts.",
                normalized);
        }

        if (normalized.Contains("Patch dry-run failed", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Patch dry-run apply failed", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Patch apply failed", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("does not exist in index", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("patch does not apply", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("error:", StringComparison.OrdinalIgnoreCase))
        {
            return new PackageProblem(
                ignoreSpaceChange ? "Patch failed with whitespace mode" : "Patch dry-run failed",
                BuildPatchFailureSummary(normalized),
                ignoreSpaceChange
                    ? "The patch still does not match even with whitespace-compatible context matching. Check target file contents and package base."
                    : "Check that package paths are relative to the Git root. If the package came from a subdirectory snapshot, retry with --path-prefix.",
                normalized);
        }

        return new PackageProblem(
            "Validation failed",
            "The package did not pass rpack validation.",
            "Review details",
            normalized);
    }

    public static PackageProblem SummarizeException(Exception exception)
    {
        var message = Normalize(exception.Message);
        if (exception is FileNotFoundException)
        {
            return new PackageProblem(
                "Package not found",
                "The selected .rpack file does not exist.",
                "Check the package path",
                message);
        }

        if (exception is InvalidDataException || message.Contains("End of Central Directory", StringComparison.OrdinalIgnoreCase))
        {
            return new PackageProblem(
                "Invalid package archive",
                "The selected file is not a readable .rpack ZIP archive.",
                "Regenerate or download the package again",
                message);
        }

        if (message.Contains("manifest.json is missing", StringComparison.OrdinalIgnoreCase))
        {
            return new PackageProblem(
                "Missing manifest",
                "The package does not contain manifest.json.",
                "Regenerate the package",
                message);
        }

        if (message.Contains("manifest.json is invalid", StringComparison.OrdinalIgnoreCase))
        {
            return new PackageProblem(
                "Invalid manifest",
                "manifest.json could not be parsed by rpack.",
                "Regenerate the package with valid rpack-v1 metadata",
                message);
        }

        if (message.Contains("Target path is not a Git repository", StringComparison.OrdinalIgnoreCase))
        {
            return new PackageProblem(
                "Repository not found",
                "rpack could not find a Git repository for this package.",
                "Place the package inside the target repo or open with --repo <repo>",
                message);
        }

        return new PackageProblem(
            "Unexpected error",
            "rpack-open hit an unexpected error while preparing the package.",
            "Review details",
            message);
    }

    private static string BuildPatchFailureSummary(string message)
    {
        var file = ExtractFirstGitFile(message);
        return string.IsNullOrWhiteSpace(file)
            ? "Git could not apply this patch to the target working tree."
            : $"Git could not apply this patch to `{file}` in the target working tree.";
    }

    private static string ExtractFirstGitFile(string message)
    {
        var match = Regex.Match(message, @"error:\s+([^:\r\n]+):");
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    private static string Normalize(string message)
    {
        return string.IsNullOrWhiteSpace(message)
            ? "(no details)"
            : message.Trim();
    }
}

internal sealed record PackageProblem(string Title, string Summary, string Suggestion, string Details);
