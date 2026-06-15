namespace Rpack.Core;

public interface IGitWorkingTreeStatus
{
    RpackResult EnsureCleanWorkingTree(string repositoryPath);
    RpackResult EnsureCleanWorkingTreeExcept(string repositoryPath, IReadOnlyList<string> allowedPaths);
    IReadOnlyList<string> GetChangedPaths(string repositoryPath);
    IReadOnlyList<string> GetTrackedChangedPaths(string repositoryPath);
}
