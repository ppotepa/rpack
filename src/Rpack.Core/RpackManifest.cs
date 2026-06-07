using System.Text.Json.Serialization;

namespace Rpack.Core;

public sealed class RpackManifest
{
    public string Format { get; init; } = "rpack-v1";
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string CreatedAtUtc { get; init; } = "";
    public string BaseCommit { get; init; } = "";
    public RpackSourceInfo? Source { get; init; }
    public bool RequiresCleanTree { get; init; } = true;
    public string Mode { get; init; } = "working-tree-patch";
    public List<RpackPatch> Patches { get; init; } = [];
    public List<RpackValidation> Validation { get; init; } = [];
}

public sealed class RpackSourceInfo
{
    public string Repository { get; init; } = "";
    public string ProjectPath { get; init; } = "";
    public string BaseCommit { get; init; } = "";
    public string HeadCommit { get; init; } = "";
}

public sealed class RpackPatch
{
    public string Path { get; init; } = "";
    public string Kind { get; init; } = "git-diff";
    public string Sha256 { get; init; } = "";
}

public sealed class RpackValidation
{
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public bool Optional { get; init; }
}

public sealed class RpackApplyLog
{
    public string ApplyId { get; init; } = "";
    public string PackageId { get; init; } = "";
    public string Title { get; init; } = "";
    public string AppliedAtUtc { get; init; } = "";
    public string UndoneAtUtc { get; init; } = "";
    public string BaseCommit { get; init; } = "";
    public string TargetHeadAtApply { get; init; } = "";
    public string PatchPath { get; init; } = "";
    public string PackagePath { get; init; } = "";
}

[JsonSerializable(typeof(RpackManifest))]
[JsonSerializable(typeof(RpackApplyLog))]
[JsonSerializable(typeof(List<RpackApplyLog>))]
internal sealed partial class RpackJsonContext : JsonSerializerContext;
