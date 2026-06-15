using Rpack.Core;
using Rpack.Core.Packages;
using Rpack.Core.Results;

namespace Rpack.App;

public sealed class RebasePackageUseCase
{
    private readonly RpackPackageRebaseService _rebaseService;

    public RebasePackageUseCase(RpackPackageRebaseService rebaseService)
    {
        _rebaseService = rebaseService;
    }

    public RpackResult Execute(RebasePackageOptions options)
    {
        return _rebaseService.Execute(options);
    }

    public RpackOperationResult ExecuteDetailed(RebasePackageOptions options)
    {
        return _rebaseService.ExecuteDetailed(options);
    }
}
