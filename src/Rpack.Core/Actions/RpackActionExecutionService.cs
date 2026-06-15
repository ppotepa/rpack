using System.IO.Compression;
using System.Text;
using Rpack.Core;
using Rpack.Core.Patches;

namespace Rpack.Core.Actions;

public sealed class RpackActionExecutionService
{
    private readonly GitClient _gitClient;

    public RpackActionExecutionService()
        : this(new GitClient(new ProcessRunner()))
    {
    }

    public RpackActionExecutionService(GitClient gitClient)
    {
        _gitClient = gitClient;
    }

    public ActionExecutionSummary RunActions(
        string stage,
        IReadOnlyList<RpackAction> actions,
        TempActionSet actionSet,
        string repositoryPath,
        RpackManifest manifest,
        string applyId,
        IReadOnlyList<string> changedFiles,
        IReadOnlyList<int>? selectedActionIndices,
        Action<RpackActionResult>? onActionExecuted)
    {
        var results = new List<RpackActionResult>();
        var selected = selectedActionIndices is null
            ? null
            : new HashSet<int>(selectedActionIndices);
        var runner = new RpackActionRunner(_gitClient, new ProcessRunner());
        for (var i = 0; i < actions.Count; i++)
        {
            if (selected is not null && !selected.Contains(i))
            {
                continue;
            }

            var action = actions[i];
            string? extractedActionPath = null;
            if (!string.IsNullOrWhiteSpace(action.Path))
            {
                actionSet.ActionPaths.TryGetValue(action.Path, out extractedActionPath);
            }

            var result = runner.Run(
                stage,
                action,
                repositoryPath,
                extractedActionPath,
                applyId,
                manifest.Id,
                manifest.Title,
                changedFiles);
            onActionExecuted?.Invoke(result);
            results.Add(result);
            if (!result.Success && !action.Optional)
            {
                return new ActionExecutionSummary(false, results);
            }
        }

        return new ActionExecutionSummary(true, results);
    }

    public TempActionSet ExtractActionsToTempDirectory(ZipArchive archive, IReadOnlyList<RpackAction> actions)
    {
        var actionsWithPaths = actions
            .Where(action => !string.IsNullOrWhiteSpace(action.Path))
            .GroupBy(action => action.Path, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        if (actionsWithPaths.Length == 0)
        {
            return new TempActionSet("", new Dictionary<string, string>(StringComparer.Ordinal));
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"rpack-actions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        var actionPaths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var action in actionsWithPaths)
        {
            if (!IsSafeArchivePath(action.Path))
            {
                throw new InvalidOperationException($"Unsafe action path: {action.Path}");
            }

            var entry = archive.GetEntry(action.Path) ?? throw new InvalidOperationException($"Action script is missing: {action.Path}");
            var tempPath = Path.Combine(tempDirectory, action.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
            entry.ExtractToFile(tempPath, overwrite: true);
            actionPaths[action.Path] = tempPath;
        }

        return new TempActionSet(tempDirectory, actionPaths);
    }

    public static string BuildActionFailureMessage(string prefix, IReadOnlyList<RpackActionResult> results)
    {
        var failed = results.FirstOrDefault(result => !result.Success && !result.Optional)
            ?? results.LastOrDefault(result => !result.Success)
            ?? results.LastOrDefault();
        if (failed is null)
        {
            return prefix;
        }

        var details = string.IsNullOrWhiteSpace(failed.Message)
            ? failed.StandardError
            : failed.Message;
        return $"{prefix}: {failed.Stage}/{failed.Name} ({failed.Kind}) exited {failed.ExitCode}.{Environment.NewLine}{details}".TrimEnd();
    }

    public static string BuildActionSuccessSuffix(IReadOnlyList<RpackActionResult> actionResults)
    {
        if (actionResults.Count == 0)
        {
            return "";
        }

        var commit = actionResults.LastOrDefault(result =>
            string.Equals(RpackActionRunner.NormalizeKind(result.Kind), "rpack.commit", StringComparison.OrdinalIgnoreCase)
            && result.Success
            && !string.IsNullOrWhiteSpace(result.StandardOutput));
        return commit is null
            ? $" Actions completed: {actionResults.Count}."
            : $" Actions completed: {actionResults.Count}. Commit: {commit.StandardOutput.Trim()}.";
    }

    private static bool IsSafeArchivePath(string path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && !Path.IsPathRooted(path)
            && !path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Contains("..");
    }
}

public sealed record ActionExecutionSummary(bool Success, IReadOnlyList<RpackActionResult> Results)
{
    public static ActionExecutionSummary SuccessOnly(IReadOnlyList<RpackActionResult> results) => new(true, results);
}

public sealed class TempActionSet : IDisposable
{
    public TempActionSet(string directoryPath, IReadOnlyDictionary<string, string> actionPaths)
    {
        DirectoryPath = directoryPath;
        ActionPaths = actionPaths;
    }

    public string DirectoryPath { get; }
    public IReadOnlyDictionary<string, string> ActionPaths { get; }

    public void Dispose()
    {
        if (!string.IsNullOrWhiteSpace(DirectoryPath) && Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
