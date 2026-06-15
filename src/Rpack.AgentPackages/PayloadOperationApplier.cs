using Rpack.Core;

namespace Rpack.AgentPackages;

public sealed class PayloadOperationApplier
{
    public RpackResult ApplyPayload(string targetPath, byte[] payloadBytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(targetPath))!);
        File.WriteAllBytes(targetPath, payloadBytes);
        return RpackResult.Ok($"Applied payload to {targetPath}");
    }
}
