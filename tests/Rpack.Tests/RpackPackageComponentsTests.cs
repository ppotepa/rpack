using System.IO.Compression;
using System.Text;
using Rpack.Core;
using Rpack.Core.Actions;
using Rpack.Core.Issues;
using Rpack.Core.Packages;
using Rpack.Core.Patches;
using Rpack.Core.State;

namespace Rpack.Tests;

public class RpackPackageComponentsTests
{
    [Fact]
    public void ManifestNormalizer_DefaultsMissingCollectionsAndStrings()
    {
        var normalized = RpackManifestNormalizer.Normalize(new RpackManifest
        {
            Format = "",
            Id = null!,
            Title = null!,
            Description = null!,
            CreatedAtUtc = null!,
            BaseCommit = null!,
            Mode = "",
            Patches = null!,
            PreActions = null!,
            PostActions = null!,
            Validation = null!
        });

        Assert.Equal("rpack-v1", normalized.Format);
        Assert.Equal("", normalized.Id);
        Assert.Equal("", normalized.Title);
        Assert.Equal("", normalized.Description);
        Assert.Equal("", normalized.CreatedAtUtc);
        Assert.Equal("", normalized.BaseCommit);
        Assert.Equal("working-tree-patch", normalized.Mode);
        Assert.Empty(normalized.Patches);
        Assert.Empty(normalized.PreActions);
        Assert.Empty(normalized.PostActions);
        Assert.Empty(normalized.Validation);
    }

    [Fact]
    public void ManifestValidator_RejectsUnsupportedFormatAndEmptyPatchList()
    {
        var validator = new RpackManifestValidator();

        var formatFailure = validator.Validate(new RpackManifest
        {
            Format = "other",
            Mode = "working-tree-patch",
            Patches = [new RpackPatch { Path = "patches/change.patch", Kind = "git-diff", Sha256 = "abc" }]
        });

        var emptyFailure = validator.Validate(new RpackManifest
        {
            Format = "rpack-v1",
            Mode = "working-tree-patch",
            Patches = []
        });

        Assert.False(formatFailure.Success);
        Assert.Contains("Unsupported package format", formatFailure.Message);
        Assert.False(emptyFailure.Success);
        Assert.Contains("at least one patch", emptyFailure.Message);
    }

    [Fact]
    public void ChecksumVerifier_DetectsTamperedPatchBytes()
    {
        using var workspace = new TempWorkspace();
        var packagePath = Path.Combine(workspace.Path, "check.rpack");
        var originalBytes = Encoding.UTF8.GetBytes("patch-one");
        var hash = Sha256.ForBytes(originalBytes);

        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            WriteZipEntry(archive, "patches/change.patch", originalBytes);
        }

        var verifier = new RpackChecksumVerifier();
        var manifest = new RpackManifest
        {
            Format = "rpack-v1",
            Mode = "working-tree-patch",
            Patches = [new RpackPatch { Path = "patches/change.patch", Kind = "git-diff", Sha256 = hash }]
        };

        RpackResult valid;
        using (var archiveRead = ZipFile.OpenRead(packagePath))
        {
            valid = verifier.VerifyChecksums(archiveRead, manifest);
        }

        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
        {
            archive.GetEntry("patches/change.patch")?.Delete();
            WriteZipEntry(archive, "patches/change.patch", Encoding.UTF8.GetBytes("tampered"));
        }

        RpackResult invalid;
        using (var archiveTampered = ZipFile.OpenRead(packagePath))
        {
            invalid = verifier.VerifyChecksums(archiveTampered, manifest);
        }

