using System.Security.Cryptography;

namespace Rpack.Core;

public static class Sha256
{
    public static string ForBytes(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
