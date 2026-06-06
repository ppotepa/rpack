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

    public RpackResult EnsureCleanWorkingTree(string repositoryPath)
    {
        var result = _processRunner.Run("git", ["status", "--porcelain"], repositoryPath);
        if (!result.Success)
        {
            return RpackResult.Fail(result.CombinedOutput);
        }

        return string.IsNullOrWhiteSpace(result.StandardOutput)
            ? RpackResult.Ok("Working tree is clean.")
            : RpackResult.Fail("Working tree is not clean.");
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

    public RpackResult CheckApply(string repositoryPath, string patchPath)
    {
        var result = _processRunner.Run("git", ["apply", "--check", patchPath], repositoryPath);
        return result.Success
            ? RpackResult.Ok("Patch can be applied.")
            : RpackResult.Fail(result.CombinedOutput);
    }

    public RpackResult Apply(string repositoryPath, string patchPath)
    {
        var result = _processRunner.Run("git", ["apply", patchPath], repositoryPath);
        return result.Success
            ? RpackResult.Ok("Patch applied.")
            : RpackResult.Fail(result.CombinedOutput);
    }

    public RpackResult CheckReverseApply(string repositoryPath, string patchPath)
    {
        var result = _processRunner.Run("git", ["apply", "--reverse", "--check", patchPath], repositoryPath);
        return result.Success
            ? RpackResult.Ok("Patch can be reverted.")
            : RpackResult.Fail(result.CombinedOutput);
    }

    public RpackResult ReverseApply(string repositoryPath, string patchPath)
    {
        var result = _processRunner.Run("git", ["apply", "--reverse", patchPath], repositoryPath);
        return result.Success
            ? RpackResult.Ok("Patch reverted.")
            : RpackResult.Fail(result.CombinedOutput);
    }
}

public sealed record GitRepository(string RootPath, string StatePath);
