using System.Text;
using Rpack.Core;

namespace Rpack.Tests;

public class Sha256Tests
{
    [Fact]
    public void ForBytes_ReturnsLowercaseSha256()
    {
        var hash = Sha256.ForBytes(Encoding.UTF8.GetBytes("rpack"));

        Assert.Equal("cb893f49b736b9697cc50ac438bfeb829ffd606f42c9eb06b536745654a0de11", hash);
    }
}
