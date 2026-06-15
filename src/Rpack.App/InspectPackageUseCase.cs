using Rpack.Core;
using Rpack.Core.Packages;

namespace Rpack.App;

public sealed class InspectPackageUseCase
{
    private readonly RpackPackageInspectService _inspectService;

    public InspectPackageUseCase(RpackPackageInspectService inspectService)
    {
        _inspectService = inspectService;
    }

    public PackageInspection Execute(InspectPackageOptions options)
    {
        return _inspectService.Execute(options);
    }
}
