using System.Text;
using Rpack.Core.Patches;

namespace Rpack.Core.State;

public sealed class RpackRepositoryPolicyService
{
    private readonly IGitPatchOperations _gitPatchOperations;
    private readonly IGitWorkingTreeStatus _gitWorkingTreeStatus;
    private readonly RpackPatchParser _patchParser;

    public RpackRepositoryPolicyService()
        : this(new GitClient(new ProcessRunner()), new RpackPatchParser())
    {
    }

    public RpackRepositoryPolicyService(GitClient gitClient, RpackPatchParser patchParser)
        : this(new GitPatchOperations(gitClient), new GitWorkingTreeStatus(gitClient), patchParser)
    {
    }

    public RpackRepositoryPolicyService(IGitPatchOperations gitPatchOperations, IGitWorkingTreeStatus gitWorkingTreeStatus, RpackPatchParser patchParser)
    {
        _gitPatchOperations = gitPatchOperations;
        _gitWorkingTreeStatus = gitWorkingTreeStatus;
        _patchParser = patchParser;
    }

    public string FindFirstIndividuallyFailingPatch(string repositoryPath, TempPatchSet tempPatchSet, bool ignoreSpaceChange)
    {
        foreach (var patch in tempPatchSet.Patches)
        {
            var check = _gitPatchOperations.CheckApply(repositoryPath, patch.TempPath, ignoreSpaceChange);
            if (!check.Success)
            {
                return patch.ManifestPath;
            }
        }

        return "unknown patch";
    }

    public string FindManifestPatchForFailure(string message, TempPatchSet tempPatchSet)
    {
        var failedPaths = ExtractGitFailurePaths(message)
            .Select(NormalizeGitPath)
            .ToArray();
        if (failedPaths.Length > 0)
        {
            foreach (var failedPath in failedPaths)
            {
                foreach (var patch in tempPatchSet.Patches)
                {
                    var files = _patchParser.Analyze(File.ReadAllText(patch.TempPath, Encoding.UTF8));
                    if (files.Any(file => string.Equals(NormalizeGitPath(file.Path), failedPath, StringComparison.Ordinal)))
                    {
                        return patch.ManifestPath;
                    }
                }
            }
        }

        return tempPatchSet.Patches.Count == 1
            ? tempPatchSet.Patches[0].ManifestPath
            : "unknown patch";
    }

    public RpackResult EnsureNoChangesOutsidePatches(string repositoryPath, IReadOnlyList<string> patchPaths)
    {
        var allowedPaths = patchPaths
            .SelectMany(path => _patchParser.Analyze(File.ReadAllText(path, Encoding.UTF8)))
            .Select(file => NormalizeGitPath(file.Path))
            .ToHashSet(StringComparer.Ordinal);
        var dirtyPaths = _gitWorkingTreeStatus.GetChangedPaths(repositoryPath)
            .Select(NormalizeGitPath)
            .ToArray();
        var outsidePaths = dirtyPaths
            .Where(path => !allowedPaths.Contains(path))
            .ToArray();

        return outsidePaths.Length == 0
            ? RpackResult.Ok("Working tree changes are limited to the last applied package.")
            : RpackResult.Fail($"Working tree has changes outside the last applied package: {string.Join(", ", outsidePaths)}. Use --allow-dirty to undo anyway.");
    }

    private static IReadOnlyList<string> ExtractGitFailurePaths(string message)
    {
        var paths = new List<string>();
        foreach (var rawLine in message.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            const string patchFailedPrefix = "error: patch failed: ";
            if (line.StartsWith(patchFailedPrefix, StringComparison.Ordinal))
            {
                var content = line[patchFailedPrefix.Length..];
                var lineNumberSeparator = content.LastIndexOf(':');
                if (lineNumberSeparator > 0)
                {
                    paths.Add(content[..lineNumberSeparator]);
                }

                continue;
            }

            const string errorPrefix = "error: ";
            if (line.StartsWith(errorPrefix, StringComparison.Ordinal))
            {
                var content = line[errorPrefix.Length..];
                var separator = content.IndexOf(':');
                if (separator > 0)
                {
                    paths.Add(content[..separator]);
                }
            }
        }

        return paths;
    }

    private static string NormalizeGitPath(string path)
    {
        return path.Replace('\\', '/').Trim();
    }
}
