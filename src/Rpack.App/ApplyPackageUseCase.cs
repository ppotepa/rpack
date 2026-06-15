using Rpack.Core;
using Rpack.Core.Packages;
using Rpack.Core.Results;

namespace Rpack.App;

public sealed class ApplyPackageUseCase
{
    private readonly RpackPackageApplyService _applyService;

    public ApplyPackageUseCase(RpackPackageApplyService applyService)
    {
        _applyService = applyService;
    }

    public RpackResult Execute(ApplyPackageOptions options)
    {
        return _applyService.Execute(options);
    }

    public RpackOperationResult ExecuteDetailed(ApplyPackageOptions options)
    {
        return _applyService.ExecuteDetailed(options);
    }
}
