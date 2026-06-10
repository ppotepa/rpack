namespace Rpack.Core;

internal sealed class RpackActionRunner
{
    private readonly GitClient _gitClient;
    private readonly ProcessRunner _processRunner;

    public RpackActionRunner(GitClient gitClient, ProcessRunner processRunner)
    {
        _gitClient = gitClient;
        _processRunner = processRunner;
    }

    public RpackActionResult Run(
        string stage,
        RpackAction action,
        string repositoryPath,
        string? extractedActionPath,
        string applyId,
        string packageId,
        string packageTitle,
        IReadOnlyList<string> changedFiles)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            var normalizedKind = NormalizeKind(action.Kind);
            if (normalizedKind == "rpack.commit")
            {
                return RunCommitAction(stage, action, repositoryPath, applyId, packageId, packageTitle, changedFiles, started);
            }

            var command = BuildCommand(normalizedKind, action, extractedActionPath);
            if (command is null)
            {
                return BuildResult(stage, action, false, -1, $"Unsupported action kind: {action.Kind}", "", "", started);
            }

            var environment = BuildEnvironment(stage, applyId, packageId, packageTitle, repositoryPath, changedFiles);
            var result = _processRunner.Run(command.Value.FileName, command.Value.Arguments, repositoryPath, environment);
            return BuildResult(
                stage,
                action,
                result.Success || action.Optional,
                result.ExitCode,
                result.Success ? "Action completed." : result.CombinedOutput,
                result.StandardOutput,
                result.StandardError,
                started);
        }
        catch (Exception ex)
        {
            return BuildResult(stage, action, action.Optional, -1, ex.Message, "", ex.ToString(), started);
        }
    }

    private RpackActionResult RunCommitAction(
        string stage,
        RpackAction action,
        string repositoryPath,
        string applyId,
        string packageId,
        string packageTitle,
        IReadOnlyList<string> changedFiles,
        DateTimeOffset started)
    {
        if (!string.Equals(stage, "post", StringComparison.OrdinalIgnoreCase))
        {
            return BuildResult(stage, action, action.Optional, -1, "rpack.commit is only supported as a PostAction.", "", "", started);
        }

        var stageResult = _gitClient.StagePaths(repositoryPath, changedFiles);
        if (!stageResult.Success)
        {
            return BuildResult(stage, action, action.Optional, -1, stageResult.Message, "", stageResult.Message, started);
        }

        var message = string.IsNullOrWhiteSpace(action.Message)
            ? BuildDefaultCommitMessage(packageId, packageTitle, applyId)
            : ExpandActionMessage(action.Message, packageId, packageTitle, applyId);
        var commit = _gitClient.Commit(repositoryPath, message);
        if (!commit.Success)
        {
            return BuildResult(stage, action, action.Optional, -1, commit.Message, "", commit.Message, started);
        }

        var commitSha = _gitClient.GetHeadCommit(repositoryPath);
        return BuildResult(stage, action, true, 0, $"Commit created: {commitSha}", commitSha, "", started);
    }

    private static RpackActionResult BuildResult(
        string stage,
        RpackAction action,
        bool success,
        int exitCode,
        string message,
        string stdout,
        string stderr,
        DateTimeOffset started)
    {
        return new RpackActionResult
        {
            Stage = stage,
            Name = action.Name,
            Kind = action.Kind,
            Success = success,
            Optional = action.Optional,
            ExitCode = exitCode,
            Message = message,
            StandardOutput = stdout,
            StandardError = stderr,
            StartedAtUtc = started.ToString("O"),
            FinishedAtUtc = DateTimeOffset.UtcNow.ToString("O")
        };
    }

    private static (string FileName, string[] Arguments)? BuildCommand(string kind, RpackAction action, string? extractedActionPath)
    {
        if (kind == "powershell")
        {
            return string.IsNullOrWhiteSpace(extractedActionPath)
                ? null
                : ("powershell", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", extractedActionPath]);
        }

        if (kind == "batch")
        {
            if (string.IsNullOrWhiteSpace(extractedActionPath))
            {
                return null;
            }

            return OperatingSystem.IsWindows()
                ? ("cmd", ["/c", extractedActionPath])
                : ("/bin/sh", [extractedActionPath]);
        }

        if (kind == "command")
        {
            if (string.IsNullOrWhiteSpace(action.Command))
            {
                return null;
            }

            return OperatingSystem.IsWindows()
                ? ("cmd", ["/c", action.Command])
                : ("/bin/sh", ["-c", action.Command]);
        }

        return null;
    }

    private static Dictionary<string, string> BuildEnvironment(
        string stage,
        string applyId,
        string packageId,
        string packageTitle,
        string repositoryPath,
        IReadOnlyList<string> changedFiles)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RPACK_ACTION_STAGE"] = stage,
            ["RPACK_APPLY_ID"] = applyId,
            ["RPACK_PACKAGE_ID"] = packageId,
            ["RPACK_PACKAGE_TITLE"] = packageTitle,
            ["RPACK_REPOSITORY_PATH"] = repositoryPath,
            ["RPACK_CHANGED_FILES"] = string.Join(Path.PathSeparator, changedFiles)
        };
    }

    private static string BuildDefaultCommitMessage(string packageId, string packageTitle, string applyId)
    {
        var title = string.IsNullOrWhiteSpace(packageTitle) ? packageId : packageTitle;
        return $"rpack: apply {title}{Environment.NewLine}{Environment.NewLine}Package: {packageId}{Environment.NewLine}ApplyId: {applyId}";
    }

    private static string ExpandActionMessage(string message, string packageId, string packageTitle, string applyId)
    {
        return message
            .Replace("{PackageId}", packageId, StringComparison.Ordinal)
            .Replace("{PackageTitle}", packageTitle, StringComparison.Ordinal)
            .Replace("{ApplyId}", applyId, StringComparison.Ordinal);
    }

    public static string NormalizeKind(string kind)
    {
        return kind.Trim().ToLowerInvariant() switch
        {
            "ps1" => "powershell",
            "powershell" => "powershell",
            "bat" => "batch",
            "cmd" => "batch",
            "batch" => "batch",
            "command" => "command",
            "shell" => "command",
            "rpack.commit" => "rpack.commit",
            var value => value
        };
    }
}
