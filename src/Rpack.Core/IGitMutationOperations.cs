namespace Rpack.Core;

public interface IGitMutationOperations
{
    RpackResult StagePaths(string repositoryPath, IReadOnlyList<string> paths);
    RpackResult Commit(string repositoryPath, string message);
}
