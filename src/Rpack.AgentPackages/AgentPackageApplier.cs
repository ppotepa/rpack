using System.Text.Json;
using Rpack.Core;

namespace Rpack.AgentPackages;

public sealed class AgentPackageApplier
{
    private readonly AgentPackageReader _reader = new();
    private readonly PayloadStoreReader _payloadStore = new();
    private readonly PayloadOperationApplier _payloadApplier = new();

    public RpackResult Apply(string packageRoot, string targetRepositoryPath)
    {
        return Apply(packageRoot, targetRepositoryPath, skippedPaths: null);
    }

    public RpackResult ApplyRemaining(string packageRoot, string targetRepositoryPath)
    {
        var repository = new GitClient(new ProcessRunner()).InspectRepository(targetRepositoryPath);
        var skippedPaths = AgentPackageUndoer.ReadAppliedPaths(repository.StatePath);
        return Apply(packageRoot, targetRepositoryPath, skippedPaths);
    }

    private RpackResult Apply(string packageRoot, string targetRepositoryPath, IReadOnlySet<string>? skippedPaths)
    {
        var validator = new AgentPackageRootValidator();
        var validation = validator.Validate(packageRoot);
        if (!validation.Success)
        {
            return validation;
        }

        var root = Path.GetFullPath(packageRoot);
        var targetRoot = Path.GetFullPath(targetRepositoryPath);
        var manifest = _reader.ReadManifest(root);
        var operations = _reader.ReadOperations(root, manifest.OperationsPath);
        var planBuilder = new AgentApplyPlanBuilder();
        var plan = planBuilder.BuildPlan(root, targetRoot, skippedPaths);
        if (!plan.Success)
        {
            return RpackResult.Fail(plan.ErrorMessage);
        }

        var repository = new GitClient(new ProcessRunner()).InspectRepository(targetRoot);
        var journal = new AgentPackageJournal
        {
            ApplyId = CreateApplyId(),
            PackageId = manifest.Id,
            PackageFormat = manifest.Format,
            StartedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            TargetRepository = repository.RootPath,
            Operations = []
        };

        var journalStore = new AgentPackageJournalStore();
        var journalDirectory = journalStore.CreateJournalDirectory(repository, journal.ApplyId);
        try
        {
            foreach (var operation in operations.Operations)
            {
                var plannedOperations = plan.Operations ?? [];
                var operationPlan = plannedOperations.FirstOrDefault(candidate => string.Equals(candidate.Id, operation.Id, StringComparison.Ordinal));
                if (operationPlan is null)
                {
                    return RollbackAndFail(journalStore, journalDirectory, journal, $"Operation not found in plan: {operation.Id}");
                }

                if (operationPlan.Status is AgentOperationStatus.AlreadyApplied or AgentOperationStatus.SkippedOptional)
                {
                    journal.Operations.Add(AgentJournalOperation.Applied(operation, operationPlan.Status.ToString(), existedBefore: true));
                    continue;
                }

                if (operationPlan.Status is AgentOperationStatus.Unsupported
                    or AgentOperationStatus.ConflictBaseHashMismatch
                    or AgentOperationStatus.ConflictExistingDifferentFile
                    or AgentOperationStatus.ConflictMissingFile
                    or AgentOperationStatus.ConflictPatchFailed
                    or AgentOperationStatus.ConflictUnsafePath
                    or AgentOperationStatus.ConflictDuplicateOperationPath)
                {
                    return RollbackAndFail(journalStore, journalDirectory, journal, $"{operation.Id}: {operationPlan.Message}");
                }

                var targetPath = ResolveTargetPath(repository.RootPath, operation.Path);
                var existedBefore = File.Exists(targetPath);
                var beforeHash = existedBefore ? Sha256.ForBytes(File.ReadAllBytes(targetPath)) : "";
                var backupPath = Path.Combine(journalDirectory, "backups", operation.Id, "original");
                if (existedBefore)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    File.Copy(targetPath, backupPath, overwrite: true);
                }

                try
                {
                    ApplyOperation(root, operation, targetPath);
                    var afterHash = File.Exists(targetPath) ? Sha256.ForBytes(File.ReadAllBytes(targetPath)) : "";
                    journal.Operations.Add(new AgentJournalOperation
                    {
                        OperationId = operation.Id,
                        Path = operation.Path,
                        Method = ResolveMethod(operation.Kind),
                        Status = "Applied",
                        ExistedBefore = existedBefore,
                        BeforeSha256 = beforeHash,
                        AfterSha256 = afterHash,
                        BackupPath = existedBefore ? RelativeJournalPath(repository, backupPath) : ""
                    });
                }
                catch (Exception ex)
                {
                    RestoreOperation(targetPath, existedBefore, backupPath);
                    RollbackAppliedOperations(repository, journal);
                    return RollbackAndFail(journalStore, journalDirectory, journal, $"{operation.Id}: {ex.Message}");
                }
            }

            journal.CompletedAtUtc = DateTimeOffset.UtcNow.ToString("O");
            journalStore.WriteJournal(journalDirectory, journal);
            return RpackResult.Ok($"Agent package applied. Apply id: {journal.ApplyId}");
        }
        catch (Exception ex)
        {
            return RollbackAndFail(journalStore, journalDirectory, journal, ex.Message);
        }
    }

    private static string ResolveMethod(string kind)
    {
        return kind switch
        {
            "AddTextFile" => "Payload",
            "ModifyTextFile" => "Payload",
            "ReplaceTextFile" => "Payload",
            "DeleteTextFile" => "Delete",
            "PatchOnly" => "Patch",
            _ => "Unknown"
        };
    }

    private void ApplyOperation(string packageRoot, AgentOperation operation, string targetPath)
    {
        switch (operation.Kind)
        {
            case "AddTextFile":
            case "ModifyTextFile":
            case "ReplaceTextFile":
                if (string.IsNullOrWhiteSpace(operation.PayloadPath))
                {
                    throw new InvalidOperationException("Missing payload path.");
                }

                var payloadBytes = _payloadStore.ReadPayload(packageRoot, operation.PayloadPath);
                var payloadHash = Sha256.ForBytes(payloadBytes);
                if (!string.IsNullOrWhiteSpace(operation.TargetSha256)
                    && !string.Equals(payloadHash, operation.TargetSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Payload hash mismatch.");
                }

                var result = _payloadApplier.ApplyPayload(targetPath, payloadBytes);
                if (!result.Success)
                {
                    throw new InvalidOperationException(result.Message);
                }

                if (!string.IsNullOrWhiteSpace(operation.TargetSha256))
                {
                    var afterHash = Sha256.ForBytes(File.ReadAllBytes(targetPath));
                    if (!string.Equals(afterHash, operation.TargetSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Applied payload did not match declared target hash.");
                    }
                }

                return;

            case "DeleteTextFile":
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                return;

            default:
                throw new InvalidOperationException($"Unsupported operation kind: {operation.Kind}");
        }
    }

    private static void RestoreOperation(string targetPath, bool existedBefore, string backupPath)
    {
        if (existedBefore && File.Exists(backupPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(backupPath, targetPath, overwrite: true);
            return;
        }

        if (!existedBefore && File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }
    }

    private static void RollbackAppliedOperations(GitRepository repository, AgentPackageJournal journal)
    {
        for (var i = journal.Operations.Count - 1; i >= 0; i--)
        {
            var operation = journal.Operations[i];
            var targetPath = ResolveTargetPath(repository.RootPath, operation.Path);
            if (operation.ExistedBefore)
            {
                if (string.IsNullOrWhiteSpace(operation.BackupPath))
                {
                    continue;
                }

                var backupPath = Path.Combine(repository.StatePath, operation.BackupPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(backupPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    File.Copy(backupPath, targetPath, overwrite: true);
                }

                continue;
            }

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
        }
    }

    private static RpackResult RollbackAndFail(AgentPackageJournalStore journalStore, string journalDirectory, AgentPackageJournal journal, string message)
    {
        journal.CompletedAtUtc = DateTimeOffset.UtcNow.ToString("O");
        journalStore.WriteJournal(journalDirectory, journal);
        return RpackResult.Fail(message);
    }

    private static string ResolveTargetPath(string repositoryRoot, string relativePath)
    {
        if (Path.IsPathRooted(relativePath) || relativePath.Contains(':'))
        {
            throw new InvalidOperationException($"Unsafe target path: {relativePath}");
        }

        var full = Path.GetFullPath(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var normalizedRoot = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(normalizedRoot, comparison))
        {
            throw new InvalidOperationException($"Unsafe target path: {relativePath}");
        }

        return full;
    }

    private static string RelativeJournalPath(GitRepository repository, string fullPath)
    {
        return Path.GetRelativePath(repository.StatePath, fullPath).Replace('\\', '/');
    }

    private static string CreateApplyId()
    {
        return $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
    }
}

public sealed class AgentPackageJournalStore
{
    public string CreateJournalDirectory(GitRepository repository, string applyId)
    {
        var directory = Path.Combine(repository.StatePath, "operation-journal", applyId);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public void WriteJournal(string journalDirectory, AgentPackageJournal journal)
    {
        Directory.CreateDirectory(journalDirectory);
        var path = Path.Combine(journalDirectory, "journal.json");
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, journal, AgentPackageJournalJsonContext.Default.AgentPackageJournal);
    }
}
