using System.Text;
using System.Text.Json;
using Rpack.Core;

namespace Rpack.AgentPackages;

public sealed class AgentApplyPlanBuilder
{
    private readonly AgentPackageReader _reader = new();
    private readonly PayloadStoreReader _payloadStore = new();

    public AgentApplyPlan BuildPlan(string packageRoot, string? targetRepositoryPath = null)
    {
        return BuildPlan(packageRoot, targetRepositoryPath, skippedPaths: null);
    }

    public AgentApplyPlan BuildPlan(string packageRoot, string? targetRepositoryPath, IReadOnlySet<string>? skippedPaths)
    {
        var validator = new AgentPackageRootValidator();
        var validation = validator.Validate(packageRoot);
        if (!validation.Success)
        {
            return AgentApplyPlan.FromFailure(validation.Message);
        }

        var root = Path.GetFullPath(packageRoot);
        var manifest = _reader.ReadManifest(root);
        var operations = _reader.ReadOperations(root, manifest.OperationsPath);
        var targetRoot = string.IsNullOrWhiteSpace(targetRepositoryPath)
            ? Path.GetFullPath(Directory.GetCurrentDirectory())
            : Path.GetFullPath(targetRepositoryPath);

        var plans = operations.Operations
            .Select(operation => BuildOperationPlan(root, targetRoot, operation, skippedPaths))
            .ToArray();

        return new AgentApplyPlan(manifest, plans);
    }

    public string Render(AgentApplyPlan plan)
    {
        if (!plan.Success)
        {
            return plan.ErrorMessage;
        }

        if (plan.Manifest is null || plan.Operations is null)
        {
            return "Package plan is incomplete.";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Package: {plan.Manifest.Title} ({plan.Manifest.Id})");
        builder.AppendLine($"Operations: {plan.Operations.Count}");
        foreach (var operation in plan.Operations)
        {
            builder.AppendLine($"{operation.Id}  {operation.Status.ToString().ToLowerInvariant()}  {operation.Kind}  {operation.Path}");
        }

        return builder.ToString().TrimEnd();
    }

    public string RenderJson(AgentApplyPlan plan)
    {
        if (!plan.Success || plan.Manifest is null || plan.Operations is null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = plan.ErrorMessage
            });
        }

