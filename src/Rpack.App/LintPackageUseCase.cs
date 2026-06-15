using Rpack.Core;
using Rpack.Core.Packages;
using Rpack.Core.Results;

namespace Rpack.App;

public sealed class LintPackageUseCase
{
    private readonly RpackPackageLintService _lintService;

    public LintPackageUseCase(RpackPackageLintService lintService)
    {
        _lintService = lintService;
    }

    public RpackResult Execute(LintPackageOptions options)
    {
        return _lintService.Execute(options);
    }

    public RpackOperationResult ExecuteDetailed(LintPackageOptions options)
    {
        return _lintService.ExecuteDetailed(options);
    }
}
