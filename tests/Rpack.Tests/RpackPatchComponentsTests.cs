using Rpack.Core.Patches;

namespace Rpack.Tests;

public class RpackPatchComponentsTests
{
    [Fact]
    public void Parser_DetectsRenamesBinaryAndQuotedPaths()
    {
        var parser = new RpackPatchParser();
        var patch = """
            diff --git a/src/old name.txt b/src/new name.txt
            similarity index 100%
            rename from src/old name.txt
            rename to src/new name.txt
            diff --git a/assets/logo.png b/assets/logo.png
            Binary files a/assets/logo.png and b/assets/logo.png differ
            diff --git "a/docs/quoted name.txt" "b/docs/quoted name.txt"
            --- "a/docs/quoted name.txt"
            +++ "b/docs/quoted name.txt"
            @@ -1 +1 @@
            -one
            +two
            """;

        var files = parser.Analyze(patch);

        Assert.Equal(3, files.Count);
        Assert.Equal("Renamed", files[0].Status);
        Assert.Equal("src/new name.txt", files[0].Path);
        Assert.Equal("Modified", files[1].Status);
        Assert.True(files[1].IsBinary);
        Assert.Equal("Assets", files[1].Category);
        Assert.Equal("Modified", files[2].Status);
        Assert.Equal("docs/quoted name.txt", files[2].Path);
    }

    [Fact]
    public void PathTransformer_RewritesDiffAndBinaryLines()
    {
        var transformer = new RpackPatchPathTransformer();
        var patch = """
            diff --git a/src/file.txt b/src/file.txt
            --- a/src/file.txt
            +++ b/src/file.txt
            Binary files a/assets/logo.png and b/assets/logo.png differ
            rename from src/file.txt
            rename to src/file-renamed.txt
            """;

        var rewritten = transformer.RewritePatchPaths(patch, "nested");

        Assert.Contains("diff --git a/nested/src/file.txt b/nested/src/file.txt", rewritten);
        Assert.Contains("--- a/nested/src/file.txt", rewritten);
        Assert.Contains("+++ b/nested/src/file.txt", rewritten);
        Assert.Contains("Binary files a/nested/assets/logo.png and b/nested/assets/logo.png differ", rewritten);
        Assert.Contains("rename from nested/src/file.txt", rewritten);
        Assert.Contains("rename to nested/src/file-renamed.txt", rewritten);
    }
}
