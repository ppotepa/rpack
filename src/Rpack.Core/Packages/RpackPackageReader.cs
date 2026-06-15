using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Rpack.Core.Packages;

public sealed class RpackPackageReader
{
    public const string ManifestPath = "manifest.json";

    public ZipArchive OpenRead(string packagePath)
    {
        return ZipFile.OpenRead(packagePath);
    }

    public RpackManifest ReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry(ManifestPath) ?? throw new InvalidOperationException("manifest.json is missing.");
        using var stream = entry.Open();
        try
        {
            var manifest = JsonSerializer.Deserialize(stream, RpackJsonContext.Default.RpackManifest);
            return manifest is null
                ? throw new InvalidOperationException("manifest.json is invalid.")
                : RpackManifestNormalizer.Normalize(manifest);
        }
        catch (JsonException ex) when (ex.Path?.StartsWith("$.Validation", StringComparison.Ordinal) == true)
        {
            throw new InvalidOperationException("manifest.json is invalid: Validation must be an array of objects with Name, Command, and Optional fields. Use Validation: [] when the package does not declare validation commands.", ex);
        }
    }

    public string ReadEntryText(ZipArchive archive, string path)
    {
        if (!IsSafeArchivePath(path))
        {
            throw new InvalidOperationException($"Unsafe archive path: {path}");
        }

        var entry = archive.GetEntry(path) ?? throw new InvalidOperationException($"Archive entry is missing: {path}");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public IReadOnlyList<string> ListEntries(ZipArchive archive)
    {
        return archive.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ToArray();
    }

    public static bool IsSafeArchivePath(string path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && !Path.IsPathRooted(path)
            && !path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Contains("..");
    }
}
