namespace Rpack.Core;

public sealed class GitClient
{
    private readonly ProcessRunner _processRunner;

    public GitClient(ProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public RpackResult EnsureRepository(string repositoryPath)
    {
        var result = _processRunner.Run("git", ["rev-parse", "--show-toplevel"], repositoryPath);
        return result.Success
            ? RpackResult.Ok(result.StandardOutput.Trim())
            : RpackResult.Fail("Target path is not a Git repository.");
    }

    public GitRepository InspectRepository(string repositoryPath)
    {
        var root = _processRunner.Run("git", ["rev-parse", "--show-toplevel"], repositoryPath);
        if (!root.Success)
        {
            throw new InvalidOperationException("Target path is not a Git repository.");
        }

        var rootPath = Path.GetFullPath(root.StandardOutput.Trim());
        var state = _processRunner.Run("git", ["rev-parse", "--git-path", "rpack"], rootPath);
        if (!state.Success)
        {
            throw new InvalidOperationException(state.CombinedOutput);
        }

        var statePath = state.StandardOutput.Trim();
        if (!Path.IsPathRooted(statePath))
        {
            statePath = Path.GetFullPath(Path.Combine(rootPath, statePath));
        }

        return new GitRepository(rootPath, statePath);
    }

    public GitRepository? FindRepositoryFrom(string startPath)
    {
        var path = Path.GetFullPath(startPath);
        var directory = File.Exists(path)
            ? Path.GetDirectoryName(path)
            : path;

        while (!string.IsNullOrWhiteSpace(directory))
        {
            var root = _processRunner.Run("git", ["rev-parse", "--show-toplevel"], directory);
            if (root.Success)
            {
                return InspectRepository(directory);
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        return null;
    }

    public RpackResult EnsureCleanWorkingTree(string repositoryPath)
    {
        try
        {
            return GetChangedPaths(repositoryPath).Count == 0
                ? RpackResult.Ok("Working tree is clean.")
                : RpackResult.Fail("Working tree is not clean.");
        }
        catch (InvalidOperationException ex)
        {
            return RpackResult.Fail(ex.Message);
        }
    }

    public RpackResult EnsureCleanWorkingTreeExcept(string repositoryPath, IReadOnlyList<string> allowedPaths)
    {
        var allowed = allowedPaths
            .Select(NormalizeGitPath)
            .ToHashSet(StringComparer.Ordinal);
        var dirtyPaths = GetChangedPaths(repositoryPath)
            .Select(NormalizeGitPath)
            .Where(path => !allowed.Contains(path))
            .ToArray();

        return dirtyPaths.Length == 0
            ? RpackResult.Ok("Working tree is clean except allowed package paths.")
            : RpackResult.Fail("Working tree is not clean.");
    }

    public IReadOnlyList<string> GetChangedPaths(string repositoryPath)
    {
        RefreshIndex(repositoryPath);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        AddDiffPaths(repositoryPath, ["diff", "--name-only"], paths);
        AddDiffPaths(repositoryPath, ["diff", "--cached", "--name-only"], paths);

        var status = _processRunner.Run("git", ["status", "--porcelain", "--untracked-files=all"], repositoryPath);
        if (!status.Success)
        {
            throw new InvalidOperationException(status.CombinedOutput);
        }

        foreach (var path in status.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("?? ", StringComparison.Ordinal))
            .Select(ParseStatusPath)
            .Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            paths.Add(path);
        }

        return paths.ToArray();
    }

    private void AddDiffPaths(string repositoryPath, string[] args, HashSet<string> paths)
    {
        var result = _processRunner.Run("git", args, repositoryPath);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.CombinedOutput);
        }

        foreach (var path in result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r').Trim())
            .Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            paths.Add(path);
        }
    }

    private void RefreshIndex(string repositoryPath)
    {
        _processRunner.Run("git", ["update-index", "-q", "--refresh"], repositoryPath);
    }

    private static string ParseStatusPath(string statusLine)
    {
        if (statusLine.Length <= 3)
        {
            return "";
        }

        var path = statusLine[3..].Trim().Trim('"');
        var renameIndex = path.LastIndexOf(" -> ", StringComparison.Ordinal);
        return renameIndex >= 0
            ? path[(renameIndex + " -> ".Length)..].Trim().Trim('"')
            : path;
    }

    private static string NormalizeGitPath(string path)
    {
        return path.Replace('\\', '/').Trim();
    }

