namespace Rpack.Core.Patches;

public sealed class RpackPatchPathTransformer
{
    public string RewritePatchPaths(string patch, string pathPrefix)
    {
        if (string.IsNullOrWhiteSpace(pathPrefix))
        {
            return patch;
        }

        var lines = patch.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = RewritePatchLine(lines[i].TrimEnd('\r'), pathPrefix);
        }

        return string.Join('\n', lines);
    }

    public string RewritePatchLine(string line, string pathPrefix)
    {
        if (line.StartsWith("diff --git ", StringComparison.Ordinal))
        {
            var parts = line["diff --git ".Length..].Split(' ', 2);
            return parts.Length == 2
                ? $"diff --git {PrefixPatchPath(parts[0], pathPrefix)} {PrefixPatchPath(parts[1], pathPrefix)}"
                : line;
        }

        if (line.StartsWith("--- ", StringComparison.Ordinal) || line.StartsWith("+++ ", StringComparison.Ordinal))
        {
            return $"{line[..4]}{PrefixPatchPath(line[4..], pathPrefix)}";
        }

        if (line.StartsWith("rename from ", StringComparison.Ordinal))
        {
            return $"rename from {PrefixPlainPath(line["rename from ".Length..], pathPrefix)}";
        }

        if (line.StartsWith("rename to ", StringComparison.Ordinal))
        {
            return $"rename to {PrefixPlainPath(line["rename to ".Length..], pathPrefix)}";
        }

        if (line.StartsWith("copy from ", StringComparison.Ordinal))
        {
            return $"copy from {PrefixPlainPath(line["copy from ".Length..], pathPrefix)}";
        }

        if (line.StartsWith("copy to ", StringComparison.Ordinal))
        {
            return $"copy to {PrefixPlainPath(line["copy to ".Length..], pathPrefix)}";
        }

        if (line.StartsWith("Binary files ", StringComparison.Ordinal))
        {
            return RewriteBinaryFilesLine(line, pathPrefix);
        }

        return line;
    }

    public string PrefixPatchPath(string path, string pathPrefix)
    {
        path = path.Trim();
        if (path == "/dev/null")
        {
            return path;
        }

        if (path.StartsWith("a/", StringComparison.Ordinal) || path.StartsWith("b/", StringComparison.Ordinal))
        {
            return $"{path[..2]}{pathPrefix}/{path[2..]}";
        }

        return PrefixPlainPath(path, pathPrefix);
    }

    public string PrefixPlainPath(string path, string pathPrefix)
    {
        path = path.Trim();
        return path == "/dev/null"
            ? path
            : $"{pathPrefix}/{path}";
    }

    public string RewriteBinaryFilesLine(string line, string pathPrefix)
    {
        const string prefix = "Binary files ";
        const string separator = " and ";
        const string suffix = " differ";
        if (!line.EndsWith(suffix, StringComparison.Ordinal))
        {
            return line;
        }

        var content = line[prefix.Length..^suffix.Length];
        var parts = content.Split(separator, 2, StringSplitOptions.None);
        return parts.Length == 2
            ? $"{prefix}{PrefixPatchPath(parts[0], pathPrefix)}{separator}{PrefixPatchPath(parts[1], pathPrefix)}{suffix}"
            : line;
    }
}
