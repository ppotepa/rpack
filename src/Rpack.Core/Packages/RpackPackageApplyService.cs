using System.Text;
using Rpack.Core.Actions;
using Rpack.Core.Issues;
using Rpack.Core.Patches;
using Rpack.Core.State;
using Rpack.Core.Results;

namespace Rpack.Core.Packages;

public sealed class RpackPackageApplyService
{
    private readonly IGitRepositoryInspector _gitRepositoryInspector;
    private readonly IGitPatchOperations _gitPatchOperations;
    private readonly RpackPackageReader _packageReader;
    private readonly RpackPatchParser _patchParser;
    private readonly RpackActionExecutionService _actionExecutionService;
    private readonly RpackStateStore _stateStore;
    private readonly RpackApplyPlanBuilder _planBuilder;

    public RpackPackageApplyService(
        IGitRepositoryInspector gitRepositoryInspector,
        IGitPatchOperations gitPatchOperations,
        RpackPackageReader packageReader,
        RpackPatchParser patchParser,
        RpackActionExecutionService actionExecutionService,
        RpackStateStore stateStore,
        RpackApplyPlanBuilder planBuilder)
    {
        _gitRepositoryInspector = gitRepositoryInspector;
        _gitPatchOperations = gitPatchOperations;
        _packageReader = packageReader;
        _patchParser = patchParser;
        _actionExecutionService = actionExecutionService;
        _stateStore = stateStore;
        _planBuilder = planBuilder;
    }

    public RpackResult Execute(ApplyPackageOptions options)
    {
        return ToLegacyResult(ExecuteDetailed(options));
    }

    public RpackOperationResult ExecuteDetailed(ApplyPackageOptions options)
    {
        if (!_planBuilder.TryBuild(new CheckPackageOptions
        {
            PackagePath = options.PackagePath,
            RepositoryPath = options.RepositoryPath,
            AllowDirty = options.AllowDirty,
            StrictBase = options.StrictBase,
            PathPrefix = options.PathPrefix,
            AddedFileConflictResolution = options.AddedFileConflictResolution,
            AllowedDirtyPaths = options.AllowedDirtyPaths,
            IgnoreSpaceChange = options.IgnoreSpaceChange
        }, out var plan, out var failure))
        {
            return RpackResultMapper.ToOperationResult(
                failure,
                failureIssue: RpackApplyPlanBuilder.ClassifyFailureIssue(failure.Message));
        }

        using var applyPlan = plan!;
        var archive = _packageReader.OpenRead(options.PackagePath);
        using (archive)
        {
            var manifest = _packageReader.ReadManifest(archive);
            using var tempActionSet = _actionExecutionService.ExtractActionsToTempDirectory(archive, manifest.PreActions.Concat(manifest.PostActions).ToArray());

            var applyId = _stateStore.CreateApplyId();
            var changedFiles = GetChangedFilesFromPatchSet(applyPlan.TempPatchSet);
            var actionResults = new List<RpackActionResult>();
            if (!options.SkipActions)
            {
                var preActions = _actionExecutionService.RunActions(
                    "pre",
                    manifest.PreActions,
                    tempActionSet,
                    applyPlan.Repository.RootPath,
                    manifest,
                    applyId,
                    changedFiles,
                    options.SelectedPreActions,
                    options.OnActionExecuted);
                actionResults.AddRange(preActions.Results);
                if (!preActions.Success)
                {
                    var message = RpackActionExecutionService.BuildActionFailureMessage("PreAction failed", preActions.Results);
                    return RpackOperationResult.Fail(message, [new RpackIssue(
                        "action.pre-failed",
                        RpackSeverity.Error,
                        RpackStage.PreAction,
                        message,
                        "Fix the pre-action before applying the package.",
                        RawDetails: message)]);
                }
            }

            var appliedPatches = new List<string>();
            foreach (var patch in applyPlan.TempPatchSet.Patches)
            {
                var apply = _gitPatchOperations.Apply(applyPlan.Repository.RootPath, patch.TempPath, options.IgnoreSpaceChange);
                if (!apply.Success)
                {
                    foreach (var appliedPatch in appliedPatches.AsEnumerable().Reverse())
                    {
                        _gitPatchOperations.ReverseApply(applyPlan.Repository.RootPath, appliedPatch, options.IgnoreSpaceChange);
                    }

                    var message = $"Patch apply failed for {patch.ManifestPath}:{Environment.NewLine}{apply.Message}";
                    return RpackOperationResult.Fail(message, [new RpackIssue(
                        "patch.apply-failed",
                        RpackSeverity.Error,
                        RpackStage.PatchApply,
                        message,
                        "Inspect the conflicting patch and retry with a matching target checkout.",
                        PatchPath: patch.ManifestPath,
                        RawDetails: message)]);
                }

                appliedPatches.Add(patch.TempPath);
            }

            string storedPackagePath;
            try
            {
                storedPackagePath = _stateStore.SaveAppliedPackage(applyPlan.Repository, applyId, manifest, applyPlan.TempPatchSet.Patches);
            }
            catch (Exception ex)
            {
                RollbackPatches(applyPlan.Repository.RootPath, appliedPatches);
                return StateStoreFailure($"Failed to store applied package: {ex.Message}", ex);
            }
            ActionExecutionSummary postActions = ActionExecutionSummary.SuccessOnly(Array.Empty<RpackActionResult>());
            if (!options.SkipActions)
            {
                postActions = _actionExecutionService.RunActions(
                    "post",
                    manifest.PostActions,
                    tempActionSet,
                    applyPlan.Repository.RootPath,
                    manifest,
                    applyId,
                    changedFiles,
                    options.SelectedPostActions,
                    options.OnActionExecuted);
                actionResults.AddRange(postActions.Results);
            }

            var commitResult = actionResults.LastOrDefault(result =>
                string.Equals(NormalizeActionKind(result.Kind), "rpack.commit", StringComparison.OrdinalIgnoreCase)
                && result.Success
                && !string.IsNullOrWhiteSpace(result.StandardOutput));
            try
            {
                _stateStore.AppendApplyLog(applyPlan.Repository, new RpackApplyLog
                {
                    ApplyId = applyId,
                    PackageId = manifest.Id,
                    Title = manifest.Title,
                    AppliedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                    BaseCommit = GetManifestBaseCommit(manifest),
                    TargetHeadAtApply = _gitRepositoryInspector.ResolveCommit(applyPlan.Repository.RootPath, "HEAD"),
                    PatchPath = applyPlan.TempPatchSet.Patches.Count == 1 ? $"{storedPackagePath}/{applyPlan.TempPatchSet.Patches[0].ManifestPath}" : "",
                    PackagePath = storedPackagePath,
                    ActionResults = actionResults,
                    PostActionFailed = !postActions.Success,
                    CommitSha = commitResult?.StandardOutput.Trim() ?? "",
                    CommittedAtUtc = commitResult is null ? "" : DateTimeOffset.UtcNow.ToString("O")
                });
            }
            catch (Exception ex)
            {
                RollbackPatches(applyPlan.Repository.RootPath, appliedPatches);
                return StateStoreFailure($"Failed to store apply log: {ex.Message}", ex);
            }

            if (!postActions.Success)
            {
                var message = RpackActionExecutionService.BuildActionFailureMessage($"Package applied. Apply id: {applyId}. PostAction failed", postActions.Results);
                return RpackOperationResult.Fail(message, [new RpackIssue(
                    "action.post-failed",
                    RpackSeverity.Error,
                    RpackStage.PostAction,
                    message,
                    "Fix the post-action or rerun with --no-actions.",
                    RawDetails: message)]);
            }

            var suffix = options.SkipActions
                ? " Actions were skipped."
                : RpackActionExecutionService.BuildActionSuccessSuffix(actionResults);
            return RpackOperationResult.Ok($"Package applied. Apply id: {applyId}{suffix}");
        }
    }

