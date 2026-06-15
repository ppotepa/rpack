using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rpack.AgentPackages;
using Rpack.App;
using Rpack.Core;
using Rpack.Core.Results;
using Rpack.Cli;

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

var services = new ServiceCollection().AddRpackApp();
using var serviceProvider = services.BuildServiceProvider();
var gitClient = serviceProvider.GetRequiredService<GitClient>();
var createUseCase = serviceProvider.GetRequiredService<CreatePackageUseCase>();
var inspectUseCase = serviceProvider.GetRequiredService<InspectPackageUseCase>();
var checkUseCase = serviceProvider.GetRequiredService<CheckPackageUseCase>();
var diagnoseUseCase = serviceProvider.GetRequiredService<DiagnosePackageUseCase>();
var lintUseCase = serviceProvider.GetRequiredService<LintPackageUseCase>();
var applyUseCase = serviceProvider.GetRequiredService<ApplyPackageUseCase>();
var rebaseUseCase = serviceProvider.GetRequiredService<RebasePackageUseCase>();
var undoUseCase = serviceProvider.GetRequiredService<UndoLastApplyUseCase>();
var historyUseCase = serviceProvider.GetRequiredService<ReadHistoryUseCase>();
var agentPackageApplier = new AgentPackageApplier();
var agentConflictReportBuilder = new AgentConflictReportBuilder();
var agentPackageUndoer = new AgentPackageUndoer();
var agentRepairPackageGenerator = new AgentRepairPackageGenerator();

