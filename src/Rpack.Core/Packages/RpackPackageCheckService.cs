using Rpack.Core.Patches;
using Rpack.Core.State;
using Rpack.Core.Issues;
using Rpack.Core.Results;

namespace Rpack.Core.Packages;

public sealed class RpackPackageCheckService
{
    private readonly RpackApplyPlanBuilder _planBuilder;

    public RpackPackageCheckService(RpackApplyPlanBuilder planBuilder)
    {
        _planBuilder = planBuilder;
    }

    public RpackResult Execute(CheckPackageOptions options)
    {
        return ToLegacyResult(ExecuteDetailed(options));
    }

    public RpackOperationResult ExecuteDetailed(CheckPackageOptions options)
    {
        if (!_planBuilder.TryBuild(options, out var plan, out var failure))
        {
            return RpackResultMapper.ToOperationResult(
                failure,
                failureIssue: RpackApplyPlanBuilder.ClassifyFailureIssue(failure.Message),
                stage: RpackStage.PackageOpen);
        }

        using var applyPlan = plan!;
        return applyPlan.CheckReport;
    }

    private static RpackResult ToLegacyResult(RpackOperationResult result)
    {
        return result.Success
            ? RpackResult.Ok(result.Summary)
            : RpackResult.Fail(result.Summary);
    }
}
