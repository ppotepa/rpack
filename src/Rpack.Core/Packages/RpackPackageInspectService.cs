using Rpack.Core.Patches;

namespace Rpack.Core.Packages;

public sealed class RpackPackageInspectService
{
    private readonly RpackPackageReader _packageReader;
    private readonly RpackPatchParser _patchParser;
    private readonly RpackPatchPathTransformer _patchTransformer;

    public RpackPackageInspectService(
        RpackPackageReader packageReader,
        RpackPatchParser patchParser,
        RpackPatchPathTransformer patchTransformer)
    {
        _packageReader = packageReader;
        _patchParser = patchParser;
        _patchTransformer = patchTransformer;
    }

    public PackageInspection Execute(InspectPackageOptions options)
    {
        var pathPrefix = NormalizePathPrefix(options.PathPrefix);
        using var archive = _packageReader.OpenRead(options.PackagePath);
        var manifest = _packageReader.ReadManifest(archive);
        var patchStats = new List<RpackPatchDiffStats>();
        var changedFiles = new List<RpackFileDiffStats>();
        for (var index = 0; index < manifest.Patches.Count; index++)
        {
            var patch = manifest.Patches[index];
            var patchContent = _patchTransformer.RewritePatchPaths(_packageReader.ReadEntryText(archive, patch.Path), pathPrefix);
            var files = _patchParser.Analyze(patchContent);
            changedFiles.AddRange(files);
            patchStats.Add(_patchParser.BuildPatchDiffStats(index + 1, patch, files));
        }

        return new PackageInspection
        {
            Manifest = manifest,
            Entries = _packageReader.ListEntries(archive),
            ChangedFiles = changedFiles,
            DiffStats = new RpackDiffStats
            {
                PatchCount = patchStats.Count,
                FileCount = changedFiles.Count,
                AddedLines = changedFiles.Sum(file => file.AddedLines),
                RemovedLines = changedFiles.Sum(file => file.RemovedLines),
                HunkCount = changedFiles.Sum(file => file.HunkCount),
                BinaryFileCount = changedFiles.Count(file => file.IsBinary),
                Patches = patchStats
            }
        };
    }

    private static string NormalizePathPrefix(string? pathPrefix)
    {
        return string.IsNullOrWhiteSpace(pathPrefix)
            ? ""
            : pathPrefix.Replace('\\', '/').Trim().Trim('/');
    }
}
