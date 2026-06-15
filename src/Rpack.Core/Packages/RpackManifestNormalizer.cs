namespace Rpack.Core.Packages;

public static class RpackManifestNormalizer
{
    public static RpackManifest Normalize(RpackManifest manifest)
    {
        return new RpackManifest
        {
            Format = string.IsNullOrWhiteSpace(manifest.Format) ? "rpack-v1" : manifest.Format,
            Id = manifest.Id ?? "",
            Title = manifest.Title ?? "",
            Description = manifest.Description ?? "",
            CreatedAtUtc = manifest.CreatedAtUtc ?? "",
            BaseCommit = manifest.BaseCommit ?? "",
            Source = manifest.Source,
            RequiresCleanTree = manifest.RequiresCleanTree,
            Mode = string.IsNullOrWhiteSpace(manifest.Mode) ? "working-tree-patch" : manifest.Mode,
            Patches = manifest.Patches ?? [],
            PreActions = manifest.PreActions ?? [],
            PostActions = manifest.PostActions ?? [],
            Validation = manifest.Validation ?? []
        };
    }
}
