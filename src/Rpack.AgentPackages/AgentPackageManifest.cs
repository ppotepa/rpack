using System.Text.Json.Serialization;

namespace Rpack.AgentPackages;

public sealed class AgentPackageManifest
{
    public string Format { get; init; } = "rpack-agent-package";
    public string SchemaVersion { get; init; } = "1.0";
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string CreatedAtUtc { get; init; } = "";
    public string OperationsPath { get; init; } = "operations.json";
    public string DefaultApplyStrategy { get; init; } = "ApplyReadyAndFallbacks";
    public List<string> RequiredCapabilities { get; init; } = [];
    public List<string> DeclaredFiles { get; init; } = [];
    public string? Description { get; init; }
    public AgentPackageSource? Source { get; init; }
    public AgentPackageGenerator? Generator { get; init; }
    public AgentPackageSecurityPolicy? SecurityPolicy { get; init; }
    public List<AgentPackageValidationCommand> Validation { get; init; } = [];
    public string? RepairsPackageId { get; init; }
    public List<string> RepairsOperations { get; init; } = [];
}

public sealed class AgentPackageSource
{
    public string? Repository { get; init; }
    public string? ProjectPathHint { get; init; }
    public string? SourceKind { get; init; }
    public string? SourceConcatSha256 { get; init; }
}

public sealed class AgentPackageGenerator
{
    public string? Name { get; init; }
    public string? Model { get; init; }
    public string? Intent { get; init; }
}

public sealed class AgentPackageSecurityPolicy
{
    public string? DefaultActionTrust { get; init; }
    public bool AllowScriptActions { get; init; }
    public bool AllowPayloadReplace { get; init; } = true;
}

[JsonSerializable(typeof(AgentPackageManifest))]
internal sealed partial class AgentPackageJsonContext : JsonSerializerContext;
