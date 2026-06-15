using System.Text.Json;
using Rpack.Core;
using Rpack.Core.Issues;
using Rpack.Core.Results;

namespace Rpack.AgentPackages;

public sealed class AgentPackageUndoer
{
    public RpackResult Undo(string targetRepositoryPath)
    {
        return RpackResultMapper.ToLegacyResult(UndoDetailed(targetRepositoryPath));
    }

    public RpackResult Undo(string targetRepositoryPath, bool force)
    {
        return RpackResultMapper.ToLegacyResult(UndoDetailed(targetRepositoryPath, force));
    }

    public RpackOperationResult UndoDetailed(string targetRepositoryPath, bool force = false)
    {
        var gitClient = new GitClient(new ProcessRunner());
        var repository = gitClient.InspectRepository(targetRepositoryPath);
        var latestJournal = FindLatestJournal(repository.StatePath);
        if (latestJournal is null)
        {
            return RpackOperationResult.Fail(
                "No agent package journal found.",
                [new RpackIssue(
                    "state.journal-missing",
                    RpackSeverity.Error,
                    RpackStage.Undo,
                    "No agent package journal found.",
                    "Apply an agent package before attempting undo.",
                    RawDetails: "No agent package journal found.")]);
        }

        foreach (var operation in latestJournal.Operations.AsEnumerable().Reverse())
        {
            var targetPath = Path.Combine(repository.RootPath, operation.Path.Replace('/', Path.DirectorySeparatorChar));
            var backupPath = string.IsNullOrWhiteSpace(operation.BackupPath)
                ? ""
                : Path.Combine(repository.StatePath, operation.BackupPath.Replace('/', Path.DirectorySeparatorChar));
            var currentHash = File.Exists(targetPath)
                ? Sha256.ForBytes(File.ReadAllBytes(targetPath))
                : "";

            if (!string.IsNullOrWhiteSpace(operation.AfterSha256)
                && !string.Equals(currentHash, operation.AfterSha256, StringComparison.OrdinalIgnoreCase))
            {
                if (force)
                {
                    // Proceed with undo even if the target changed.
                }
                else
                {
                return RpackOperationResult.Fail(
                    $"Target file changed after apply for operation {operation.OperationId}.",
                    [new RpackIssue(
                        "state.target-changed-after-apply",
                        RpackSeverity.Error,
                        RpackStage.Undo,
                        $"Target file changed after apply for operation {operation.OperationId}.",
                        "Reapply the package or use a forced undo flow.",
                        PackagePath: operation.Path,
                        RawDetails: $"Target file changed after apply for operation {operation.OperationId}.")]);
                }
            }

            if (operation.ExistedBefore)
            {
                if (!File.Exists(backupPath))
                {
                    if (force)
                    {
                        if (File.Exists(targetPath))
                        {
                            File.Delete(targetPath);
                        }

                        continue;
                    }

                    return RpackOperationResult.Fail(
                        $"Missing backup for operation {operation.OperationId}.",
                        [new RpackIssue(
                            "state.backup-missing",
                            RpackSeverity.Error,
                            RpackStage.Undo,
                            $"Missing backup for operation {operation.OperationId}.",
                            "Restore the missing backup from the operation journal before undoing.",
                            PackagePath: operation.BackupPath,
                            RawDetails: $"Missing backup for operation {operation.OperationId}.")]);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(backupPath, targetPath, overwrite: true);
                continue;
            }

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
        }

        latestJournal.CompletedAtUtc = DateTimeOffset.UtcNow.ToString("O");
        latestJournal.UndoneAtUtc = DateTimeOffset.UtcNow.ToString("O");
        WriteJournal(repository.StatePath, latestJournal);
        return RpackOperationResult.Ok($"Undid agent package apply {latestJournal.ApplyId}.");
    }

    public static IReadOnlySet<string> ReadAppliedPaths(string statePath)
    {
        var journalRoot = Path.Combine(statePath, "operation-journal");
        if (!Directory.Exists(journalRoot))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var journalPath in Directory.EnumerateFiles(journalRoot, "journal.json", SearchOption.AllDirectories))
        {
            using var stream = File.OpenRead(journalPath);
            var journal = JsonSerializer.Deserialize(stream, AgentPackageJournalJsonContext.Default.AgentPackageJournal);
            if (journal is null)
            {
                continue;
            }

            foreach (var operation in journal.Operations)
            {
                if (!string.IsNullOrWhiteSpace(operation.Path))
                {
                    paths.Add(operation.Path.Replace('\\', '/'));
                }
            }
        }

        return paths;
    }

    private static AgentPackageJournal? FindLatestJournal(string statePath)
    {
        var journalRoot = Path.Combine(statePath, "operation-journal");
        if (!Directory.Exists(journalRoot))
        {
            return null;
        }

        var journals = Directory.EnumerateFiles(journalRoot, "journal.json", SearchOption.AllDirectories)
            .Select(path =>
            {
                using var stream = File.OpenRead(path);
                var journal = JsonSerializer.Deserialize(stream, AgentPackageJournalJsonContext.Default.AgentPackageJournal);
                return (Path: path, Journal: journal);
            })
            .Where(entry => entry.Journal is not null)
            .Select(entry => entry.Journal!)
            .OrderByDescending(journal => journal.StartedAtUtc, StringComparer.Ordinal)
            .ToArray();

        return journals.FirstOrDefault();
    }

    private static void WriteJournal(string statePath, AgentPackageJournal journal)
    {
        var journalDirectory = Path.Combine(statePath, "operation-journal", journal.ApplyId);
        Directory.CreateDirectory(journalDirectory);
        var path = Path.Combine(journalDirectory, "journal.json");
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, journal, AgentPackageJournalJsonContext.Default.AgentPackageJournal);
    }
}

public sealed class AgentPackageJournal
{
    public string ApplyId { get; set; } = "";
    public string PackageId { get; set; } = "";
    public string PackageFormat { get; set; } = "";
    public string StartedAtUtc { get; set; } = "";
    public string CompletedAtUtc { get; set; } = "";
    public string UndoneAtUtc { get; set; } = "";
    public string TargetRepository { get; set; } = "";
    public List<AgentJournalOperation> Operations { get; set; } = [];
}

public sealed class AgentJournalOperation
{
    public string OperationId { get; set; } = "";
    public string Path { get; set; } = "";
    public string Method { get; set; } = "";
    public string Status { get; set; } = "";
    public bool ExistedBefore { get; set; }
    public string BeforeSha256 { get; set; } = "";
    public string AfterSha256 { get; set; } = "";
    public string BackupPath { get; set; } = "";

    public static AgentJournalOperation Applied(AgentOperation operation, string status, bool existedBefore)
    {
        return new AgentJournalOperation
        {
            OperationId = operation.Id,
            Path = operation.Path,
            Method = operation.Kind,
            Status = status,
            ExistedBefore = existedBefore
        };
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(AgentPackageJournal))]
internal sealed partial class AgentPackageJournalJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
