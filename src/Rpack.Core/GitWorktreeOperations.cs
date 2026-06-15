namespace Rpack.Core;

public sealed class GitWorktreeOperations : IGitWorktreeOperations
{
    private readonly GitClient _gitClient;

    public GitWorktreeOperations(GitClient gitClient)
    {
        _gitClient = gitClient;
    }

    public string CreateDetachedWorktree(string repositoryPath) => _gitClient.CreateDetachedWorktree(repositoryPath);

    public RpackResult RemoveDetachedWorktree(string repositoryPath, string worktreePath) => _gitClient.RemoveDetachedWorktree(repositoryPath, worktreePath);
}
