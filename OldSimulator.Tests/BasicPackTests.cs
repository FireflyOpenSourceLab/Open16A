using Open16A.BasicPack;
using Xunit;

namespace OldSimulator.Tests;

public sealed class BasicPackTests
{
    [Fact]
    public void PacksSortedLinesWithBigEndianHeaderAndAutoRunFlag()
    {
        BasicProgramImage image = BasicTokenizer.ParseProgram("20 END\n10 PRINT \"HI\"", autoRun: true);

        byte[] bytes = image.ToBytes();

        Assert.Equal("B16P"u8.ToArray(), bytes[..4]);
        Assert.Equal(BasicProgramFormat.Version, bytes[4]);
        Assert.Equal(BasicProgramFormat.AutoRun, bytes[5]);
        Assert.Equal(new byte[] { 0x00, 0x02 }, bytes[8..10]);
        Assert.Equal(new byte[] { 0x00, 0x0A }, bytes[10..12]);
        Assert.Equal(new byte[] { 0x91, 0x83, 0x02, (byte)'H', (byte)'I' }, bytes[14..19]);
    }

    [Fact]
    public void EncodesVariablesFloatingLiteralsAndStrings()
    {
        byte[] tokens = BasicTokenizer.Tokenize("A%=1.5: PRINT A$, \"OK\"");

        Assert.Equal(BasicProgramFormat.Variable, tokens[0]);
        Assert.Equal((byte)(BasicProgramFormat.TypeInteger | 0), tokens[1]);
        Assert.Equal((byte)'=', tokens[2]);
        Assert.Equal(BasicProgramFormat.FloatLiteral, tokens[3]);
        Assert.Contains(BasicProgramFormat.StringLiteral, tokens);
    }

    [Fact]
    public void RejectsDuplicateLineNumbersAndNonAsciiStrings()
    {
        Assert.Throws<BasicPackException>(() => BasicTokenizer.ParseProgram("10 END\n10 END"));
        Assert.Throws<BasicPackException>(() => BasicTokenizer.ParseProgram("10 PRINT \"你好\""));
    }
}
