namespace Rpack.Core;

public sealed class GitRepositoryInspector : IGitRepositoryInspector
{
    private readonly GitClient _gitClient;

    public GitRepositoryInspector(GitClient gitClient)
    {
        _gitClient = gitClient;
    }

    public RpackResult EnsureRepository(string repositoryPath) => _gitClient.EnsureRepository(repositoryPath);

    public GitRepository InspectRepository(string repositoryPath) => _gitClient.InspectRepository(repositoryPath);

    public GitRepository? FindRepositoryFrom(string startPath) => _gitClient.FindRepositoryFrom(startPath);

    public string ResolveCommit(string repositoryPath, string revision) => _gitClient.ResolveCommit(repositoryPath, revision);
}
