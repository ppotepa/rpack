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
