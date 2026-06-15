using System.Text;
using System.Text.Json;
using Rpack.Core;

namespace Rpack.AgentPackages;

public sealed class AgentRepairPackageGenerator
{
    private readonly AgentApplyPlanBuilder _planBuilder = new();
    private readonly AgentPackageReader _reader = new();

    public RpackResult Generate(string packageRoot, string targetRepositoryPath, string outputRoot)
    {
        var plan = _planBuilder.BuildPlan(packageRoot, targetRepositoryPath);
        if (!plan.Success || plan.Manifest is null || plan.Operations is null)
        {
            return RpackResult.Fail(plan.ErrorMessage);
        }

        var conflicts = plan.Operations
            .Where(operation => operation.Status is AgentOperationStatus.ConflictBaseHashMismatch
                or AgentOperationStatus.ConflictExistingDifferentFile
                or AgentOperationStatus.ConflictMissingFile
                or AgentOperationStatus.ConflictPatchFailed
                or AgentOperationStatus.ConflictUnsafePath
                or AgentOperationStatus.ConflictDuplicateOperationPath
                or AgentOperationStatus.Unsupported)
            .ToArray();

        if (conflicts.Length == 0)
        {
            return RpackResult.Fail("No conflicts available to repair.");
        }

        var root = Path.GetFullPath(outputRoot);
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));

        var originalManifest = _reader.ReadManifest(Path.GetFullPath(packageRoot));
        var repairManifest = new AgentPackageManifest
        {
            Id = $"repair-{originalManifest.Id}",
            Title = $"Repair for {originalManifest.Id}",
            CreatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            OperationsPath = "operations.json",
            DefaultApplyStrategy = originalManifest.DefaultApplyStrategy,
            RequiredCapabilities = [.. originalManifest.RequiredCapabilities],
            Description = originalManifest.Description,
            Source = originalManifest.Source,
            Generator = originalManifest.Generator,
            SecurityPolicy = originalManifest.SecurityPolicy,
            RepairsPackageId = originalManifest.Id,
            RepairsOperations = [.. conflicts.Select(operation => operation.Id)]
        };

        var operations = new List<AgentOperation>();
        foreach (var conflict in conflicts)
        {
            var targetPath = Path.Combine(targetRepositoryPath, conflict.Path.Replace('/', Path.DirectorySeparatorChar));
            var payloadBytes = File.Exists(targetPath)
                ? File.ReadAllBytes(targetPath)
                : Encoding.UTF8.GetBytes(string.Empty);
            var payloadHash = Sha256.ForBytes(payloadBytes);
            var payloadPath = Path.Combine("payload", "text", $"{payloadHash}.txt").Replace('\\', '/');
            var payloadFullPath = Path.Combine(root, payloadPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(payloadFullPath)!);
            File.WriteAllBytes(payloadFullPath, payloadBytes);

            operations.Add(new AgentOperation
            {
                Id = conflict.Id,
                Kind = conflict.Kind,
                Path = conflict.Path,
                BaseSha256 = payloadHash,
                TargetSha256 = payloadHash,
                PayloadPath = payloadPath,
                ApplyPolicy = "PayloadIfBaseMatches",
                Required = conflict.Status != AgentOperationStatus.SkippedOptional,
                Group = null,
                Reason = $"Repair seed for {conflict.Path}."
            });
        }

        File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(repairManifest, AgentPackageJsonContext.Default.AgentPackageManifest), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), JsonSerializer.Serialize(new AgentOperationDocument
        {
            Operations = operations,
            Groups = []
        }, AgentOperationJsonContext.Default.AgentOperationDocument), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "README.md"), BuildReadme(repairManifest, conflicts), new UTF8Encoding(false));

        return RpackResult.Ok($"Repair package-root created at {root}");
    }

    private static string BuildReadme(AgentPackageManifest manifest, IReadOnlyList<AgentOperationPlan> conflicts)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Repair package: {manifest.Id}");
        builder.AppendLine($"Repairs original package: {manifest.RepairsPackageId}");
        builder.AppendLine("Conflicted operations:");
        foreach (var conflict in conflicts)
        {
            builder.AppendLine($"- {conflict.Id} {conflict.Path} {conflict.Status}");
        }

        return builder.ToString().TrimEnd();
    }
}