    public string ResolveCommit(string repositoryPath, string revision)
    {
        var result = _processRunner.Run("git", ["rev-parse", revision], repositoryPath);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.CombinedOutput);
        }

        return result.StandardOutput.Trim();
    }

    public string CreateDiff(string repositoryPath, string fromRevision, string toRevision)
    {
        var result = _processRunner.Run("git", ["diff", "--binary", fromRevision, toRevision], repositoryPath);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.CombinedOutput);
        }

        return result.StandardOutput;
    }

    public string CreateNoIndexDiff(string repositoryPath, string leftPath, string rightPath)
    {
        var result = _processRunner.Run("git", ["diff", "--binary", "--no-index", "--", leftPath, rightPath], repositoryPath);
        if (!result.Success && result.ExitCode != 1)
        {
            throw new InvalidOperationException(result.CombinedOutput);
        }

        return result.StandardOutput;
    }

    public string CreateWorkingTreeDiff(string repositoryPath)
    {
        var result = _processRunner.Run("git", ["diff", "--binary"], repositoryPath);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.CombinedOutput);
        }

        return result.StandardOutput;
    }

    public string CreateStagedDiff(string repositoryPath)
    {
        var result = _processRunner.Run("git", ["diff", "--binary", "--cached"], repositoryPath);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.CombinedOutput);
        }

        return result.StandardOutput;
    }

    public string CreateDiffStat(string repositoryPath, string patchPath)
    {
        var result = _processRunner.Run("git", ["apply", "--stat", patchPath], repositoryPath);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.CombinedOutput);
        }

        return result.StandardOutput;
    }

    public RpackResult CheckApply(string repositoryPath, string patchPath, bool ignoreSpaceChange = false)
    {
        var args = new List<string> { "apply", "--check", "--allow-empty" };
        if (ignoreSpaceChange)
        {
            args.Add("--ignore-space-change");
        }

        args.Add(patchPath);
        var result = _processRunner.Run("git", args, repositoryPath);
        return result.Success
            ? RpackResult.Ok("Patch can be applied.")
            : RpackResult.Fail(result.CombinedOutput);
    }

    public RpackResult CheckApply(string repositoryPath, IReadOnlyList<string> patchPaths, bool ignoreSpaceChange = false)
    {
        var args = new List<string> { "apply", "--check", "--allow-empty" };
        if (ignoreSpaceChange)
        {
            args.Add("--ignore-space-change");
        }

        args.AddRange(patchPaths);
        var result = _processRunner.Run("git", args, repositoryPath);
        return result.Success
            ? RpackResult.Ok("Patch set can be applied.")
            : RpackResult.Fail(result.CombinedOutput);
    }

    public RpackResult Apply(string repositoryPath, string patchPath, bool ignoreSpaceChange = false)
    {
        var args = new List<string> { "apply", "--allow-empty" };
        if (ignoreSpaceChange)
        {
            args.Add("--ignore-space-change");
        }

        args.Add(patchPath);
        var result = _processRunner.Run("git", args, repositoryPath);
        return result.Success
            ? RpackResult.Ok("Patch applied.")
            : RpackResult.Fail(result.CombinedOutput);
    }

    public RpackResult CheckReverseApply(string repositoryPath, string patchPath, bool ignoreSpaceChange = false)
    {
        var args = new List<string> { "apply", "--reverse", "--check", "--allow-empty" };
        if (ignoreSpaceChange)
        {
            args.Add("--ignore-space-change");
        }

        args.Add(patchPath);
        var result = _processRunner.Run("git", args, repositoryPath);
        return result.Success
            ? RpackResult.Ok("Patch can be reverted.")
            : RpackResult.Fail(result.CombinedOutput);
    }

    public RpackResult ReverseApply(string repositoryPath, string patchPath, bool ignoreSpaceChange = false)
    {
        var args = new List<string> { "apply", "--reverse", "--allow-empty" };
        if (ignoreSpaceChange)
        {
            args.Add("--ignore-space-change");
        }

        args.Add(patchPath);
        var result = _processRunner.Run("git", args, repositoryPath);
        return result.Success
            ? RpackResult.Ok("Patch reverted.")
            : RpackResult.Fail(result.CombinedOutput);
    }

    public string CreateDetachedWorktree(string repositoryPath)
    {
        var temporaryWorktreePath = Path.Combine(Path.GetTempPath(), $"rpack-worktree-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryWorktreePath);
        var result = _processRunner.Run("git", ["worktree", "add", "--detach", temporaryWorktreePath, "HEAD"], repositoryPath);
        if (!result.Success)
        {
            Directory.Delete(temporaryWorktreePath, recursive: true);
            throw new InvalidOperationException(result.CombinedOutput);
        }

        return temporaryWorktreePath;
    }

    public RpackResult RemoveDetachedWorktree(string repositoryPath, string worktreePath)
    {
        if (string.IsNullOrWhiteSpace(worktreePath))
        {
            return RpackResult.Ok("No worktree path provided.");
        }

        var result = _processRunner.Run("git", ["worktree", "remove", "--force", worktreePath], repositoryPath);
        if (!result.Success)
        {
            try
            {
                Directory.Delete(worktreePath, recursive: true);
                return RpackResult.Ok("Worktree removed.");
            }
            catch
            {
                return RpackResult.Fail(result.CombinedOutput);
            }
        }

        return RpackResult.Ok("Worktree removed.");
    }
}

public sealed record GitRepository(string RootPath, string StatePath);
