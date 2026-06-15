using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Rpack.Core.Packages;

public sealed class RpackPackageCreateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly IGitRepositoryInspector _gitRepositoryInspector;
    private readonly IGitPatchOperations _gitPatchOperations;

    public RpackPackageCreateService(IGitRepositoryInspector gitRepositoryInspector, IGitPatchOperations gitPatchOperations)
    {
        _gitRepositoryInspector = gitRepositoryInspector;
        _gitPatchOperations = gitPatchOperations;
    }

    public RpackResult Execute(CreatePackageOptions options)
    {
        var repository = _gitRepositoryInspector.InspectRepository(options.RepositoryPath);

        if (options.Staged && (!string.IsNullOrWhiteSpace(options.FromRevision) || !string.IsNullOrWhiteSpace(options.ToRevision)))
        {
            return RpackResult.Fail("--staged cannot be combined with --from/--to.");
        }

        string patch;
        string baseCommit;
        string headCommit;
        if (!string.IsNullOrWhiteSpace(options.FromRevision) || !string.IsNullOrWhiteSpace(options.ToRevision))
        {
            if (string.IsNullOrWhiteSpace(options.FromRevision) || string.IsNullOrWhiteSpace(options.ToRevision))
            {
                return RpackResult.Fail("Both --from and --to are required when creating a revision range package.");
            }

            baseCommit = _gitRepositoryInspector.ResolveCommit(repository.RootPath, options.FromRevision);
            headCommit = _gitRepositoryInspector.ResolveCommit(repository.RootPath, options.ToRevision);
            patch = _gitPatchOperations.CreateDiff(repository.RootPath, options.FromRevision, options.ToRevision);
        }
        else if (options.Staged)
        {
            baseCommit = _gitRepositoryInspector.ResolveCommit(repository.RootPath, "HEAD");
            headCommit = baseCommit;
            patch = _gitPatchOperations.CreateStagedDiff(repository.RootPath);
        }
        else
        {
            baseCommit = _gitRepositoryInspector.ResolveCommit(repository.RootPath, "HEAD");
            headCommit = baseCommit;
            patch = _gitPatchOperations.CreateWorkingTreeDiff(repository.RootPath);
        }

        if (string.IsNullOrWhiteSpace(patch))
        {
            return RpackResult.Fail("No changes found.");
        }

        var patchBytes = Encoding.UTF8.GetBytes(patch);
        var patchHash = Sha256.ForBytes(patchBytes);
        var manifest = new RpackManifest
        {
            Id = string.IsNullOrWhiteSpace(options.Id) ? $"rpack-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}" : options.Id,
            Title = string.IsNullOrWhiteSpace(options.Title) ? "Repository patch package" : options.Title,
            Description = options.Description ?? "",
            CreatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            BaseCommit = baseCommit,
            Source = new RpackSourceInfo
            {
                Repository = GetRepositoryName(repository.RootPath),
                ProjectPath = repository.RootPath,
                BaseCommit = baseCommit,
                HeadCommit = headCommit
            },
            Patches =
            [
                new RpackPatch
                {
                    Path = "patches/change.patch",
                    Kind = "git-diff",
                    Sha256 = patchHash
                }
            ]
        };

        var fullOutputPath = Path.GetFullPath(options.OutputPath);
        var outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        if (File.Exists(fullOutputPath))
        {
            File.Delete(fullOutputPath);
        }

        using var archive = ZipFile.Open(fullOutputPath, ZipArchiveMode.Create);
        WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(manifest, JsonOptions));
        WriteEntry(archive, "patches/change.patch", patchBytes);
        WriteEntry(archive, "checksums.sha256", $"{patchHash}  patches/change.patch{Environment.NewLine}");
        WriteEntry(archive, "README.md", $"# {manifest.Title}{Environment.NewLine}{Environment.NewLine}{manifest.Description}{Environment.NewLine}");

        return RpackResult.Ok($"Created {fullOutputPath}");
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        WriteEntry(archive, path, Encoding.UTF8.GetBytes(content));
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static string GetRepositoryName(string repositoryPath)
    {
        var normalized = Path.GetFullPath(repositoryPath).Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? repositoryPath : segments[^1];
    }
}
