using System.Text.Json.Serialization;

namespace Rpack.AgentPackages;

public sealed class AgentOperationDocument
{
    public List<AgentOperation> Operations { get; init; } = [];
    public List<AgentOperationGroup> Groups { get; init; } = [];
}

public sealed class AgentOperationGroup
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public bool Required { get; init; }
    public List<string> DependsOn { get; init; } = [];
}

public sealed class AgentOperation
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Path { get; init; } = "";
    public string? BaseSha256 { get; init; }
    public string? TargetSha256 { get; init; }
    public string? PayloadPath { get; init; }
    public string? PatchPath { get; init; }
    public string ApplyPolicy { get; init; } = "";
    public bool Required { get; init; }
    public string? Group { get; init; }
    public string? Reason { get; init; }
}

[JsonSerializable(typeof(AgentOperationDocument))]
internal sealed partial class AgentOperationJsonContext : JsonSerializerContext;
