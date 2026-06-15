using Rpack.Core;
using Rpack.Core.Issues;
using Rpack.Core.Results;

namespace Rpack.Core.State;

public sealed class RpackPackageUndoService
{
    private readonly IGitRepositoryInspector _gitRepositoryInspector;
    private readonly IGitPatchOperations _gitPatchOperations;
    private readonly RpackStateStore _stateStore;
    private readonly RpackRepositoryPolicyService _repositoryPolicyService;

    public RpackPackageUndoService(
        IGitRepositoryInspector gitRepositoryInspector,
        IGitPatchOperations gitPatchOperations,
        RpackStateStore stateStore,
        RpackRepositoryPolicyService repositoryPolicyService)
    {
        _gitRepositoryInspector = gitRepositoryInspector;
        _gitPatchOperations = gitPatchOperations;
        _stateStore = stateStore;
        _repositoryPolicyService = repositoryPolicyService;
    }

    public RpackResult Execute(string repositoryPath, bool allowDirty = false)
    {
        return ToLegacyResult(ExecuteDetailed(repositoryPath, allowDirty));
    }

    public RpackOperationResult ExecuteDetailed(string repositoryPath, bool allowDirty = false)
    {
        var repository = _gitRepositoryInspector.InspectRepository(repositoryPath);

        var logs = _stateStore.ReadApplyLogs(repository).ToList();
        var index = logs.FindLastIndex(log => string.IsNullOrWhiteSpace(log.UndoneAtUtc));
        if (index < 0)
        {
            return RpackOperationResult.Fail(
                "No applied package to undo.",
                [new RpackIssue(
                    "state.apply-log-missing",
                    RpackSeverity.Error,
                    RpackStage.Undo,
                    "No applied package to undo.",
                    "Apply a package before attempting undo.",
                    RawDetails: "No applied package to undo.")]);
        }

        var log = logs[index];
        var storedPackage = _stateStore.ReadStoredPackage(repository, log);
        if (!storedPackage.Exists)
        {
            var code = storedPackage.ErrorMessage.Contains("manifest", StringComparison.OrdinalIgnoreCase)
                ? "state.stored-package-missing"
                : "state.stored-package-missing";
            return RpackOperationResult.Fail(
                storedPackage.ErrorMessage,
                [new RpackIssue(
                    code,
                    RpackSeverity.Error,
                    RpackStage.Undo,
                    storedPackage.ErrorMessage,
                    "Restore the stored package from the `.git/rpack` state before undoing.",
                    RawDetails: storedPackage.ErrorMessage)]);
        }

        if (!allowDirty)
        {
            var dirtyCheck = _repositoryPolicyService.EnsureNoChangesOutsidePatches(repository.RootPath, storedPackage.PatchPaths);
            if (!dirtyCheck.Success)
            {
                return RpackResultMapper.ToOperationResult(dirtyCheck, stage: RpackStage.RepositoryPolicy);
            }
        }

        foreach (var patchPath in storedPackage.PatchPaths.AsEnumerable().Reverse())
        {
            var check = _gitPatchOperations.CheckReverseApply(repository.RootPath, patchPath);
            if (!check.Success)
            {
                return RpackResultMapper.ToOperationResult(check, stage: RpackStage.Undo);
            }
        }

        var reversedPatches = new List<string>();
        foreach (var patchPath in storedPackage.PatchPaths.AsEnumerable().Reverse())
        {
            var undo = _gitPatchOperations.ReverseApply(repository.RootPath, patchPath);
            if (!undo.Success)
            {
                foreach (var reversedPatch in reversedPatches.AsEnumerable().Reverse())
                {
                    _gitPatchOperations.Apply(repository.RootPath, reversedPatch);
                }

                return RpackResultMapper.ToOperationResult(undo, stage: RpackStage.Undo);
            }

            reversedPatches.Add(patchPath);
        }

        logs[index] = new RpackApplyLog
        {
            ApplyId = log.ApplyId,
            PackageId = log.PackageId,
            Title = log.Title,
            AppliedAtUtc = log.AppliedAtUtc,
            UndoneAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            BaseCommit = log.BaseCommit,
            TargetHeadAtApply = log.TargetHeadAtApply,
            PatchPath = log.PatchPath,
            PackagePath = log.PackagePath,
            ActionResults = log.ActionResults,
            PostActionFailed = log.PostActionFailed,
            CommitSha = log.CommitSha,
            CommittedAtUtc = log.CommittedAtUtc
        };
        _stateStore.WriteApplyLogs(repository, logs);

        return RpackOperationResult.Ok($"Undone apply id: {log.ApplyId}");
    }

    private static RpackResult ToLegacyResult(RpackOperationResult result)
    {
        return result.Success
            ? RpackResult.Ok(result.Summary)
            : RpackResult.Fail(result.Summary);
    }
}
