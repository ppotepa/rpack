using Rpack.Core;

var command = args.FirstOrDefault();
if (string.IsNullOrWhiteSpace(command) || command is "-h" or "--help" or "help")
{
    PrintHelp();
    return 0;
}

var service = new RpackPackageService(new GitClient(new ProcessRunner()));

try
{
    return command switch
    {
        "create" => RunCreate(args.Skip(1).ToArray(), service),
        "inspect" => RunInspect(args.Skip(1).ToArray(), service),
        "check" => RunCheck(args.Skip(1).ToArray(), service),
        "apply" => RunApply(args.Skip(1).ToArray(), service),
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

    var inspection = service.Inspect(options.Positionals[0]);
    Console.WriteLine($"{inspection.Manifest.Title} ({inspection.Manifest.Id})");
    Console.WriteLine($"format: {inspection.Manifest.Format}");
    Console.WriteLine($"mode: {inspection.Manifest.Mode}");
    Console.WriteLine($"createdAtUtc: {inspection.Manifest.CreatedAtUtc}");
    Console.WriteLine($"sourceRepository: {inspection.Manifest.Source?.Repository}");
    Console.WriteLine($"baseCommit: {inspection.Manifest.Source?.BaseCommit ?? inspection.Manifest.BaseCommit}");
    Console.WriteLine($"headCommit: {inspection.Manifest.Source?.HeadCommit}");
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
        StrictBase = options.Has("--strict-base")
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
        StrictBase = options.Has("--strict-base")
    });

    return PrintResult(result);
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
      rpack inspect <package.rpack>
      rpack check <package.rpack> [repo] [--allow-dirty] [--strict-base]
      rpack apply <package.rpack> [repo] [--allow-dirty] [--strict-base]
      rpack undo [repo] [--allow-dirty]
      rpack history [repo]

    Model:
      rpack applies validated patch packages to the Git working tree.
      It does not create commits, branches, or modify Git history.
      Local state is stored per repository under the Git metadata path for rpack.
    """);
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
        "--description"
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
