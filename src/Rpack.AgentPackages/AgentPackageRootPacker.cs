using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Rpack.Core;
using Rpack.Core.Issues;
using Rpack.Core.Results;

namespace Rpack.AgentPackages;

public sealed class AgentPackageRootPacker
{
    private static readonly string[] CategoryOrder =
    [
        "manifest.json",
        "operations.json",
        "metadata/",
        "payload/",
        "patches/",
        "diagnostics/",
        "README.md"
    ];

    public RpackResult Pack(string packageRoot, string outputPath)
    {
        return RpackResultMapper.ToLegacyResult(PackDetailed(packageRoot, outputPath));
    }

    public RpackOperationResult PackDetailed(string packageRoot, string outputPath)
    {
        var validation = new AgentPackageRootValidator().ValidateDetailed(packageRoot);
        if (!validation.Success)
        {
            return validation;
        }

        var outputFullPath = Path.GetFullPath(outputPath);
        try
        {
            var root = Path.GetFullPath(packageRoot);
            var entries = EnumerateEntries(root)
                .Where(path => !string.Equals(path, "checksums.json", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var checksums = new SortedDictionary<string, string>(StringComparer.Ordinal);

            var outputDir = Path.GetDirectoryName(outputFullPath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            if (File.Exists(outputFullPath))
            {
                File.Delete(outputFullPath);
            }

            using (var archive = ZipFile.Open(outputFullPath, ZipArchiveMode.Create))
            {
                foreach (var relativePath in entries)
                {
                    var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    var bytes = File.ReadAllBytes(fullPath);
                    var entry = archive.CreateEntry(relativePath.Replace('\\', '/'));
                    using var stream = entry.Open();
                    stream.Write(bytes);
                    checksums[relativePath.Replace('\\', '/')] = Sha256.ForBytes(bytes);
                }

                var checksumsContent = string.Join(
                    Environment.NewLine,
                    checksums.Select(entry => $"{entry.Value}  {entry.Key}")) + Environment.NewLine;
                var checksumsEntry = archive.CreateEntry("checksums.json");
                using (var stream = checksumsEntry.Open())
                {
                    var bytes = Encoding.UTF8.GetBytes(checksumsContent);
                    stream.Write(bytes);
                }
            }

            using var reopen = ZipFile.OpenRead(outputFullPath);
            foreach (var entry in reopen.Entries.Where(entry => !string.Equals(entry.FullName, "checksums.json", StringComparison.Ordinal)))
            {
                using var stream = entry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                var hash = Sha256.ForBytes(memory.ToArray());
                if (!checksums.TryGetValue(entry.FullName, out var expected) || expected != hash)
                {
                    DeleteIfExists(outputFullPath);
                    return Fail(
                        "checksum.mismatch",
                        RpackStage.ChecksumVerification,
                        $"Checksum verification failed for {entry.FullName}.",
                        "Rebuild the package root and pack again.",
                        packagePath: outputFullPath,
                        filePath: entry.FullName);
                }
            }

            return new RpackOperationResult(
                true,
                $"Packed {outputFullPath}",
                [],
                [new RpackArtifact("package", outputFullPath, "Packed agent package archive.")],
                []);
        }
        catch (Exception ex)
        {
            DeleteIfExists(outputFullPath);
            return Fail(
                "package.pack-failed",
                RpackStage.PackageOpen,
                ex.Message,
                "Check filesystem access and retry.",
                packagePath: outputFullPath,
                rawDetails: ex.ToString());
        }
    }

    private static IReadOnlyList<string> EnumerateEntries(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(PathCategory, StringComparer.Ordinal)
            .ThenBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string PathCategory(string path)
    {
        var normalized = path.Replace('\\', '/');
        foreach (var category in CategoryOrder)
        {
            if (category.EndsWith("/", StringComparison.Ordinal))
            {
                if (normalized.StartsWith(category, StringComparison.Ordinal))
                {
                    return category;
                }

                continue;
            }

            if (string.Equals(normalized, category, StringComparison.Ordinal))
            {
                return category;
            }
        }

        return "zzz/";
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static RpackOperationResult Fail(
        string code,
        RpackStage stage,
        string message,
        string suggestion,
        string? packagePath = null,
        string? patchPath = null,
        string? filePath = null,
        string? rawDetails = null)
    {
        var issue = new RpackIssue(
            code,
            RpackSeverity.Error,
            stage,
            message,
            suggestion,
            packagePath,
            patchPath,
            filePath,
            rawDetails);

        return RpackOperationResult.Fail(message, [issue]);
    }
}
