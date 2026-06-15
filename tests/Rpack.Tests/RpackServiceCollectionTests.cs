using Microsoft.Extensions.DependencyInjection;
using Rpack.App;

namespace Rpack.Tests;

public class RpackServiceCollectionTests
{
    [Fact]
    public void ServiceProvider_ResolvesAllUseCases()
    {
        using var provider = new ServiceCollection()
            .AddRpackApp()
            .BuildServiceProvider();

        Assert.IsType<CheckPackageUseCase>(provider.GetRequiredService<CheckPackageUseCase>());
        Assert.IsType<DiagnosePackageUseCase>(provider.GetRequiredService<DiagnosePackageUseCase>());
        Assert.IsType<LintPackageUseCase>(provider.GetRequiredService<LintPackageUseCase>());
        Assert.IsType<ApplyPackageUseCase>(provider.GetRequiredService<ApplyPackageUseCase>());
        Assert.IsType<RebasePackageUseCase>(provider.GetRequiredService<RebasePackageUseCase>());
        Assert.IsType<UndoLastApplyUseCase>(provider.GetRequiredService<UndoLastApplyUseCase>());
        Assert.IsType<ReadHistoryUseCase>(provider.GetRequiredService<ReadHistoryUseCase>());
    }

    [Fact]
    public void CliComposition_ResolvesApplyUseCase()
    {
        using var provider = new ServiceCollection()
            .AddRpackApp()
            .BuildServiceProvider();

        Assert.IsType<ApplyPackageUseCase>(provider.GetRequiredService<ApplyPackageUseCase>());
    }
}
