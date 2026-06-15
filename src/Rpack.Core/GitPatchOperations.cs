namespace Rpack.Core;

public sealed class GitPatchOperations : IGitPatchOperations
{
    private readonly GitClient _gitClient;

    public GitPatchOperations(GitClient gitClient)
    {
        _gitClient = gitClient;
    }

    public string CreateDiff(string repositoryPath, string fromRevision, string toRevision) => _gitClient.CreateDiff(repositoryPath, fromRevision, toRevision);

    public string CreateNoIndexDiff(string repositoryPath, string leftPath, string rightPath) => _gitClient.CreateNoIndexDiff(repositoryPath, leftPath, rightPath);

    public string CreateWorkingTreeDiff(string repositoryPath) => _gitClient.CreateWorkingTreeDiff(repositoryPath);

    public string CreateStagedDiff(string repositoryPath) => _gitClient.CreateStagedDiff(repositoryPath);

    public string CreateDiffStat(string repositoryPath, string patchPath) => _gitClient.CreateDiffStat(repositoryPath, patchPath);

    public RpackResult CheckApply(string repositoryPath, string patchPath, bool ignoreSpaceChange = false) => _gitClient.CheckApply(repositoryPath, patchPath, ignoreSpaceChange);

    public RpackResult CheckApply(string repositoryPath, IReadOnlyList<string> patchPaths, bool ignoreSpaceChange = false) => _gitClient.CheckApply(repositoryPath, patchPaths, ignoreSpaceChange);

    public RpackResult Apply(string repositoryPath, string patchPath, bool ignoreSpaceChange = false) => _gitClient.Apply(repositoryPath, patchPath, ignoreSpaceChange);

    public RpackResult CheckReverseApply(string repositoryPath, string patchPath, bool ignoreSpaceChange = false) => _gitClient.CheckReverseApply(repositoryPath, patchPath, ignoreSpaceChange);

    public RpackResult ReverseApply(string repositoryPath, string patchPath, bool ignoreSpaceChange = false) => _gitClient.ReverseApply(repositoryPath, patchPath, ignoreSpaceChange);
}
