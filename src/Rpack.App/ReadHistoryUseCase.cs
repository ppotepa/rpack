using Rpack.Core;
using Rpack.Core.Results;
using Rpack.Core.State;

namespace Rpack.App;

public sealed class ReadHistoryUseCase
{
    private readonly RpackPackageHistoryService _historyService;

    public ReadHistoryUseCase(RpackPackageHistoryService historyService)
    {
        _historyService = historyService;
    }

    public IReadOnlyList<RpackApplyLog> Execute(string repositoryPath)
    {
        return _historyService.Execute(repositoryPath);
    }

    public RpackOperationResult<IReadOnlyList<RpackApplyLog>> ExecuteDetailed(string repositoryPath)
    {
        return _historyService.ExecuteDetailed(repositoryPath);
    }
}
