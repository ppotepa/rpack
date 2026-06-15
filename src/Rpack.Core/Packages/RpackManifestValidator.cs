namespace Rpack.Core.Packages;

public sealed class RpackManifestValidator
{
    private static readonly HashSet<string> AllowedActionKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "powershell",
        "ps1",
        "batch",
        "bat",
        "cmd",
        "command",
        "shell",
        "rpack.commit"
    };

    private const string ActionPathPrefix = "actions/";

    public RpackResult Validate(RpackManifest manifest)
    {
        if (manifest.Format != "rpack-v1")
        {
            return RpackResult.Fail($"Unsupported package format: {manifest.Format}");
        }

        if (manifest.Mode != "working-tree-patch")
        {
            return RpackResult.Fail($"Unsupported package mode: {manifest.Mode}");
        }

        if (manifest.Patches.Count < 1)
        {
            return RpackResult.Fail("Packages must contain at least one patch in rpack-v1.");
        }

        foreach (var patch in manifest.Patches)
        {
            if (string.IsNullOrWhiteSpace(patch.Path) || string.IsNullOrWhiteSpace(patch.Sha256))
            {
                return RpackResult.Fail("Patch path or checksum is missing.");
            }

            if (patch.Kind != "git-diff")
            {
                return RpackResult.Fail($"Unsupported patch kind: {patch.Kind}");
            }
        }

        foreach (var action in manifest.PreActions)
        {
            var validation = ValidateAction(action, "PreActions");
            if (!validation.Success)
            {
                return validation;
            }
        }

        foreach (var action in manifest.PostActions)
        {
            var validation = ValidateAction(action, "PostActions");
            if (!validation.Success)
            {
                return validation;
            }
        }

        return RpackResult.Ok("Manifest is valid.");
    }

    private static RpackResult ValidateAction(RpackAction action, string listName)
    {
        if (string.IsNullOrWhiteSpace(action.Name))
        {
            return RpackResult.Fail($"{listName} contains an action with a missing Name.");
        }

        if (string.IsNullOrWhiteSpace(action.Kind))
        {
            return RpackResult.Fail($"{listName}:{action.Name} has a missing Kind.");
        }

        if (!AllowedActionKinds.Contains(action.Kind))
        {
            return RpackResult.Fail($"Unsupported action kind: {action.Kind}");
        }

        var normalizedKind = RpackActionRunner.NormalizeKind(action.Kind);
        if (normalizedKind is "powershell" or "batch")
        {
            if (string.IsNullOrWhiteSpace(action.Path))
            {
                return RpackResult.Fail($"{listName}:{action.Name} requires Path.");
            }

            if (!RpackPackageReader.IsSafeArchivePath(action.Path) || !NormalizeGitPath(action.Path).StartsWith(ActionPathPrefix, StringComparison.Ordinal))
            {
                return RpackResult.Fail($"Unsafe action path: {action.Path}. Actions must live under {ActionPathPrefix}.");
            }

            if (string.IsNullOrWhiteSpace(action.Sha256))
            {
                return RpackResult.Fail($"{listName}:{action.Name} action checksum is missing.");
            }
        }
        else if (normalizedKind == "command")
        {
            if (string.IsNullOrWhiteSpace(action.Command))
            {
                return RpackResult.Fail($"{listName}:{action.Name} requires Command.");
            }
        }
        else if (normalizedKind == "rpack.commit" && !string.IsNullOrWhiteSpace(action.Path))
        {
            return RpackResult.Fail($"{listName}:{action.Name} rpack.commit must not declare Path.");
        }

        return RpackResult.Ok("Action is valid.");
    }

    private static string NormalizeGitPath(string path)
    {
        return path.Replace('\\', '/').Trim();
    }
}
