namespace Rpack.Core;

public sealed class GitMutationOperations : IGitMutationOperations
{
    private readonly GitClient _gitClient;

    public GitMutationOperations(GitClient gitClient)
    {
        _gitClient = gitClient;
    }

    public RpackResult StagePaths(string repositoryPath, IReadOnlyList<string> paths) => _gitClient.StagePaths(repositoryPath, paths);

    public RpackResult Commit(string repositoryPath, string message) => _gitClient.Commit(repositoryPath, message);
}
