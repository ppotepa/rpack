namespace Rpack.Core;

public interface IGitPatchOperations
{
    string CreateDiff(string repositoryPath, string fromRevision, string toRevision);
    string CreateNoIndexDiff(string repositoryPath, string leftPath, string rightPath);
    string CreateWorkingTreeDiff(string repositoryPath);
    string CreateStagedDiff(string repositoryPath);
    string CreateDiffStat(string repositoryPath, string patchPath);
    RpackResult CheckApply(string repositoryPath, string patchPath, bool ignoreSpaceChange = false);
    RpackResult CheckApply(string repositoryPath, IReadOnlyList<string> patchPaths, bool ignoreSpaceChange = false);
    RpackResult Apply(string repositoryPath, string patchPath, bool ignoreSpaceChange = false);
    RpackResult CheckReverseApply(string repositoryPath, string patchPath, bool ignoreSpaceChange = false);
    RpackResult ReverseApply(string repositoryPath, string patchPath, bool ignoreSpaceChange = false);
}
