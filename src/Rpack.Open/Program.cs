using System.Runtime.InteropServices;
using System.Windows.Forms;
using Rpack.Core;

namespace Rpack.Open;

internal static class Program
{
    private const int VkShift = 0x10;

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            ShowError("rpack open failed", ex.Message);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        var options = OpenOptions.Parse(args);
        if (options.Positionals.Count < 1)
        {
            ShowError("Missing package", "No .rpack package path was provided.");
            return 1;
        }

        var packagePath = Path.GetFullPath(options.Positionals[0]);
        if (!File.Exists(packagePath))
        {
            ShowError("Package not found", packagePath);
            return 1;
        }

        var gitClient = new GitClient(new ProcessRunner());
        var service = new RpackPackageService(gitClient);
        var shiftPressed = IsShiftPressed();
        var allowDirty = shiftPressed || options.Has("--allow-dirty");
        var dirtyReason = shiftPressed ? "Shift key" : options.Has("--allow-dirty") ? "--allow-dirty" : "";
        var pathPrefix = options.Value("--path-prefix");
        var repositoryPath = ResolveRepositoryPath(options, gitClient, packagePath);
        if (repositoryPath is null)
        {
            return 0;
        }

        var allowedDirtyPaths = GetPackageDirtyException(repositoryPath, packagePath);
        var inspection = service.Inspect(new InspectPackageOptions
        {
            PackagePath = packagePath,
            PathPrefix = pathPrefix
        });
        var dirtyPaths = allowDirty
            ? gitClient.GetChangedPaths(repositoryPath)
                .Where(path => !allowedDirtyPaths.Contains(NormalizeGitPath(path)))
                .ToArray()
            : Array.Empty<string>();
        var check = service.Check(new CheckPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = repositoryPath,
            AllowDirty = allowDirty,
            StrictBase = options.Has("--strict-base"),
            PathPrefix = pathPrefix,
            AllowedDirtyPaths = allowedDirtyPaths
        });

        if (!check.Success)
        {
            ShowCheckFailure(inspection, repositoryPath, check.Message, allowDirty);
            return 1;
        }

        var confirmation = BuildConfirmationMessage(
            inspection,
            repositoryPath,
            check.Message,
            allowDirty,
            dirtyReason,
            dirtyPaths.Length,
            pathPrefix);
        var answer = MessageBox.Show(
            confirmation,
            allowDirty ? "Apply rpack package with dirty tree allowed?" : "Apply rpack package?",
            MessageBoxButtons.YesNo,
            allowDirty ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes)
        {
            return 0;
        }

        var apply = service.Apply(new ApplyPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = repositoryPath,
            AllowDirty = allowDirty,
            StrictBase = options.Has("--strict-base"),
            PathPrefix = pathPrefix,
            AllowedDirtyPaths = allowedDirtyPaths
        });

        if (!apply.Success)
        {
            ShowError("Package apply failed", apply.Message);
            return 1;
        }

        MessageBox.Show(
            $"{apply.Message}{Environment.NewLine}{Environment.NewLine}Review the working tree before committing.{Environment.NewLine}Rollback: rpack undo",
            "rpack package applied",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        return 0;
    }

    private static string? ResolveRepositoryPath(OpenOptions options, GitClient gitClient, string packagePath)
    {
        var explicitRepository = options.Value("--repo")
            ?? (options.Positionals.Count > 1 ? options.Positionals[1] : null);
        if (!string.IsNullOrWhiteSpace(explicitRepository))
        {
            return gitClient.InspectRepository(explicitRepository).RootPath;
        }

        var packageRepository = gitClient.FindRepositoryFrom(packagePath);
        if (packageRepository is not null)
        {
            return packageRepository.RootPath;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the target Git repository for this .rpack package.",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return null;
        }

        return gitClient.InspectRepository(dialog.SelectedPath).RootPath;
    }

    private static IReadOnlyList<string> GetPackageDirtyException(string repositoryPath, string packagePath)
    {
        var repositoryRoot = Path.GetFullPath(repositoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPackagePath = Path.GetFullPath(packagePath);
        var repositoryPrefix = repositoryRoot + Path.DirectorySeparatorChar;
        if (!fullPackagePath.StartsWith(repositoryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var relativePath = Path.GetRelativePath(repositoryRoot, fullPackagePath)
            .Replace('\\', '/');
        return relativePath.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relativePath)
            ? []
            : [relativePath];
    }

    private static string NormalizeGitPath(string path)
    {
        return path.Replace('\\', '/').Trim();
    }

    private static void ShowCheckFailure(PackageInspection inspection, string repositoryPath, string message, bool allowDirty)
    {
        var hint = !allowDirty && string.Equals(message, "Working tree is not clean.", StringComparison.Ordinal)
            ? $"{Environment.NewLine}{Environment.NewLine}Hold Shift while double-clicking this package to allow a dirty working tree."
            : "";
        ShowError(
            "Package cannot be applied",
            $"{inspection.Manifest.Title} ({inspection.Manifest.Id}){Environment.NewLine}{Environment.NewLine}Target repo:{Environment.NewLine}{repositoryPath}{Environment.NewLine}{Environment.NewLine}Reason:{Environment.NewLine}{TrimForDialog(message)}{hint}");
    }

    private static string BuildConfirmationMessage(
        PackageInspection inspection,
        string repositoryPath,
        string checkMessage,
        bool allowDirty,
        string dirtyReason,
        int dirtyCount,
        string? pathPrefix)
    {
        var mode = allowDirty
            ? $"enabled by {dirtyReason}"
            : "disabled";
        var prefix = string.IsNullOrWhiteSpace(pathPrefix)
            ? ""
            : $"{Environment.NewLine}Path prefix: {pathPrefix}";
        var dirtyLine = allowDirty
            ? $"{Environment.NewLine}Dirty files already present: {dirtyCount}"
            : "";

        return $"""
            Apply this rpack package?

            Package: {inspection.Manifest.Title}
            Id: {inspection.Manifest.Id}
            Target repo:
            {repositoryPath}

            Patches: {inspection.Manifest.Patches.Count}
            Changed files: {inspection.ChangedFiles.Count}
            Lines: +{inspection.AddedLines} -{inspection.RemovedLines}
            Dirty mode: {mode}{dirtyLine}{prefix}

            Validation:
            {TrimForDialog(checkMessage)}
            """;
    }

    private static string TrimForDialog(string message)
    {
        const int maxLength = 1600;
        message = message.Trim();
        return message.Length <= maxLength
            ? message
            : $"{message[..maxLength]}{Environment.NewLine}...";
    }

    private static void ShowError(string title, string message)
    {
        MessageBox.Show(
            message,
            title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static bool IsShiftPressed()
    {
        return (GetKeyState(VkShift) & 0x8000) != 0;
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
}

internal sealed class OpenOptions
{
    private static readonly HashSet<string> OptionsWithValues = new(StringComparer.Ordinal)
    {
        "--repo",
        "--path-prefix"
    };

    private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Positionals { get; private set; } = [];

    public static OpenOptions Parse(string[] args)
    {
        var options = new OpenOptions();
        var positionals = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("-", StringComparison.Ordinal))
            {
                positionals.Add(arg);
                continue;
            }

            if (OptionsWithValues.Contains(arg))
            {
                if (i + 1 >= args.Length)
                {
                    throw new InvalidOperationException($"Missing value for {arg}.");
                }

                options._values[arg] = args[i + 1];
                i++;
            }
            else
            {
                options._values[arg] = null;
            }
        }

        options.Positionals = positionals;
        return options;
    }

    public bool Has(string name) => _values.ContainsKey(name);

    public string? Value(string name) => _values.TryGetValue(name, out var value) ? value : null;
}
