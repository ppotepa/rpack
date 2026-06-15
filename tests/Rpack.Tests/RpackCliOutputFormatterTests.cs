using System.Text.Json.Nodes;
using Rpack.Cli;
using Rpack.Core;
using Rpack.Core.Issues;
using Rpack.Core.Results;

namespace Rpack.Tests;

public class RpackCliOutputFormatterTests
{
    [Fact]
    public void FormatInspectionJson_IsValidJson()
    {
        var inspection = new PackageInspection
        {
            Manifest = new RpackManifest
            {
                Id = "pkg-1",
                Title = "Package",
                Format = "rpack-v1",
                Mode = "working-tree-patch"
            },
            Entries = ["manifest.json"],
            ChangedFiles = [],
            DiffStats = new RpackDiffStats
            {
                PatchCount = 0,
                FileCount = 0,
                AddedLines = 0,
                RemovedLines = 0,
                HunkCount = 0,
                BinaryFileCount = 0,
                Patches = []
            }
        };

        var json = CliOutputFormatter.FormatInspectionJson(inspection);
        var node = JsonNode.Parse(json);

        Assert.NotNull(node);
        Assert.Equal("pkg-1", node!["manifest"]?["id"]?.GetValue<string>());
        Assert.Equal("manifest.json", node["entries"]?[0]?.GetValue<string>());
    }

    [Fact]
    public void FormatResultJson_ContainsIssues()
    {
        var json = CliOutputFormatter.FormatResultJson("check", RpackResult.Fail("Checksum mismatch."));
        var node = JsonNode.Parse(json);

        Assert.NotNull(node);
        Assert.False(node!["success"]?.GetValue<bool>());
        Assert.Equal("check", node["command"]?.GetValue<string>());
        Assert.Equal("checksum.mismatch", node["issues"]?[0]?["code"]?.GetValue<string>());
    }

    [Fact]
    public void FormatStructuredResultJson_EmitsIssueMetadata()
    {
        var structured = RpackOperationResult.Fail(
            "Base mismatch.",
            [
                new RpackIssue(
                    "repo.base-mismatch",
                    RpackSeverity.Warning,
                    RpackStage.RepositoryPolicy,
                    "Package base commit differs from repository HEAD.",
                    "Regenerate the package from the target repository HEAD.")
            ]);

        var json = CliOutputFormatter.FormatResultJson("check", structured);
        var node = JsonNode.Parse(json);

        Assert.NotNull(node);
        Assert.True(node!["success"]?.GetValue<bool>() is false);
        Assert.Equal("repo.base-mismatch", node["issues"]?[0]?["code"]?.GetValue<string>());
        Assert.Equal("warning", node["issues"]?[0]?["severity"]?.GetValue<string>());
        Assert.Equal("RepositoryPolicy", node["issues"]?[0]?["stage"]?.GetValue<string>());
    }

    [Fact]
    public void FormatLintStructuredJson_EmitsMultipleIssues()
    {
        var structured = RpackOperationResult.Fail(
            "Lint found 2 issue(s):",
            [
                new RpackIssue(
                    "forbidden-path",
                    RpackSeverity.Error,
                    RpackStage.PayloadValidation,
                    "bin/debug/app.dll matches forbidden path pattern.",
                    "Remove generated outputs or binaries from the package.",
                    PatchPath: "patches/change.patch"),
                new RpackIssue(
                    "local-path",
                    RpackSeverity.Warning,
                    RpackStage.PayloadValidation,
                    "config.txt contains local path marker.",
                    "Replace machine-specific paths with repository-relative paths.",
                    PatchPath: "patches/change.patch")
            ]);

        var json = CliOutputFormatter.FormatResultJson("lint", structured);
        var node = JsonNode.Parse(json);

        Assert.NotNull(node);
        Assert.Equal("lint", node!["command"]?.GetValue<string>());
        Assert.Equal("forbidden-path", node["issues"]?[0]?["code"]?.GetValue<string>());
        Assert.Equal("local-path", node["issues"]?[1]?["code"]?.GetValue<string>());
    }

    [Fact]
    public void FormatApplyStructuredJson_EmitsActionFailureCode()
    {
        var structured = RpackOperationResult.Fail(
            "Patch apply failed for patches/change.patch:",
            [
                new RpackIssue(
                    "patch.apply-failed",
                    RpackSeverity.Error,
                    RpackStage.PatchApply,
                    "Patch apply failed for patches/change.patch:",
                    "Inspect the conflicting patch and retry with a matching target checkout.",
                    PatchPath: "patches/change.patch")
            ]);

        var json = CliOutputFormatter.FormatResultJson("apply", structured);
        var node = JsonNode.Parse(json);

        Assert.NotNull(node);
        Assert.Equal("apply", node!["command"]?.GetValue<string>());
        Assert.Equal("patch.apply-failed", node["issues"]?[0]?["code"]?.GetValue<string>());
        Assert.Equal("PatchApply", node["issues"]?[0]?["stage"]?.GetValue<string>());
    }

    [Fact]
    public void FormatRebaseStructuredJson_EmitsArtifactAndIssueCodes()
    {
        var structured = new RpackOperationResult(
            false,
            "Rebase produced no changes.",
            [
                new RpackIssue(
                    "patch.no-output",
                    RpackSeverity.Error,
                    RpackStage.Rebase,
                    "Rebase produced no changes.",
                    "Ensure the rebased worktree actually contains changes.")
            ],
            [new RpackArtifact("file", "out.rpack", "Rebased package archive")],
            []);

        var json = CliOutputFormatter.FormatResultJson("rebase", structured);
        var node = JsonNode.Parse(json);

        Assert.NotNull(node);
        Assert.Equal("rebase", node!["command"]?.GetValue<string>());
        Assert.Equal("patch.no-output", node["issues"]?[0]?["code"]?.GetValue<string>());
        Assert.Equal("file", node["artifacts"]?[0]?["kind"]?.GetValue<string>());
    }

    [Fact]
    public void FormatDiagnoseLlmReport_ContainsConflictFields()
    {
        var report = CliOutputFormatter.FormatDiagnoseLlmReport("package.rpack", "repo", RpackResult.Fail("Patch apply failed for a.patch"));

        Assert.Contains("OperationId:", report);
        Assert.Contains("Path:", report);
        Assert.Contains("Problem:", report);
        Assert.Contains("SuggestedAction:", report);
        Assert.Contains("PromptForRegeneration:", report);
    }

    [Fact]
    public void MapExitCode_UsesExpectedCodes()
    {
        Assert.Equal(2, CliOutputFormatter.MapExitCode(RpackResult.Fail("anything"), invalidArgs: true));
        Assert.Equal(3, CliOutputFormatter.MapExitCode(RpackResult.Fail("Checksum mismatch.")));
        Assert.Equal(4, CliOutputFormatter.MapExitCode(RpackResult.Fail("Unsafe path prefix.")));
        Assert.Equal(5, CliOutputFormatter.MapExitCode(RpackResult.Fail("Patch apply failed for a.patch.")));

        var structured = RpackOperationResult.Fail(
            "Package root does not exist or is not a directory.",
            [
                new RpackIssue(
                    "package.root-not-directory",
                    RpackSeverity.Error,
                    RpackStage.PackageOpen,
                    "Package root does not exist or is not a directory.",
                    "Point the command at an existing package root.")
            ]);

        Assert.Equal(3, CliOutputFormatter.MapExitCode(structured));
    }
}
