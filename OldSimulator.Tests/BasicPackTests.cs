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
    public void EncodesVariablesAndStringsAndRejectsFloatingPoint()
    {
        byte[] tokens = BasicTokenizer.Tokenize("A%=15: PRINT A$, \"OK\"");

        Assert.Equal(BasicProgramFormat.Variable, tokens[0]);
        Assert.Equal((byte)(BasicProgramFormat.TypeInteger | 0), tokens[1]);
        Assert.Equal((byte)'=', tokens[2]);
        Assert.Equal(BasicProgramFormat.IntegerLiteral, tokens[3]);
        Assert.Contains(BasicProgramFormat.StringLiteral, tokens);
        Assert.Throws<BasicPackException>(() => BasicTokenizer.Tokenize("A=1.5"));
    }

    [Fact]
    public void RejectsDuplicateLineNumbersAndNonAsciiStrings()
    {
        Assert.Throws<BasicPackException>(() => BasicTokenizer.ParseProgram("10 END\n10 END"));
        Assert.Throws<BasicPackException>(() => BasicTokenizer.ParseProgram("10 PRINT \"你好\""));
    }

    [Fact]
    public void EncodesDataReadRestoreAndContUsingStableExtensionTokens()
    {
        byte[] tokens = BasicTokenizer.Tokenize("DATA 1,\"X\": READ A%, A$: RESTORE 100: CONT");

        Assert.Equal(BasicProgramFormat.Data, tokens[0]);
        Assert.Contains(BasicProgramFormat.Read, tokens);
        Assert.Contains(BasicProgramFormat.Restore, tokens);
        Assert.Contains(BasicProgramFormat.Cont, tokens);
        Assert.Contains(BasicProgramFormat.IntegerLiteral, tokens);
        Assert.Contains(BasicProgramFormat.StringLiteral, tokens);
    }

    [Fact]
    public void EncodesBasic11GraphicsAndIoKeywords()
    {
        byte[] tokens = BasicTokenizer.Tokenize(
            "SCREEN 0: PSET (1,2),3: LINE (0,0)-(4,4),5: CIRCLE (8,8),3,6: " +
            "PALETTE 1,2,3,4: PRESENT: OUT 80,INP(81): A=POINT(1,2)");

        Assert.Contains(BasicProgramFormat.Screen, tokens);
        Assert.Contains(BasicProgramFormat.Pset, tokens);
        Assert.Contains(BasicProgramFormat.Line, tokens);
        Assert.Contains(BasicProgramFormat.Circle, tokens);
        Assert.Contains(BasicProgramFormat.Palette, tokens);
        Assert.Contains(BasicProgramFormat.Present, tokens);
        Assert.Contains(BasicProgramFormat.Out, tokens);
        Assert.Contains(BasicProgramFormat.Inp, tokens);
        Assert.Contains(BasicProgramFormat.Point, tokens);
    }
}
