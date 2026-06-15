using System.Text.Json;

namespace Rpack.AgentPackages;

public sealed class AgentPackageReader
{
    public const string ManifestPath = "manifest.json";

    public AgentPackageManifest ReadManifest(string packageRoot)
    {
        var path = Path.Combine(packageRoot, ManifestPath);
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize(stream, AgentPackageJsonContext.Default.AgentPackageManifest)
            ?? throw new InvalidOperationException("manifest.json is invalid.");
    }

    public AgentOperationDocument ReadOperations(string packageRoot, string operationsPath)
    {
        var path = Path.Combine(packageRoot, operationsPath);
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize(stream, AgentOperationJsonContext.Default.AgentOperationDocument)
            ?? throw new InvalidOperationException($"{operationsPath} is invalid.");
    }
}
