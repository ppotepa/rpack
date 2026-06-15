namespace Rpack.AgentPackages;

public sealed class PayloadStoreReader
{
    public byte[] ReadPayload(string packageRoot, string relativePath)
    {
        var fullPath = Path.Combine(packageRoot, relativePath);
        return File.ReadAllBytes(fullPath);
    }
}
