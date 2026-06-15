using System.Text.Json;
using Rpack.Core.Issues;
using Rpack.Core;
using Rpack.Core.Results;

namespace Rpack.Cli;

public static class CliOutputFormatter
{
    public static string FormatResultJson(string command, RpackResult result)
    {
        var payload = new
        {
            command,
            success = result.Success,
            message = result.Message,
            issues = new[]
            {
                new
                {
                    code = ClassifyCode(result.Message),
                    severity = result.Success ? "info" : "error",
                    stage = ClassifyStage(result.Message),
                    message = result.Message
                }
            }
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string FormatResultJson(string command, RpackOperationResult result)
    {
        var payload = new
        {
            command,
            success = result.Success,
            message = result.Summary,
            issues = result.Issues.Select(issue => new
            {
                code = issue.Code,
                severity = issue.Severity.ToString().ToLowerInvariant(),
                stage = issue.Stage.ToString(),
                message = issue.Message,
                suggestion = issue.Suggestion,
                packagePath = issue.PackagePath,
                patchPath = issue.PatchPath,
                filePath = issue.FilePath
            }),
            artifacts = result.Artifacts.Select(artifact => new
            {
                kind = artifact.Kind,
                path = artifact.Path,
                description = artifact.Description
            })
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string FormatInspectionJson(PackageInspection inspection)
    {
        var payload = new
        {
            success = true,
            manifest = new
            {
                id = inspection.Manifest.Id,
                title = inspection.Manifest.Title,
                format = inspection.Manifest.Format,
                mode = inspection.Manifest.Mode
            },
            diff = new
            {
                patchCount = inspection.DiffStats.PatchCount,
                fileCount = inspection.DiffStats.FileCount,
                addedLines = inspection.DiffStats.AddedLines,
                removedLines = inspection.DiffStats.RemovedLines,
                hunkCount = inspection.DiffStats.HunkCount,
                binaryFileCount = inspection.DiffStats.BinaryFileCount
            },
            entries = inspection.Entries
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string FormatDiagnoseLlmReport(string packagePath, string repositoryPath, RpackResult result)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("RPACK Conflict Report");
        builder.AppendLine($"Package: {packagePath}");
        builder.AppendLine($"Target: {repositoryPath}");
        builder.AppendLine("OperationId: unknown");
        builder.AppendLine("Path: unknown");
        builder.AppendLine($"Problem: {ClassifyCode(result.Message)}");
        builder.AppendLine("ExpectedBaseSha256: unknown");
        builder.AppendLine("ActualTargetSha256: unknown");
        builder.AppendLine("TargetSha256FromPackage: unknown");
        builder.AppendLine("PayloadAvailable: unknown");
        builder.AppendLine("SafePayloadFallback: false");
        builder.AppendLine("PreferredMethod: unknown");
        builder.AppendLine("Diagnostics:");
        builder.AppendLine(result.Message);
        builder.AppendLine("SuggestedAction: Regenerate only the conflicted file(s) against the current target.");
        builder.AppendLine("PromptForRegeneration: Produce a repair package containing only the conflicted file(s).");
        return builder.ToString().TrimEnd();
    }

    public static int MapExitCode(RpackResult result, bool invalidArgs = false)
    {
        if (invalidArgs)
        {
            return 2;
        }

        if (result.Success)
        {
            return 0;
        }

        var message = result.Message;
        if (message.Contains("checksum", StringComparison.OrdinalIgnoreCase)
            || message.Contains("manifest", StringComparison.OrdinalIgnoreCase)
            || message.Contains("package root", StringComparison.OrdinalIgnoreCase)
            || message.Contains("payload hash mismatch", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (message.Contains("unsafe", StringComparison.OrdinalIgnoreCase)
            || message.Contains("security", StringComparison.OrdinalIgnoreCase)
            || message.Contains("path", StringComparison.OrdinalIgnoreCase) && message.Contains("prefix", StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        if (message.Contains("apply failed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("action failed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("rollback", StringComparison.OrdinalIgnoreCase)
            || message.Contains("postaction", StringComparison.OrdinalIgnoreCase)
            || message.Contains("preaction", StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        return 1;
    }

    public static int MapExitCode(RpackOperationResult result, bool invalidArgs = false)
    {
        if (invalidArgs)
        {
            return 2;
        }

        if (result.Success)
        {
            return 0;
        }

        var primaryCode = result.Issues.FirstOrDefault()?.Code ?? "";
        if (primaryCode is "checksum.mismatch"
            or "package.root-missing"
            or "package.root-not-directory"
            or "manifest.missing"
            or "manifest.read-failed"
            or "manifest.unsupported-format"
            or "manifest.unsupported-schema"
            or "manifest.required-fields-missing"
            or "manifest.repair-metadata-incomplete"
            or "package.entry-missing"
            or "operations.missing"
            or "operations.read-failed"
            or "operation.id-invalid"
            or "operation.kind-unsupported"
            or "operation.path-invalid"
            or "payload.missing"
            or "payload.target-hash-missing"
            or "payload.hash-mismatch"
            or "patch.missing"
            or "agent.risk-generated-output"
            or "agent.risk-secret-marker"
            or "agent.risk-binary-change"
            or "agent.metadata-missing"
            or "agent.declared-files-mismatch"
            or "agent.payload-missing"
            or "agent.payload-hash-mismatch"
            or "agent.base-hash-mismatch"
            or "agent.operation-duplicate-path")
        {
            return 3;
        }

        if (primaryCode is "package.path-unsafe" or "conflict.path-unsafe")
        {
            return 4;
        }

        if (primaryCode.StartsWith("patch.", StringComparison.OrdinalIgnoreCase)
            || primaryCode.StartsWith("action.", StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        return 1;
    }

    private static string ClassifyCode(string message)
    {
        if (message.Contains("checksum", StringComparison.OrdinalIgnoreCase))
        {
            return "checksum.mismatch";
        }

        if (message.Contains("manifest", StringComparison.OrdinalIgnoreCase))
        {
            return "manifest.invalid-json";
        }

        if (message.Contains("unsafe", StringComparison.OrdinalIgnoreCase))
        {
            return "package.path-unsafe";
        }

        if (message.Contains("apply failed", StringComparison.OrdinalIgnoreCase))
        {
            return "patch.apply-failed";
        }

        if (message.Contains("action failed", StringComparison.OrdinalIgnoreCase))
        {
            return "action.post-failed";
        }

        return "info";
    }

    private static string ClassifyStage(string message)
    {
        if (message.Contains("checksum", StringComparison.OrdinalIgnoreCase))
        {
            return "ChecksumVerification";
        }

        if (message.Contains("manifest", StringComparison.OrdinalIgnoreCase))
        {
            return "ManifestValidation";
        }

        if (message.Contains("unsafe", StringComparison.OrdinalIgnoreCase))
        {
            return "PackageOpen";
        }

        if (message.Contains("apply failed", StringComparison.OrdinalIgnoreCase))
        {
            return "PatchApply";
        }

        if (message.Contains("action failed", StringComparison.OrdinalIgnoreCase))
        {
            return "PostAction";
        }

        return "Unknown";
    }
}
