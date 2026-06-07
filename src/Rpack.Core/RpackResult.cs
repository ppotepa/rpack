namespace Rpack.Core;

public sealed class RpackResult
{
    private RpackResult(bool success, string message)
    {
        Success = success;
        Message = message;
    }

    public bool Success { get; }
    public string Message { get; }

    public static RpackResult Ok(string message) => new(true, message);
    public static RpackResult Fail(string message) => new(false, message);
}

public sealed class PackageInspection
{
    public required RpackManifest Manifest { get; init; }
    public required IReadOnlyList<string> Entries { get; init; }
    public required IReadOnlyList<RpackFileDiffStats> ChangedFiles { get; init; }
    public required RpackDiffStats DiffStats { get; init; }
    public int AddedLines => ChangedFiles.Sum(file => file.AddedLines);
    public int RemovedLines => ChangedFiles.Sum(file => file.RemovedLines);
    public int HunkCount => ChangedFiles.Sum(file => file.HunkCount);
}

public sealed class RpackDiffStats
{
    public required int PatchCount { get; init; }
    public required int FileCount { get; init; }
    public required int AddedLines { get; init; }
    public required int RemovedLines { get; init; }
    public required int HunkCount { get; init; }
    public required int BinaryFileCount { get; init; }
    public required IReadOnlyList<RpackPatchDiffStats> Patches { get; init; }
}

public sealed class RpackPatchDiffStats
{
    public required string PatchPath { get; init; }
    public required string Title { get; init; }
    public required int FileCount { get; init; }
    public required int AddedLines { get; init; }
    public required int RemovedLines { get; init; }
    public required int HunkCount { get; init; }
    public required IReadOnlyList<RpackFileDiffStats> Files { get; init; }
}

public class RpackFileDiffStats
{
    public required string Path { get; init; }
    public required string Status { get; init; }
    public int AddedLines { get; init; }
    public int RemovedLines { get; init; }
    public int HunkCount { get; init; }
    public bool IsBinary { get; init; }
    public string Category { get; init; } = "Other";
}

public sealed class PatchFileSummary : RpackFileDiffStats
{
}
