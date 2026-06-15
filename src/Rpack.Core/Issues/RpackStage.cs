namespace Rpack.Core.Issues;

public enum RpackStage
{
    PackageOpen,
    FormatDetection,
    ManifestRead,
    ManifestValidation,
    ChecksumVerification,
    PatchRead,
    PatchTransform,
    PatchAnalyze,
    RepositoryInspect,
    RepositoryPolicy,
    ConflictDetection,
    PatchDryRun,
    PreAction,
    PatchApply,
    PostAction,
    StateStore,
    Undo,
    Rebase,
    AgentPackageValidation,
    PayloadValidation,
    PayloadApply
}
