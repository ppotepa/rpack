using Rpack.Core;
using Rpack.Core.Issues;
using Rpack.Core.Results;

namespace Rpack.Tests;

public class RpackResultMapperTests
{
    [Fact]
    public void LegacySuccess_MapsToStructuredSuccessAndBack()
    {
        var legacy = RpackResult.Ok("done");

        var structured = RpackResultMapper.ToOperationResult(legacy, stage: RpackStage.RepositoryInspect);
        var roundTrip = RpackResultMapper.ToLegacyResult(structured);

        Assert.True(structured.Success);
        Assert.Equal("done", structured.Summary);
        Assert.Empty(structured.Issues);
        Assert.True(roundTrip.Success);
        Assert.Equal("done", roundTrip.Message);
    }

    [Fact]
    public void LegacyFailure_MapsToStructuredFailureWithDefaultIssue()
    {
        var legacy = RpackResult.Fail("manifest failed");

        var structured = RpackResultMapper.ToOperationResult(legacy, stage: RpackStage.ManifestValidation);

        Assert.False(structured.Success);
        Assert.Single(structured.Issues);

        var issue = structured.Issues[0];
        Assert.Equal("legacy.result", issue.Code);
        Assert.Equal(RpackSeverity.Error, issue.Severity);
        Assert.Equal(RpackStage.ManifestValidation, issue.Stage);
        Assert.Equal("manifest failed", issue.Message);
        Assert.Equal("Review the legacy message for details.", issue.Suggestion);
        Assert.Equal("manifest failed", issue.RawDetails);
    }

    [Fact]
    public void ExplicitFailureIssue_IsPreserved()
    {
        var issue = new RpackIssue(
            "checksum.mismatch",
            RpackSeverity.Fatal,
            RpackStage.ChecksumVerification,
            "Checksum mismatch for patches/change.patch.",
            "Regenerate the package.");

        var legacy = RpackResult.Fail("Checksum mismatch for patches/change.patch.");
        var structured = RpackResultMapper.ToOperationResult(legacy, failureIssue: issue);

        Assert.False(structured.Success);
        var mapped = Assert.Single(structured.Issues);
        Assert.Equal(issue, mapped);
    }
}
