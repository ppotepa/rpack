using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Rpack.Cli;
using Rpack.Core;

namespace Rpack.Tests;

public class RpackCliIntegrationTests
{
    [Fact]
    public void FakeProjectScenario_CreatesInspectsAppliesAndUndoes()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");

        InitializeRepository(source);
        InitializeRepository(target);

        SeedFakeProject(source);
        SeedFakeProject(target);

        File.WriteAllText(Path.Combine(source, "README.md"), "# Fake Project\n\nThis is a v2 fixture.\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(source, "src", "App.txt"), "App v2", new UTF8Encoding(false));

        var packagePath = Path.Combine(workspace.Path, "fake-project.rpack");
        var create = RunCli("create", "-o", packagePath, "--repo", source, "--id", "fake-project-1", "--title", "Fake Project");
        Assert.Equal(0, create.ExitCode);
        Assert.True(File.Exists(packagePath));

        var inspect = RunCli("inspect", packagePath, "--json");
        Assert.Equal(0, inspect.ExitCode);
        var inspectNode = JsonNode.Parse(inspect.StandardOutput);
        Assert.NotNull(inspectNode);
        Assert.Equal("fake-project-1", inspectNode!["manifest"]?["id"]?.GetValue<string>());
        Assert.Equal(2, inspectNode["diff"]?["fileCount"]?.GetValue<int>());

        var check = RunCli("check", packagePath, target);
        Assert.Equal(0, check.ExitCode);
        Assert.Contains("can be applied", check.StandardOutput, StringComparison.OrdinalIgnoreCase);

        var apply = RunCli("apply", packagePath, target);
        Assert.Equal(0, apply.ExitCode);
        Assert.Equal("# Fake Project\n\nThis is a v2 fixture.\n", NormalizeLineEndings(File.ReadAllText(Path.Combine(target, "README.md"), new UTF8Encoding(false))));
        Assert.Equal("App v2", NormalizeLineEndings(File.ReadAllText(Path.Combine(target, "src", "App.txt"), new UTF8Encoding(false))));
        Assert.Equal("Feature v1", NormalizeLineEndings(File.ReadAllText(Path.Combine(target, "src", "Feature.txt"), new UTF8Encoding(false))));

        var undo = RunCli("undo", target);
        Assert.Equal(0, undo.ExitCode);
        Assert.Equal("# Fake Project\n\nThis is a tiny project fixture used for end-to-end rpack scenarios.\n", NormalizeLineEndings(File.ReadAllText(Path.Combine(target, "README.md"), new UTF8Encoding(false))));
        Assert.Equal("App v1", NormalizeLineEndings(File.ReadAllText(Path.Combine(target, "src", "App.txt"), new UTF8Encoding(false))));
        Assert.Equal("Feature v1", NormalizeLineEndings(File.ReadAllText(Path.Combine(target, "src", "Feature.txt"), new UTF8Encoding(false))));
    }

    [Fact]
    public void IncrementalFakeProjectScenario_AppliesFiveFeaturePackages()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");

        InitializeRepository(source);
        InitializeRepository(target);
        SeedIncrementalProject(source);
        SeedIncrementalProject(target);

        for (var featureNumber = 1; featureNumber <= 5; featureNumber++)
        {
            ApplyFeaturePackage(workspace, source, target, featureNumber);
        }

        Assert.Equal("version=5", NormalizeLineEndings(File.ReadAllText(Path.Combine(target, "src", "Config.txt"), new UTF8Encoding(false))));
        Assert.Equal("Feature 5 enabled", NormalizeLineEndings(File.ReadAllText(Path.Combine(target, "src", "Feature.txt"), new UTF8Encoding(false))));
        Assert.Equal("Scenario 5 passes", NormalizeLineEndings(File.ReadAllText(Path.Combine(target, "tests", "Smoke.txt"), new UTF8Encoding(false))));