    private static RpackResult ToLegacyResult(RpackOperationResult result)
    {
        return result.Success
            ? RpackResult.Ok(result.Summary)
            : RpackResult.Fail(result.Summary);
    }

    private static void RollbackPatches(string repositoryRoot, List<string> appliedPatches)
    {
        foreach (var appliedPatch in appliedPatches.AsEnumerable().Reverse())
        {
            try
            {
                new GitClient(new ProcessRunner()).ReverseApply(repositoryRoot, appliedPatch, ignoreSpaceChange: false);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    private static RpackOperationResult StateStoreFailure(string message, Exception ex)
    {
        return RpackOperationResult.Fail(message, [new RpackIssue(
            "state.store-failed",
            RpackSeverity.Error,
            RpackStage.StateStore,
            message,
            "Fix the state store path or permissions and retry the apply.",
            RawDetails: ex.ToString())]);
    }

    private IReadOnlyList<string> GetChangedFilesFromPatchSet(TempPatchSet tempPatchSet)
    {
        return tempPatchSet.Patches
            .SelectMany(patch => _patchParser.Analyze(File.ReadAllText(patch.TempPath, Encoding.UTF8)))
            .Select(file => NormalizeGitPath(file.Path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetManifestBaseCommit(RpackManifest manifest)
    {
        return !string.IsNullOrWhiteSpace(manifest.Source?.BaseCommit)
            ? manifest.Source.BaseCommit
            : manifest.BaseCommit;
    }

    private static string NormalizeActionKind(string kind)
    {
        return kind.Trim().ToLowerInvariant();
    }

    private static string NormalizeGitPath(string path)
    {
        return path.Replace('\\', '/').Trim();
    }

}
