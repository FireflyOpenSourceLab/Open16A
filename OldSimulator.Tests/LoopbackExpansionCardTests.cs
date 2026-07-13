using System.Text.Json;
using OldSimulator.Expansion;
using OldSimulator.Expansion.Loopback;
using Xunit;

namespace OldSimulator.Tests;

public sealed class LoopbackExpansionCardTests
{
    [Fact]
    public void EchoCompletesAfterTheConfiguredVirtualLatency()
    {
        IExpansionCard card = CreateCard("{\"latencyCycles\":32}");
        using (card)
        {
            byte[] mailbox = Enumerable.Range(0, ExpansionCardApi.MailboxSize)
                .Select(index => (byte)index)
                .ToArray();
            byte[] original = [.. mailbox];
            var completion = new CompletionProbe();

            card.BeginCommand(0, mailbox, completion);
            card.AdvanceCycles(31);
            Assert.False(completion.Completed);

            card.AdvanceCycles(1);

            Assert.True(completion.Completed);
            Assert.Equal(original, mailbox);
        }
    }

    [Fact]
    public void ZeroLatencyUnknownCommandWritesTheCardSpecificErrorMarker()
    {
        IExpansionCard card = CreateCard("{\"latencyCycles\":0}");
        using (card)
        {
            byte[] mailbox = new byte[ExpansionCardApi.MailboxSize];
            var completion = new CompletionProbe();

            card.BeginCommand(1, mailbox, completion);

            Assert.True(completion.Completed);
            Assert.Equal((byte)0xFF, mailbox[0]);
            Assert.Equal((byte)0xFF, mailbox[1]);
        }
    }

    [Fact]
    public void ResetCancelsThePendingCompletion()
    {
        IExpansionCard card = CreateCard("{\"latencyCycles\":10}");
        using (card)
        {
            var completion = new CompletionProbe();
            card.BeginCommand(0, new byte[ExpansionCardApi.MailboxSize], completion);

            card.Reset();
            card.AdvanceCycles(10);

            Assert.False(completion.Completed);
        }
    }

    private static IExpansionCard CreateCard(string settingsJson)
    {
        using JsonDocument document = JsonDocument.Parse(settingsJson);
        var plugin = new LoopbackExpansionCardPlugin();
        return plugin.Create(
            LoopbackExpansionCardPlugin.CardId,
            new ExpansionCardCreateContext(0),
            document.RootElement.Clone());
    }

    private sealed class CompletionProbe : IExpansionCardCommand
    {
        public bool Completed { get; private set; }

        public void Complete() => Completed = true;
    }
}
