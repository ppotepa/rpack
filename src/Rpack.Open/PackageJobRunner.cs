using Rpack.App;
using Rpack.Core;

namespace Rpack.Open;

internal sealed class PackageJobRunner
{
    private readonly InspectPackageUseCase _inspectUseCase;
    private readonly CheckPackageUseCase _checkUseCase;
    private readonly ApplyPackageUseCase _applyUseCase;

    public PackageJobRunner(
        InspectPackageUseCase inspectUseCase,
        CheckPackageUseCase checkUseCase,
        ApplyPackageUseCase applyUseCase)
    {
        _inspectUseCase = inspectUseCase;
        _checkUseCase = checkUseCase;
        _applyUseCase = applyUseCase;
    }

    public PackageInspection Inspect(string packagePath, string? pathPrefix)
    {
        return _inspectUseCase.Execute(new InspectPackageOptions
        {
            PackagePath = packagePath,
            PathPrefix = pathPrefix
        });
    }

    public RpackResult Check(
        string packagePath,
        string repositoryPath,
        bool allowDirty,
        bool strictBase,
        string? pathPrefix,
        IReadOnlyList<string> allowedDirtyPaths,
        bool ignoreSpaceChange)
    {
        return _checkUseCase.Execute(new CheckPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = repositoryPath,
            AllowDirty = allowDirty,
            StrictBase = strictBase,
            PathPrefix = pathPrefix,
            AllowedDirtyPaths = allowedDirtyPaths,
            IgnoreSpaceChange = ignoreSpaceChange
        });
    }

    public RpackResult Apply(
        string packagePath,
        string repositoryPath,
        bool allowDirty,
        bool strictBase,
        string? pathPrefix,
        IReadOnlyList<string> allowedDirtyPaths,
        bool ignoreSpaceChange,
        bool skipActions,
        IReadOnlyList<int>? selectedPreActions,
        IReadOnlyList<int>? selectedPostActions,
        Action<RpackActionResult>? onActionExecuted)
    {
        return _applyUseCase.Execute(new ApplyPackageOptions
        {
            PackagePath = packagePath,
            RepositoryPath = repositoryPath,
            AllowDirty = allowDirty,
            StrictBase = strictBase,
            PathPrefix = pathPrefix,
            AllowedDirtyPaths = allowedDirtyPaths,
            IgnoreSpaceChange = ignoreSpaceChange,
            SkipActions = skipActions,
            SelectedPreActions = selectedPreActions,
            SelectedPostActions = selectedPostActions,
            OnActionExecuted = onActionExecuted
        });
    }
}