        Assert.True(valid.Success, valid.Message);
        Assert.False(invalid.Success);
        Assert.Contains("Checksum mismatch", invalid.Message);
    }

    [Fact]
    public void ChecksumVerifier_DetailedReportsMissingAndMismatchedActionScripts()
    {
        using var workspace = new TempWorkspace();
        var packagePath = Path.Combine(workspace.Path, "check-actions.rpack");
        var patchBytes = Encoding.UTF8.GetBytes("patch-one");
        var patchHash = Sha256.ForBytes(patchBytes);
        var goodScript = Encoding.UTF8.GetBytes("Write-Output 'ok'");
        var badScript = Encoding.UTF8.GetBytes("Write-Output 'bad'");
        var goodHash = Sha256.ForBytes(goodScript);
        var badHash = Sha256.ForBytes(badScript);

        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            WriteZipEntry(archive, "patches/change.patch", patchBytes);
            WriteZipEntry(archive, "actions/good.ps1", goodScript);
            WriteZipEntry(archive, "manifest.json", Encoding.UTF8.GetBytes($$"""
                {
                  "Format": "rpack-v1",
                  "Mode": "working-tree-patch",
                  "Patches": [
                    { "Path": "patches/change.patch", "Kind": "git-diff", "Sha256": "{{patchHash}}" }
                  ],
                  "PreActions": [
                    { "Name": "Good", "Kind": "ps1", "Path": "actions/good.ps1", "Sha256": "{{goodHash}}", "Optional": false },
                    { "Name": "Missing", "Kind": "ps1", "Path": "actions/missing.ps1", "Sha256": "{{goodHash}}", "Optional": false },
                    { "Name": "Mismatch", "Kind": "ps1", "Path": "actions/mismatch.ps1", "Sha256": "{{goodHash}}", "Optional": false }
                  ],
                  "PostActions": [],
                  "Validation": []
                }
                """));
        }

        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
        {
            WriteZipEntry(archive, "actions/mismatch.ps1", badScript);
        }

        var verifier = new RpackChecksumVerifier();
        using var archiveRead = ZipFile.OpenRead(packagePath);
        var manifest = new RpackPackageReader().ReadManifest(archiveRead);
        var detailed = verifier.VerifyChecksumsDetailed(archiveRead, manifest);

        Assert.False(detailed.Success);
        Assert.Contains(detailed.Issues, issue => issue.Code == "action.script-missing");
        Assert.Contains(detailed.Issues, issue => issue.Code == "action.script-checksum-mismatch");
    }

    [Fact]
    public void ApplyPlanBuilder_PreparesSharedPlanForCheckAndApply()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateSubdirectory("source");
        var target = workspace.CreateSubdirectory("target");
        InitializeRepository(source);
        CopyDirectory(source, target);

        File.WriteAllText(System.IO.Path.Combine(source, "hello.txt"), "two");
        var packagePath = System.IO.Path.Combine(workspace.Path, "change.rpack");
        var createService = new RpackPackageService(new GitClient(new ProcessRunner()));
        var create = createService.Create(new CreatePackageOptions
        {
            RepositoryPath = source,
            OutputPath = packagePath,
            Id = "test-package",
            Title = "Test package"
        });
        Assert.True(create.Success, create.Message);

        var gitClient = new GitClient(new ProcessRunner());
        var builder = new RpackApplyPlanBuilder(
            gitClient,
            gitClient,
            gitClient,
            new RpackPackageReader(),
            new RpackManifestValidator(),
            new RpackChecksumVerifier(),
            new RpackPatchPreparer(),
            new RpackRepositoryPolicyService(gitClient, new RpackPatchParser()));

        var success = builder.TryBuild(new CheckPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = target
        }, out var plan, out var failure);

        Assert.True(success, failure.Message);
        using (plan)
        {
            Assert.True(plan!.CheckResult.Success, plan.CheckResult.Message);
            Assert.Equal("test-package", plan.Manifest.Id);
            Assert.EndsWith("target", plan.Repository.RootPath, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ApplyPlanBuilder_ProducesStructuredIssueReportForBaseMismatch()
    {
        using var workspace = new TempWorkspace();
        var source = workspace.CreateSubdirectory("source");
        var target = workspace.CreateSubdirectory("target");
        InitializeRepository(source);
        CopyDirectory(source, target);

        File.WriteAllText(System.IO.Path.Combine(source, "hello.txt"), "two");
        var packagePath = System.IO.Path.Combine(workspace.Path, "change.rpack");
        var createService = new RpackPackageService(new GitClient(new ProcessRunner()));
        var create = createService.Create(new CreatePackageOptions
        {
            RepositoryPath = source,
            OutputPath = packagePath,
            Id = "test-package",
            Title = "Test package"
        });
        Assert.True(create.Success, create.Message);

        File.WriteAllText(System.IO.Path.Combine(target, "note.txt"), "extra");
        Git(target, "add", "note.txt");
        Git(target, "commit", "-m", "advance target");

        var gitClient = new GitClient(new ProcessRunner());
        var builder = new RpackApplyPlanBuilder(
            gitClient,
            gitClient,
            gitClient,
            new RpackPackageReader(),
            new RpackManifestValidator(),
            new RpackChecksumVerifier(),
            new RpackPatchPreparer(),
            new RpackRepositoryPolicyService(gitClient, gitClient, new RpackPatchParser()));

        var success = builder.TryBuild(new CheckPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = target
        }, out var plan, out var failure);

        Assert.True(success, failure.Message);
        using (plan)
        {
            var report = plan!.CheckReport;
            Assert.True(report.Success, report.Summary);
            var issue = Assert.Single(report.Issues);
            Assert.Equal("repo.base-mismatch", issue.Code);
            Assert.Equal(RpackSeverity.Warning, issue.Severity);
            Assert.Equal(RpackStage.RepositoryPolicy, issue.Stage);
        }
    }

    private static void WriteZipEntry(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static void InitializeRepository(string path)
    {
        Git(path, "init");
        Git(path, "config", "user.email", "test@example.com");
        Git(path, "config", "user.name", "Test User");
        File.WriteAllText(System.IO.Path.Combine(path, "hello.txt"), "one");
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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rpack-components-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateSubdirectory(string name)
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
