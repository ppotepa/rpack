using Rpack.Core;
using Rpack.Core.Issues;
using Rpack.Core.Results;
using System.Text;

namespace Rpack.AgentPackages;

public sealed class AgentPackageRootValidator
{
    private readonly ProcessRunner _processRunner;

    public AgentPackageRootValidator()
        : this(new ProcessRunner())
    {
    }

    public AgentPackageRootValidator(ProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    private static readonly HashSet<string> AllowedKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "AddTextFile",
        "ModifyTextFile",
        "DeleteTextFile",
        "ReplaceTextFile",
        "PatchOnly"
    };

    private static readonly string[] GeneratedOutputPathPatterns =
    [
        "bin/",
        "obj/",
        "logs/",
        "artifacts/",
        "release/",
        ".dll",
        ".exe",
        ".pdb",
        ".rpack"
    ];

    private static readonly string[] SecretMarkers =
    [
        "BEGIN PRIVATE KEY",
        "api_key",
        "apikey",
        "secret",
        "token",
        "password"
    ];

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".md",
        ".json",
        ".ps1",
        ".csproj",
        ".sln",
        ".slnx",
        ".xml",
        ".txt",
        ".yml",
        ".yaml"
    };

    public RpackResult Validate(string packageRoot)
    {
        return RpackResultMapper.ToLegacyResult(ValidateDetailed(packageRoot));
    }

    public RpackOperationResult ValidateDetailed(string packageRoot)
    {
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            return Fail(
                "package.root-missing",
                RpackStage.PackageOpen,
                "Package root is missing.",
                "Provide a package root path.");
        }

        var fullRoot = Path.GetFullPath(packageRoot);
        if (!Directory.Exists(fullRoot))
        {
            return Fail(
                "package.root-not-directory",
                RpackStage.PackageOpen,
                "Package root does not exist or is not a directory.",
                "Point the command at an existing package root.");
        }

        var reader = new AgentPackageReader();
        var manifestPath = Path.Combine(fullRoot, AgentPackageReader.ManifestPath);
        if (!File.Exists(manifestPath))
        {
            return Fail(
                "manifest.missing",
                RpackStage.PackageOpen,
                "manifest.json is missing.",
                "Add manifest.json to the package root.",
                packagePath: AgentPackageReader.ManifestPath);
        }

        AgentPackageManifest manifest;
        try
        {
            manifest = reader.ReadManifest(fullRoot);
        }
        catch (Exception ex)
        {
            return Fail(
                "manifest.read-failed",
                RpackStage.ManifestRead,
                ex.Message,
                "Fix the manifest.json payload and retry.",
                packagePath: AgentPackageReader.ManifestPath,
                rawDetails: ex.ToString());
        }

        if (manifest.Format != "rpack-agent-package")
        {
            return Fail(
                "manifest.unsupported-format",
                RpackStage.ManifestValidation,
                $"Unsupported package format: {manifest.Format}",
                "Use the rpack-agent-package manifest format.",
                packagePath: AgentPackageReader.ManifestPath);
        }

        if (manifest.SchemaVersion != "1.0")
        {
            return Fail(
                "manifest.unsupported-schema",
                RpackStage.ManifestValidation,
                $"Unsupported package schema version: {manifest.SchemaVersion}",
                "Regenerate the package with schema version 1.0.",
                packagePath: AgentPackageReader.ManifestPath);
        }

        if (string.IsNullOrWhiteSpace(manifest.Id)
            || string.IsNullOrWhiteSpace(manifest.Title)
            || string.IsNullOrWhiteSpace(manifest.CreatedAtUtc)
            || string.IsNullOrWhiteSpace(manifest.OperationsPath)
            || string.IsNullOrWhiteSpace(manifest.DefaultApplyStrategy))
        {
            return Fail(
                "agent.metadata-missing",
                RpackStage.ManifestValidation,
                "Manifest is missing required fields.",
                "Populate the required manifest fields and retry.",
                packagePath: AgentPackageReader.ManifestPath);
        }

        var repairsOperationsCount = manifest.RepairsOperations?.Count ?? 0;
        if (!string.IsNullOrWhiteSpace(manifest.RepairsPackageId) || repairsOperationsCount > 0)
        {
            if (string.IsNullOrWhiteSpace(manifest.RepairsPackageId) || repairsOperationsCount == 0)
            {
                return Fail(
                    "manifest.repair-metadata-incomplete",
                    RpackStage.ManifestValidation,
                    "Repair manifest metadata is incomplete.",
                    "Set both RepairsPackageId and RepairsOperations.",
                    packagePath: AgentPackageReader.ManifestPath);
            }
        }

        var operationsPath = Path.Combine(fullRoot, manifest.OperationsPath);
        if (!File.Exists(operationsPath))
        {
            return Fail(
                "package.entry-missing",
                RpackStage.PatchRead,
                $"Missing operations file: {manifest.OperationsPath}",
                "Add the operations file referenced by the manifest.",
                packagePath: manifest.OperationsPath);
        }

        AgentOperationDocument operations;
        try
        {
            operations = reader.ReadOperations(fullRoot, manifest.OperationsPath);
        }
        catch (Exception ex)
        {
            return Fail(
                "operations.read-failed",
                RpackStage.PatchRead,
                ex.Message,
                "Fix the operations file and retry.",
                packagePath: manifest.OperationsPath,
                rawDetails: ex.ToString());
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var operation in operations.Operations)
        {
            if (string.IsNullOrWhiteSpace(operation.Id) || !seenIds.Add(operation.Id))
            {
                return Fail(
                    "operation.id-invalid",
                    RpackStage.ManifestValidation,
                    "Duplicate or missing operation id.",
                    "Ensure each operation has a unique non-empty Id.",
                    packagePath: manifest.OperationsPath);
            }

            if (string.IsNullOrWhiteSpace(operation.Kind) || !AllowedKinds.Contains(operation.Kind))
            {
                return Fail(
                    "operation.kind-unsupported",
                    RpackStage.ManifestValidation,
                    $"Unsupported operation kind: {operation.Kind}",
                    "Use one of the supported operation kinds.",
                    packagePath: manifest.OperationsPath);
            }

            if (string.IsNullOrWhiteSpace(operation.Path) || !IsSafeRelativePath(operation.Path) || !seenPaths.Add(operation.Path))
            {
                var isDuplicatePath = !string.IsNullOrWhiteSpace(operation.Path) && seenPaths.Contains(operation.Path);
                return Fail(
                    isDuplicatePath ? "agent.operation-duplicate-path" : "operation.path-invalid",
                    RpackStage.ManifestValidation,
                    $"Invalid or duplicate operation path: {operation.Path}",
                    "Use a unique safe relative path for each operation.",
                    packagePath: manifest.OperationsPath,
                    filePath: operation.Path);
            }

            if (!string.IsNullOrWhiteSpace(operation.PayloadPath))
            {
                var payloadFullPath = Path.Combine(fullRoot, operation.PayloadPath);
                if (!File.Exists(payloadFullPath))
                {
                    return Fail(
                        "payload.missing",
                        RpackStage.PayloadValidation,
                        $"Missing payload file: {operation.PayloadPath}",
                        "Add the payload file referenced by the operation.",
                        packagePath: operation.PayloadPath,
                        filePath: operation.Path);
                }

                if (string.IsNullOrWhiteSpace(operation.TargetSha256))
                {
                    return Fail(
                        "payload.target-hash-missing",
                        RpackStage.PayloadValidation,
                        $"Missing target hash for payload-backed operation: {operation.Id}",
                        "Declare TargetSha256 for payload-backed operations.",
                        packagePath: operation.PayloadPath,
                        filePath: operation.Path);
                }

                var payloadHash = Sha256.ForBytes(File.ReadAllBytes(payloadFullPath));
                if (!string.Equals(payloadHash, operation.TargetSha256, StringComparison.OrdinalIgnoreCase))
                {
                    return Fail(
                        "agent.payload-hash-mismatch",
                        RpackStage.PayloadValidation,
                        $"Payload hash mismatch for operation {operation.Id}.",
                        "Regenerate the payload from the current source content.",
                        packagePath: operation.PayloadPath,
                        filePath: operation.Path,
                        rawDetails: $"expected={operation.TargetSha256};actual={payloadHash}");
                }
            }

            if (!string.IsNullOrWhiteSpace(operation.PatchPath) && !File.Exists(Path.Combine(fullRoot, operation.PatchPath)))
            {
                return Fail(
                    "patch.missing",
                    RpackStage.PatchRead,
                    $"Missing patch file: {operation.PatchPath}",
                    "Add the patch file referenced by the operation.",
                    packagePath: operation.PatchPath,
                    patchPath: operation.PatchPath,
                    filePath: operation.Path);
            }
        }

        var risk = ScanPackageRootRisks(fullRoot, manifestPath, operationsPath);
        if (!risk.Success)
        {
            return risk;
        }

        var declaredFilesResult = ValidateDeclaredFiles(manifest, operations);
        if (!declaredFilesResult.Success)
        {
            return declaredFilesResult;
        }

        var validationResult = RunValidationCommands(fullRoot, manifest.Validation);
        if (!validationResult.Success)
        {
            return validationResult;
        }

        return RpackOperationResult.Ok("Agent package root is valid.");
    }

    private static RpackOperationResult Fail(
        string code,
        RpackStage stage,
        string message,
        string suggestion,
        string? packagePath = null,
        string? patchPath = null,
        string? filePath = null,
        string? rawDetails = null)
    {
        var issue = new RpackIssue(
            code,
            RpackSeverity.Error,
            stage,
            message,
            suggestion,
            packagePath,
            patchPath,
            filePath,
            rawDetails);

        return RpackOperationResult.Fail(message, [issue]);
    }

    private static bool IsSafeRelativePath(string path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && !Path.IsPathRooted(path)
            && !path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Contains("..");
    }

    private static RpackOperationResult ScanPackageRootRisks(string fullRoot, string manifestPath, string operationsPath)
    {
        foreach (var file in Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(file, manifestPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(file, operationsPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(fullRoot, file).Replace('\\', '/');
            if (IsGeneratedOutputPath(relativePath))
            {
                return Fail(
                    "agent.risk-generated-output",
                    RpackStage.PayloadValidation,
                    $"Generated output path is not allowed in package-root: {relativePath}",
                    "Remove generated outputs, logs, or build artifacts from the package-root.",
                    packagePath: relativePath,
                filePath: relativePath);
            }

            var bytes = File.ReadAllBytes(file);
            if (LooksBinaryContent(bytes))
            {
                return Fail(
                    "agent.risk-binary-change",
                    RpackStage.PayloadValidation,
                    $"Package-root contains binary content: {relativePath}",
                    "Remove binary files from the package-root or move them to a safer packaging flow.",
                    packagePath: relativePath,
                    filePath: relativePath);
            }

            if (!LooksTextLike(relativePath))
            {
                continue;
            }

            var content = Encoding.UTF8.GetString(bytes);
            var secretMarker = SecretMarkers.FirstOrDefault(marker => content.Contains(marker, StringComparison.OrdinalIgnoreCase));
            if (secretMarker is not null)
            {
                return Fail(
                    "agent.risk-secret-marker",
                    RpackStage.PayloadValidation,
                    $"Package-root file contains a likely secret marker: {relativePath}",
                    "Remove secrets from payloads, patches, and metadata before packaging.",
                    packagePath: relativePath,
                    filePath: relativePath,
                    rawDetails: secretMarker);
            }
        }

        return RpackOperationResult.Ok("Package-root security scan passed.");
    }

    private static bool IsGeneratedOutputPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').ToLowerInvariant();
        foreach (var pattern in GeneratedOutputPathPatterns)
        {
            if (pattern.EndsWith("/", StringComparison.Ordinal))
            {
                if (normalized.StartsWith(pattern, StringComparison.Ordinal) || normalized.Contains($"/{pattern}", StringComparison.Ordinal))
                {
                    return true;
                }

                continue;
            }

            if (normalized.EndsWith(pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksTextLike(string relativePath)
    {
        return TextExtensions.Contains(Path.GetExtension(relativePath));
    }

    private static bool LooksBinaryContent(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return false;
        }

        var suspicious = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            if (b == 0)
            {
                return true;
            }

            if ((b < 0x09) || (b > 0x0D && b < 0x20))
            {
                suspicious++;
            }
        }

        return suspicious > Math.Max(8, bytes.Length / 10);
    }

    private static RpackOperationResult ValidateDeclaredFiles(AgentPackageManifest manifest, AgentOperationDocument operations)
    {
        var declaredFiles = manifest.DeclaredFiles ?? [];
        if (declaredFiles.Count == 0)
        {
            return RpackOperationResult.Ok("Declared files are valid.");
        }

        var declared = declaredFiles
            .Select(NormalizeDeclaredPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actual = (operations.Operations ?? [])
            .Select(operation => NormalizeDeclaredPath(operation.Path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!declared.SetEquals(actual))
        {
            var missing = declared.Except(actual, StringComparer.OrdinalIgnoreCase).ToArray();
            var extra = actual.Except(declared, StringComparer.OrdinalIgnoreCase).ToArray();
            return Fail(
                "agent.declared-files-mismatch",
                RpackStage.ManifestValidation,
                "Declared files do not match operations.",
                "Keep DeclaredFiles aligned with the operation paths.",
                rawDetails: $"missing=[{string.Join(",", missing)}];extra=[{string.Join(",", extra)}]");
        }

        return RpackOperationResult.Ok("Declared files are valid.");
    }

    private RpackOperationResult RunValidationCommands(string packageRoot, IReadOnlyList<AgentPackageValidationCommand> validationCommands)
    {
        validationCommands ??= [];
        foreach (var command in validationCommands)
        {
            if (string.IsNullOrWhiteSpace(command.Command))
            {
                return Fail(
                    "agent.validation-command-failed",
                    RpackStage.ManifestValidation,
                    $"Validation command `{command.Name}` is empty.",
                    "Provide a validation command or remove the entry.",
                    rawDetails: command.Name);
            }

            var result = OperatingSystem.IsWindows()
                ? _processRunner.Run("cmd.exe", ["/c", command.Command], packageRoot)
                : _processRunner.Run("/bin/sh", ["-c", command.Command], packageRoot);

            if (result.Success)
            {
                continue;
            }

            if (command.Optional)
            {
                continue;
            }

            return Fail(
                "agent.validation-command-failed",
                RpackStage.ManifestValidation,
                $"Validation command `{command.Name}` failed.",
                "Fix the validation command or remove it from the manifest.",
                rawDetails: result.CombinedOutput);
        }

        return RpackOperationResult.Ok("Validation commands passed.");
    }

    private static string NormalizeDeclaredPath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? ""
            : path.Replace('\\', '/').Trim().Trim('/');
    }
}
