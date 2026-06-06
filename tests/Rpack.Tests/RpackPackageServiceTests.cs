using System.IO.Compression;
using Rpack.Core;

namespace Rpack.Tests;

public class RpackPackageServiceTests
{
    [Fact]
    public void WorkingTreePatch_CanApplyToDifferentRepositoryHistory_ThenUndo()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        InitializeRepository(source);
        InitializeRepository(target);

        File.WriteAllText(Path.Combine(source, "hello.txt"), "two");

        File.WriteAllText(Path.Combine(target, "other.txt"), "target-only");
        Git(target, "add", "other.txt");
        Git(target, "commit", "-m", "target-only");

        var packagePath = Path.Combine(workspace.Path, "change.rpack");
        var service = new RpackPackageService(new GitClient(new ProcessRunner()));

        var create = service.Create(new CreatePackageOptions
        {
            RepositoryPath = source,
            OutputPath = packagePath,
            Id = "test-package",
            Title = "Test package"
        });
        var inspection = service.Inspect(packagePath);
        var check = service.Check(new CheckPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = target
        });
        var strictCheck = service.Check(new CheckPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = target,
            StrictBase = true
        });
        var apply = service.Apply(new ApplyPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = target
        });
        var historyAfterApply = service.ReadHistory(target);

        Assert.True(create.Success, create.Message);
        Assert.Single(inspection.ChangedFiles);
        Assert.Equal("hello.txt", inspection.ChangedFiles[0].Path);
        Assert.Equal(1, inspection.AddedLines);
        Assert.Equal(1, inspection.RemovedLines);
        Assert.True(check.Success, check.Message);
        Assert.Contains("Warning:", check.Message);
        Assert.False(strictCheck.Success);
        Assert.True(apply.Success, apply.Message);
        Assert.Equal("two", File.ReadAllText(Path.Combine(target, "hello.txt")));
        Assert.Single(historyAfterApply);

        var undo = service.UndoLastApply(target);
        var historyAfterUndo = service.ReadHistory(target);

        Assert.True(undo.Success, undo.Message);
        Assert.Equal("one", File.ReadAllText(Path.Combine(target, "hello.txt")));
        Assert.Single(historyAfterUndo);
        Assert.False(string.IsNullOrWhiteSpace(historyAfterUndo[0].UndoneAtUtc));
    }

    [Fact]
    public void StagedPatch_ContainsOnlyStagedChanges()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        InitializeRepository(source);
        CopyDirectory(source, target);

        File.WriteAllText(Path.Combine(source, "hello.txt"), "two");
        File.WriteAllText(Path.Combine(source, "unstaged.txt"), "not-in-package");
        Git(source, "add", "hello.txt");

        var packagePath = Path.Combine(workspace.Path, "staged.rpack");
        var service = new RpackPackageService(new GitClient(new ProcessRunner()));

        var create = service.Create(new CreatePackageOptions
        {
            RepositoryPath = source,
            OutputPath = packagePath,
            Staged = true
        });
        var apply = service.Apply(new ApplyPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = target
        });

        Assert.True(create.Success, create.Message);
        Assert.True(apply.Success, apply.Message);
        Assert.Equal("two", File.ReadAllText(Path.Combine(target, "hello.txt")));
        Assert.False(File.Exists(Path.Combine(target, "unstaged.txt")));
    }

    [Fact]
    public void MultiPatchPackage_AppliesInManifestOrder_AndUndoReversesAllPatches()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        InitializeRepository(source);
        CopyDirectory(source, target);

        File.WriteAllText(Path.Combine(source, "hello.txt"), "two");
        var patch1 = GitOutput(source, "diff", "--binary");
        Git(source, "add", "hello.txt");
        Git(source, "commit", "-m", "hello-two");

        File.WriteAllText(Path.Combine(source, "person.txt"), "Ada");
        Git(source, "add", "-N", "person.txt");
        var patch2 = GitOutput(source, "diff", "--binary");

        var packagePath = Path.Combine(workspace.Path, "multi.rpack");
        WriteManualPackage(packagePath, ("patches/0001-hello.patch", patch1), ("patches/0002-person.patch", patch2));
        var service = new RpackPackageService(new GitClient(new ProcessRunner()));

        var inspection = service.Inspect(packagePath);
        var check = service.Check(new CheckPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = target
        });
        var apply = service.Apply(new ApplyPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = target
        });
        var historyAfterApply = service.ReadHistory(target);

        Assert.Equal(2, inspection.ChangedFiles.Count);
        Assert.True(check.Success, check.Message);
        Assert.Contains("2 patch", check.Message);
        Assert.True(apply.Success, apply.Message);
        Assert.Equal("two", File.ReadAllText(Path.Combine(target, "hello.txt")));
        Assert.Equal("Ada", File.ReadAllText(Path.Combine(target, "person.txt")));
        Assert.Single(historyAfterApply);
        Assert.False(string.IsNullOrWhiteSpace(historyAfterApply[0].PackagePath));

        var undo = service.UndoLastApply(target);

        Assert.True(undo.Success, undo.Message);
        Assert.Equal("one", File.ReadAllText(Path.Combine(target, "hello.txt")));
        Assert.False(File.Exists(Path.Combine(target, "person.txt")));
    }

    [Fact]
    public void MultiPatchCheck_FailsOnLaterPatch_AndLeavesWorkingTreeUnchanged()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        InitializeRepository(source);
        CopyDirectory(source, target);

        File.WriteAllText(Path.Combine(source, "hello.txt"), "two");
        var patch1 = GitOutput(source, "diff", "--binary");
        var badPatch2 = """
            diff --git a/missing.txt b/missing.txt
            --- a/missing.txt
            +++ b/missing.txt
            @@ -1 +1 @@
            -old
            +new
            """;

        var packagePath = Path.Combine(workspace.Path, "multi-fail.rpack");
        WriteManualPackage(packagePath, ("patches/0001-hello.patch", patch1), ("patches/0002-bad.patch", badPatch2));
        var service = new RpackPackageService(new GitClient(new ProcessRunner()));

        var check = service.Check(new CheckPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = target
        });

        Assert.False(check.Success);
        Assert.Equal("one", File.ReadAllText(Path.Combine(target, "hello.txt")));
    }

    [Fact]
    public void Apply_WithPathPrefix_MapsSnapshotPathsToGitRootPaths()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");

        Git(source, "init");
        Git(source, "config", "user.email", "test@example.com");
        Git(source, "config", "user.name", "Test User");
        Directory.CreateDirectory(Path.Combine(source, "aot"));
        File.WriteAllText(Path.Combine(source, "aot", "hello.txt"), "one");
        Git(source, "add", "aot/hello.txt");
        Git(source, "commit", "-m", "initial");

        Git(target, "init");
        Git(target, "config", "user.email", "test@example.com");
        Git(target, "config", "user.name", "Test User");
        Directory.CreateDirectory(Path.Combine(target, "src", "aot"));
        File.WriteAllText(Path.Combine(target, "src", "aot", "hello.txt"), "one");
        Git(target, "add", "src/aot/hello.txt");
        Git(target, "commit", "-m", "initial");

        File.WriteAllText(Path.Combine(source, "aot", "hello.txt"), "two");
        var packagePath = Path.Combine(workspace.Path, "snapshot-root.rpack");
        var service = new RpackPackageService(new GitClient(new ProcessRunner()));
        var create = service.Create(new CreatePackageOptions
        {
            RepositoryPath = source,
            OutputPath = packagePath
        });
        var inspection = service.Inspect(new InspectPackageOptions
        {
            PackagePath = packagePath,
            PathPrefix = "src"
        });
        var checkWithoutPrefix = service.Check(new CheckPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = target
        });
        var checkWithPrefix = service.Check(new CheckPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = target,
            PathPrefix = "src"
        });
        var apply = service.Apply(new ApplyPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = target,
            PathPrefix = "src"
        });

        Assert.True(create.Success, create.Message);
        Assert.Single(inspection.ChangedFiles);
        Assert.Equal("src/aot/hello.txt", inspection.ChangedFiles[0].Path);
        Assert.False(checkWithoutPrefix.Success);
        Assert.True(checkWithPrefix.Success, checkWithPrefix.Message);
        Assert.True(apply.Success, apply.Message);
        Assert.Equal("two", File.ReadAllText(Path.Combine(target, "src", "aot", "hello.txt")));
    }

    [Fact]
    public void GitClient_FindRepositoryFrom_WalksUpFromPackageLocation()
    {
        using var workspace = new TempWorkspace();
        var repository = workspace.CreateDirectory("repo");
        InitializeRepository(repository);

        var nested = Path.Combine(repository, "incoming", "packages");
        Directory.CreateDirectory(nested);
        var packagePath = Path.Combine(nested, "change.rpack");
        File.WriteAllText(packagePath, "");

        var found = new GitClient(new ProcessRunner()).FindRepositoryFrom(packagePath);

        Assert.NotNull(found);
        Assert.Equal(Path.GetFullPath(repository), found.RootPath);
    }

    [Fact]
    public void Check_AllowsExplicitPackageFileDirtyException()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        InitializeRepository(source);
        CopyDirectory(source, target);
        File.WriteAllText(Path.Combine(source, "hello.txt"), "two");

        var incoming = Path.Combine(target, "incoming");
        Directory.CreateDirectory(incoming);
        var packagePath = Path.Combine(incoming, "change.rpack");
        var service = new RpackPackageService(new GitClient(new ProcessRunner()));
        var create = service.Create(new CreatePackageOptions
        {
            RepositoryPath = source,
            OutputPath = packagePath
        });

        var strictCheck = service.Check(new CheckPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = target
        });
        var openCheck = service.Check(new CheckPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = target,
            AllowedDirtyPaths = ["incoming/change.rpack"]
        });

        Assert.True(create.Success, create.Message);
        Assert.False(strictCheck.Success);
        Assert.Equal("Working tree is not clean.", strictCheck.Message);
        Assert.True(openCheck.Success, openCheck.Message);
    }

    [Fact]
    public void Check_FailsWhenChecksumDoesNotMatch()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        InitializeRepository(source);
        CopyDirectory(source, target);
        File.WriteAllText(Path.Combine(source, "hello.txt"), "two");

        var packagePath = Path.Combine(workspace.Path, "checksum.rpack");
        var service = new RpackPackageService(new GitClient(new ProcessRunner()));
        var create = service.Create(new CreatePackageOptions
        {
            RepositoryPath = source,
            OutputPath = packagePath
        });
        ReplaceZipEntry(packagePath, "patches/change.patch", "not the original patch");

        var check = service.Check(new CheckPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = target
        });

        Assert.True(create.Success, create.Message);
        Assert.False(check.Success);
        Assert.Contains("Checksum mismatch", check.Message);
    }

    [Fact]
    public void Check_FailsWhenManifestContainsUnsafePatchPath()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        InitializeRepository(source);
        CopyDirectory(source, target);
        File.WriteAllText(Path.Combine(source, "hello.txt"), "two");

        var packagePath = Path.Combine(workspace.Path, "unsafe.rpack");
        var service = new RpackPackageService(new GitClient(new ProcessRunner()));
        var create = service.Create(new CreatePackageOptions
        {
            RepositoryPath = source,
            OutputPath = packagePath
        });
        var manifest = ReadZipEntry(packagePath, "manifest.json")
            .Replace("\"Path\": \"patches/change.patch\"", "\"Path\": \"../evil.patch\"", StringComparison.Ordinal);
        ReplaceZipEntry(packagePath, "manifest.json", manifest);

        var check = service.Check(new CheckPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = target
        });

        Assert.True(create.Success, create.Message);
        Assert.False(check.Success);
        Assert.Contains("Unsafe archive path", check.Message);
    }

    [Fact]
    public void Apply_FailsOnDirtyWorkingTreeUnlessAllowed()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        InitializeRepository(source);
        CopyDirectory(source, target);
        File.WriteAllText(Path.Combine(source, "hello.txt"), "two");
        File.WriteAllText(Path.Combine(target, "dirty.txt"), "dirty");

        var packagePath = Path.Combine(workspace.Path, "dirty.rpack");
        var service = new RpackPackageService(new GitClient(new ProcessRunner()));
        var create = service.Create(new CreatePackageOptions
        {
            RepositoryPath = source,
            OutputPath = packagePath
        });
        var apply = service.Apply(new ApplyPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = target
        });

        Assert.True(create.Success, create.Message);
        Assert.False(apply.Success);
        Assert.Equal("Working tree is not clean.", apply.Message);
    }

    [Fact]
    public void Undo_FailsWhenExtraDirtyPathsExistUnlessAllowed()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        InitializeRepository(source);
        CopyDirectory(source, target);
        File.WriteAllText(Path.Combine(source, "hello.txt"), "two");

        var packagePath = Path.Combine(workspace.Path, "undo-dirty.rpack");
        var service = new RpackPackageService(new GitClient(new ProcessRunner()));
        var create = service.Create(new CreatePackageOptions
        {
            RepositoryPath = source,
            OutputPath = packagePath
        });
        var apply = service.Apply(new ApplyPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = target
        });
        File.WriteAllText(Path.Combine(target, "extra.txt"), "extra");

        var undo = service.UndoLastApply(target);
        var forcedUndo = service.UndoLastApply(target, allowDirty: true);

        Assert.True(create.Success, create.Message);
        Assert.True(apply.Success, apply.Message);
        Assert.False(undo.Success);
        Assert.Contains("changes outside", undo.Message);
        Assert.True(forcedUndo.Success, forcedUndo.Message);
        Assert.Equal("one", File.ReadAllText(Path.Combine(target, "hello.txt")));
        Assert.True(File.Exists(Path.Combine(target, "extra.txt")));
    }

    private static void InitializeRepository(string path)
    {
        Git(path, "init");
        Git(path, "config", "user.email", "test@example.com");
        Git(path, "config", "user.name", "Test User");
        File.WriteAllText(Path.Combine(path, "hello.txt"), "one");
        Git(path, "add", "hello.txt");
        Git(path, "commit", "-m", "initial");
    }

    private static void Git(string workingDirectory, params string[] args)
    {
        var result = new ProcessRunner().Run("git", args, workingDirectory);
        Assert.True(result.Success, result.CombinedOutput);
    }

    private static string GitOutput(string workingDirectory, params string[] args)
    {
        var result = new ProcessRunner().Run("git", args, workingDirectory);
        Assert.True(result.Success, result.CombinedOutput);
        return result.StandardOutput;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(source, target, StringComparison.Ordinal));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, target, StringComparison.Ordinal));
        }
    }

    private static string ReadZipEntry(string packagePath, string entryPath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entry = archive.GetEntry(entryPath) ?? throw new InvalidOperationException($"Missing entry: {entryPath}");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void ReplaceZipEntry(string packagePath, string entryPath, string content)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update);
        archive.GetEntry(entryPath)?.Delete();
        var entry = archive.CreateEntry(entryPath);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static void WriteManualPackage(string packagePath, params (string Path, string Content)[] patches)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var patchJson = new List<string>();
        var checksums = new List<string>();

        foreach (var patch in patches)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(patch.Content);
            var sha = Sha256.ForBytes(bytes);
            WriteZipEntry(archive, patch.Path, bytes);
            checksums.Add($"{sha}  {patch.Path}");
            patchJson.Add($$"""
                {
                  "Path": "{{patch.Path}}",
                  "Kind": "git-diff",
                  "Sha256": "{{sha}}"
                }
                """);
        }

        var manifest = $$"""
            {
              "Format": "rpack-v1",
              "Id": "manual-test-package",
              "Title": "Manual test package",
              "Description": "",
              "CreatedAtUtc": "2026-06-06T00:00:00Z",
              "BaseCommit": "",
              "RequiresCleanTree": true,
              "Mode": "working-tree-patch",
              "Patches": [
                {{string.Join($",{Environment.NewLine}", patchJson)}}
              ],
              "Validation": []
            }
            """;

        WriteZipEntry(archive, "manifest.json", manifest);
        WriteZipEntry(archive, "checksums.sha256", string.Join(Environment.NewLine, checksums));
        WriteZipEntry(archive, "README.md", "# Manual test package");
    }

    private static void WriteZipEntry(ZipArchive archive, string entryPath, string content)
    {
        WriteZipEntry(archive, entryPath, System.Text.Encoding.UTF8.GetBytes(content));
    }

    private static void WriteZipEntry(ZipArchive archive, string entryPath, byte[] bytes)
    {
        var entry = archive.CreateEntry(entryPath);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rpack-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateDirectory(string name)
        {
            var path = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(Path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(entry, FileAttributes.Normal);
                }

                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
