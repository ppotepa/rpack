using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Rpack.Core.Actions;
using Rpack.Core.Patches;
using Rpack.Core.Issues;
using Rpack.Core.Results;

namespace Rpack.Core.Packages;

public sealed class RpackPackageLintService
{
    private const string ActionPathPrefix = "actions/";
    private static readonly string[] ForbiddenPathPatterns =
    [
        "logs/",
        "artifacts/",
        "release/",
        "bin/",
        "obj/",
        ".env",
        ".key",
        ".pem",
        ".pdb",
        ".exe",
        ".dll",
        ".rpack",
        ".user",
        ".suo"
    ];

    private static readonly Regex SecretAssignmentPattern = new(
        @"(?i)\b(api[_-]?key|secret|token|password)\b\s*[:=]\s*[""']?[A-Za-z0-9_\-./+=]{6,}",
        RegexOptions.Compiled);

    private static readonly string[] LocalPathMarkers =
    [
        "D:\\Git\\",
        "C:\\Users\\",
        "/home/"
    ];

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".md",
        ".json",
        ".ps1",
        ".csproj",
        ".sln",
        ".slnx",
        ".xml",
        ".txt",
        ".yml",
        ".yaml"
    };

    private readonly RpackPackageReader _packageReader;
    private readonly RpackManifestValidator _manifestValidator;
    private readonly RpackChecksumVerifier _checksumVerifier;
    private readonly RpackPatchParser _patchParser;
    private readonly RpackPatchPathTransformer _patchTransformer;

    public RpackPackageLintService(
        RpackPackageReader packageReader,
        RpackManifestValidator manifestValidator,
        RpackChecksumVerifier checksumVerifier,
        RpackPatchParser patchParser,
        RpackPatchPathTransformer patchTransformer)
    {
        _packageReader = packageReader;
        _manifestValidator = manifestValidator;
        _checksumVerifier = checksumVerifier;
        _patchParser = patchParser;
        _patchTransformer = patchTransformer;
    }

    public RpackResult Execute(LintPackageOptions options)
    {
        return ToLegacyResult(ExecuteDetailed(options));
    }

    public RpackOperationResult ExecuteDetailed(LintPackageOptions options)
    {
        var pathPrefix = NormalizePathPrefix(options.PathPrefix);
        using var archive = _packageReader.OpenRead(options.PackagePath);
        var manifest = _packageReader.ReadManifest(archive);
        var manifestResult = _manifestValidator.Validate(manifest);
        if (!manifestResult.Success)
        {
            return RpackResultMapper.ToOperationResult(manifestResult, failureIssue: new RpackIssue(
                "manifest.validation-invalid",
                RpackSeverity.Error,
                RpackStage.ManifestValidation,
                manifestResult.Message,
                "Fix the manifest fields before running lint.",
                RawDetails: manifestResult.Message));
        }

        var checksumResult = _checksumVerifier.VerifyChecksumsDetailed(archive, manifest);
        if (!checksumResult.Success)
        {
            return checksumResult;
        }

        var issues = new List<LintIssue>();
        foreach (var patch in manifest.Patches)
        {
            var patchContent = _patchTransformer.RewritePatchPaths(_packageReader.ReadEntryText(archive, patch.Path), pathPrefix);
            var changedFiles = _patchParser.Analyze(patchContent);
            foreach (var file in changedFiles)
            {
                AddPathLintIssues(issues, patch.Path, file.Path);
            }

            AddContentLintIssues(issues, patch.Path, patchContent);
            AddQualityLintIssues(issues, patch.Path, patchContent, changedFiles);
        }

        foreach (var action in manifest.PreActions.Concat(manifest.PostActions))
        {
            AddActionLintIssues(issues, archive, action);
        }

        if (issues.Count == 0)
        {
            return RpackOperationResult.Ok("Lint passed.");
        }

        var hasErrors = issues.Any(issue => issue.Severity == "error");
        var builder = new StringBuilder();
        builder.AppendLine($"Lint found {issues.Count} issue(s):");
        foreach (var issue in issues)
        {
            builder.AppendLine($"[{issue.Severity}] {issue.Code}: {issue.Message}");
        }

        var structuredIssues = issues.Select(issue => new RpackIssue(
            issue.Code,
            issue.Severity == "error" ? RpackSeverity.Error : RpackSeverity.Warning,
            ClassifyStage(issue.Code),
            issue.Message,
            SuggestionFor(issue.Code),
            PatchPath: ExtractPatchPath(issue.Message),
            RawDetails: issue.Message)).ToArray();

        return hasErrors
            ? RpackOperationResult.Fail(builder.ToString().TrimEnd(), structuredIssues)
            : new RpackOperationResult(true, builder.ToString().TrimEnd(), structuredIssues, [], []);
    }

    private static RpackResult ToLegacyResult(RpackOperationResult result)
    {
        return result.Success
            ? RpackResult.Ok(result.Summary)
            : RpackResult.Fail(result.Summary);
    }

    private static void AddPathLintIssues(List<LintIssue> issues, string patchPath, string filePath)
    {
        var normalized = NormalizeGitPath(filePath);
        var lower = normalized.ToLowerInvariant();
        foreach (var pattern in ForbiddenPathPatterns)
        {
            if (pattern.EndsWith("/", StringComparison.Ordinal))
            {
                if (lower.StartsWith(pattern, StringComparison.Ordinal) || lower.Contains($"/{pattern}", StringComparison.Ordinal))
                {
                    issues.Add(new LintIssue("error", "forbidden-path", $"{patchPath}: {filePath} matches forbidden path pattern `{pattern}**`."));
                }

                continue;
            }

            if (lower.EndsWith(pattern, StringComparison.Ordinal))
            {
                issues.Add(new LintIssue("error", "forbidden-path", $"{patchPath}: {filePath} matches forbidden file pattern `*{pattern}`."));
            }
        }
    }

    private static void AddContentLintIssues(List<LintIssue> issues, string patchPath, string patchContent)
    {
        var addedLines = patchContent
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
            .Select(line => line[1..])
            .ToArray();

        var secretMatch = addedLines.FirstOrDefault(line => line.Contains("BEGIN PRIVATE KEY", StringComparison.OrdinalIgnoreCase)
            || SecretAssignmentPattern.IsMatch(line));
        if (secretMatch is not null)
        {
            issues.Add(new LintIssue("error", "secret-marker", $"{patchPath}: patch content contains a likely secret assignment or private key marker."));
        }

        foreach (var marker in LocalPathMarkers)
        {
            if (addedLines.Any(line => line.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new LintIssue("warning", "local-path", $"{patchPath}: patch content contains local path marker `{marker}`."));
            }
        }
    }

    private void AddActionLintIssues(List<LintIssue> issues, ZipArchive archive, RpackAction action)
    {
        var normalizedKind = NormalizeActionKind(action.Kind);
        if (normalizedKind is "powershell" or "batch")
        {
            if (!NormalizeGitPath(action.Path).StartsWith(ActionPathPrefix, StringComparison.Ordinal))
            {
                issues.Add(new LintIssue("error", "unsafe-action-path", $"{action.Name}: action path must live under `{ActionPathPrefix}`."));
                return;
            }

            var content = _packageReader.ReadEntryText(archive, action.Path);
            AddScriptContentLintIssues(issues, action.Path, content);
        }
        else if (normalizedKind == "command")
        {
            AddScriptContentLintIssues(issues, action.Name, action.Command);
        }
    }

    private static void AddScriptContentLintIssues(List<LintIssue> issues, string source, string content)
    {
        if (content.Contains("BEGIN PRIVATE KEY", StringComparison.OrdinalIgnoreCase)
            || SecretAssignmentPattern.IsMatch(content))
        {
            issues.Add(new LintIssue("error", "secret-marker", $"{source}: action content contains a likely secret assignment or private key marker."));
        }

        foreach (var marker in LocalPathMarkers)
        {
            if (content.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new LintIssue("warning", "local-path", $"{source}: action content contains local path marker `{marker}`."));
            }
        }

        if (content.Contains("Invoke-WebRequest", StringComparison.OrdinalIgnoreCase)
            || content.Contains("curl ", StringComparison.OrdinalIgnoreCase)
            || content.Contains("wget ", StringComparison.OrdinalIgnoreCase)
            || content.Contains("iex ", StringComparison.OrdinalIgnoreCase)
            || content.Contains("Start-BitsTransfer", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new LintIssue("warning", "action.network-risk", $"{source}: action content appears to download or execute remote content."));
        }

        if (content.Contains("Invoke-Expression", StringComparison.OrdinalIgnoreCase)
            || content.Contains("-EncodedCommand", StringComparison.OrdinalIgnoreCase)
            || content.Contains("FromBase64String", StringComparison.OrdinalIgnoreCase)
            || content.Contains("New-Object Net.WebClient", StringComparison.OrdinalIgnoreCase)
            || content.Contains("Set-ExecutionPolicy Bypass", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new LintIssue("warning", "action.untrusted-script", $"{source}: action content uses a high-risk script execution pattern."));
        }
    }

    private static void AddQualityLintIssues(List<LintIssue> issues, string patchPath, string patchContent, IReadOnlyList<RpackFileDiffStats> changedFiles)
    {
        foreach (var file in changedFiles)
        {
            var extension = Path.GetExtension(file.Path);
            if (file.Status == "binary" && TextExtensions.Contains(extension))
            {
                issues.Add(new LintIssue("warning", "binary-text-file", $"{patchPath}: {file.Path} is a text-like file represented as a binary patch."));
            }

            if (file.RemovedLines > 0 && file.AddedLines > 0)
            {
                var bigger = Math.Max(file.AddedLines, file.RemovedLines);
                var smaller = Math.Min(file.AddedLines, file.RemovedLines);
                if (bigger >= 200 && smaller > 0 && (double)bigger / smaller > 4)
                {
                    issues.Add(new LintIssue("warning", "possible-whole-file-rewrite", $"{patchPath}: {file.Path} looks like a possible whole-file rewrite (+{file.AddedLines}/-{file.RemovedLines})."));
                }
            }
        }

        var hunkAdded = 0;
        var hunkRemoved = 0;
        foreach (var rawLine in patchContent.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("@@ ", StringComparison.Ordinal))
            {
                FlushHunk();
                continue;
            }

            if (line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
            {
                hunkAdded++;
                if (line.Length > 1 && (line.EndsWith(' ') || line.EndsWith('\t')))
                {
                    issues.Add(new LintIssue("warning", "trailing-whitespace", $"{patchPath}: added line contains trailing whitespace."));
                }
            }
            else if (line.StartsWith('-') && !line.StartsWith("---", StringComparison.Ordinal))
            {
                hunkRemoved++;
            }
            else if (line.StartsWith(@"\ No newline", StringComparison.Ordinal))
            {
                issues.Add(new LintIssue("warning", "no-final-newline", $"{patchPath}: patch contains no-final-newline marker."));
            }
        }

        FlushHunk();

        void FlushHunk()
        {
            if (hunkAdded + hunkRemoved > 300)
            {
                issues.Add(new LintIssue("warning", "large-hunk", $"{patchPath}: hunk changes {hunkAdded + hunkRemoved} line(s)."));
            }

            hunkAdded = 0;
            hunkRemoved = 0;
        }
    }

    private static string NormalizeGitPath(string path)
    {
        return path.Replace('\\', '/').Trim();
    }

    private static string NormalizePathPrefix(string? pathPrefix)
    {
        return string.IsNullOrWhiteSpace(pathPrefix)
            ? ""
            : pathPrefix.Replace('\\', '/').Trim().Trim('/');
    }

    private static string NormalizeActionKind(string kind)
    {
        return kind.Trim().ToLowerInvariant();
    }

    private static RpackStage ClassifyStage(string code)
    {
        return code switch
        {
            "forbidden-path" or "unsafe-action-path" or "secret-marker" or "local-path" or "action.network-risk" or "action.untrusted-script" or "binary-text-file" or "possible-whole-file-rewrite" or "trailing-whitespace" or "no-final-newline" or "large-hunk" => RpackStage.PayloadValidation,
            _ => RpackStage.PayloadValidation
        };
    }

    private static string SuggestionFor(string code)
    {
        return code switch
        {
            "forbidden-path" => "Remove generated outputs or binaries from the package.",
            "unsafe-action-path" => "Move scripts under the actions/ directory or remove the action.",
            "secret-marker" => "Remove secrets from the package contents.",
            "local-path" => "Replace machine-specific paths with repository-relative paths.",
            "action.network-risk" => "Avoid remote downloads or inline execution in package actions.",
            "action.untrusted-script" => "Avoid high-risk script execution patterns in package actions.",
            "binary-text-file" => "Store text files as text patches, not binary patches.",
            "possible-whole-file-rewrite" => "Break large rewrites into smaller, reviewable changes.",
            "trailing-whitespace" => "Remove trailing whitespace from added lines.",
            "no-final-newline" => "Normalize final newlines before packaging.",
            "large-hunk" => "Split large hunks into smaller changes.",
            _ => "Review the lint message for details."
        };
    }

    private static string? ExtractPatchPath(string message)
    {
        var colon = message.IndexOf(':');
        return colon > 0 ? message[..colon] : null;
    }

    private sealed record LintIssue(string Severity, string Code, string Message);
}
