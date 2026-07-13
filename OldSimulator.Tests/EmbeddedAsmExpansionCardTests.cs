using System.Text.Json;
using OldSimulator.Expansion;
using OldSimulator.Expansion.EmbeddedAsm;
using Open16A.Asm;
using Xunit;

namespace OldSimulator.Tests;

public sealed class EmbeddedAsmExpansionCardTests
{
    [Fact]
    public void CompiledFirmwareReceivesTheCommandInterruptAndReturnsItsMailbox()
    {
        AssemblyResult firmware = new Assembler().Assemble(FirmwareSource);
        Assert.Equal((uint)EmbeddedAsmCardLayout.ProgramAddress, firmware.Origin);

        var plugin = new EmbeddedAsmExpansionCardPlugin();
        IExpansionCard card = plugin.Create(
            EmbeddedAsmExpansionCardPlugin.CardId,
            new ExpansionCardCreateContext(0),
            JsonSerializer.SerializeToElement(new { firmwareBase64 = Convert.ToBase64String(firmware.Bytes) }));
        using (card)
        {
            byte[] mailbox = new byte[ExpansionCardApi.MailboxSize];
            mailbox[0] = 0x41;
            var completion = new CompletionProbe();

            card.BeginCommand(0xBEEF, mailbox, completion);
            card.AdvanceCycles(1_000);

            Assert.True(completion.Completed);
            Assert.Equal((byte)0x42, mailbox[0]);
            Assert.Equal((byte)0xBE, mailbox[2]);
            Assert.Equal((byte)0xEF, mailbox[3]);
        }
    }

    [Fact]
    public void FirmwareMustBeCompiledBytecodeThatFitsBeforeTheMailbox()
    {
        var plugin = new EmbeddedAsmExpansionCardPlugin();

        ArgumentException missing = Assert.Throws<ArgumentException>(() => plugin.Create(
            EmbeddedAsmExpansionCardPlugin.CardId,
            new ExpansionCardCreateContext(0),
            JsonSerializer.SerializeToElement(new { })));
        Assert.Contains("firmwareBase64", missing.Message, StringComparison.Ordinal);

        ArgumentException oversized = Assert.Throws<ArgumentException>(() => plugin.Create(
            EmbeddedAsmExpansionCardPlugin.CardId,
            new ExpansionCardCreateContext(0),
            JsonSerializer.SerializeToElement(new
            {
                firmwareBase64 = Convert.ToBase64String(new byte[EmbeddedAsmCardLayout.MailboxAddress - EmbeddedAsmCardLayout.ProgramAddress + 1])
            })));
        Assert.Contains("firmware", oversized.Message, StringComparison.OrdinalIgnoreCase);
    }

    private const string FirmwareSource = """
        .org 0300h
        LI R1, handler
        LI R2, 0010h
        ST.W R1, [R2]
        EI
        LI R4, wait
        wait:
        HALT
        JMP R4
        handler:
        LI R1, FC00h
        ST.W R0, [R1 + 2]
        LD.BU R2, [R1]
        LI R3, 1
        ADD R2, R2, R3
        ST.B R2, [R1]
        IRET
        """;

    private sealed class CompletionProbe : IExpansionCardCommand
    {
        public bool Completed { get; private set; }

        public void Complete() => Completed = true;
    }
}