try
{
    return command switch
    {
        "create" => RunCreate(args.Skip(1).ToArray(), createUseCase),
        "inspect" => RunInspect(args.Skip(1).ToArray(), inspectUseCase),
        "check" => RunCheck(args.Skip(1).ToArray(), checkUseCase),
        "diagnose" => RunDiagnose(args.Skip(1).ToArray(), diagnoseUseCase),
        "lint" => RunLint(args.Skip(1).ToArray(), lintUseCase),
        "apply" => RunApply(args.Skip(1).ToArray(), applyUseCase),
        "rebase" => RunRebase(args.Skip(1).ToArray(), rebaseUseCase),
        "pack-root" => RunPackRoot(args.Skip(1).ToArray()),
        "validate-package-root" => RunValidatePackageRoot(args.Skip(1).ToArray()),
        "plan" => RunPlan(args.Skip(1).ToArray()),
        "apply-root" => RunApplyRoot(args.Skip(1).ToArray(), agentPackageApplier),
        "apply-remaining-root" => RunApplyRemainingRoot(args.Skip(1).ToArray(), agentPackageApplier),
        "diagnose-root" => RunDiagnoseRoot(args.Skip(1).ToArray(), agentConflictReportBuilder),
        "repair-root" => RunRepairRoot(args.Skip(1).ToArray(), agentRepairPackageGenerator),
        "undo-root" => RunUndoRoot(args.Skip(1).ToArray(), agentPackageUndoer),
        "open" => RunOpen(args.Skip(1).ToArray(), inspectUseCase, checkUseCase, applyUseCase, gitClient),
        "undo" => RunUndo(args.Skip(1).ToArray(), undoUseCase),
        "history" => RunHistory(args.Skip(1).ToArray(), historyUseCase),
        _ => FailUsage($"Unknown command: {command}")
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static int RunCreate(string[] args, CreatePackageUseCase useCase)
{
    var options = CliOptions.Parse(args);
    var output = options.Value("-o") ?? options.Value("--output");
    if (string.IsNullOrWhiteSpace(output))
    {
        return FailUsage("Missing output path. Use -o package.rpack.");
    }

    var result = useCase.Execute(new CreatePackageOptions
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

static int RunInspect(string[] args, InspectPackageUseCase useCase)
{
    var options = CliOptions.Parse(args);
    if (options.Positionals.Count < 1)
    {
        return FailUsage("Usage: rpack inspect <package.rpack> [--json]");
    }

    var inspection = useCase.Execute(new InspectPackageOptions
    {
        PackagePath = options.Positionals[0],
        PathPrefix = options.Value("--path-prefix")
    });
    var diff = inspection.DiffStats;

    if (options.Has("--json"))
    {
        Console.WriteLine(CliOutputFormatter.FormatInspectionJson(inspection));
        return 0;
    }

    Console.WriteLine($"{inspection.Manifest.Title} ({inspection.Manifest.Id})");
    Console.WriteLine($"format: {inspection.Manifest.Format}");
    Console.WriteLine($"mode: {inspection.Manifest.Mode}");
    Console.WriteLine($"createdAtUtc: {inspection.Manifest.CreatedAtUtc}");
    Console.WriteLine($"sourceRepository: {inspection.Manifest.Source?.Repository}");
    Console.WriteLine($"sourceProjectPath: {inspection.Manifest.Source?.ProjectPath}");
    Console.WriteLine($"baseCommit: {inspection.Manifest.Source?.BaseCommit ?? inspection.Manifest.BaseCommit}");
    Console.WriteLine($"headCommit: {inspection.Manifest.Source?.HeadCommit}");
    Console.WriteLine("Package summary:");
    Console.WriteLine($"- patches: {diff.PatchCount}");
    Console.WriteLine($"- files changed: {diff.FileCount}");
    Console.WriteLine($"- lines added: {diff.AddedLines}");
    Console.WriteLine($"- lines removed: {diff.RemovedLines}");
    Console.WriteLine($"- total diff hunks: {diff.HunkCount}");
    Console.WriteLine($"- binary files: {diff.BinaryFileCount}");
    Console.WriteLine($"- pre actions: {inspection.Manifest.PreActions.Count}");
    Console.WriteLine($"- post actions: {inspection.Manifest.PostActions.Count}");

    PrintActions("PreActions", inspection.Manifest.PreActions);
    PrintActions("PostActions", inspection.Manifest.PostActions);

    if (diff.Patches.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Patches:");
        foreach (var patch in diff.Patches)
        {
            Console.WriteLine($"- {patch.Title} ({patch.PatchPath})");
            foreach (var file in patch.Files)
            {
                var estimate = file.AddedLines + file.RemovedLines;
                Console.WriteLine($"  {file.Status,-8} +{file.AddedLines,-4} -{file.RemovedLines,-4} h={file.HunkCount,-3} ~{estimate,-4} {file.Category,-8} {file.Path}");
            }

            Console.WriteLine($"  Subtotal: +{patch.AddedLines} -{patch.RemovedLines} hunks:{patch.HunkCount}");
        }

        Console.WriteLine();
        Console.WriteLine("Per-file:");
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

static int RunCheck(string[] args, CheckPackageUseCase useCase)
{
    var options = CliOptions.Parse(args);
    if (options.Positionals.Count < 1)
    {
        return FailUsage("Usage: rpack check <package.rpack> [repo] [--json]");
    }

    var optionsModel = new CheckPackageOptions
    {
        PackagePath = options.Positionals[0],
        RepositoryPath = ResolveRepositoryArgument(options),
        AllowDirty = options.Has("--allow-dirty"),
        StrictBase = options.Has("--strict-base"),
        PathPrefix = options.Value("--path-prefix"),
        AddedFileConflictResolution = ResolveAddedFileConflictResolution(options),
        IgnoreSpaceChange = ResolveIgnoreSpaceChange(options)
    };

    if (options.Has("--json"))
    {
        var detailed = useCase.ExecuteDetailed(optionsModel);
        Console.WriteLine(CliOutputFormatter.FormatResultJson("check", detailed));
        return CliOutputFormatter.MapExitCode(detailed);
    }

    var result = useCase.Execute(optionsModel);
    return PrintCommandResult("check", result, false);
}

static int RunRebase(string[] args, RebasePackageUseCase useCase)
{
    var options = CliOptions.Parse(args);
    if (options.Positionals.Count < 1)
    {
        return FailUsage("Usage: rpack rebase <package.rpack> [repo] -o <package-rebased.rpack> [--json]");
    }

    var output = options.Value("-o") ?? options.Value("--output");
    if (string.IsNullOrWhiteSpace(output))
    {
        return FailUsage("Missing output path. Use -o package-rebased.rpack.");
    }

    var rebaseOptions = new RebasePackageOptions
    {
        PackagePath = options.Positionals[0],
        RepositoryPath = ResolveRepositoryArgument(options),
        OutputPath = output,
        PathPrefix = options.Value("--path-prefix"),
        AddedFileConflictResolution = ResolveAddedFileConflictResolution(options),
        IgnoreSpaceChange = ResolveIgnoreSpaceChange(options)
    };

    if (options.Has("--json"))
    {
        var detailed = useCase.ExecuteDetailed(rebaseOptions);
        Console.WriteLine(CliOutputFormatter.FormatResultJson("rebase", detailed));
        return CliOutputFormatter.MapExitCode(detailed);
    }

    var result = useCase.Execute(rebaseOptions);
    return PrintResult(result);
}

static int RunPackRoot(string[] args)
{
    var options = CliOptions.Parse(args);
    if (options.Positionals.Count < 1)
    {
        return FailUsage("Usage: rpack pack-root <package-root> -o <package.rpack> [--json]");
    }

    var output = options.Value("-o") ?? options.Value("--output");
    if (string.IsNullOrWhiteSpace(output))
    {
        return FailUsage("Missing output path. Use -o package.rpack.");
    }

    var result = new AgentPackageRootPacker().PackDetailed(options.Positionals[0], output);
    return PrintStructuredCommandResult("pack-root", result, options.Has("--json"));
}

static int RunValidatePackageRoot(string[] args)
{
    var options = CliOptions.Parse(args);
    if (options.Positionals.Count < 1)
    {
        return FailUsage("Usage: rpack validate-package-root <package-root> [--json]");
    }

    var result = new AgentPackageRootValidator().ValidateDetailed(options.Positionals[0]);
    return PrintStructuredCommandResult("validate-package-root", result, options.Has("--json"));
}

static int RunPlan(string[] args)
{
    var options = CliOptions.Parse(args);
    if (options.Positionals.Count < 1)
    {
        return FailUsage("Usage: rpack plan <package-root> [repo] [--json]");
    }

    var targetRepository = options.Positionals.Count > 1 ? options.Positionals[1] : Directory.GetCurrentDirectory();
    var planBuilder = new AgentApplyPlanBuilder();
    var plan = planBuilder.BuildPlan(options.Positionals[0], targetRepository);
    if (options.Has("--json"))
    {
        Console.WriteLine(planBuilder.RenderJson(plan));
    }
    else
    {
        Console.WriteLine(planBuilder.Render(plan));
    }

    return 0;
}

static int RunApplyRoot(string[] args, AgentPackageApplier applier)
{
    var options = CliOptions.Parse(args);
    if (options.Positionals.Count < 1)
    {
        return FailUsage("Usage: rpack apply-root <package-root> [repo]");
    }

    var targetRepository = options.Positionals.Count > 1 ? options.Positionals[1] : Directory.GetCurrentDirectory();
    var result = applier.Apply(options.Positionals[0], targetRepository);
    return PrintResult(result);
}

static int RunApplyRemainingRoot(string[] args, AgentPackageApplier applier)
{
    var options = CliOptions.Parse(args);
    if (options.Positionals.Count < 1)
    {
        return FailUsage("Usage: rpack apply-remaining-root <package-root> [repo]");
    }

    var targetRepository = options.Positionals.Count > 1 ? options.Positionals[1] : Directory.GetCurrentDirectory();
    var result = applier.ApplyRemaining(options.Positionals[0], targetRepository);
    return PrintResult(result);
}

static int RunDiagnoseRoot(string[] args, AgentConflictReportBuilder reportBuilder)
{
    var options = CliOptions.Parse(args);
    if (options.Positionals.Count < 1)
    {
        return FailUsage("Usage: rpack diagnose-root <package-root> [repo] [--json] [--llm] [--conflicts]");
    }

    var targetRepository = options.Positionals.Count > 1 ? options.Positionals[1] : Directory.GetCurrentDirectory();
    var plan = new AgentApplyPlanBuilder().BuildPlan(options.Positionals[0], targetRepository);
    if (!plan.Success)
    {
        return Fail(plan.ErrorMessage);
    }

    if (options.Has("--json"))
    {
        Console.WriteLine(reportBuilder.RenderJson(plan, options.Positionals[0], targetRepository));
    }
    else if (options.Has("--llm") || options.Has("--conflicts"))
    {
        Console.WriteLine(reportBuilder.RenderLlm(plan, options.Positionals[0], targetRepository));
    }
    else
    {
        Console.WriteLine(reportBuilder.RenderText(plan, options.Positionals[0], targetRepository));
    }

    return 0;
}

static int RunRepairRoot(string[] args, AgentRepairPackageGenerator generator)
{
    var options = CliOptions.Parse(args);
    if (options.Positionals.Count < 1)
    {
        return FailUsage("Usage: rpack repair-root <package-root> [repo] -o <repair-root> [--json]");
    }

    var output = options.Value("-o") ?? options.Value("--output");
    if (string.IsNullOrWhiteSpace(output))
    {
        return FailUsage("Missing output path. Use -o repair-root.");
    }

    var targetRepository = options.Positionals.Count > 1 ? options.Positionals[1] : Directory.GetCurrentDirectory();
    var result = generator.Generate(options.Positionals[0], targetRepository, output);
    if (options.Has("--json"))
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            success = result.Success,
            outputPath = output,
            message = result.Message
        }, new JsonSerializerOptions { WriteIndented = true }));
        return result.Success ? 0 : 1;
    }

    return PrintResult(result);
}

static int RunUndoRoot(string[] args, AgentPackageUndoer undoer)
{
    var options = CliOptions.Parse(args);
    if (options.Positionals.Count < 1)
    {
        return FailUsage("Usage: rpack undo-root <repo> [--force]");
    }

    var result = undoer.Undo(options.Positionals[0], options.Has("--force"));
    return PrintResult(result);
}

static int RunApply(string[] args, ApplyPackageUseCase useCase)
{
    var options = CliOptions.Parse(args);
    if (options.Positionals.Count < 1)
    {
        return FailUsage("Usage: rpack apply <package.rpack> [repo] [--json]");
    }

    var applyOptions = new ApplyPackageOptions
    {
        PackagePath = options.Positionals[0],
        RepositoryPath = ResolveRepositoryArgument(options),
        AllowDirty = options.Has("--allow-dirty"),
        StrictBase = options.Has("--strict-base"),
        PathPrefix = options.Value("--path-prefix"),
        AddedFileConflictResolution = ResolveAddedFileConflictResolution(options),
        IgnoreSpaceChange = ResolveIgnoreSpaceChange(options),
        SkipActions = options.Has("--no-actions")
    };

    if (options.Has("--json"))
    {
        var detailed = useCase.ExecuteDetailed(applyOptions);
        Console.WriteLine(CliOutputFormatter.FormatResultJson("apply", detailed));
        return CliOutputFormatter.MapExitCode(detailed);
    }

    var result = useCase.Execute(applyOptions);
    return PrintCommandResult("apply", result, false);
}

static int RunDiagnose(string[] args, DiagnosePackageUseCase useCase)
{
    var options = CliOptions.Parse(args);
    if (options.Positionals.Count < 1)
    {
        return FailUsage("Usage: rpack diagnose <package.rpack> [repo] [--json] [--llm] [--conflicts]");
    }

    var result = useCase.Execute(new DiagnosePackageOptions
    {
        PackagePath = options.Positionals[0],
        RepositoryPath = ResolveRepositoryArgument(options),
        AllowDirty = options.Has("--allow-dirty"),
        StrictBase = options.Has("--strict-base"),
        PathPrefix = options.Value("--path-prefix"),
        AddedFileConflictResolution = ResolveAddedFileConflictResolution(options),
        IgnoreSpaceChange = ResolveIgnoreSpaceChange(options)
    });

    if (options.Has("--json"))
    {
        Console.WriteLine(CliOutputFormatter.FormatResultJson("diagnose", result));
        return CliOutputFormatter.MapExitCode(result);
    }

    if (options.Has("--llm") || options.Has("--conflicts"))
    {
        Console.WriteLine(CliOutputFormatter.FormatDiagnoseLlmReport(options.Positionals[0], ResolveRepositoryArgument(options), result));
        return CliOutputFormatter.MapExitCode(result);
    }

    return PrintResult(result);
}

static int RunLint(string[] args, LintPackageUseCase useCase)
{
    var options = CliOptions.Parse(args);
    if (options.Positionals.Count < 1)
    {
        return FailUsage("Usage: rpack lint <package.rpack> [--json]");
    }

    var lintOptions = new LintPackageOptions
    {
        PackagePath = options.Positionals[0],
        PathPrefix = options.Value("--path-prefix")
    };

    if (options.Has("--json"))
    {
        var detailed = useCase.ExecuteDetailed(lintOptions);
        Console.WriteLine(CliOutputFormatter.FormatResultJson("lint", detailed));
        return CliOutputFormatter.MapExitCode(detailed);
    }

    var result = useCase.Execute(lintOptions);
    return PrintCommandResult("lint", result, false);
}

static int RunOpen(
    string[] args,
    InspectPackageUseCase inspectUseCase,
    CheckPackageUseCase checkUseCase,
    ApplyPackageUseCase applyUseCase,
    GitClient gitClient)
{
    var options = CliOptions.Parse(args);
    if (options.Positionals.Count < 1)
    {
        return FailUsage("Usage: rpack open <package.rpack> [repo]");
    }

    var packagePath = Path.GetFullPath(options.Positionals[0]);
    var repositoryPath = ResolveOpenRepositoryArgument(options, gitClient, packagePath, inspectUseCase);
    var allowDirty = options.Has("--allow-dirty");
    var strictBase = options.Has("--strict-base");
    var pathPrefix = options.Value("--path-prefix");
    var ignoreSpaceChange = ResolveIgnoreSpaceChange(options);
    var allowedDirtyPaths = GetPackageDirtyException(repositoryPath, packagePath);

    var inspection = inspectUseCase.Execute(new InspectPackageOptions
    {
        PackagePath = packagePath,
        PathPrefix = pathPrefix
    });

    PrintOpenSummary(inspection, repositoryPath, allowDirty, ignoreSpaceChange, pathPrefix);

    var check = checkUseCase.Execute(new CheckPackageOptions
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

    var apply = applyUseCase.Execute(new ApplyPackageOptions
    {
        PackagePath = packagePath,
        RepositoryPath = repositoryPath,
        AllowDirty = allowDirty,
        StrictBase = strictBase,
        PathPrefix = pathPrefix,
        AddedFileConflictResolution = ResolveAddedFileConflictResolution(options),
        AllowedDirtyPaths = allowedDirtyPaths,
        IgnoreSpaceChange = ignoreSpaceChange,
        SkipActions = options.Has("--no-actions")
    });

    return PrintCommandResult("open", apply, options.Has("--json"));
}

static int RunUndo(string[] args, UndoLastApplyUseCase useCase)
{
    var options = CliOptions.Parse(args);
    var result = useCase.Execute(
        ResolveRepositoryArgument(options, positionalOffset: 0),
        options.Has("--allow-dirty"));

    return PrintCommandResult("undo", result, false);
}

static int RunHistory(string[] args, ReadHistoryUseCase useCase)
{
    var options = CliOptions.Parse(args);
    var logs = useCase.Execute(ResolveRepositoryArgument(options, positionalOffset: 0));
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

static string ResolveOpenRepositoryArgument(CliOptions options, GitClient gitClient, string packagePath, InspectPackageUseCase inspectUseCase)
{
    var explicitRepository = options.Value("--repo")
        ?? (options.Positionals.Count > 1 ? options.Positionals[1] : null);
    if (!string.IsNullOrWhiteSpace(explicitRepository))
    {
        return explicitRepository;
    }

    var packageRepositoryHint = TryGetProjectPathFromPackage(packagePath, gitClient, inspectUseCase);
    if (!string.IsNullOrWhiteSpace(packageRepositoryHint))
    {
        return packageRepositoryHint;
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

static string? TryGetProjectPathFromPackage(string packagePath, GitClient gitClient, InspectPackageUseCase inspectUseCase)
{
    try
    {
        var inspection = inspectUseCase.Execute(new InspectPackageOptions
        {
            PackagePath = packagePath
        });
        var projectPath = inspection.Manifest.Source?.ProjectPath;
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return null;
        }

        return gitClient.InspectRepository(projectPath).RootPath;
    }
    catch
    {
        return null;
    }
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
    var diff = inspection.DiffStats;

    Console.WriteLine($"{inspection.Manifest.Title} ({inspection.Manifest.Id})");
    Console.WriteLine($"packageId: {inspection.Manifest.Id}");
    Console.WriteLine($"mode: {inspection.Manifest.Mode}");
    Console.WriteLine($"targetRepository: {repositoryPath}");
    Console.WriteLine($"patches: {diff.PatchCount}");
    Console.WriteLine($"files changed: {diff.FileCount}");
    Console.WriteLine($"addedLines: {diff.AddedLines}");
    Console.WriteLine($"removedLines: {diff.RemovedLines}");
    Console.WriteLine($"hunks: {diff.HunkCount}");
    Console.WriteLine($"binary files: {diff.BinaryFileCount}");
    Console.WriteLine($"allowDirty: {allowDirty}");
    Console.WriteLine($"patchContext: {(ignoreSpaceChange ? "whitespace-compatible" : "strict")}");
    Console.WriteLine($"preActions: {inspection.Manifest.PreActions.Count}");
    Console.WriteLine($"postActions: {inspection.Manifest.PostActions.Count}");
    PrintActions("PreActions", inspection.Manifest.PreActions);
    PrintActions("PostActions", inspection.Manifest.PostActions);
    if (!string.IsNullOrWhiteSpace(pathPrefix))
    {
        Console.WriteLine($"pathPrefix: {pathPrefix}");
    }

    if (diff.Patches.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("patch:");
        foreach (var patch in diff.Patches)
        {
            Console.WriteLine($"  {patch.Title}");
            foreach (var file in patch.Files)
            {
                Console.WriteLine($"    {file.Status,-8} +{file.AddedLines,-4} -{file.RemovedLines,-4} {file.Path}");
            }
        }
    }
}

static void PrintActions(string title, IReadOnlyList<RpackAction> actions)
{
    if (actions.Count == 0)
    {
        return;
    }

    Console.WriteLine();
    Console.WriteLine(title + ":");
    foreach (var action in actions)
    {
        var target = string.IsNullOrWhiteSpace(action.Path)
            ? action.Command
            : action.Path;
        Console.WriteLine($"  - {action.Name} [{action.Kind}] {(action.Optional ? "optional" : "required")} {target}".TrimEnd());
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
    return CliOutputFormatter.MapExitCode(result);
}

static int PrintCommandResult(string command, RpackResult result, bool asJson)
{
    if (asJson)
    {
        Console.WriteLine(CliOutputFormatter.FormatResultJson(command, result));
        return CliOutputFormatter.MapExitCode(result);
    }

    return PrintResult(result);
}

static int PrintStructuredCommandResult(string command, RpackOperationResult result, bool asJson)
{
    if (asJson)
    {
        Console.WriteLine(CliOutputFormatter.FormatResultJson(command, result));
        return CliOutputFormatter.MapExitCode(result);
    }

    var output = result.Success ? Console.Out : Console.Error;
    output.WriteLine(result.Summary);
    return CliOutputFormatter.MapExitCode(result);
}

static int FailUsage(string message)
{
    Console.Error.WriteLine(message);
    return 2;
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
      rpack inspect <package.rpack> [--path-prefix <prefix>] [--json]
      rpack check <package.rpack> [repo] [--allow-dirty] [--strict-base] [--path-prefix <prefix>] [--strict] [--json]
      rpack diagnose <package.rpack> [repo] [--allow-dirty] [--strict-base] [--path-prefix <prefix>] [--strict] [--json] [--llm] [--conflicts]
      rpack rebase <package.rpack> [repo] -o <package-rebased.rpack> [--path-prefix <prefix>] [--strict]
      rpack lint <package.rpack> [--path-prefix <prefix>] [--json]
      rpack apply <package.rpack> [repo] [--allow-dirty] [--strict-base] [--path-prefix <prefix>] [--strict] [--no-actions] [--resolve-added-file-conflicts <mode>] [--allow-existing-added-files <mode>] [--json]
      rpack apply-root <package-root> [repo]
      rpack apply-remaining-root <package-root> [repo]
      rpack diagnose-root <package-root> [repo] [--json] [--llm] [--conflicts]
      rpack repair-root <package-root> [repo] -o <repair-root> [--json]
      rpack pack-root <package-root> -o <package.rpack> [--json]
      rpack validate-package-root <package-root> [--json]
      rpack undo-root <repo> [--force]
      rpack open <package.rpack> [repo] [--allow-dirty] [--strict-base] [--path-prefix <prefix>] [--strict] [--no-actions] [--yes]
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
      --no-actions           Apply patches without running manifest PreActions or PostActions.
      --resolve-added-file-conflicts <mode>  How to resolve add-conflict: abort|skip|modify|overwrite.
      --allow-existing-added-files <mode>    Alias for --resolve-added-file-conflicts.
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

static string ResolveAddedFileConflictResolution(CliOptions options)
{
    return options.Value("--resolve-added-file-conflicts")
        ?? options.Value("--allow-existing-added-files")
        ?? "abort";
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
        "--resolve-added-file-conflicts",
        "--allow-existing-added-files",
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
