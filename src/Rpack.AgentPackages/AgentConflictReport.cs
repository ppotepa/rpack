using System.Text;
using System.Text.Json;
using Rpack.Core;

namespace Rpack.AgentPackages;

public sealed class AgentConflictReportBuilder
{
    public string Render(AgentApplyPlan plan, string packageRoot, string targetRepositoryPath)
    {
        return RenderText(plan, packageRoot, targetRepositoryPath);
    }

    public string RenderText(AgentApplyPlan plan, string packageRoot, string targetRepositoryPath)
    {
        if (!plan.Success || plan.Manifest is null || plan.Operations is null)
        {
            return plan.ErrorMessage;
        }

        var conflicts = plan.Operations
            .Where(operation => IsConflict(operation.Status))
            .ToArray();

        if (conflicts.Length == 0)
        {
            return $"No conflicts found for package {plan.Manifest.Id}.";
        }

        var builder = new StringBuilder();
        builder.AppendLine("RPACK Agent Package conflict report");
        builder.AppendLine();
        builder.AppendLine($"Package: {plan.Manifest.Id}");
        builder.AppendLine($"Target: {targetRepositoryPath}");
        builder.AppendLine();
        builder.AppendLine("Only these operations need regeneration:");
        builder.AppendLine();

        for (var index = 0; index < conflicts.Length; index++)
        {
            var operation = conflicts[index];
            builder.AppendLine($"{index + 1}. {operation.Id}");
            builder.AppendLine($"OperationId: {operation.Id}");
            builder.AppendLine($"Path: {operation.Path}");
            builder.AppendLine($"Problem: {operation.Status}");
            if (!string.IsNullOrWhiteSpace(operation.IssueCode))
            {
                builder.AppendLine($"ProblemCode: {operation.IssueCode}");
            }
            builder.AppendLine($"ExpectedBaseSha256: {GetOperationBaseSha256(packageRoot, operation.Id)}");
            builder.AppendLine($"ActualTargetSha256: {GetActualTargetSha256(targetRepositoryPath, operation.Path)}");
            builder.AppendLine($"TargetSha256FromPackage: {GetOperationTargetSha256(packageRoot, operation.Id)}");
            builder.AppendLine($"PayloadAvailable: {HasPayload(packageRoot, operation.Id)}");
            builder.AppendLine($"SafePayloadFallback: {operation.Status == AgentOperationStatus.ConflictBaseHashMismatch}");
            builder.AppendLine($"PreferredMethod: {GetPreferredMethod(operation.Kind)}");
            builder.AppendLine($"Diagnostics: {operation.Message}");
            builder.AppendLine($"SuggestedAction: Regenerate only this file against current target content.");
            builder.AppendLine($"PromptForRegeneration: Produce a repair package containing only {operation.Path}.");
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    public string RenderLlm(AgentApplyPlan plan, string packageRoot, string targetRepositoryPath)
    {
        return RenderText(plan, packageRoot, targetRepositoryPath);
    }

    public string RenderJson(AgentApplyPlan plan, string packageRoot, string targetRepositoryPath)
    {
        if (!plan.Success || plan.Manifest is null || plan.Operations is null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = plan.ErrorMessage
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        var conflicts = plan.Operations
            .Where(operation => IsConflict(operation.Status))
            .Select(operation => new
            {
                operationId = operation.Id,
                path = operation.Path,
                problem = operation.Status.ToString(),
                issueCode = operation.IssueCode,
                expectedBaseSha256 = GetOperationBaseSha256(packageRoot, operation.Id),
                actualTargetSha256 = GetActualTargetSha256(targetRepositoryPath, operation.Path),
                targetSha256FromPackage = GetOperationTargetSha256(packageRoot, operation.Id),
                payloadAvailable = HasPayload(packageRoot, operation.Id),
                safePayloadFallback = operation.Status == AgentOperationStatus.ConflictBaseHashMismatch,
                preferredMethod = GetPreferredMethod(operation.Kind),
                diagnostics = operation.Message,
                suggestedAction = $"Regenerate only this file against current target content.",
                promptForRegeneration = $"Produce a repair package containing only {operation.Path}."
            })
            .ToArray();

        return JsonSerializer.Serialize(new
        {
            success = true,
            package = plan.Manifest.Id,
            target = targetRepositoryPath,
            conflicts
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static bool IsConflict(AgentOperationStatus status)
    {
        return status is AgentOperationStatus.ConflictBaseHashMismatch
            or AgentOperationStatus.ConflictExistingDifferentFile
            or AgentOperationStatus.ConflictMissingFile
            or AgentOperationStatus.ConflictPatchFailed
            or AgentOperationStatus.ConflictUnsafePath
            or AgentOperationStatus.ConflictDuplicateOperationPath
            or AgentOperationStatus.Unsupported;
    }

    private static string GetPreferredMethod(string kind)
    {
        return kind switch
        {
            "DeleteTextFile" => "Delete",
            "PatchOnly" => "Patch",
            _ => "Payload"
        };
    }

    private static string GetOperationBaseSha256(string packageRoot, string operationId)
    {
        var operation = LoadOperation(packageRoot, operationId);
        return operation?.BaseSha256 ?? "";
    }

    private static string GetOperationTargetSha256(string packageRoot, string operationId)
    {
        var operation = LoadOperation(packageRoot, operationId);
        return operation?.TargetSha256 ?? "";
    }

    private static bool HasPayload(string packageRoot, string operationId)
    {
        var operation = LoadOperation(packageRoot, operationId);
        return !string.IsNullOrWhiteSpace(operation?.PayloadPath)
            && File.Exists(Path.Combine(packageRoot, operation.PayloadPath));
    }

    private static string GetActualTargetSha256(string targetRepositoryPath, string relativePath)
    {
        var targetPath = Path.Combine(targetRepositoryPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(targetPath)
            ? Sha256.ForBytes(File.ReadAllBytes(targetPath))
            : "";
    }

    private static AgentOperation? LoadOperation(string packageRoot, string operationId)
    {
        var reader = new AgentPackageReader();
        var manifest = reader.ReadManifest(packageRoot);
        var operations = reader.ReadOperations(packageRoot, manifest.OperationsPath);
        return operations.Operations.FirstOrDefault(operation => string.Equals(operation.Id, operationId, StringComparison.Ordinal));
    }
}
