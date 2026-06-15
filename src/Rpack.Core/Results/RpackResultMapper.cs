using Rpack.Core.Issues;

namespace Rpack.Core.Results;

public static class RpackResultMapper
{
    private const string LegacyResultCode = "legacy.result";

    public static RpackOperationResult ToOperationResult(
        RpackResult result,
        RpackIssue? failureIssue = null,
        RpackStage stage = RpackStage.PackageOpen)
    {
        if (result.Success)
        {
            return RpackOperationResult.Ok(result.Message);
        }

        var issue = failureIssue
            ?? new RpackIssue(
                LegacyResultCode,
                RpackSeverity.Error,
                stage,
                result.Message,
                "Review the legacy message for details.",
                RawDetails: result.Message);

        return RpackOperationResult.Fail(result.Message, [issue]);
    }

    public static RpackOperationResult<T> ToOperationResult<T>(
        RpackResult result,
        T? value = default,
        RpackIssue? failureIssue = null,
        RpackStage stage = RpackStage.PackageOpen)
    {
        if (result.Success)
        {
            return RpackOperationResult<T>.Ok(result.Message, value!);
        }

        var issue = failureIssue
            ?? new RpackIssue(
                LegacyResultCode,
                RpackSeverity.Error,
                stage,
                result.Message,
                "Review the legacy message for details.",
                RawDetails: result.Message);

        return RpackOperationResult<T>.Fail(result.Message, [issue]);
    }

    public static RpackResult ToLegacyResult(RpackOperationResult result)
    {
        return result.Success
            ? RpackResult.Ok(result.Summary)
            : RpackResult.Fail(result.Summary);
    }

    public static RpackResult ToLegacyResult<T>(RpackOperationResult<T> result)
    {
        return result.Success
            ? RpackResult.Ok(result.Summary)
            : RpackResult.Fail(result.Summary);
    }
}
