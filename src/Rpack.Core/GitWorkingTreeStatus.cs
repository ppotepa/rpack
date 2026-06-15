namespace Rpack.Core;

public sealed class GitWorkingTreeStatus : IGitWorkingTreeStatus
{
    private readonly GitClient _gitClient;

    public GitWorkingTreeStatus(GitClient gitClient)
    {
        _gitClient = gitClient;
    }

    public RpackResult EnsureCleanWorkingTree(string repositoryPath) => _gitClient.EnsureCleanWorkingTree(repositoryPath);

    public RpackResult EnsureCleanWorkingTreeExcept(string repositoryPath, IReadOnlyList<string> allowedPaths) =>
        _gitClient.EnsureCleanWorkingTreeExcept(repositoryPath, allowedPaths);

    public IReadOnlyList<string> GetChangedPaths(string repositoryPath) => _gitClient.GetChangedPaths(repositoryPath);

    public IReadOnlyList<string> GetTrackedChangedPaths(string repositoryPath) => _gitClient.GetTrackedChangedPaths(repositoryPath);
}
