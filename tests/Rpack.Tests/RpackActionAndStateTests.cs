using System.IO.Compression;
using System.Text;
using Rpack.Core;
using Rpack.Core.Actions;
using Rpack.Core.Results;
using Rpack.Core.Patches;
using Rpack.Core.State;

namespace Rpack.Tests;

public class RpackActionAndStateTests
{
    [Fact]
    public void ActionExecutionService_ExtractsActionFilesToTempDirectory()
    {
        using var workspace = new TempWorkspace();
        var archivePath = Path.Combine(workspace.Path, "actions.rpack");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            WriteZipEntry(archive, "actions/pre.ps1", Encoding.UTF8.GetBytes("Write-Host pre"));
        }

        using var archiveRead = ZipFile.OpenRead(archivePath);
        var service = new RpackActionExecutionService(new GitClient(new ProcessRunner()));
        using var tempActionSet = service.ExtractActionsToTempDirectory(
            archiveRead,
            [new RpackAction { Name = "pre", Kind = "powershell", Path = "actions/pre.ps1" }]);

        Assert.True(File.Exists(tempActionSet.ActionPaths["actions/pre.ps1"]));
        Assert.Equal("Write-Host pre", File.ReadAllText(tempActionSet.ActionPaths["actions/pre.ps1"], Encoding.UTF8));
    }

    [Fact]
    public void StateStore_SavesPackagesAndAppendsLogs()
    {
        using var workspace = new TempWorkspace();
        var repository = new GitRepository(
            Path.Combine(workspace.Path, "repo"),
            Path.Combine(workspace.Path, "repo", ".git", "rpack"));
        Directory.CreateDirectory(repository.RootPath);

        var patch = new TempPatch("patches/change.patch", Path.Combine(workspace.Path, "patch.tmp"));
        File.WriteAllText(patch.TempPath, "patch content", new UTF8Encoding(false));

        var store = new RpackStateStore();
        var packagePath = store.SaveAppliedPackage(
            repository,
            "apply-123",
            new RpackManifest
            {
                Id = "pkg-1",
                Title = "Test package",
                Patches = [new RpackPatch { Path = patch.ManifestPath, Kind = "git-diff", Sha256 = "x" }]
            },
            [patch]);

        var log = new RpackApplyLog
        {
            ApplyId = "apply-123",
            PackageId = "pkg-1",
            Title = "Test package",
            AppliedAtUtc = "2026-06-14T00:00:00.0000000Z",
            PackagePath = packagePath
        };

        store.AppendApplyLog(repository, log);
        var logs = store.ReadApplyLogs(repository);
        var storedPackage = store.ReadStoredPackage(repository, log);

        Assert.Equal("applied/apply-123", packagePath);
        Assert.Single(logs);
        Assert.True(storedPackage.Exists);
        Assert.Single(storedPackage.PatchPaths);
        Assert.True(File.Exists(storedPackage.PatchPaths[0]));
        Assert.Equal("patch content", File.ReadAllText(storedPackage.PatchPaths[0], Encoding.UTF8));
    }

    [Fact]
    public void StateStore_CreatesApplyIdsAndResolvesStatePaths()
    {
        using var workspace = new TempWorkspace();
        var repository = new GitRepository(
            Path.Combine(workspace.Path, "repo"),
            Path.Combine(workspace.Path, "repo", ".git", "rpack"));
        Directory.CreateDirectory(repository.RootPath);

        var store = new RpackStateStore();
        var applyId = store.CreateApplyId();
        var resolved = store.ResolveStatePath(repository, $"applied/{applyId}/manifest.json");

        Assert.Matches(@"^\d{14}-[a-f0-9]{8}$", applyId);
        Assert.Contains(Path.Combine("repo", ".git", "rpack"), resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UndoService_DetailedReportsMissingApplyLogAndStoredPackage()
    {
        using var workspace = new TempWorkspace();
        var repository = new GitRepository(
            Path.Combine(workspace.Path, "repo"),
            Path.Combine(workspace.Path, "repo", ".git", "rpack"));
        Directory.CreateDirectory(repository.RootPath);
        Git(repository.RootPath, "init");
        Git(repository.RootPath, "config", "user.email", "test@example.com");
        Git(repository.RootPath, "config", "user.name", "Test User");

        var service = new RpackPackageUndoService(
            new GitRepositoryInspector(new GitClient(new ProcessRunner())),
            new GitPatchOperations(new GitClient(new ProcessRunner())),
            new RpackStateStore(),
            new RpackRepositoryPolicyService(new GitClient(new ProcessRunner()), new RpackPatchParser()));

        var missingLog = service.ExecuteDetailed(repository.RootPath);
        Assert.False(missingLog.Success);
        Assert.Contains(missingLog.Issues, issue => issue.Code == "state.apply-log-missing");

        var store = new RpackStateStore();
        var log = new RpackApplyLog
        {
            ApplyId = "apply-1",
            PackageId = "pkg-1",
            Title = "Test package",
            AppliedAtUtc = "2026-06-14T00:00:00Z",
            PackagePath = "applied/apply-1"
        };
        store.AppendApplyLog(repository, log);

        var missingPackage = service.ExecuteDetailed(repository.RootPath);
        Assert.False(missingPackage.Success);
        Assert.Contains(missingPackage.Issues, issue => issue.Code == "state.stored-package-missing");
    }

    [Fact]
    public void HistoryService_DetailedReportsInvalidHistoryJson()
    {
        using var workspace = new TempWorkspace();
        var repository = new GitRepository(
            Path.Combine(workspace.Path, "repo"),
            Path.Combine(workspace.Path, "repo", ".git", "rpack"));
        Directory.CreateDirectory(repository.RootPath);
        Git(repository.RootPath, "init");
        Git(repository.RootPath, "config", "user.email", "test@example.com");
        Git(repository.RootPath, "config", "user.name", "Test User");

        var statePath = Path.Combine(repository.StatePath, "apply-log.json");
        Directory.CreateDirectory(repository.StatePath);
        File.WriteAllText(statePath, "{ invalid json", Encoding.UTF8);

        var service = new RpackPackageHistoryService(
            new GitRepositoryInspector(new GitClient(new ProcessRunner())),
            new RpackStateStore());

        var result = service.ExecuteDetailed(repository.RootPath);

        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Code == "state.history-invalid");
    }

    private static void Git(string workingDirectory, params string[] args)
    {
        var result = new ProcessRunner().Run("git", args, workingDirectory);
        Assert.True(result.Success, result.CombinedOutput);
    }

    private static void WriteZipEntry(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rpack-actions-state-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
