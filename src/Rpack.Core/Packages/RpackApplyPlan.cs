using Rpack.Core.Patches;
using Rpack.Core.Results;

namespace Rpack.Core.Packages;

public sealed class RpackApplyPlan : IDisposable
{
    public required RpackManifest Manifest { get; init; }
    public required GitRepository Repository { get; init; }
    public required TempPatchSet TempPatchSet { get; init; }
    public required RpackResult CheckResult { get; init; }
    public required RpackOperationResult CheckReport { get; init; }

    public void Dispose()
    {
        TempPatchSet.Dispose();
    }
}
