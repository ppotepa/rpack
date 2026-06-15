using System.Text.Json;
using Rpack.Core.Issues;
using Rpack.Core.Results;

namespace Rpack.Core.State;

public sealed class RpackPackageHistoryService
{
    private readonly IGitRepositoryInspector _gitRepositoryInspector;
    private readonly RpackStateStore _stateStore;

    public RpackPackageHistoryService(IGitRepositoryInspector gitRepositoryInspector, RpackStateStore stateStore)
    {
        _gitRepositoryInspector = gitRepositoryInspector;
        _stateStore = stateStore;
    }

    public IReadOnlyList<RpackApplyLog> Execute(string repositoryPath)
    {
        return ExecuteDetailed(repositoryPath).Value ?? [];
    }

    public RpackOperationResult<IReadOnlyList<RpackApplyLog>> ExecuteDetailed(string repositoryPath)
    {
        try
        {
            var repository = _gitRepositoryInspector.InspectRepository(repositoryPath);
            var logs = _stateStore.ReadApplyLogs(repository);
            return RpackOperationResult<IReadOnlyList<RpackApplyLog>>.Ok("History loaded.", logs);
        }
        catch (JsonException ex)
        {
            return RpackOperationResult<IReadOnlyList<RpackApplyLog>>.Fail(
                "History is invalid.",
                [new RpackIssue(
                    "state.history-invalid",
                    RpackSeverity.Error,
                    RpackStage.StateStore,
                    "History file is invalid JSON.",
                    "Delete or repair `.git/rpack/apply-log.json` and retry.",
                    RawDetails: ex.Message)]);
        }
    }
}
