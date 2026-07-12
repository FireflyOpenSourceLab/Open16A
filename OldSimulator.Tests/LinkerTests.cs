using Open16A.Ld;
using Open16A.Asm;
using Xunit;

namespace OldSimulator.Tests;

public sealed class LinkerTests
{
    [Fact]
    public void LinksFixedAddressModulesAndFillsGaps()
    {
        string directory = Path.Combine(Environment.CurrentDirectory, ".test-linker");
        Directory.CreateDirectory(directory);
        string first = Path.Combine(directory, "first.bin");
        string second = Path.Combine(directory, "second.bin");
        File.WriteAllBytes(first, [0xAA, 0xBB]);
        File.WriteAllBytes(second, [0xCC]);

        LinkResult result = new Linker().Link([new LinkInput(first, 0x0300), new LinkInput(second, 0x0304)]);

        Assert.Equal(0x0300u, result.Origin);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0x00, 0x00, 0xCC }, result.Bytes);
    }

    [Fact]
    public void RejectsOverlappingModules()
    {
        string directory = Path.Combine(Environment.CurrentDirectory, ".test-linker");
        Directory.CreateDirectory(directory);
        string first = Path.Combine(directory, "overlap-a.bin");
        string second = Path.Combine(directory, "overlap-b.bin");
        File.WriteAllBytes(first, [0xAA, 0xBB]);
        File.WriteAllBytes(second, [0xCC]);

        Assert.Throws<LinkException>(() => new Linker().Link([new LinkInput(first, 0x0300), new LinkInput(second, 0x0301)]));
    }

    [Fact]
    public void LinksExternalAbsolute16SymbolsAndRelocatesLocalDataAddresses()
    {
        ObjectModule caller = new Assembler().AssembleObject("""
            .global main
            .extern putchar
            main:
                LI R0, message
                CALLA putchar
                HALT
            message:
                .word 1234h
            """);
        ObjectModule callee = new Assembler().AssembleObject("""
            .global putchar
            putchar:
                RET
            """);

        LinkResult result = new Linker().LinkObjects([caller, callee], 0x0300);

        Assert.Equal(0x0300u, result.Origin);
        Assert.Equal(new byte[]
        {
            0x10, 0x00, 0x03, 0x0A, // LI R0, message
            0xF8, 0x02, 0x03, 0x0C, // CALLA putchar
            0xE8, 0x00,
            0x12, 0x34,
            0x98, 0x00 // RET
        }, result.Bytes);
    }

    [Fact]
    public void LinksExternalAbsolute20AndRelative16Symbols()
    {
        ObjectModule longCaller = new Assembler().AssembleObject("""
            .extern target
            CALLL target
            HALT
            """);
        ObjectModule branchCaller = new Assembler().AssembleObject("""
            .extern target
            BEQ R0, R0, target
            HALT
            """);
        ObjectModule target = new Assembler().AssembleObject("""
            .global target
            target:
                RET
            """);

        LinkResult result = new Linker().LinkObjects([longCaller, branchCaller, target], 0xF0000);

        Assert.Equal(new byte[]
        {
            0xF8, 0x04, 0x00, 0x0E, 0x00, 0x0F, // CALLL F000Eh
            0xE8, 0x00,
            0x78, 0x00, 0x00, 0x01, // BEQ R0, R0, +1 word
            0xE8, 0x00,
            0x98, 0x00
        }, result.Bytes);
    }

    [Fact]
    public void RelocatesSymbolicShortMemoryDisplacements()
    {
        ObjectModule module = new Assembler().AssembleObject("""
            LD.W R0, [R1 + table]
            HALT
            table:
                .word 0000h
            """);

        LinkResult result = new Linker().LinkObjects([module], 0x0300);

        Assert.Equal(new byte[] { 0x20, 0x20, 0x03, 0x06, 0xE8, 0x00, 0x00, 0x00 }, result.Bytes);
    }

    [Fact]
    public void RejectsUnresolvedExternalSymbols()
    {
        ObjectModule caller = new Assembler().AssembleObject("""
            .extern missing
            CALLA missing
            """);

        LinkException exception = Assert.Throws<LinkException>(() => new Linker().LinkObjects([caller], 0x0300));

        Assert.Contains("missing", exception.Message);
    }
}
