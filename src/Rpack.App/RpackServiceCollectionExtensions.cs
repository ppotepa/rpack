using Microsoft.Extensions.DependencyInjection;
using Rpack.Core;
using Rpack.Core.Actions;
using Rpack.Core.Packages;
using Rpack.Core.Patches;
using Rpack.Core.State;

namespace Rpack.App;

public static class RpackServiceCollectionExtensions
{
    public static IServiceCollection AddRpackCore(this IServiceCollection services)
    {
        services.AddSingleton<ProcessRunner>();
        services.AddSingleton<GitClient>();
        services.AddSingleton<GitRepositoryInspector>();
        services.AddSingleton<GitWorkingTreeStatus>();
        services.AddSingleton<GitPatchOperations>();
        services.AddSingleton<GitWorktreeOperations>();
        services.AddSingleton<GitMutationOperations>();
        services.AddSingleton<IGitRepositoryInspector>(sp => sp.GetRequiredService<GitRepositoryInspector>());
        services.AddSingleton<IGitWorkingTreeStatus>(sp => sp.GetRequiredService<GitWorkingTreeStatus>());
        services.AddSingleton<IGitPatchOperations>(sp => sp.GetRequiredService<GitPatchOperations>());
        services.AddSingleton<IGitWorktreeOperations>(sp => sp.GetRequiredService<GitWorktreeOperations>());
        services.AddSingleton<IGitMutationOperations>(sp => sp.GetRequiredService<GitMutationOperations>());
        services.AddSingleton<RpackPackageService>();
        return services;
    }

    public static IServiceCollection AddRpackPackages(this IServiceCollection services)
    {
        services.AddSingleton<RpackPackageReader>();
        services.AddSingleton<RpackManifestValidator>();
        services.AddSingleton<RpackChecksumVerifier>();
        services.AddSingleton<RpackApplyPlanBuilder>();
        services.AddSingleton<RpackPackageCreateService>();
        services.AddSingleton<RpackPackageInspectService>();
        services.AddSingleton<RpackPackageCheckService>();
        services.AddSingleton<RpackPackageDiagnoseService>();
        services.AddSingleton<RpackPackageLintService>();
        services.AddSingleton<RpackPackageRebaseService>();
        return services;
    }

    public static IServiceCollection AddRpackPatches(this IServiceCollection services)
    {
        services.AddSingleton<RpackPatchParser>();
        services.AddSingleton<RpackPatchPathTransformer>();
        services.AddSingleton<RpackPatchPreparer>();
        return services;
    }

    public static IServiceCollection AddRpackValidation(this IServiceCollection services)
    {
        return services;
    }

    public static IServiceCollection AddRpackGit(this IServiceCollection services)
    {
        return services;
    }

    public static IServiceCollection AddRpackActions(this IServiceCollection services)
    {
        services.AddSingleton<RpackActionExecutionService>();
        return services;
    }

    public static IServiceCollection AddRpackState(this IServiceCollection services)
    {
        services.AddSingleton<RpackStateStore>();
        services.AddSingleton<RpackRepositoryPolicyService>(sp =>
            new RpackRepositoryPolicyService(
                sp.GetRequiredService<IGitPatchOperations>(),
                sp.GetRequiredService<IGitWorkingTreeStatus>(),
                sp.GetRequiredService<RpackPatchParser>()));
        services.AddSingleton<RpackPackageUndoService>();
        services.AddSingleton<RpackPackageHistoryService>();
        return services;
    }

    public static IServiceCollection AddRpackApp(this IServiceCollection services)
    {
        services.AddRpackCore();
        services.AddRpackPackages();
        services.AddRpackPatches();
        services.AddRpackValidation();
        services.AddRpackGit();
        services.AddRpackActions();
        services.AddRpackState();
        services.AddSingleton<CreatePackageUseCase>();
        services.AddSingleton<InspectPackageUseCase>();
        services.AddSingleton<CheckPackageUseCase>();
        services.AddSingleton<RpackPackageApplyService>();
        services.AddSingleton<DiagnosePackageUseCase>();
        services.AddSingleton<LintPackageUseCase>();
        services.AddSingleton<ApplyPackageUseCase>();
        services.AddSingleton<RebasePackageUseCase>();
        services.AddSingleton<UndoLastApplyUseCase>();
        services.AddSingleton<ReadHistoryUseCase>();
        return services;
    }

    public static IServiceCollection AddRpackInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ProcessRunner>();
        return services;
    }
}
