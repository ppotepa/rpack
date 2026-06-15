using Rpack.Core;
using Rpack.Core.Results;
using Rpack.Core.State;

namespace Rpack.App;

public sealed class UndoLastApplyUseCase
{
    private readonly RpackPackageUndoService _undoService;

    public UndoLastApplyUseCase(RpackPackageUndoService undoService)
    {
        _undoService = undoService;
    }

    public RpackResult Execute(string repositoryPath, bool allowDirty = false)
    {
        return _undoService.Execute(repositoryPath, allowDirty);
    }

    public RpackOperationResult ExecuteDetailed(string repositoryPath, bool allowDirty = false)
    {
        return _undoService.ExecuteDetailed(repositoryPath, allowDirty);
    }
}
