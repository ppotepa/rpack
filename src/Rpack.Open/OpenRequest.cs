namespace Rpack.Open;

internal sealed class OpenRequest
{
    public List<string> PackagePaths { get; init; } = [];
    public string? RepositoryPath { get; init; }
    public string? PathPrefix { get; init; }
    public bool AllowDirty { get; init; }
    public bool StrictBase { get; init; }
    public bool IgnoreSpaceChange { get; init; }
    public bool StrictWhitespace { get; init; }
    public bool NoActions { get; init; }
    public string DirtyReason { get; init; } = "";

    public static OpenRequest FromOptions(OpenOptions options, bool shiftPressed)
    {
        var explicitRepository = options.Value("--repo");
        var packagePaths = options.Positionals.ToList();
        string? repositoryPath = explicitRepository;

        if (string.IsNullOrWhiteSpace(repositoryPath)
            && packagePaths.Count > 1
            && !LooksLikePackage(packagePaths[^1]))
        {
            repositoryPath = packagePaths[^1];
            packagePaths.RemoveAt(packagePaths.Count - 1);
        }

        var allowDirty = shiftPressed || options.Has("--allow-dirty");
        var dirtyReason = shiftPressed
            ? "Shift key"
            : options.Has("--allow-dirty") ? "--allow-dirty" : "";
        var strictWhitespace = options.Has("--strict") || options.Has("--strict-whitespace");

        return new OpenRequest
        {
            PackagePaths = packagePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            RepositoryPath = string.IsNullOrWhiteSpace(repositoryPath) ? null : repositoryPath,
            PathPrefix = options.Value("--path-prefix"),
            AllowDirty = allowDirty,
            StrictBase = options.Has("--strict-base"),
            IgnoreSpaceChange = !strictWhitespace,
            StrictWhitespace = strictWhitespace,
            NoActions = options.Has("--no-actions"),
            DirtyReason = dirtyReason
        };
    }

    private static bool LooksLikePackage(string path)
    {
        return string.Equals(Path.GetExtension(path), ".rpack", StringComparison.OrdinalIgnoreCase);
    }
}
