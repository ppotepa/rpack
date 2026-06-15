using System.Text.RegularExpressions;

namespace Rpack.Core.Patches;

public sealed class RpackPatchParser
{
    public IReadOnlyList<RpackFileDiffStats> Analyze(string patch)
    {
        var files = new List<PatchFileSummaryBuilder>();
        PatchFileSummaryBuilder? current = null;

        foreach (var rawLine in patch.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                AddCurrent();
                var path = ParseDiffGitPath(line);
                current = new PatchFileSummaryBuilder
                {
                    Path = path,
                    Status = "Modified",
                    Category = ClassifyPath(path)
                };
                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (line.StartsWith("new file mode ", StringComparison.Ordinal))
            {
                current.Status = "Added";
            }
            else if (line.StartsWith("deleted file mode ", StringComparison.Ordinal))
            {
                current.Status = "Deleted";
            }
            else if (line.StartsWith("rename to ", StringComparison.Ordinal))
            {
                current.Status = "Renamed";
                current.Path = StripGitPath(line["rename to ".Length..]);
                current.Category = ClassifyPath(current.Path);
            }
            else if (line.StartsWith("@@ ", StringComparison.Ordinal))
            {
                current.HunkCount++;
            }
            else if (line is "GIT binary patch" || line.StartsWith("Binary files ", StringComparison.Ordinal))
            {
                current.IsBinary = true;
                if (current.Status is not ("Added" or "Deleted"))
                {
                    current.Status = "Modified";
                }
            }
            else if (line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
            {
                current.AddedLines++;
            }
            else if (line.StartsWith('-') && !line.StartsWith("---", StringComparison.Ordinal))
            {
                current.RemovedLines++;
            }
        }

        AddCurrent();
        return files
            .Select(file => new RpackFileDiffStats
            {
                Path = file.Path,
                Status = file.Status,
                AddedLines = file.AddedLines,
                RemovedLines = file.RemovedLines,
                HunkCount = file.HunkCount,
                IsBinary = file.IsBinary,
                Category = file.Category
            })
            .ToArray();

        void AddCurrent()
        {
            if (current is not null && !string.IsNullOrWhiteSpace(current.Path))
            {
                files.Add(current);
            }
        }
    }

    public string BuildPatchTitle(RpackPatch patch, int patchNumber)
    {
        var name = Path.GetFileNameWithoutExtension(patch.Path);
        if (string.IsNullOrWhiteSpace(name))
        {
            return $"PATCH {patchNumber:D3}";
        }

        var numberMatch = Regex.Match(name, @"^(?<number>\d+)");
        var title = name;
        if (numberMatch.Success)
        {
            title = name[numberMatch.Length..];
            if (title.StartsWith("-", StringComparison.Ordinal) || title.StartsWith("_", StringComparison.Ordinal))
            {
                title = title[1..];
            }
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return $"PATCH {patchNumber:D3}";
        }

        var normalized = char.ToUpperInvariant(title[0]) + title[1..];
        return $"PATCH {patchNumber:D3} — {normalized}";
    }

    public RpackPatchDiffStats BuildPatchDiffStats(int index, RpackPatch patch, IReadOnlyList<RpackFileDiffStats> files)
    {
        return new RpackPatchDiffStats
        {
            PatchPath = patch.Path,
            Title = BuildPatchTitle(patch, index),
            FileCount = files.Count,
            AddedLines = files.Sum(file => file.AddedLines),
            RemovedLines = files.Sum(file => file.RemovedLines),
            HunkCount = files.Sum(file => file.HunkCount),
            Files = files
                .Select(file => new RpackFileDiffStats
                {
                    Path = file.Path,
                    Status = file.Status,
                    AddedLines = file.AddedLines,
                    RemovedLines = file.RemovedLines,
                    HunkCount = file.HunkCount,
                    IsBinary = file.IsBinary,
                    Category = file.Category
                })
                .ToArray()
        };
    }

    public static string ParseDiffGitPath(string line)
    {
        var remainder = line["diff --git ".Length..].Trim();
        var (_, rest) = ReadGitPathToken(remainder);
        if (string.IsNullOrWhiteSpace(rest))
        {
            return StripGitPath(remainder);
        }

        var (rightPath, _) = ReadGitPathToken(rest.TrimStart());
        return StripGitPath(rightPath);
    }

    public static string StripGitPath(string path)
    {
        path = path.Trim().Trim('"');
        return path.StartsWith("b/", StringComparison.Ordinal) || path.StartsWith("a/", StringComparison.Ordinal)
            ? path[2..]
            : path;
    }

    private static (string Token, string Remainder) ReadGitPathToken(string value)
    {
        value = value.TrimStart();
        if (value.Length == 0)
        {
            return ("", "");
        }

        if (value[0] == '"')
        {
            var end = 1;
            while (end < value.Length)
            {
                if (value[end] == '"' && value[end - 1] != '\\')
                {
                    var token = value[1..end];
                    var remainder = end + 1 < value.Length ? value[(end + 1)..] : "";
                    return (token, remainder);
                }

                end++;
            }

            return (value.Trim('"'), "");
        }

        var separator = value.IndexOf(' ');
        return separator < 0
            ? (value, "")
            : (value[..separator], value[(separator + 1)..]);
    }

    public static string ClassifyPath(string path)
    {
        var normalizedPath = path.Replace('\\', '/').ToLowerInvariant();

        if (normalizedPath.Contains("/test/", StringComparison.Ordinal) || normalizedPath.Contains("/tests/", StringComparison.Ordinal))
        {
            return "Tests";
        }

        var extension = Path.GetExtension(normalizedPath);
        return extension switch
        {
            ".cs" or ".csproj" or ".sln" or ".slnx" => "Code",
            ".md" or ".txt" or ".json" or ".yml" or ".yaml" or ".xml" => "Docs",
            ".ps1" or ".sh" or ".bash" or ".cmd" or ".bat" => "Scripts",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg" => "Assets",
            _ => normalizedPath.Contains("/assets/") || normalizedPath.Contains("/artifacts/") || normalizedPath.Contains("/release/") ? "Assets" : "Other"
        };
    }

    private sealed class PatchFileSummaryBuilder
    {
        public string Path { get; set; } = "";
        public string Status { get; set; } = "modified";
        public int AddedLines { get; set; }
        public int RemovedLines { get; set; }
        public int HunkCount { get; set; }
        public bool IsBinary { get; set; }
        public string Category { get; set; } = "Other";
    }
}
