using System.Text.Json;
using OldSimulator.Expansion;
using OldSimulator.Expansion.EmbeddedAsm;
using Xunit;

namespace OldSimulator.Tests;

public sealed class EmbeddedAsmExpansionCardTests
{
    [Fact]
    public void EmbeddedFirmwareReceivesTheCommandInterruptAndReturnsItsMailbox()
    {
        var plugin = new EmbeddedAsmExpansionCardPlugin();
        IExpansionCard card = plugin.Create(
            EmbeddedAsmExpansionCardPlugin.CardId,
            new ExpansionCardCreateContext(0),
            JsonSerializer.SerializeToElement(new { }));
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
    public void FirmwareIsEmbeddedInThePluginAssembly()
    {
        string[] resources = typeof(EmbeddedAsmExpansionCardPlugin).Assembly.GetManifestResourceNames();

        Assert.Contains("Open16A.EmbeddedAsm.firmware.bin", resources);
    }

    [Fact]
    public void FirmwareConfigurationDoesNotAcceptRuntimeBase64Overrides()
    {
        var plugin = new EmbeddedAsmExpansionCardPlugin();

        ArgumentException error = Assert.Throws<ArgumentException>(() => plugin.Create(
            EmbeddedAsmExpansionCardPlugin.CardId,
            new ExpansionCardCreateContext(0),
            JsonSerializer.SerializeToElement(new { firmwareBase64 = "AA==" })));

        Assert.Contains("rebuild", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CompletionProbe : IExpansionCardCommand
    {
        public bool Completed { get; private set; }

        public void Complete() => Completed = true;
    }
}
