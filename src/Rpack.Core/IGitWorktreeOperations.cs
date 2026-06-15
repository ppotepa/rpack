namespace Rpack.Core;

public interface IGitWorktreeOperations
{
    string CreateDetachedWorktree(string repositoryPath);
    RpackResult RemoveDetachedWorktree(string repositoryPath, string worktreePath);
}