        var payload = new
        {
            success = true,
            package = new
            {
                id = plan.Manifest.Id,
                title = plan.Manifest.Title
            },
            operations = plan.Operations.Select(operation => new
            {
                id = operation.Id,
                kind = operation.Kind,
                path = operation.Path,
                status = operation.Status.ToString(),
                issueCode = operation.IssueCode,
                message = operation.Message
            }).ToArray()
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private AgentOperationPlan BuildOperationPlan(string packageRoot, string? targetRoot, AgentOperation operation, IReadOnlySet<string>? skippedPaths)
    {
        if (skippedPaths is not null && skippedPaths.Contains(operation.Path))
        {
            return new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, AgentOperationStatus.SkippedOptional, "Operation already applied or superseded by journal.");
        }

        var targetPath = Path.Combine(targetRoot!, operation.Path.Replace('/', Path.DirectorySeparatorChar));
        var targetExists = File.Exists(targetPath);
        var payloadPath = string.IsNullOrWhiteSpace(operation.PayloadPath)
            ? null
            : Path.Combine(packageRoot, operation.PayloadPath!);
        var payloadExists = payloadPath is not null && File.Exists(payloadPath);
        var payloadHash = payloadExists ? Sha256.ForBytes(_payloadStore.ReadPayload(packageRoot, operation.PayloadPath!)) : "";
        var targetHash = targetExists ? Sha256.ForBytes(File.ReadAllBytes(targetPath)) : "";

        return operation.Kind switch
        {
            "AddTextFile" => PlanAddTextFile(operation, targetExists, targetHash, payloadExists, payloadHash),
            "ModifyTextFile" => PlanModifyTextFile(operation, targetExists, targetHash, payloadExists, payloadHash),
            "DeleteTextFile" => PlanDeleteTextFile(operation, targetExists, targetHash),
            "ReplaceTextFile" => PlanReplaceTextFile(operation, targetExists, targetHash, payloadExists, payloadHash),
            "PatchOnly" => new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, payloadExists ? AgentOperationStatus.ReadyWithPatch : AgentOperationStatus.Unsupported, payloadExists ? "Patch ready." : "Missing patch payload.", payloadExists ? "" : "agent.payload-missing"),
            _ => new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, AgentOperationStatus.Unsupported, $"Unsupported operation kind: {operation.Kind}")
        };
    }

    private static AgentOperationPlan PlanAddTextFile(AgentOperation operation, bool targetExists, string targetHash, bool payloadExists, string payloadHash)
    {
        if (!payloadExists)
        {
            return new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, AgentOperationStatus.Unsupported, "Missing payload.", "agent.payload-missing");
        }

        if (!string.IsNullOrWhiteSpace(operation.TargetSha256) && string.Equals(operation.TargetSha256, targetHash, StringComparison.OrdinalIgnoreCase))
        {
            return new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, AgentOperationStatus.AlreadyApplied, "Target already matches payload.");
        }

        if (targetExists)
        {
            return new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, AgentOperationStatus.ConflictExistingDifferentFile, "Target already exists with different content.");
        }

        if (!string.Equals(operation.TargetSha256, payloadHash, StringComparison.OrdinalIgnoreCase))
        {
            return new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, AgentOperationStatus.ConflictBaseHashMismatch, "Payload hash does not match declared target hash.", "agent.payload-hash-mismatch");
        }

        return new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, AgentOperationStatus.ReadyWithPayload, "Payload can be applied.");
    }

    private static AgentOperationPlan PlanModifyTextFile(AgentOperation operation, bool targetExists, string targetHash, bool payloadExists, string payloadHash)
    {
        if (!targetExists)
        {
            return new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, AgentOperationStatus.ConflictMissingFile, "Target file is missing.");
        }

        if (!string.IsNullOrWhiteSpace(operation.TargetSha256) && string.Equals(operation.TargetSha256, targetHash, StringComparison.OrdinalIgnoreCase))
        {
            return new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, AgentOperationStatus.AlreadyApplied, "Target already matches desired content.");
        }

        if (string.IsNullOrWhiteSpace(operation.BaseSha256))
        {
            return new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, AgentOperationStatus.Unsupported, "Missing base hash.", "agent.metadata-missing");
        }

        if (!string.Equals(operation.BaseSha256, targetHash, StringComparison.OrdinalIgnoreCase))
        {
            return new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, AgentOperationStatus.ConflictBaseHashMismatch, "Target content has changed since the base hash.", "agent.base-hash-mismatch");
        }

        if (payloadExists && string.Equals(operation.TargetSha256, payloadHash, StringComparison.OrdinalIgnoreCase))
        {
            return new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, AgentOperationStatus.ReadyWithPayloadFallback, "Payload can be applied after confirming base.");
        }

        return new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, AgentOperationStatus.ReadyWithPatch, "Patch should be applied.");
    }

    private static AgentOperationPlan PlanDeleteTextFile(AgentOperation operation, bool targetExists, string targetHash)
    {
        if (!targetExists)
        {
            return new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, AgentOperationStatus.AlreadyApplied, "Target file already deleted.");
        }

        if (string.IsNullOrWhiteSpace(operation.BaseSha256))
        {
            return new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, AgentOperationStatus.Unsupported, "Missing base hash.", "agent.metadata-missing");
        }

        if (!string.Equals(operation.BaseSha256, targetHash, StringComparison.OrdinalIgnoreCase))
        {
            return new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, AgentOperationStatus.ConflictBaseHashMismatch, "Target content has changed since the base hash.", "agent.base-hash-mismatch");
        }

        return new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, AgentOperationStatus.ReadyWithPayload, "Delete can be applied.");
    }

    private static AgentOperationPlan PlanReplaceTextFile(AgentOperation operation, bool targetExists, string targetHash, bool payloadExists, string payloadHash)
    {
        if (!targetExists)
        {
            return new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, AgentOperationStatus.ConflictMissingFile, "Target file is missing.");
        }

        if (payloadExists && string.Equals(operation.TargetSha256, payloadHash, StringComparison.OrdinalIgnoreCase))
        {
            return new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, AgentOperationStatus.ReadyWithPayload, "Payload can replace target.");
        }

        if (string.IsNullOrWhiteSpace(operation.BaseSha256))
        {
            return new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, AgentOperationStatus.Unsupported, "Missing base hash.", "agent.metadata-missing");
        }

        return string.Equals(operation.BaseSha256, targetHash, StringComparison.OrdinalIgnoreCase)
            ? new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, AgentOperationStatus.ReadyWithPatch, "Patch can be applied.")
            : new AgentOperationPlan(operation.Id, operation.Kind, operation.Path, AgentOperationStatus.ConflictBaseHashMismatch, "Target content has changed since the base hash.", "agent.base-hash-mismatch");
    }
}

public sealed class AgentApplyPlan
{
    public AgentApplyPlan(AgentPackageManifest manifest, IReadOnlyList<AgentOperationPlan> operations)
    {
        Manifest = manifest;
        Operations = operations;
    }

    private AgentApplyPlan(string errorMessage)
    {
        ErrorMessage = errorMessage;
    }

    public AgentPackageManifest? Manifest { get; }
    public IReadOnlyList<AgentOperationPlan>? Operations { get; }
    public bool Success => string.IsNullOrWhiteSpace(ErrorMessage);
    public string ErrorMessage { get; } = "";

    public static AgentApplyPlan FromFailure(string errorMessage) => new(errorMessage);
}

public sealed class AgentOperationPlan
{
    public AgentOperationPlan(string id, string kind, string path, AgentOperationStatus status, string message, string issueCode = "")
    {
        Id = id;
        Kind = kind;
        Path = path;
        Status = status;
        Message = message;
        IssueCode = issueCode;
    }

    public string Id { get; }
    public string Kind { get; }
    public string Path { get; }
    public AgentOperationStatus Status { get; }
    public string Message { get; }
    public string IssueCode { get; }
}

public enum AgentOperationStatus
{
    ReadyWithPayload,
    ReadyWithPatch,
    ReadyWithPayloadFallback,
    AlreadyApplied,
    SkippedOptional,
    ConflictMissingFile,
    ConflictExistingDifferentFile,
    ConflictBaseHashMismatch,
    ConflictPatchFailed,
    ConflictUnsafePath,
    ConflictDuplicateOperationPath,
    Unsupported
}
