using Rpack.Core;
using Rpack.Core.Packages;

namespace Rpack.App;

public sealed class CreatePackageUseCase
{
    private readonly RpackPackageCreateService _createService;

    public CreatePackageUseCase(RpackPackageCreateService createService)
    {
        _createService = createService;
    }

    public RpackResult Execute(CreatePackageOptions options)
    {
        return _createService.Execute(options);
    }
}
