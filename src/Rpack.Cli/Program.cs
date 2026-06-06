using System.Reflection;
using Rpack.Core;

var command = args.FirstOrDefault();
if (command is "--version" or "-v" or "version")
{
    PrintVersion();
    return 0;
}

if (string.IsNullOrWhiteSpace(command) || command is "-h" or "--help" or "help")
{
    PrintHelp();
    return 0;
}

var gitClient = new GitClient(new ProcessRunner());
var service = new RpackPackageService(gitClient);

try
{
    return command switch
    {
        "create" => RunCreate(args.Skip(1).ToArray(), service),
        "inspect" => RunInspect(args.Skip(1).ToArray(), service),
        "check" => RunCheck(args.Skip(1).ToArray(), service),
        "apply" => RunApply(args.Skip(1).ToArray(), service),
        "open" => RunOpen(args.Skip(1).ToArray(), service, gitClient),
        "undo" => RunUndo(args.Skip(1).ToArray(), service),
        "history" => RunHistory(args.Skip(1).ToArray(), service),
        _ => Fail($"Unknown command: {command}")
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static int RunCreate(string[] args, RpackPackageService service)
{
    var options = CliOptions.Parse(args);
    var output = options.Value("-o") ?? options.Value("--output");
    if (string.IsNullOrWhiteSpace(output))
    {
        return Fail("Missing output path. Use -o package.rpack.");
    }

    var result = service.Create(new CreatePackageOptions
    {
        RepositoryPath = options.Value("--repo") ?? Directory.GetCurrentDirectory(),
        OutputPath = output,
        FromRevision = options.Value("--from"),
        ToRevision = options.Value("--to"),
        Staged = options.Has("--staged"),
        Id = options.Value("--id"),
        Title = options.Value("--title"),
        Description = options.Value("--description")
    });

    return PrintResult(result);
}

static int RunInspect(string[] args, RpackPackageService service)
{
    var options = CliOptions.Parse(args);
    if (options.Positionals.Count < 1)
    {
        return Fail("Usage: rpack inspect <package.rpack>");
    }

    var inspection = service.Inspect(new InspectPackageOptions
    {
        PackagePath = options.Positionals[0],
        PathPrefix = options.Value("--path-prefix")
    });
    Console.WriteLine($"{inspection.Manifest.Title} ({inspection.Manifest.Id})");
    Console.WriteLine($"format: {inspection.Manifest.Format}");
    Console.WriteLine($"mode: {inspection.Manifest.Mode}");
    Console.WriteLine($"createdAtUtc: {inspection.Manifest.CreatedAtUtc}");
    Console.WriteLine($"sourceRepository: {inspection.Manifest.Source?.Repository}");
    Console.WriteLine($"baseCommit: {inspection.Manifest.Source?.BaseCommit ?? inspection.Manifest.BaseCommit}");
    Console.WriteLine($"headCommit: {inspection.Manifest.Source?.HeadCommit}");
    Console.WriteLine($"changedFiles: {inspection.ChangedFiles.Count}");
    Console.WriteLine($"addedLines: {inspection.AddedLines}");
    Console.WriteLine($"removedLines: {inspection.RemovedLines}");
    if (inspection.ChangedFiles.Count > 0)
    {
        Console.WriteLine("patch:");
        foreach (var file in inspection.ChangedFiles)
        {
            Console.WriteLine($"  {file.Status,-8} +{file.AddedLines,-4} -{file.RemovedLines,-4} {file.Path}");
        }
    }

    Console.WriteLine("entries:");
    foreach (var entry in inspection.Entries)
    {
        Console.WriteLine($"  {entry}");
    }

    return 0;
}

static int RunCheck(string[] args, RpackPackageService service)
{
    var options = CliOptions.Parse(args);
    if (options.Positionals.Count < 1)
    {
        return Fail("Usage: rpack check <package.rpack> [repo]");
    }

    var result = service.Check(new CheckPackageOptions
    {
        PackagePath = options.Positionals[0],
        RepositoryPath = ResolveRepositoryArgument(options),
        AllowDirty = options.Has("--allow-dirty"),
        StrictBase = options.Has("--strict-base"),
        PathPrefix = options.Value("--path-prefix"),
        IgnoreSpaceChange = ResolveIgnoreSpaceChange(options)
    });

    return PrintResult(result);
}

static int RunApply(string[] args, RpackPackageService service)
{
    var options = CliOptions.Parse(args);
    if (options.Positionals.Count < 1)
    {
        return Fail("Usage: rpack apply <package.rpack> [repo]");
    }

    var result = service.Apply(new ApplyPackageOptions
    {
        PackagePath = options.Positionals[0],
        RepositoryPath = ResolveRepositoryArgument(options),
        AllowDirty = options.Has("--allow-dirty"),
        StrictBase = options.Has("--strict-base"),
        PathPrefix = options.Value("--path-prefix"),
        IgnoreSpaceChange = ResolveIgnoreSpaceChange(options)
    });

    return PrintResult(result);
}

static int RunOpen(string[] args, RpackPackageService service, GitClient gitClient)
{
    var options = CliOptions.Parse(args);
    if (options.Positionals.Count < 1)
    {
        return Fail("Usage: rpack open <package.rpack> [repo]");
    }

    var packagePath = Path.GetFullPath(options.Positionals[0]);
    var repositoryPath = ResolveOpenRepositoryArgument(options, gitClient, packagePath);
    var allowDirty = options.Has("--allow-dirty");
    var strictBase = options.Has("--strict-base");
    var pathPrefix = options.Value("--path-prefix");
    var ignoreSpaceChange = ResolveIgnoreSpaceChange(options);
    var allowedDirtyPaths = GetPackageDirtyException(repositoryPath, packagePath);

    var inspection = service.Inspect(new InspectPackageOptions
    {
        PackagePath = packagePath,
        PathPrefix = pathPrefix
    });

    PrintOpenSummary(inspection, repositoryPath, allowDirty, ignoreSpaceChange, pathPrefix);

    var check = service.Check(new CheckPackageOptions
    {
        PackagePath = packagePath,
        RepositoryPath = repositoryPath,
        AllowDirty = allowDirty,
        StrictBase = strictBase,
        PathPrefix = pathPrefix,
        AllowedDirtyPaths = allowedDirtyPaths,
        IgnoreSpaceChange = ignoreSpaceChange
    });

    if (!check.Success)
    {
        return PrintResult(check);
    }

    Console.WriteLine(check.Message);
    if (!options.Has("--yes") && !Confirm("Apply this package?"))
    {
        Console.WriteLine("Cancelled.");
        return 0;
    }

    var apply = service.Apply(new ApplyPackageOptions
    {
        PackagePath = packagePath,
        RepositoryPath = repositoryPath,
        AllowDirty = allowDirty,
        StrictBase = strictBase,
        PathPrefix = pathPrefix,
        AllowedDirtyPaths = allowedDirtyPaths,
        IgnoreSpaceChange = ignoreSpaceChange
    });

    return PrintResult(apply);
}

static int RunUndo(string[] args, RpackPackageService service)
{
    var options = CliOptions.Parse(args);
    var result = service.UndoLastApply(
        ResolveRepositoryArgument(options, positionalOffset: 0),
        options.Has("--allow-dirty"));

    return PrintResult(result);
}

static int RunHistory(string[] args, RpackPackageService service)
{
    var options = CliOptions.Parse(args);
    var logs = service.ReadHistory(ResolveRepositoryArgument(options, positionalOffset: 0));
    if (logs.Count == 0)
    {
        Console.WriteLine("No rpack history.");
        return 0;
    }

    foreach (var log in logs)
    {
        var status = string.IsNullOrWhiteSpace(log.UndoneAtUtc) ? "applied" : "undone";
        Console.WriteLine($"{log.ApplyId}  {status}  {log.PackageId}  {log.Title}");
        Console.WriteLine($"  appliedAtUtc: {log.AppliedAtUtc}");
        if (!string.IsNullOrWhiteSpace(log.UndoneAtUtc))
        {
            Console.WriteLine($"  undoneAtUtc: {log.UndoneAtUtc}");
        }
    }

    return 0;
}

static string ResolveRepositoryArgument(CliOptions options, int positionalOffset = 1)
{
    return options.Value("--repo")
        ?? (options.Positionals.Count > positionalOffset ? options.Positionals[positionalOffset] : Directory.GetCurrentDirectory());
}

static string ResolveOpenRepositoryArgument(CliOptions options, GitClient gitClient, string packagePath)
{
    var explicitRepository = options.Value("--repo")
        ?? (options.Positionals.Count > 1 ? options.Positionals[1] : null);
    if (!string.IsNullOrWhiteSpace(explicitRepository))
    {
        return explicitRepository;
    }

    var packageRepository = gitClient.FindRepositoryFrom(packagePath);
    if (packageRepository is not null)
    {
        return packageRepository.RootPath;
    }

    var currentRepository = gitClient.FindRepositoryFrom(Directory.GetCurrentDirectory());
    if (currentRepository is not null)
    {
        return currentRepository.RootPath;
    }

    throw new InvalidOperationException("Could not find a Git repository from the package location or current directory. Use --repo <repo>.");
}

static IReadOnlyList<string> GetPackageDirtyException(string repositoryPath, string packagePath)
{
    var repositoryRoot = Path.GetFullPath(repositoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    var fullPackagePath = Path.GetFullPath(packagePath);
    var comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    var repositoryPrefix = repositoryRoot + Path.DirectorySeparatorChar;
    if (!fullPackagePath.StartsWith(repositoryPrefix, comparison))
    {
        return [];
    }

    var relativePath = Path.GetRelativePath(repositoryRoot, fullPackagePath)
        .Replace('\\', '/');
    return relativePath.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relativePath)
        ? []
        : [relativePath];
}

static void PrintOpenSummary(PackageInspection inspection, string repositoryPath, bool allowDirty, bool ignoreSpaceChange, string? pathPrefix)
{
    Console.WriteLine($"{inspection.Manifest.Title} ({inspection.Manifest.Id})");
    Console.WriteLine($"packageId: {inspection.Manifest.Id}");
    Console.WriteLine($"mode: {inspection.Manifest.Mode}");
    Console.WriteLine($"targetRepository: {repositoryPath}");
    Console.WriteLine($"patches: {inspection.Manifest.Patches.Count}");
    Console.WriteLine($"changedFiles: {inspection.ChangedFiles.Count}");
    Console.WriteLine($"addedLines: {inspection.AddedLines}");
    Console.WriteLine($"removedLines: {inspection.RemovedLines}");
    Console.WriteLine($"allowDirty: {allowDirty}");
    Console.WriteLine($"patchContext: {(ignoreSpaceChange ? "whitespace-compatible" : "strict")}");
    if (!string.IsNullOrWhiteSpace(pathPrefix))
    {
        Console.WriteLine($"pathPrefix: {pathPrefix}");
    }

    if (inspection.ChangedFiles.Count > 0)
    {
        Console.WriteLine("patch:");
        foreach (var file in inspection.ChangedFiles.Take(20))
        {
            Console.WriteLine($"  {file.Status,-8} +{file.AddedLines,-4} -{file.RemovedLines,-4} {file.Path}");
        }

        if (inspection.ChangedFiles.Count > 20)
        {
            Console.WriteLine($"  ... {inspection.ChangedFiles.Count - 20} more file(s)");
        }
    }
}

static bool Confirm(string prompt)
{
    Console.Write($"{prompt} [y/N] ");
    var answer = Console.ReadLine();
    return string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
        || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);
}

static int PrintResult(RpackResult result)
{
    var output = result.Success ? Console.Out : Console.Error;
    output.WriteLine(result.Message);
    return result.Success ? 0 : 1;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine("""
    rpack - Repository Pack

    Usage:
      rpack create -o <package.rpack> [--repo <repo>]
      rpack create --staged -o <package.rpack> [--repo <repo>]
      rpack create --from <rev> --to <rev> -o <package.rpack> [--repo <repo>]
      rpack inspect <package.rpack> [--path-prefix <prefix>]
      rpack check <package.rpack> [repo] [--allow-dirty] [--strict-base] [--path-prefix <prefix>] [--strict]
      rpack apply <package.rpack> [repo] [--allow-dirty] [--strict-base] [--path-prefix <prefix>] [--strict]
      rpack open <package.rpack> [repo] [--allow-dirty] [--strict-base] [--path-prefix <prefix>] [--strict] [--yes]
      rpack undo [repo] [--allow-dirty]
      rpack history [repo]
      rpack --version

    Model:
      rpack applies validated patch packages to the Git working tree.
      It does not create commits, branches, or modify Git history.
      Local state is stored per repository under the Git metadata path for rpack.

    Safety options:
      --allow-dirty          Allow checking or applying into a dirty working tree.
      --strict-base          Fail when the package base commit differs from HEAD.
      --path-prefix <prefix> Prefix patch paths at check/apply time.
      --strict               Require exact patch context whitespace.
      --strict-whitespace    Alias for --strict.
      --ignore-space-change  Compatibility no-op; this is the default mode.
    """);
}

static bool ResolveIgnoreSpaceChange(CliOptions options)
{
    var strictWhitespace = options.Has("--strict") || options.Has("--strict-whitespace");
    if (strictWhitespace && options.Has("--ignore-space-change"))
    {
        throw new InvalidOperationException("Use either --strict/--strict-whitespace or --ignore-space-change, not both.");
    }

    return !strictWhitespace;
}

static void PrintVersion()
{
    var version = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "unknown";

    Console.WriteLine($"rpack {version}");
}

internal sealed class CliOptions
{
    private static readonly HashSet<string> OptionsWithValues = new(StringComparer.Ordinal)
    {
        "-o",
        "--output",
        "--repo",
        "--from",
        "--to",
        "--id",
        "--title",
        "--description",
        "--path-prefix"
    };

    private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Positionals { get; private set; } = [];

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
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
