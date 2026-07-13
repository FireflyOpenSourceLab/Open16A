using System.Text.Json;
using OldSimulator.Expansion;
using OldSimulator.Expansion.EmbeddedAsm;
using OldSimulator.Expansion.Loopback;
using Xunit;

namespace OldSimulator.Tests;

public sealed class ExpansionPluginLoaderTests
{
    [Fact]
    public void LoadDiscoversThePackagedLoopbackPlugin()
    {
        using var fixture = new LoaderFixture(
            new
            {
                version = 1,
                slots = new[]
                {
                    new
                    {
                        slot = 0,
                        assembly = typeof(LoopbackExpansionCardPlugin).Assembly.Location,
                        cardId = LoopbackExpansionCardPlugin.CardId,
                        settings = new { latencyCycles = 0 }
                    }
                }
            });

        IReadOnlyList<ExpansionCardInstallation> installations =
            ExpansionPluginLoader.Load(fixture.ConfigPath);

        ExpansionCardInstallation installation = Assert.Single(installations);
        try
        {
            Assert.Equal(LoopbackExpansionCardPlugin.CardId, installation.Descriptor.Id);
            byte[] mailbox = new byte[ExpansionCardApi.MailboxSize];
            mailbox[7] = 0xA5;
            var completion = new CompletionProbe();

            installation.Card.BeginCommand(0, mailbox, completion);

            Assert.True(completion.Completed);
            Assert.Equal((byte)0xA5, mailbox[7]);
        }
        finally
        {
            installation.Card.Dispose();
        }
    }

    [Fact]
    public void LoadDiscoversTheEmbeddedAsmPluginWithItsPrivateCoreDependency()
    {
        using var fixture = new LoaderFixture(
            new
            {
                version = 1,
                slots = new[]
                {
                    new
                    {
                        slot = 1,
                        assembly = typeof(EmbeddedAsmExpansionCardPlugin).Assembly.Location,
                        cardId = EmbeddedAsmExpansionCardPlugin.CardId,
                        settings = new { firmwareBase64 = Convert.ToBase64String([0]) }
                    }
                }
            });

        IReadOnlyList<ExpansionCardInstallation> installations =
            ExpansionPluginLoader.Load(fixture.ConfigPath);

        ExpansionCardInstallation installation = Assert.Single(installations);
        try
        {
            Assert.Equal(EmbeddedAsmExpansionCardPlugin.CardId, installation.Descriptor.Id);
        }
        finally
        {
            installation.Card.Dispose();
        }
    }

