using System.Text;
using Rpack.Core.Results;
using Rpack.Core.Issues;

namespace Rpack.Core.Packages;

public sealed class RpackPackageDiagnoseService
{
    private readonly RpackPackageInspectService _inspectService;
    private readonly RpackPackageCheckService _checkService;

    public RpackPackageDiagnoseService(
        RpackPackageInspectService inspectService,
        RpackPackageCheckService checkService)
    {
        _inspectService = inspectService;
        _checkService = checkService;
    }

    public RpackResult Execute(DiagnosePackageOptions options)
    {
        PackageInspection inspection;
        try
        {
            inspection = _inspectService.Execute(new InspectPackageOptions
            {
                PackagePath = options.PackagePath,
                PathPrefix = options.PathPrefix
            });
        }
        catch (Exception ex)
        {
            return RpackResult.Fail($"Diagnosis failed while reading package:{Environment.NewLine}{ex.Message}");
        }

        var check = _checkService.ExecuteDetailed(new CheckPackageOptions
        {
            PackagePath = options.PackagePath,
            RepositoryPath = options.RepositoryPath,
            AllowDirty = options.AllowDirty,
            StrictBase = options.StrictBase,
            PathPrefix = options.PathPrefix,
            AddedFileConflictResolution = options.AddedFileConflictResolution,
            IgnoreSpaceChange = options.IgnoreSpaceChange
        });

        var builder = new StringBuilder();
        builder.AppendLine($"Package: {options.PackagePath}");
        builder.AppendLine($"Repository: {options.RepositoryPath}");
        builder.AppendLine($"Id: {inspection.Manifest.Id}");
        builder.AppendLine($"Title: {inspection.Manifest.Title}");
        builder.AppendLine($"Files: {inspection.ChangedFiles.Count}");
        builder.AppendLine($"Patches: {inspection.Manifest.Patches.Count}");
        builder.AppendLine();
        builder.AppendLine(check.Success ? "Status: Ready" : "Status: Failed");
        builder.AppendLine();

        if (check.Success)
        {
            builder.AppendLine("Check:");
            builder.AppendLine(check.Summary);
            return RpackResult.Ok(builder.ToString().TrimEnd());
        }

        builder.AppendLine("Problem:");
        builder.AppendLine(ClassifyCheckFailure(check.Issues));
        builder.AppendLine();
        builder.AppendLine("Raw details:");
        builder.AppendLine(check.Summary);
        return RpackResult.Fail(builder.ToString().TrimEnd());
    }

    private static string ClassifyCheckFailure(IReadOnlyList<RpackIssue> issues)
    {
        var primary = issues.FirstOrDefault();
        if (primary is not null)
        {
            return $"{primary.Suggestion}{Environment.NewLine}{primary.Message}";
        }

        return "Package check failed. Review raw details.";
    }
}
