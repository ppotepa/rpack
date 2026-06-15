namespace Rpack.Core.Issues;

public sealed record RpackIssue(
    string Code,
    RpackSeverity Severity,
    RpackStage Stage,
    string Message,
    string Suggestion,
    string? PackagePath = null,
    string? PatchPath = null,
    string? FilePath = null,
    string? RawDetails = null);
