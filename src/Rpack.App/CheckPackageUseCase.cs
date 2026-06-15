using Rpack.Core;
using Rpack.Core.Packages;
using Rpack.Core.Results;

namespace Rpack.App;

public sealed class CheckPackageUseCase
{
    private readonly RpackPackageCheckService _checkService;

    public CheckPackageUseCase(
        RpackPackageCheckService checkService)
    {
        _checkService = checkService;
    }

    public RpackResult Execute(CheckPackageOptions options)
    {
        return _checkService.Execute(options);
    }

    public RpackOperationResult ExecuteDetailed(CheckPackageOptions options)
    {
        return _checkService.ExecuteDetailed(options);
    }
}
