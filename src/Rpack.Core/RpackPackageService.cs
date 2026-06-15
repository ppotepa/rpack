using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rpack.Core.Actions;
using Rpack.Core.Packages;
using Rpack.Core.Patches;
using Rpack.Core.State;

namespace Rpack.Core;

public sealed class RpackPackageService
{
    private const string ManifestPath = "manifest.json";
    private const string PatchPath = "patches/change.patch";
    private const string ActionPathPrefix = "actions/";
    private const string ApplyLogPath = "apply-log.json";
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        TypeInfoResolver = RpackJsonContext.Default,
        WriteIndented = true
    };

    private readonly GitClient _gitClient;
    private readonly RpackPackageReader _packageReader;
    private readonly RpackManifestValidator _manifestValidator;
    private readonly RpackChecksumVerifier _checksumVerifier;
    private readonly RpackPatchParser _patchParser;
    private readonly RpackPatchPathTransformer _patchTransformer;
    private readonly RpackPatchPreparer _patchPreparer;
    private readonly RpackActionExecutionService _actionExecutionService;
    private readonly RpackStateStore _stateStore;
    private readonly RpackRepositoryPolicyService _repositoryPolicyService;
    private readonly RpackPackageCheckService _packageCheckService;
    private readonly RpackPackageCreateService _packageCreateService;
    private readonly RpackPackageInspectService _packageInspectService;
    private readonly RpackPackageDiagnoseService _packageDiagnoseService;
    private readonly RpackPackageLintService _packageLintService;
    private readonly RpackPackageApplyService _packageApplyService;
    private readonly RpackPackageRebaseService _packageRebaseService;
    private readonly RpackPackageUndoService _packageUndoService;
    private readonly RpackPackageHistoryService _packageHistoryService;
    private readonly RpackApplyPlanBuilder _applyPlanBuilder;

    public RpackPackageService(GitClient gitClient)
        : this(
            gitClient,
            new RpackPackageReader(),
            new RpackManifestValidator(),
            new RpackChecksumVerifier(),
            new RpackPatchParser(),
            new RpackPatchPathTransformer(),
            new RpackPatchPreparer(),
            new RpackActionExecutionService(gitClient),
            new RpackStateStore(),
            new RpackRepositoryPolicyService(gitClient, new RpackPatchParser()))
    {
    }

    public RpackPackageService(
        GitClient gitClient,
        RpackPackageReader packageReader,
        RpackManifestValidator manifestValidator,
        RpackChecksumVerifier checksumVerifier,
        RpackPatchParser patchParser,
        RpackPatchPathTransformer patchTransformer,
        RpackPatchPreparer patchPreparer,
        RpackActionExecutionService actionExecutionService,
        RpackStateStore stateStore,
        RpackRepositoryPolicyService repositoryPolicyService)
    {
        _gitClient = gitClient;
        _packageReader = packageReader;
        _manifestValidator = manifestValidator;
        _checksumVerifier = checksumVerifier;
        _patchParser = patchParser;
        _patchTransformer = patchTransformer;
        _patchPreparer = patchPreparer;
        _actionExecutionService = actionExecutionService;
        _stateStore = stateStore;
        _repositoryPolicyService = repositoryPolicyService;
        _applyPlanBuilder = new RpackApplyPlanBuilder(gitClient, gitClient, gitClient, packageReader, manifestValidator, checksumVerifier, patchPreparer, repositoryPolicyService);
        _packageCheckService = new RpackPackageCheckService(_applyPlanBuilder);
        _packageCreateService = new RpackPackageCreateService(gitClient, gitClient);
        _packageInspectService = new RpackPackageInspectService(packageReader, patchParser, patchTransformer);
        _packageDiagnoseService = new RpackPackageDiagnoseService(_packageInspectService, _packageCheckService);
        _packageLintService = new RpackPackageLintService(packageReader, manifestValidator, checksumVerifier, patchParser, patchTransformer);
        _packageApplyService = new RpackPackageApplyService(gitClient, gitClient, packageReader, patchParser, actionExecutionService, stateStore, _applyPlanBuilder);
        _packageRebaseService = new RpackPackageRebaseService(gitClient, gitClient, gitClient, packageReader, manifestValidator, checksumVerifier, patchPreparer, repositoryPolicyService);
        _packageUndoService = new RpackPackageUndoService(gitClient, gitClient, stateStore, repositoryPolicyService);
        _packageHistoryService = new RpackPackageHistoryService(gitClient, stateStore);
    }

    public RpackResult Create(CreatePackageOptions options)
    {
        return _packageCreateService.Execute(options);
    }

    public PackageInspection Inspect(string packagePath)
    {
        return Inspect(new InspectPackageOptions
        {
            PackagePath = packagePath
        });
    }

    public PackageInspection Inspect(InspectPackageOptions options)
    {
        return _packageInspectService.Execute(options);
    }

    public RpackResult Check(CheckPackageOptions options)
    {
        return _packageCheckService.Execute(options);
    }

    public RpackResult Diagnose(DiagnosePackageOptions options)
    {
        return _packageDiagnoseService.Execute(options);
    }

    public RpackResult Lint(LintPackageOptions options)
    {
        return _packageLintService.Execute(options);
    }

    public RpackResult Apply(ApplyPackageOptions options)
    {
        return _packageApplyService.Execute(options);
    }

    public RpackResult Rebase(RebasePackageOptions options)
    {
        return _packageRebaseService.Execute(options);
    }

    public RpackResult UndoLastApply(string repositoryPath, bool allowDirty = false)
    {
        return _packageUndoService.Execute(repositoryPath, allowDirty);
    }

    public IReadOnlyList<RpackApplyLog> ReadHistory(string repositoryPath)
    {
        return _packageHistoryService.Execute(repositoryPath);
    }

}

