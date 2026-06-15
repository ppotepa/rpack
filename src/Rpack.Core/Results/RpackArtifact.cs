namespace Rpack.Core.Results;

public sealed record RpackArtifact(
    string Kind,
    string Path,
    string Description);
