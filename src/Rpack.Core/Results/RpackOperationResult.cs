using Rpack.Core.Issues;

namespace Rpack.Core.Results;

public sealed record RpackOperationResult(
    bool Success,
    string Summary,
    IReadOnlyList<RpackIssue> Issues,
    IReadOnlyList<RpackArtifact> Artifacts,
    IReadOnlyList<ProcessLogEntry> Logs)
{
    public static RpackOperationResult Ok(string summary) =>
        new(true, summary, [], [], []);

    public static RpackOperationResult Fail(string summary, IReadOnlyList<RpackIssue>? issues = null) =>
        new(false, summary, issues ?? [], [], []);
}

public sealed record RpackOperationResult<T>(
    bool Success,
    string Summary,
    T? Value,
    IReadOnlyList<RpackIssue> Issues,
    IReadOnlyList<RpackArtifact> Artifacts,
    IReadOnlyList<ProcessLogEntry> Logs)
{
    public static RpackOperationResult<T> Ok(string summary, T value) =>
        new(true, summary, value, [], [], []);

    public static RpackOperationResult<T> Fail(string summary, IReadOnlyList<RpackIssue>? issues = null) =>
        new(false, summary, default, issues ?? [], [], []);
}
