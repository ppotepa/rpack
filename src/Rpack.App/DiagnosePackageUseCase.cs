using Rpack.Core;
using Rpack.Core.Packages;

namespace Rpack.App;

public sealed class DiagnosePackageUseCase
{
    private readonly RpackPackageDiagnoseService _diagnoseService;

    public DiagnosePackageUseCase(RpackPackageDiagnoseService diagnoseService)
    {
        _diagnoseService = diagnoseService;
    }

    public RpackResult Execute(DiagnosePackageOptions options)
    {
        return _diagnoseService.Execute(options);
    }
}
