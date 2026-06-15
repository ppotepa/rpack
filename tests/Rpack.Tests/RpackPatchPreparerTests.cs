using System.IO.Compression;
using System.Text;
using Rpack.Core;
using Rpack.Core.Patches;

namespace Rpack.Tests;

public class RpackPatchPreparerTests
{
    [Fact]
    public void ExtractPatchesToTempDirectory_RewritesPathPrefix()
    {
        using var workspace = new TempWorkspace();
        var archivePath = Path.Combine(workspace.Path, "package.rpack");
        var patchText = """
            diff --git a/src/file.txt b/src/file.txt
            --- a/src/file.txt
            +++ b/src/file.txt
            @@ -1 +1 @@
            -old
            +new
            """;

        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            WriteZipEntry(archive, "patches/change.patch", Encoding.UTF8.GetBytes(patchText));
        }

        using var archiveRead = ZipFile.OpenRead(archivePath);
        var preparer = new RpackPatchPreparer();
        using var tempPatchSet = preparer.ExtractPatchesToTempDirectory(
            archiveRead,
            [new RpackPatch { Path = "patches/change.patch", Kind = "git-diff", Sha256 = "x" }],
            "nested");

        var rewritten = File.ReadAllText(tempPatchSet.Patches[0].TempPath, Encoding.UTF8);

        Assert.Contains("diff --git a/nested/src/file.txt b/nested/src/file.txt", rewritten);
        Assert.Contains("--- a/nested/src/file.txt", rewritten);
        Assert.Contains("+++ b/nested/src/file.txt", rewritten);
    }

    [Fact]
    public void PrepareExistingAddedFiles_ModifyResolutionRewritesAddedPatch()
    {
        using var workspace = new TempWorkspace();
        var archivePath = Path.Combine(workspace.Path, "package.rpack");
        var repositoryPath = Path.Combine(workspace.Path, "repo");
        Directory.CreateDirectory(repositoryPath);
        File.WriteAllText(Path.Combine(repositoryPath, "existing.txt"), "old\n", new UTF8Encoding(false));

        var patchText = """
            diff --git a/existing.txt b/existing.txt
            new file mode 100644
            index 0000000..1111111
            --- /dev/null
            +++ b/existing.txt
            @@ -0,0 +1 @@
            +new
            """;

        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            WriteZipEntry(archive, "patches/change.patch", Encoding.UTF8.GetBytes(patchText));
        }

        using var archiveRead = ZipFile.OpenRead(archivePath);
        var preparer = new RpackPatchPreparer();
        using var tempPatchSet = preparer.ExtractPatchesToTempDirectory(
            archiveRead,
            [new RpackPatch { Path = "patches/change.patch", Kind = "git-diff", Sha256 = "x" }],
            "");

        var result = preparer.PrepareExistingAddedFiles(
            repositoryPath,
            new RpackManifest { CreatedAtUtc = "2026-06-14T00:00:00.0000000Z" },
            tempPatchSet,
            AddedFileConflictResolution.Modify);

        var rewritten = File.ReadAllText(tempPatchSet.Patches[0].TempPath, Encoding.UTF8);

        Assert.True(result.Success, result.Message);
        Assert.Contains("rewritten from add to modify", result.Message);
        Assert.Contains("diff --git a/existing.txt b/existing.txt", rewritten);
        Assert.Contains("--- a/existing.txt", rewritten);
        Assert.Contains("+++ b/existing.txt", rewritten);
        Assert.Contains("-old", rewritten);
        Assert.Contains("+new", rewritten);
    }

    [Fact]
    public void TryParseAddedFileConflictResolution_RejectsUnknownValue()
    {
        var preparer = new RpackPatchPreparer();

        var parsed = preparer.TryParseAddedFileConflictResolution("maybe", out var resolution, out var failure);

        Assert.False(parsed);
        Assert.Equal(AddedFileConflictResolution.Abort, resolution);
        Assert.False(failure.Success);
        Assert.Contains("Unknown added-file conflict resolution", failure.Message);
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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rpack-preparer-{Guid.NewGuid():N}");
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