    [Fact]
    public void LoadReportsThePluginReasonForInvalidCardSettings()
    {
        using var fixture = new LoaderFixture(
            new
            {
                version = 1,
                slots = new[]
                {
                    new
                    {
                        slot = 0,
                        assembly = typeof(LoopbackExpansionCardPlugin).Assembly.Location,
                        cardId = LoopbackExpansionCardPlugin.CardId,
                        settings = new { latencyCycles = -1 }
                    }
                }
            });

        ExpansionPluginLoadException error = Assert.Throws<ExpansionPluginLoadException>(
            () => ExpansionPluginLoader.Load(fixture.ConfigPath));

        Assert.Contains("latencyCycles", error.Message, StringComparison.Ordinal);
        Assert.Contains("non-negative integer", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadCreatesConfiguredCardWithItsDescriptorAndSettings()
    {
        using var fixture = new LoaderFixture(
            new
            {
                version = 1,
                slots = new[]
                {
                    new
                    {
                        slot = 2,
                        assembly = typeof(TestExpansionPlugin).Assembly.Location,
                        cardId = TestExpansionPlugin.CardId,
                        settings = new { marker = 0x5A }
                    }
                }
            });

        IReadOnlyList<ExpansionCardInstallation> installations =
            ExpansionPluginLoader.Load(fixture.ConfigPath);

        ExpansionCardInstallation installation = Assert.Single(installations);
        try
        {
            Assert.Equal(2, installation.Slot);
            Assert.Equal(TestExpansionPlugin.CardId, installation.Descriptor.Id);

            byte[] mailbox = new byte[ExpansionCardApi.MailboxSize];
            var completion = new CompletionProbe();
            installation.Card.BeginCommand(0x1234, mailbox, completion);

            Assert.Equal((byte)2, mailbox[0]);
            Assert.Equal((byte)0x5A, mailbox[1]);
            Assert.Equal((byte)0x34, mailbox[2]);
            Assert.True(completion.Completed);
        }
        finally
        {
            installation.Card.Dispose();
        }
    }

    [Fact]
    public void LoadCachesOnePluginFactoryPerAssemblyPath()
    {
        string assemblyPath = typeof(TestExpansionPlugin).Assembly.Location;
        using var fixture = new LoaderFixture(
            new
            {
                version = 1,
                slots = new[]
                {
                    new { slot = 0, assembly = assemblyPath, cardId = TestExpansionPlugin.CardId },
                    new { slot = 1, assembly = assemblyPath, cardId = TestExpansionPlugin.CardId }
                }
            });

        IReadOnlyList<ExpansionCardInstallation> installations =
            ExpansionPluginLoader.Load(fixture.ConfigPath);

        try
        {
            Assert.Equal(2, installations.Count);
            Assert.Equal((byte)1, GetCreationSerial(installations[0].Card));
            Assert.Equal((byte)2, GetCreationSerial(installations[1].Card));
        }
        finally
        {
            foreach (ExpansionCardInstallation installation in installations)
                installation.Card.Dispose();
        }
    }

    [Fact]
    public void LoadRejectsUnknownCardIdAndDisposesCardsCreatedEarlier()
    {
        using var fixture = new LoaderFixture();
        string disposeMarker = Path.Combine(fixture.DirectoryPath, "disposed.txt");
        fixture.WriteConfiguration(
            new
            {
                version = 1,
                slots = new object[]
                {
                    new
                    {
                        slot = 0,
                        assembly = typeof(TestExpansionPlugin).Assembly.Location,
                        cardId = TestExpansionPlugin.CardId,
                        settings = new { disposeMarker }
                    },
                    new
                    {
                        slot = 1,
                        assembly = typeof(TestExpansionPlugin).Assembly.Location,
                        cardId = "missing.card"
                    }
                }
            });

        var error = Assert.Throws<ExpansionPluginLoadException>(
            () => ExpansionPluginLoader.Load(fixture.ConfigPath));

        Assert.Contains("missing.card", error.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(disposeMarker));
    }

    [Fact]
    public void LoadRejectsAssemblyWithoutExactlyOnePluginFactory()
    {
        using var fixture = new LoaderFixture(
            new
            {
                version = 1,
                slots = new[]
                {
                    new
                    {
                        slot = 0,
                        assembly = typeof(ExpansionPluginLoader).Assembly.Location,
                        cardId = "missing.card"
                    }
                }
            });

        var error = Assert.Throws<ExpansionPluginLoadException>(
            () => ExpansionPluginLoader.Load(fixture.ConfigPath));

        Assert.Contains("exactly one", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte GetCreationSerial(IExpansionCard card)
    {
        byte[] mailbox = new byte[ExpansionCardApi.MailboxSize];
        card.BeginCommand(0, mailbox, new CompletionProbe());
        return mailbox[3];
    }

    private sealed class CompletionProbe : IExpansionCardCommand
    {
        public bool Completed { get; private set; }

        public void Complete()
        {
            Completed = true;
        }
    }

    private sealed class LoaderFixture : IDisposable
    {
        public LoaderFixture(object? configuration = null)
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"OldSimulator-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
            ConfigPath = Path.Combine(DirectoryPath, "simulator.json");
            if (configuration is not null)
                WriteConfiguration(configuration);
        }

        public string DirectoryPath { get; }

        public string ConfigPath { get; }

        public void WriteConfiguration(object configuration)
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(configuration));
        }

        public void Dispose()
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}

public sealed class TestExpansionPlugin : IExpansionCardPlugin
{
    public const string CardId = "oldsim.tests.card";

    private int cardsCreated;

    public int ApiVersion => ExpansionCardApi.Version;

    public IReadOnlyList<ExpansionCardDescriptor> Cards { get; } =
        [new ExpansionCardDescriptor(CardId, "Test expansion card", 1)];

    public IExpansionCard Create(
        string cardId,
        ExpansionCardCreateContext context,
        JsonElement settings)
    {
        int marker = settings.TryGetProperty("marker", out JsonElement markerElement)
            ? markerElement.GetInt32()
            : 0;
        string? disposeMarker = settings.TryGetProperty("disposeMarker", out JsonElement pathElement)
            ? pathElement.GetString()
            : null;

        cardsCreated++;
        return new TestExpansionCard(context.Slot, marker, cardsCreated, disposeMarker);
    }

    private sealed class TestExpansionCard(
        int slot,
        int marker,
        int creationSerial,
        string? disposeMarker) : IExpansionCard
    {
        public void BeginCommand(ushort command, Memory<byte> mailbox, IExpansionCardCommand completion)
        {
            mailbox.Span[0] = (byte)slot;
            mailbox.Span[1] = (byte)marker;
            mailbox.Span[2] = (byte)command;
            mailbox.Span[3] = (byte)creationSerial;
            completion.Complete();
        }

        public void AdvanceCycles(ulong cycles)
        {
        }

        public void Reset()
        {
        }

        public void Dispose()
        {
            if (disposeMarker is not null)
                File.WriteAllText(disposeMarker, "disposed");
        }
    }
}
