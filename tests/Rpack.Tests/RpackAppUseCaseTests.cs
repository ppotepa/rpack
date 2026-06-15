using System.IO.Compression;
using Microsoft.Extensions.DependencyInjection;
using Rpack.App;
using Rpack.Core;
using Rpack.Core.Actions;
using Rpack.Core.Packages;
using Rpack.Core.Patches;
using Rpack.Core.State;

namespace Rpack.Tests;

public class RpackAppUseCaseTests
{
    [Fact]
    public void CheckPackageUseCase_ValidatesPackageCreatedByCoreService()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        InitializeRepository(source);
        CopyDirectory(source, target);

        File.WriteAllText(Path.Combine(source, "hello.txt"), "two");
        var packagePath = Path.Combine(workspace.Path, "change.rpack");
        var service = new RpackPackageService(new GitClient(new ProcessRunner()));
        var create = service.Create(new CreatePackageOptions
        {
            RepositoryPath = source,
            OutputPath = packagePath,
            Id = "test-package",
            Title = "Test package"
        });
        Assert.True(create.Success, create.Message);

        using var provider = new ServiceCollection()
            .AddRpackApp()
            .BuildServiceProvider();
        var useCase = provider.GetRequiredService<CheckPackageUseCase>();

        var result = useCase.Execute(new CheckPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = target
        });

        Assert.True(result.Success, result.Message);
        Assert.Contains("can be applied", result.Message);
    }

    [Fact]
    public void ApplyPackageUseCase_AppliesPackageCreatedByCoreService()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        InitializeRepository(source);
        CopyDirectory(source, target);

        File.WriteAllText(Path.Combine(source, "hello.txt"), "two");
        var packagePath = Path.Combine(workspace.Path, "change.rpack");
        var service = new RpackPackageService(new GitClient(new ProcessRunner()));
        var create = service.Create(new CreatePackageOptions
        {
            RepositoryPath = source,
            OutputPath = packagePath,
            Id = "test-package",
            Title = "Test package"
        });
        Assert.True(create.Success, create.Message);

        using var provider = new ServiceCollection()
            .AddRpackApp()
            .BuildServiceProvider();
        var useCase = provider.GetRequiredService<ApplyPackageUseCase>();

        var result = useCase.Execute(new ApplyPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = target
        });

        Assert.True(result.Success, result.Message);
        Assert.Equal("two", File.ReadAllText(Path.Combine(target, "hello.txt")));
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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rpack-app-usecase-{Guid.NewGuid():N}");
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
