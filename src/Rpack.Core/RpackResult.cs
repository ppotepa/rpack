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
    public required IReadOnlyList<PatchFileSummary> ChangedFiles { get; init; }
    public int AddedLines => ChangedFiles.Sum(file => file.AddedLines);
    public int RemovedLines => ChangedFiles.Sum(file => file.RemovedLines);
}

public sealed class PatchFileSummary
{
    public required string Path { get; init; }
    public required string Status { get; init; }
    public int AddedLines { get; init; }
    public int RemovedLines { get; init; }
}
