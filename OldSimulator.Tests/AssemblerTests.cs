using Open16A.Asm;
using Xunit;

namespace OldSimulator.Tests;

public sealed class AssemblerTests
{
    [Fact]
    public void AssemblesHelloThroughTheCharacterCardAndVideoPresent()
    {
        const string source = """
            LI R0, 0000h
            OUT 0030h, R0
            OUT 0031h, R0
            OUT 0034h, R0
            LI R1, 'H'
            LI R2, 'E'
            LI R3, 'L'
            LI R4, 'L'
            LI R5, 'O'
            OUT 0035h, R1
            OUT 0035h, R2
            OUT 0035h, R3
            OUT 0035h, R4
            OUT 0035h, R5
            OUT 0020h, R0
            HALT
            """;

        AssemblyResult result = new Assembler().Assemble(source);

        Assert.Equal((uint)0, result.Origin);
        Assert.Equal(
        [
            0x10, 0x00, 0x00, 0x00,
            0xB8, 0x00, 0x00, 0x30,
            0xB8, 0x00, 0x00, 0x31,
            0xB8, 0x00, 0x00, 0x34,
            0x11, 0x00, 0x00, 0x48,
            0x12, 0x00, 0x00, 0x45,
            0x13, 0x00, 0x00, 0x4C,
            0x14, 0x00, 0x00, 0x4C,
            0x15, 0x00, 0x00, 0x4F,
            0xB8, 0x20, 0x00, 0x35,
            0xB8, 0x40, 0x00, 0x35,
            0xB8, 0x60, 0x00, 0x35,
            0xB8, 0x80, 0x00, 0x35,
            0xB8, 0xA0, 0x00, 0x35,
            0xB8, 0x00, 0x00, 0x20,
            0xE8, 0x00
        ], result.Bytes);
    }

    [Fact]
    public void LabelsDirectivesAndExtendedOperationsUseBigEndianAddresses()
    {
        const string source = """
            .org 0300h
            start:
                LI R0, target
                BEQ R0, R0, target
                CALLL F4000h
            target:
                LSTW R0, [F4002h]
                HALT
            """;

        AssemblyResult result = new Assembler().Assemble(source);

        Assert.Equal((uint)0x0300, result.Origin);
        Assert.Equal(
        [
            0x10, 0x00, 0x03, 0x0E, // LI R0, target
            0x78, 0x00, 0x00, 0x03, // BEQ R0, R0, +3 words
            0xF8, 0x04, 0x40, 0x00, 0x00, 0x0F, // CALLL F4000h
            0xF8, 0x0A, 0x00, 0x00, 0x40, 0x02, 0x00, 0x0F,
            0xE8, 0x00
        ], result.Bytes);
    }

    [Fact]
    public void ReportsInvalidRegistersAndUnresolvedLabelsWithSourceLines()
    {
        AssemblyException register = Assert.Throws<AssemblyException>(() => new Assembler().Assemble("LI R8, 0"));
        AssemblyException label = Assert.Throws<AssemblyException>(() => new Assembler().Assemble("JMPA missing"));

        Assert.StartsWith("Line 1:", register.Message);
        Assert.Contains("R0-R7", register.Message);
        Assert.StartsWith("Line 1:", label.Message);
        Assert.Contains("missing", label.Message);
    }

    [Fact]
    public void AssemblesIeeeFloatingPointAndIntegerOverlayInstructions()
    {
        AssemblyResult result = new Assembler().Assemble(".org 0300h\nFLI FP0, 1.5\nIFPLI FP1, 80000000h\nFADD FP2, FP0, FP1\n");

        Assert.Equal(0x300u, result.Origin);
        Assert.Equal(new byte[]
        {
            0xF8, 0x14, 0x00, 0x00, 0x3F, 0xC0, 0x00, 0x00,
            0xF8, 0x2A, 0x01, 0x00, 0x80, 0x00, 0x00, 0x00,
            0xF8, 0x18, 0x02, 0x04
        }, result.Bytes);
    }
}
