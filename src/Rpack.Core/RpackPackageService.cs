using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Rpack.Core;

public sealed class RpackPackageService
{
    private const string ManifestPath = "manifest.json";
    private const string PatchPath = "patches/change.patch";
    private const string ApplyLogPath = "apply-log.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        TypeInfoResolver = RpackJsonContext.Default,
        WriteIndented = true
    };

    private readonly GitClient _gitClient;

    public RpackPackageService(GitClient gitClient)
    {
        _gitClient = gitClient;
    }

    public RpackResult Create(CreatePackageOptions options)
    {
        var repository = _gitClient.InspectRepository(options.RepositoryPath);

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

            baseCommit = _gitClient.ResolveCommit(repository.RootPath, options.FromRevision);
            headCommit = _gitClient.ResolveCommit(repository.RootPath, options.ToRevision);
            patch = _gitClient.CreateDiff(repository.RootPath, options.FromRevision, options.ToRevision);
        }
        else if (options.Staged)
        {
            baseCommit = _gitClient.ResolveCommit(repository.RootPath, "HEAD");
            headCommit = baseCommit;
            patch = _gitClient.CreateStagedDiff(repository.RootPath);
        }
        else
        {
            baseCommit = _gitClient.ResolveCommit(repository.RootPath, "HEAD");
            headCommit = baseCommit;
            patch = _gitClient.CreateWorkingTreeDiff(repository.RootPath);
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
                BaseCommit = baseCommit,
                HeadCommit = headCommit
            },
            Patches =
            [
                new RpackPatch
                {
                    Path = PatchPath,
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
        WriteEntry(archive, ManifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
        WriteEntry(archive, PatchPath, patchBytes);
        WriteEntry(archive, "checksums.sha256", $"{patchHash}  {PatchPath}{Environment.NewLine}");
        WriteEntry(archive, "README.md", $"# {manifest.Title}{Environment.NewLine}{Environment.NewLine}{manifest.Description}{Environment.NewLine}");

        return RpackResult.Ok($"Created {fullOutputPath}");
    }

    public PackageInspection Inspect(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var manifest = ReadManifest(archive);
        return new PackageInspection
        {
            Manifest = manifest,
            Entries = archive.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ToArray()
        };
    }

    public RpackResult Check(CheckPackageOptions options)
    {
        using var archive = ZipFile.OpenRead(options.PackagePath);
        var manifest = ReadManifest(archive);
        var manifestResult = ValidateManifest(manifest);
        if (!manifestResult.Success)
        {
            return manifestResult;
        }

        var checksumResult = VerifyChecksums(archive, manifest);
        if (!checksumResult.Success)
        {
            return checksumResult;
        }

        var repository = _gitClient.InspectRepository(options.RepositoryPath);
        if (manifest.RequiresCleanTree && !options.AllowDirty)
        {
            var clean = _gitClient.EnsureCleanWorkingTree(repository.RootPath);
            if (!clean.Success)
            {
                return clean;
            }
        }

        var baseWarning = GetBaseMismatchMessage(repository.RootPath, manifest);
        if (options.StrictBase && !string.IsNullOrWhiteSpace(baseWarning))
        {
            return RpackResult.Fail(baseWarning);
        }

        using var tempPatch = ExtractPatchToTempFile(archive, manifest.Patches[0]);
        var applyCheck = _gitClient.CheckApply(repository.RootPath, tempPatch.Path);
        if (!applyCheck.Success)
        {
            return applyCheck;
        }

        return string.IsNullOrWhiteSpace(baseWarning)
            ? applyCheck
            : RpackResult.Ok($"{applyCheck.Message}{Environment.NewLine}Warning: {baseWarning}");
    }

    public RpackResult Apply(ApplyPackageOptions options)
    {
        var check = Check(new CheckPackageOptions
        {
            PackagePath = options.PackagePath,
            RepositoryPath = options.RepositoryPath,
            AllowDirty = options.AllowDirty,
            StrictBase = options.StrictBase
        });

        if (!check.Success)
        {
            return check;
        }

        using var archive = ZipFile.OpenRead(options.PackagePath);
        var manifest = ReadManifest(archive);
        var repository = _gitClient.InspectRepository(options.RepositoryPath);
        using var tempPatch = ExtractPatchToTempFile(archive, manifest.Patches[0]);

        var apply = _gitClient.Apply(repository.RootPath, tempPatch.Path);
        if (!apply.Success)
        {
            return apply;
        }

        var applyId = CreateApplyId();
        var storedPatchPath = StoreAppliedPatch(repository, applyId, manifest, tempPatch.Path);
        AppendApplyLog(repository, new RpackApplyLog
        {
            ApplyId = applyId,
            PackageId = manifest.Id,
            Title = manifest.Title,
            AppliedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            BaseCommit = GetManifestBaseCommit(manifest),
            TargetHeadAtApply = _gitClient.ResolveCommit(repository.RootPath, "HEAD"),
            PatchPath = storedPatchPath
        });

        return RpackResult.Ok($"Package applied. Apply id: {applyId}");
    }

    public RpackResult UndoLastApply(string repositoryPath, bool allowDirty = false)
    {
        var repository = _gitClient.InspectRepository(repositoryPath);

        var logs = ReadApplyLogs(repository);
        var index = logs.FindLastIndex(log => string.IsNullOrWhiteSpace(log.UndoneAtUtc));
        if (index < 0)
        {
            return RpackResult.Fail("No applied package to undo.");
        }

        var log = logs[index];
        var patchPath = ResolveStatePath(repository, log.PatchPath);
        if (!File.Exists(patchPath))
        {
            return RpackResult.Fail($"Stored patch is missing: {log.PatchPath}");
        }

        var check = _gitClient.CheckReverseApply(repository.RootPath, patchPath);
        if (!check.Success)
        {
            return check;
        }

        var undo = _gitClient.ReverseApply(repository.RootPath, patchPath);
        if (!undo.Success)
        {
            return undo;
        }

        logs[index] = new RpackApplyLog
        {
            ApplyId = log.ApplyId,
            PackageId = log.PackageId,
            Title = log.Title,
            AppliedAtUtc = log.AppliedAtUtc,
            UndoneAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            BaseCommit = log.BaseCommit,
            TargetHeadAtApply = log.TargetHeadAtApply,
            PatchPath = log.PatchPath
        };
        WriteApplyLogs(repository, logs);

        return RpackResult.Ok($"Undone apply id: {log.ApplyId}");
    }

    public IReadOnlyList<RpackApplyLog> ReadHistory(string repositoryPath)
    {
        var repository = _gitClient.InspectRepository(repositoryPath);
        return ReadApplyLogs(repository);
    }

    private static RpackManifest ReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry(ManifestPath) ?? throw new InvalidOperationException("manifest.json is missing.");
        using var stream = entry.Open();
        var manifest = JsonSerializer.Deserialize(stream, RpackJsonContext.Default.RpackManifest);
        return manifest ?? throw new InvalidOperationException("manifest.json is invalid.");
    }

    private static RpackResult ValidateManifest(RpackManifest manifest)
    {
        if (manifest.Format != "rpack-v1")
        {
            return RpackResult.Fail($"Unsupported package format: {manifest.Format}");
        }

        if (manifest.Mode != "working-tree-patch")
        {
            return RpackResult.Fail($"Unsupported package mode: {manifest.Mode}");
        }

        if (manifest.Patches.Count != 1)
        {
            return RpackResult.Fail("Packages must contain exactly one patch in rpack-v1.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Patches[0].Path) || string.IsNullOrWhiteSpace(manifest.Patches[0].Sha256))
        {
            return RpackResult.Fail("Patch path or checksum is missing.");
        }

        return RpackResult.Ok("Manifest is valid.");
    }

    private static RpackResult VerifyChecksums(ZipArchive archive, RpackManifest manifest)
    {
        foreach (var patch in manifest.Patches)
        {
            if (!IsSafeArchivePath(patch.Path))
            {
                return RpackResult.Fail($"Unsafe archive path: {patch.Path}");
            }

            var entry = archive.GetEntry(patch.Path);
            if (entry is null)
            {
                return RpackResult.Fail($"Patch is missing: {patch.Path}");
            }

            using var memory = new MemoryStream();
            using (var stream = entry.Open())
            {
                stream.CopyTo(memory);
            }

            var actual = Sha256.ForBytes(memory.ToArray());
            if (!string.Equals(actual, patch.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return RpackResult.Fail($"Checksum mismatch for {patch.Path}.");
            }
        }

        return RpackResult.Ok("Checksums are valid.");
    }

    private string GetBaseMismatchMessage(string repositoryPath, RpackManifest manifest)
    {
        var baseCommit = GetManifestBaseCommit(manifest);
        if (string.IsNullOrWhiteSpace(baseCommit))
        {
            return "";
        }

        var head = _gitClient.ResolveCommit(repositoryPath, "HEAD");
        return string.Equals(head, baseCommit, StringComparison.OrdinalIgnoreCase)
            ? ""
            : $"Package base commit differs from repository HEAD. Package base: {baseCommit}; repository HEAD: {head}.";
    }

    private static string GetManifestBaseCommit(RpackManifest manifest)
    {
        return !string.IsNullOrWhiteSpace(manifest.Source?.BaseCommit)
            ? manifest.Source.BaseCommit
            : manifest.BaseCommit;
    }

    private static TempFile ExtractPatchToTempFile(ZipArchive archive, RpackPatch patch)
    {
        var entry = archive.GetEntry(patch.Path) ?? throw new InvalidOperationException($"Patch is missing: {patch.Path}");
        var tempPath = Path.Combine(Path.GetTempPath(), $"rpack-{Guid.NewGuid():N}.patch");
        using (var source = entry.Open())
        using (var destination = File.Create(tempPath))
        {
            source.CopyTo(destination);
        }

        return new TempFile(tempPath);
    }

    private static bool IsSafeArchivePath(string path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && !Path.IsPathRooted(path)
            && !path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Contains("..");
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(content);
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static string StoreAppliedPatch(GitRepository repository, string applyId, RpackManifest manifest, string patchPath)
    {
        var relativeDirectory = $"applied/{applyId}";
        var directory = ResolveStatePath(repository, relativeDirectory);
        Directory.CreateDirectory(directory);

        var relativePatchPath = $"{relativeDirectory}/change.patch";
        File.Copy(patchPath, ResolveStatePath(repository, relativePatchPath), overwrite: true);

        using var manifestFile = File.Create(ResolveStatePath(repository, $"{relativeDirectory}/manifest.json"));
        JsonSerializer.Serialize(manifestFile, manifest, RpackJsonContext.Default.RpackManifest);

        return relativePatchPath;
    }

    private static List<RpackApplyLog> ReadApplyLogs(GitRepository repository)
    {
        var logPath = ResolveStatePath(repository, ApplyLogPath);
        if (!File.Exists(logPath))
        {
            return [];
        }

        using var existing = File.OpenRead(logPath);
        return JsonSerializer.Deserialize(existing, RpackJsonContext.Default.ListRpackApplyLog) ?? [];
    }

    private static void AppendApplyLog(GitRepository repository, RpackApplyLog log)
    {
        var logs = ReadApplyLogs(repository);
        logs.Add(log);
        WriteApplyLogs(repository, logs);
    }

    private static void WriteApplyLogs(GitRepository repository, List<RpackApplyLog> logs)
    {
        Directory.CreateDirectory(repository.StatePath);
        using var output = File.Create(ResolveStatePath(repository, ApplyLogPath));
        JsonSerializer.Serialize(output, logs, RpackJsonContext.Default.ListRpackApplyLog);
    }

    private static string ResolveStatePath(GitRepository repository, string relativePath)
    {
        return Path.GetFullPath(Path.Combine(repository.StatePath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string CreateApplyId()
    {
        return $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
    }

    private static string GetRepositoryName(string rootPath)
    {
        return new DirectoryInfo(rootPath).Name;
    }

    private sealed class TempFile : IDisposable
    {
        public TempFile(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}

public sealed class CreatePackageOptions
{
    public required string RepositoryPath { get; init; }
    public required string OutputPath { get; init; }
    public string? FromRevision { get; init; }
    public string? ToRevision { get; init; }
    public bool Staged { get; init; }
    public string? Id { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
}

public sealed class CheckPackageOptions
{
    public required string PackagePath { get; init; }
    public required string RepositoryPath { get; init; }
    public bool AllowDirty { get; init; }
    public bool StrictBase { get; init; }
}

public sealed class ApplyPackageOptions
{
    public required string PackagePath { get; init; }
    public required string RepositoryPath { get; init; }
    public bool AllowDirty { get; init; }
    public bool StrictBase { get; init; }
}
