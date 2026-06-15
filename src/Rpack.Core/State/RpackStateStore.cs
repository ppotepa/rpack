using System.Text.Json;
using Rpack.Core;
using Rpack.Core.Patches;

namespace Rpack.Core.State;

public sealed class RpackStateStore
{
    private const string ApplyLogPath = "apply-log.json";

    public string SaveAppliedPackage(GitRepository repository, string applyId, RpackManifest manifest, IReadOnlyList<TempPatch> patches)
    {
        var relativeDirectory = $"applied/{applyId}";
        var directory = ResolveStatePath(repository, relativeDirectory);
        Directory.CreateDirectory(directory);

        foreach (var patch in patches)
        {
            var relativePatchPath = $"{relativeDirectory}/{patch.ManifestPath}";
            var destinationPath = ResolveStatePath(repository, relativePatchPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(patch.TempPath, destinationPath, overwrite: true);
        }

        using var manifestFile = File.Create(ResolveStatePath(repository, $"{relativeDirectory}/manifest.json"));
        JsonSerializer.Serialize(manifestFile, manifest, RpackJsonContext.Default.RpackManifest);

        return relativeDirectory;
    }

    public StoredPackage ReadStoredPackage(GitRepository repository, RpackApplyLog log)
    {
        if (!string.IsNullOrWhiteSpace(log.PackagePath))
        {
            var manifestPath = ResolveStatePath(repository, $"{log.PackagePath}/manifest.json");
            if (!File.Exists(manifestPath))
            {
                return StoredPackage.Missing($"Stored manifest is missing: {log.PackagePath}/manifest.json");
            }

            using var manifestFile = File.OpenRead(manifestPath);
            var manifest = JsonSerializer.Deserialize(manifestFile, RpackJsonContext.Default.RpackManifest)
                ?? throw new InvalidOperationException("Stored manifest is invalid.");
            var patchPaths = manifest.Patches
                .Select(patch => ResolveStatePath(repository, $"{log.PackagePath}/{patch.Path}"))
                .ToArray();
            var missingPatch = patchPaths.FirstOrDefault(path => !File.Exists(path));
            if (missingPatch is not null)
            {
                return StoredPackage.Missing($"Stored patch is missing: {missingPatch}");
            }

            return new StoredPackage(patchPaths);
        }

        if (!string.IsNullOrWhiteSpace(log.PatchPath))
        {
            var patchPath = ResolveStatePath(repository, log.PatchPath);
            return File.Exists(patchPath)
                ? new StoredPackage([patchPath])
                : StoredPackage.Missing($"Stored patch is missing: {log.PatchPath}");
        }

        return StoredPackage.Missing("Stored package path is missing from apply log.");
    }

    public IReadOnlyList<RpackApplyLog> ReadApplyLogs(GitRepository repository)
    {
        var logPath = ResolveStatePath(repository, ApplyLogPath);
        if (!File.Exists(logPath))
        {
            return [];
        }

        using var existing = File.OpenRead(logPath);
        return JsonSerializer.Deserialize(existing, RpackJsonContext.Default.ListRpackApplyLog) ?? [];
    }

    public void AppendApplyLog(GitRepository repository, RpackApplyLog log)
    {
        var logs = ReadApplyLogs(repository).ToList();
        logs.Add(log);
        WriteApplyLogs(repository, logs);
    }

    public void WriteApplyLogs(GitRepository repository, List<RpackApplyLog> logs)
    {
        Directory.CreateDirectory(repository.StatePath);
        using var output = File.Create(ResolveStatePath(repository, ApplyLogPath));
        JsonSerializer.Serialize(output, logs, RpackJsonContext.Default.ListRpackApplyLog);
    }

    public string ResolveStatePath(GitRepository repository, string relativePath)
    {
        return Path.GetFullPath(Path.Combine(repository.StatePath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    public string CreateApplyId()
    {
        return $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
    }
}

public sealed class StoredPackage
{
    public StoredPackage(IReadOnlyList<string> patchPaths)
    {
        PatchPaths = patchPaths;
    }

    public IReadOnlyList<string> PatchPaths { get; }
    public bool Exists => string.IsNullOrWhiteSpace(ErrorMessage);
    public string ErrorMessage { get; private init; } = "";

    public static StoredPackage Missing(string errorMessage)
    {
        return new StoredPackage([]) { ErrorMessage = errorMessage };
    }
}