public sealed class CreatePackageOptions
{
    public required string RepositoryPath { get; init; }
    public required string OutputPath { get; init; }
    public string? FromRevision { get; init; }
    public string? ToRevision { get; init; }
    public bool Staged { get; init; }
    public string? Id { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
}

public sealed class CheckPackageOptions
{
    public required string PackagePath { get; init; }
    public required string RepositoryPath { get; init; }
    public bool AllowDirty { get; init; }
    public bool StrictBase { get; init; }
    public string? PathPrefix { get; init; }
    public string? AddedFileConflictResolution { get; init; }
    public IReadOnlyList<string> AllowedDirtyPaths { get; init; } = [];
    public bool IgnoreSpaceChange { get; init; } = true;
}

public sealed class ApplyPackageOptions
{
    public required string PackagePath { get; init; }
    public required string RepositoryPath { get; init; }
    public bool AllowDirty { get; init; }
    public bool StrictBase { get; init; }
    public string? PathPrefix { get; init; }
    public string? AddedFileConflictResolution { get; init; }
    public IReadOnlyList<string> AllowedDirtyPaths { get; init; } = [];
    public bool IgnoreSpaceChange { get; init; } = true;
    public bool SkipActions { get; init; }
    public IReadOnlyList<int>? SelectedPreActions { get; init; }
    public IReadOnlyList<int>? SelectedPostActions { get; init; }
    public Action<RpackActionResult>? OnActionExecuted { get; init; }
}

public sealed class RebasePackageOptions
{
    public required string PackagePath { get; init; }
    public required string RepositoryPath { get; init; }
    public required string OutputPath { get; init; }
    public string? PathPrefix { get; init; }
    public string? AddedFileConflictResolution { get; init; } = "modify";
    public bool IgnoreSpaceChange { get; init; } = true;
}

public sealed class DiagnosePackageOptions
{
    public required string PackagePath { get; init; }
    public required string RepositoryPath { get; init; }
    public bool AllowDirty { get; init; }
    public bool StrictBase { get; init; }
    public string? PathPrefix { get; init; }
    public string? AddedFileConflictResolution { get; init; }
    public bool IgnoreSpaceChange { get; init; } = true;
}

public sealed class LintPackageOptions
{
    public required string PackagePath { get; init; }
    public string? PathPrefix { get; init; }
}

public sealed class InspectPackageOptions
{
    public required string PackagePath { get; init; }
    public string? PathPrefix { get; init; }
}