        var history = RunCli("history", target);
        Assert.Equal(0, history.ExitCode);
        Assert.Contains("fake-feature-1", history.StandardOutput);
        Assert.Contains("fake-feature-5", history.StandardOutput);
    }

    [Fact]
    public void InspectJson_IsValidJson()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateDirectory("source");
        InitializeRepository(source);
        File.WriteAllText(Path.Combine(source, "hello.txt"), "one", new UTF8Encoding(false));
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

        var result = RunCli("inspect", packagePath, "--json");

        Assert.Equal(0, result.ExitCode);
        var node = JsonNode.Parse(result.StandardOutput);
        Assert.NotNull(node);
        Assert.Equal("fixture-v1", node!["manifest"]?["id"]?.GetValue<string>());
    }

    [Fact]
    public void ValidatePackageRootJson_ReturnsStructuredIssue()
    {
        using var workspace = new TempWorkspace();
        var packageRoot = workspace.CreateDirectory("package-root");

        var result = RunCli("validate-package-root", packageRoot, "--json");

        Assert.Equal(3, result.ExitCode);
        var node = JsonNode.Parse(result.StandardOutput);
        Assert.NotNull(node);
        Assert.False(node!["success"]?.GetValue<bool>());
        Assert.Equal("manifest.missing", node["issues"]?[0]?["code"]?.GetValue<string>());
    }

    [Fact]
    public void PackRootJson_ReturnsArtifact()
    {
        using var workspace = new TempWorkspace();
        var root = workspace.CreateDirectory("package-root");
        Directory.CreateDirectory(Path.Combine(root, "payload", "text"));

        File.WriteAllText(Path.Combine(root, "payload", "text", "abc.txt"), "hello", new UTF8Encoding(false));
        var payloadHash = Sha256.ForBytes(File.ReadAllBytes(Path.Combine(root, "payload", "text", "abc.txt")));
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "Format": "rpack-agent-package",
              "SchemaVersion": "1.0",
              "Id": "agent-1",
              "Title": "Agent package",
              "CreatedAtUtc": "2026-06-14T00:00:00Z",
              "OperationsPath": "operations.json",
              "DefaultApplyStrategy": "ApplyReadyAndFallbacks",
              "RequiredCapabilities": ["operations", "payload-store"]
            }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "operations.json"), """
            {
              "Operations": [
                {
                  "Id": "op-1",
                  "Kind": "AddTextFile",
                  "Path": "src/File.txt",
                  "TargetSha256": "__HASH__",
                  "PayloadPath": "payload/text/abc.txt",
                  "ApplyPolicy": "PayloadIfBaseMatches",
                  "Required": true
                }
              ],
              "Groups": []
            }
            """.Replace("__HASH__", payloadHash), new UTF8Encoding(false));

        var output = Path.Combine(workspace.Path, "out.rpack");
        var result = RunCli("pack-root", root, "-o", output, "--json");

        Assert.Equal(0, result.ExitCode);
        var node = JsonNode.Parse(result.StandardOutput);
        Assert.NotNull(node);
        Assert.True(node!["success"]?.GetValue<bool>());
        Assert.Equal(output, node["artifacts"]?[0]?["path"]?.GetValue<string>());
    }

    [Fact]
    public void InvalidArgs_ReturnExit2()
    {
        var result = RunCli("inspect");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Usage:", result.StandardError);
    }

    [Fact]
    public void CheckChecksumError_ReturnExit3()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        InitializeRepository(source);
        InitializeRepository(target);
        File.WriteAllText(Path.Combine(target, "hello.txt"), "one", new UTF8Encoding(false));
        Git(target, "add", "hello.txt");
        Git(target, "commit", "-m", "seed");
        File.WriteAllText(Path.Combine(source, "hello.txt"), "one", new UTF8Encoding(false));
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
        ReplaceZipEntry(packagePath, "patches/change.patch", "tampered");

        var result = RunCli("check", packagePath, target);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("checksum", result.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InspectUnsafeArchive_ReturnExit4()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        InitializeRepository(source);
        InitializeRepository(target);
        File.WriteAllText(Path.Combine(target, "hello.txt"), "one", new UTF8Encoding(false));
        Git(target, "add", "hello.txt");
        Git(target, "commit", "-m", "seed");
        File.WriteAllText(Path.Combine(source, "hello.txt"), "one", new UTF8Encoding(false));
        Git(source, "add", "hello.txt");
        Git(source, "commit", "-m", "seed");
        File.WriteAllText(Path.Combine(source, "hello.txt"), "two", new UTF8Encoding(false));

        var packagePath = Path.Combine(workspace.Path, "unsafe.rpack");
        var service = new RpackPackageService(new GitClient(new ProcessRunner()));
        var create = service.Create(new CreatePackageOptions
        {
            RepositoryPath = source,
            OutputPath = packagePath,
            Id = "unsafe",
            Title = "Unsafe"
        });

        Assert.True(create.Success, create.Message);

        var result = RunCli("check", packagePath, target, "--path-prefix", "../unsafe");

        Assert.Equal(4, result.ExitCode);
        Assert.Contains("unsafe", result.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyActionFailure_ReturnExit5()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateDirectory("source");
        var target = workspace.CreateDirectory("target");
        InitializeRepository(source);
        InitializeRepository(target);
        File.WriteAllText(Path.Combine(target, "hello.txt"), "one", new UTF8Encoding(false));
        Git(target, "add", "hello.txt");
        Git(target, "commit", "-m", "seed");
        File.WriteAllText(Path.Combine(source, "hello.txt"), "one", new UTF8Encoding(false));
        Git(source, "add", "hello.txt");
        Git(source, "commit", "-m", "seed");
        File.WriteAllText(Path.Combine(source, "hello.txt"), "two", new UTF8Encoding(false));

        var packagePath = Path.Combine(workspace.Path, "action-failure.rpack");
        var service = new RpackPackageService(new GitClient(new ProcessRunner()));
        var create = service.Create(new CreatePackageOptions
        {
            RepositoryPath = source,
            OutputPath = packagePath,
            Id = "action-failure",
            Title = "Action failure"
        });

        Assert.True(create.Success, create.Message);
        ReplaceManifestActions(packagePath, "[]", $$"""
            [
              {
                "Name": "Fail post",
                "Kind": "command",
                "Command": "exit /b 1",
                "Optional": false
              }
            ]
            """);

        var result = RunCli("apply", packagePath, target);

        Assert.True(result.ExitCode == 5, result.StandardError);
        Assert.Contains("PostAction failed", result.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    private static CliResult RunCli(params string[] args)
    {
        var cliDll = Path.GetFullPath(typeof(CliOutputFormatter).Assembly.Location);
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(cliDll);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start CLI.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new CliResult(process.ExitCode, stdout, stderr);
    }

    private static void ReplaceZipEntry(string packagePath, string entryPath, string content)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update);
        archive.GetEntry(entryPath)?.Delete();
        WriteZipEntry(archive, entryPath, content);
    }

    private static void ReplaceManifestActions(string packagePath, string preActionsJson, string postActionsJson)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update);
        var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidOperationException("manifest.json missing.");
        string manifest;
        using (var stream = manifestEntry.Open())
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            manifest = reader.ReadToEnd()
                .Replace("\"PreActions\": []", $"\"PreActions\": {preActionsJson}", StringComparison.Ordinal)
                .Replace("\"PostActions\": []", $"\"PostActions\": {postActionsJson}", StringComparison.Ordinal);
        }

        manifestEntry.Delete();
        WriteZipEntry(archive, "manifest.json", manifest);
    }

    private static void WriteZipEntry(ZipArchive archive, string entryPath, string content)
    {
        WriteZipEntry(archive, entryPath, Encoding.UTF8.GetBytes(content));
    }

    private static void WriteZipEntry(ZipArchive archive, string entryPath, byte[] bytes)
    {
        var entry = archive.CreateEntry(entryPath);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static void InitializeRepository(string path)
    {
        Git(path, "init");
        Git(path, "config", "user.email", "test@example.com");
        Git(path, "config", "user.name", "Test User");
    }

    private static void CommitAll(string path, string message)
    {
        Git(path, "add", ".");
        Git(path, "commit", "-m", message);
    }

    private static void SeedFakeProject(string path)
    {
        Directory.CreateDirectory(Path.Combine(path, "src"));
        File.WriteAllText(Path.Combine(path, "README.md"), "# Fake Project\n\nThis is a tiny project fixture used for end-to-end rpack scenarios.\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(path, "src", "App.txt"), "App v1", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(path, "src", "Feature.txt"), "Feature v1", new UTF8Encoding(false));
        CommitAll(path, "seed");
    }

    private static void SeedIncrementalProject(string path)
    {
        Directory.CreateDirectory(Path.Combine(path, "src"));
        Directory.CreateDirectory(Path.Combine(path, "tests"));
        File.WriteAllText(Path.Combine(path, "README.md"), "# Incremental Fake Project\n\nFeature level 0.\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(path, "src", "App.txt"), "App bootstraps feature level 0", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(path, "src", "Config.txt"), "version=0", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(path, "src", "Feature.txt"), "Feature 0 disabled", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(path, "tests", "Smoke.txt"), "Scenario 0 passes", new UTF8Encoding(false));
        CommitAll(path, "seed incremental project");
    }

    private static void ApplyFeaturePackage(TempWorkspace workspace, string source, string target, int featureNumber)
    {
        UpdateIncrementalProjectForFeature(source, featureNumber);

        var packagePath = Path.Combine(workspace.Path, $"feature-{featureNumber}.rpack");
        var packageId = $"fake-feature-{featureNumber}";
        var create = RunCli(
            "create",
            "-o",
            packagePath,
            "--repo",
            source,
            "--id",
            packageId,
            "--title",
            $"Fake feature {featureNumber}");
        Assert.Equal(0, create.ExitCode);

        var inspect = RunCli("inspect", packagePath, "--json");
        Assert.Equal(0, inspect.ExitCode);
        var inspectNode = JsonNode.Parse(inspect.StandardOutput);
        Assert.NotNull(inspectNode);
        Assert.Equal(packageId, inspectNode!["manifest"]?["id"]?.GetValue<string>());
        Assert.True(inspectNode["diff"]?["fileCount"]?.GetValue<int>() >= 3);

        var check = RunCli("check", packagePath, target);
        Assert.Equal(0, check.ExitCode);

        var apply = RunCli("apply", packagePath, target);
        Assert.Equal(0, apply.ExitCode);

        Assert.Equal($"version={featureNumber}", NormalizeLineEndings(File.ReadAllText(Path.Combine(target, "src", "Config.txt"), new UTF8Encoding(false))));
        Assert.Equal($"Feature {featureNumber} enabled", NormalizeLineEndings(File.ReadAllText(Path.Combine(target, "src", "Feature.txt"), new UTF8Encoding(false))));
        Assert.Equal($"Scenario {featureNumber} passes", NormalizeLineEndings(File.ReadAllText(Path.Combine(target, "tests", "Smoke.txt"), new UTF8Encoding(false))));

        CommitAll(source, $"feature {featureNumber}");
        CommitAll(target, $"apply feature {featureNumber}");
    }

    private static void UpdateIncrementalProjectForFeature(string path, int featureNumber)
    {
        File.WriteAllText(Path.Combine(path, "README.md"), $"# Incremental Fake Project\n\nFeature level {featureNumber}.\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(path, "src", "App.txt"), $"App bootstraps feature level {featureNumber}", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(path, "src", "Config.txt"), $"version={featureNumber}", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(path, "src", "Feature.txt"), $"Feature {featureNumber} enabled", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(path, "tests", "Smoke.txt"), $"Scenario {featureNumber} passes", new UTF8Encoding(false));
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

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
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

    private sealed record CliResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rpack-cli-{Guid.NewGuid():N}");
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
