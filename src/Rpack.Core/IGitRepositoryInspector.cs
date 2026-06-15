namespace Rpack.Core;

public interface IGitRepositoryInspector
{
    RpackResult EnsureRepository(string repositoryPath);
    GitRepository InspectRepository(string repositoryPath);
    GitRepository? FindRepositoryFrom(string startPath);
    string ResolveCommit(string repositoryPath, string revision);
}
