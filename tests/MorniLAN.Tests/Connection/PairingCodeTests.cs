using MorniLAN.Shared.Connection;

namespace MorniLAN.Tests.Connection;

public class PairingCodeTests
{
    [Fact]
    public void Generate_ProducesValidEightCharacterCodes()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = PairingCode.Generate();
            Assert.Equal(PairingCode.Length, code.Length);
            Assert.True(PairingCode.TryNormalize(code, out var normalized));
            Assert.Equal(code, normalized);
        }
    }

    [Fact]
    public void Generate_IsRandom()
    {
        var codes = Enumerable.Range(0, 50).Select(_ => PairingCode.Generate()).ToHashSet();
        Assert.True(codes.Count > 45);
    }

    [Theory]
    [InlineData("K7Q2-M9XD", "K7Q2M9XD")]
    [InlineData("k7q2 m9xd", "K7Q2M9XD")]
    [InlineData("  k7q2-m9xd  ", "K7Q2M9XD")]
    [InlineData("O0IL-1111", "00111111")] // verwechselbare Zeichen werden toleriert
    public void TryNormalize_AcceptsTypicalInput(string input, string expected)
    {
        Assert.True(PairingCode.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("K7Q2-M9X")] // zu kurz
    [InlineData("K7Q2-M9XDA")] // zu lang
    [InlineData("K7Q2-M9XU")] // U gibt es im Alphabet nicht
    [InlineData("K7Q2-M9X!")]
    public void TryNormalize_RejectsInvalidInput(string? input)
    {
        Assert.False(PairingCode.TryNormalize(input, out _));
    }

    [Fact]
    public void Format_InsertsDash()
    {
        Assert.Equal("K7Q2-M9XD", PairingCode.Format("K7Q2M9XD"));
    }
}
