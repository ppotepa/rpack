using System.Diagnostics;
using System.Text;
using Rpack.AgentPackages;
using Rpack.Core;

namespace Rpack.Tests;

public class RpackGoldenFixtureTests
{
    [Fact]
    public void AgentPackageFixture_ValidatesPlansAndApplies()
    {
        using var workspace = new TempWorkspace();
        var fixtureRoot = CopyFixture(workspace, "agent-payload-modify");
        var target = workspace.CreateDirectory("target");
        Directory.CreateDirectory(Path.Combine(target, "src"));
        File.WriteAllText(Path.Combine(target, "src", "File.txt"), "old", new UTF8Encoding(false));
        InitializeRepository(target);

        var validator = new AgentPackageRootValidator();
        var validate = validator.Validate(fixtureRoot);
        Assert.True(validate.Success, validate.Message);

        var plan = new AgentApplyPlanBuilder().BuildPlan(fixtureRoot, target);
        Assert.True(plan.Success, plan.ErrorMessage);
        Assert.NotNull(plan.Operations);
        Assert.Contains(plan.Operations, op => op.Id == "op-1" && op.Status == AgentOperationStatus.ReadyWithPayloadFallback);

        var apply = new AgentPackageApplier().Apply(fixtureRoot, target);
        Assert.True(apply.Success, apply.Message);
        Assert.Equal("fixture-new", File.ReadAllText(Path.Combine(target, "src", "File.txt")).TrimEnd('\r', '\n'));

        var undo = new AgentPackageUndoer().Undo(target);
        Assert.True(undo.Success, undo.Message);
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "src", "File.txt")));
    }

    [Fact]
    public void AgentPayloadBaseMismatchFixture_IsConflict()
    {
        using var workspace = new TempWorkspace();
        var fixtureRoot = CopyFixture(workspace, "agent-payload-base-mismatch");
        var target = workspace.CreateDirectory("target");
        Directory.CreateDirectory(Path.Combine(target, "src"));
        File.WriteAllText(Path.Combine(target, "src", "File.txt"), "edited", new UTF8Encoding(false));
        InitializeRepository(target);

        var plan = new AgentApplyPlanBuilder().BuildPlan(fixtureRoot, target);

        Assert.True(plan.Success, plan.ErrorMessage);
        Assert.Contains(plan.Operations!, op => op.Id == "op-1" && op.Status == AgentOperationStatus.ConflictBaseHashMismatch);
    }

    [Fact]
    public void AgentAlreadyAppliedFixture_IsAlreadyApplied()
    {
        using var workspace = new TempWorkspace();
        var fixtureRoot = CopyFixture(workspace, "agent-already-applied");
        var target = workspace.CreateDirectory("target");
        Directory.CreateDirectory(Path.Combine(target, "src"));
        File.WriteAllText(Path.Combine(target, "src", "File.txt"), "fixture-new\n", new UTF8Encoding(false));
        InitializeRepository(target);

        var plan = new AgentApplyPlanBuilder().BuildPlan(fixtureRoot, target);

        Assert.True(plan.Success, plan.ErrorMessage);
        Assert.Contains(plan.Operations!, op => op.Id == "op-1" && op.Status == AgentOperationStatus.AlreadyApplied);
    }

    [Fact]
    public void AgentSecretMarkerFixture_IsRejected()
    {
        using var workspace = new TempWorkspace();
        var fixtureRoot = CopyFixture(workspace, "agent-secret-marker");

        var result = new AgentPackageRootValidator().ValidateDetailed(fixtureRoot);

        Assert.False(result.Success);
        Assert.Single(result.Issues);
        Assert.Equal("agent.risk-secret-marker", result.Issues[0].Code);
        Assert.Contains("secret marker", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AgentRiskyOutputFixture_IsRejected()
    {
        using var workspace = new TempWorkspace();
        var fixtureRoot = CopyFixture(workspace, "agent-risky-output");

        var result = new AgentPackageRootValidator().ValidateDetailed(fixtureRoot);

        Assert.False(result.Success);
        Assert.Single(result.Issues);
        Assert.Equal("agent.risk-generated-output", result.Issues[0].Code);
        Assert.Contains("Generated output path", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CurrentV1Fixture_CreatesPackageAndInspects()
    {
        using var workspace = new TempWorkspace();
        var source = CopyFixture(workspace, "current-v1-source");
        InitializeRepository(source);
        Git(source, "add", "hello.txt");
        Git(source, "commit", "-m", "seed");
        File.WriteAllText(Path.Combine(source, "hello.txt"), "two", new UTF8Encoding(false));

        var packagePath = Path.Combine(workspace.Path, "fixture.rpack");
        var service = new RpackPackageService(new GitClient(new ProcessRunner()));
        var create = service.Create(new CreatePackageOptions
        {
            RepositoryPath = source,
            OutputPath = packagePath,
            Id = "fixture-v1",
            Title = "Fixture v1"
        });

        Assert.True(create.Success, create.Message);
        Assert.True(File.Exists(packagePath));

        var inspection = service.Inspect(packagePath);
        Assert.Equal("fixture-v1", inspection.Manifest.Id);
        Assert.Single(inspection.ChangedFiles);
        Assert.Equal("hello.txt", inspection.ChangedFiles[0].Path);
    }

    private static string CopyFixture(TempWorkspace workspace, string fixtureName)
    {
        var source = FixturePath(fixtureName);
        var destination = workspace.CreateDirectory(fixtureName);
        CopyDirectory(source, destination);
        return destination;
    }

    private static string FixturePath(params string[] parts)
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", Path.Combine(parts)));
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void InitializeRepository(string path)
    {
        Git(path, "init");
    }

    private static void Git(string workingDirectory, params string[] args)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output : error);
        }
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rpack-fixture-{Guid.NewGuid():N}");
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
            if (!Directory.Exists(Path))
            {
                return;
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(Path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(entry, FileAttributes.Normal);
            }

            Directory.Delete(Path, recursive: true);
        }
    }
}
