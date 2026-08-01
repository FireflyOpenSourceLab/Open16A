using System.Text.Json;
using OldSimulator.Expansion;
using OldSimulator.Expansion.Disk;
using Xunit;

namespace OldSimulator.Tests;

public sealed class DiskExpansionCardTests : IDisposable
{
    private readonly string tempDir;

    public DiskExpansionCardTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "open16a-disk-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; failing to remove temp files is not a test failure.
        }
    }

    [Fact]
    public void MissingImagePathIsRejected()
    {
        Assert.Throws<ArgumentException>(() => CreateCard("{\"readOnly\":false}"));
    }

    [Fact]
    public void RelativeImagePathIsRejected()
    {
        string json = JsonSerializer.Serialize(new { imagePath = "disk.img" });
        ArgumentException error = Assert.Throws<ArgumentException>(() => CreateCard(json));
        Assert.Contains("absolute", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonexistentImageIsRejected()
    {
        string missing = Path.Combine(tempDir, "missing.img");
        string json = JsonSerializer.Serialize(new { imagePath = missing });
        ArgumentException error = Assert.Throws<ArgumentException>(() => CreateCard(json));
        Assert.Contains("could not be opened", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyImageIsRejected()
    {
        string path = CreateImage(0);
        string json = JsonSerializer.Serialize(new { imagePath = path });
        ArgumentException error = Assert.Throws<ArgumentException>(() => CreateCard(json));
        Assert.Contains("multiple", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonSectorAlignedImageIsRejected()
    {
        string path = Path.Combine(tempDir, "odd.img");
        File.WriteAllBytes(path, new byte[100]);
        string json = JsonSerializer.Serialize(new { imagePath = path });
        ArgumentException error = Assert.Throws<ArgumentException>(() => CreateCard(json));
        Assert.Contains("multiple", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonBooleanReadOnlyIsRejected()
    {
        string path = CreateImage(1);
        string json = JsonSerializer.Serialize(new { imagePath = path, readOnly = "yes" });
        Assert.Throws<ArgumentException>(() => CreateCard(json));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1.5)]
    [InlineData("fast")]
    public void InvalidLatencyCyclesIsRejected(object latencyCycles)
    {
        string path = CreateImage(1);
        string json = JsonSerializer.Serialize(new { imagePath = path, latencyCycles });
        Assert.Throws<ArgumentException>(() => CreateCard(json));
    }

    [Fact]
    public void UnknownCardIdIsRejected()
    {
        string path = CreateImage(1);
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(new { imagePath = path }));
        var plugin = new DiskExpansionCardPlugin();

        Assert.Throws<ArgumentException>(() => plugin.Create(
            "open16a.not-a-card",
            new ExpansionCardCreateContext(0),
            document.RootElement.Clone()));
    }

    [Fact]
    public void IdentifyReportsCapacityAndReadWriteFlags()
    {
        string path = CreateImage(4);
        using IExpansionCard card = CreateCard(JsonSerializer.Serialize(new { imagePath = path, latencyCycles = 0 }));
        byte[] mailbox = NewMailbox();
        var completion = new CompletionProbe();

        card.BeginCommand(DiskCardProtocol.CommandIdentify, mailbox, completion);

        Assert.True(completion.Completed);
        Assert.Equal(DiskCardProtocol.StatusOk, ReadStatus(mailbox));
        Assert.Equal("ODSK", ReadAscii(mailbox, DiskCardProtocol.MagicOffset, 4));
        Assert.Equal(DiskCardProtocol.ProtocolVersion, ReadUInt16(mailbox, DiskCardProtocol.VersionOffset));
        Assert.Equal(DiskCardProtocol.SectorSize, ReadUInt16(mailbox, DiskCardProtocol.SectorSizeOffset));
        Assert.Equal(4u, ReadUInt32(mailbox, DiskCardProtocol.SectorCountOffset));
        Assert.Equal(0, ReadUInt16(mailbox, DiskCardProtocol.FlagsOffset));
    }

    [Fact]
    public void IdentifyReportsReadOnlyFlag()
    {
        string path = CreateImage(1);
        using IExpansionCard card = CreateCard(JsonSerializer.Serialize(new { imagePath = path, readOnly = true, latencyCycles = 0 }));
        byte[] mailbox = NewMailbox();

        card.BeginCommand(DiskCardProtocol.CommandIdentify, mailbox, new CompletionProbe());

        Assert.Equal(DiskCardProtocol.FlagReadOnly, ReadUInt16(mailbox, DiskCardProtocol.FlagsOffset));
    }

    [Fact]
    public void ReadReturnsTheSectorContent()
    {
        byte[] sector = Enumerable.Range(0, DiskCardProtocol.SectorSize).Select(index => (byte)(index * 3)).ToArray();
        string path = CreateImage(4, (data, _) => sector.CopyTo(data, 2 * DiskCardProtocol.SectorSize));
        using IExpansionCard card = CreateCard(JsonSerializer.Serialize(new { imagePath = path, latencyCycles = 0 }));
        byte[] mailbox = NewMailbox();
        SetLba(mailbox, 2);

        card.BeginCommand(DiskCardProtocol.CommandRead, mailbox, new CompletionProbe());

        Assert.Equal(DiskCardProtocol.StatusOk, ReadStatus(mailbox));
        Assert.Equal(sector, mailbox[DiskCardProtocol.DataOffset..(DiskCardProtocol.DataOffset + DiskCardProtocol.SectorSize)]);
    }

    [Fact]
    public void ReadAddressesSectorsBeyondTheSixteenBitRange()
    {
        const int sectorCount = 70_000;
        string path = CreateImage(sectorCount, (data, _) =>
        {
            for (var sector = 0; sector < sectorCount; sector++)
                data[sector * DiskCardProtocol.SectorSize] = (byte)sector;
        });
        using IExpansionCard card = CreateCard(JsonSerializer.Serialize(new { imagePath = path, latencyCycles = 0 }));
        byte[] mailbox = NewMailbox();
        const uint lba = 66_000;
        SetLba(mailbox, lba);

        card.BeginCommand(DiskCardProtocol.CommandRead, mailbox, new CompletionProbe());

        Assert.Equal(DiskCardProtocol.StatusOk, ReadStatus(mailbox));
        Assert.Equal((byte)(lba & 0xFF), mailbox[DiskCardProtocol.DataOffset]);
    }

    [Fact]
    public void WritePersistsTheSectorPayload()
    {
        byte[] sector = Enumerable.Range(0, DiskCardProtocol.SectorSize).Select(index => (byte)index).ToArray();
        string path = CreateImage(4);
        using (IExpansionCard card = CreateCard(JsonSerializer.Serialize(new { imagePath = path, latencyCycles = 0 })))
        {
            byte[] mailbox = NewMailbox();
            SetLba(mailbox, 3);
            sector.CopyTo(mailbox, DiskCardProtocol.DataOffset);

            card.BeginCommand(DiskCardProtocol.CommandWrite, mailbox, new CompletionProbe());

            Assert.Equal(DiskCardProtocol.StatusOk, ReadStatus(mailbox));

            byte[] readback = NewMailbox();
            SetLba(readback, 3);
            card.BeginCommand(DiskCardProtocol.CommandRead, readback, new CompletionProbe());
            Assert.Equal(sector, readback[DiskCardProtocol.DataOffset..(DiskCardProtocol.DataOffset + DiskCardProtocol.SectorSize)]);
        }

        byte[] onDisk = File.ReadAllBytes(path);
        Assert.Equal(sector, onDisk[(3 * DiskCardProtocol.SectorSize)..(4 * DiskCardProtocol.SectorSize)]);
    }

    [Fact]
    public void WriteOnReadOnlyCardIsRejectedAndLeavesTheImageUntouched()
    {
        string path = CreateImage(1);
        using (IExpansionCard card = CreateCard(JsonSerializer.Serialize(new { imagePath = path, readOnly = true, latencyCycles = 0 })))
        {
            byte[] mailbox = NewMailbox();
            SetLba(mailbox, 0);
            mailbox[DiskCardProtocol.DataOffset] = 0xAB;

            card.BeginCommand(DiskCardProtocol.CommandWrite, mailbox, new CompletionProbe());

            Assert.Equal(DiskCardProtocol.StatusWriteProtected, ReadStatus(mailbox));
            Assert.All(mailbox[DiskCardProtocol.DataOffset..(DiskCardProtocol.DataOffset + DiskCardProtocol.SectorSize)], value => Assert.Equal(0, value));
        }

        Assert.All(File.ReadAllBytes(path), value => Assert.Equal(0, value));
    }

    [Fact]
    public void ReadBeyondCapacityIsRejected()
    {
        string path = CreateImage(4);
        using IExpansionCard card = CreateCard(JsonSerializer.Serialize(new { imagePath = path, latencyCycles = 0 }));
        byte[] mailbox = NewMailbox();
        SetLba(mailbox, 4);

        card.BeginCommand(DiskCardProtocol.CommandRead, mailbox, new CompletionProbe());

        Assert.Equal(DiskCardProtocol.StatusLbaOutOfRange, ReadStatus(mailbox));
        Assert.All(mailbox[DiskCardProtocol.DataOffset..(DiskCardProtocol.DataOffset + DiskCardProtocol.SectorSize)], value => Assert.Equal(0, value));
    }

    [Fact]
    public void WriteBeyondCapacityIsRejected()
    {
        string path = CreateImage(4);
        using IExpansionCard card = CreateCard(JsonSerializer.Serialize(new { imagePath = path, latencyCycles = 0 }));
        byte[] mailbox = NewMailbox();
        SetLba(mailbox, 4_000_000_000);

        card.BeginCommand(DiskCardProtocol.CommandWrite, mailbox, new CompletionProbe());

        Assert.Equal(DiskCardProtocol.StatusLbaOutOfRange, ReadStatus(mailbox));
    }

    [Fact]
    public void UnknownCommandIsRejected()
    {
        string path = CreateImage(1);
        using IExpansionCard card = CreateCard(JsonSerializer.Serialize(new { imagePath = path, latencyCycles = 0 }));
        byte[] mailbox = NewMailbox();
        mailbox[DiskCardProtocol.DataOffset] = 0x55;

        card.BeginCommand(0x00FF, mailbox, new CompletionProbe());

        Assert.Equal(DiskCardProtocol.StatusUnknownCommand, ReadStatus(mailbox));
        Assert.All(mailbox[DiskCardProtocol.DataOffset..(DiskCardProtocol.DataOffset + DiskCardProtocol.SectorSize)], value => Assert.Equal(0, value));
    }

    [Fact]
    public void CommandCompletesAfterTheConfiguredVirtualLatency()
    {
        string path = CreateImage(1);
        using IExpansionCard card = CreateCard(JsonSerializer.Serialize(new { imagePath = path, latencyCycles = 64 }));
        byte[] mailbox = NewMailbox();
        var completion = new CompletionProbe();

        card.BeginCommand(DiskCardProtocol.CommandIdentify, mailbox, completion);
        Assert.False(completion.Completed);

        card.AdvanceCycles(63);
        Assert.False(completion.Completed);

        card.AdvanceCycles(1);
        Assert.True(completion.Completed);
    }

    [Fact]
    public void ZeroLatencyCompletesSynchronously()
    {
        string path = CreateImage(1);
        using IExpansionCard card = CreateCard(JsonSerializer.Serialize(new { imagePath = path, latencyCycles = 0 }));
        byte[] mailbox = NewMailbox();
        var completion = new CompletionProbe();

        card.BeginCommand(DiskCardProtocol.CommandIdentify, mailbox, completion);

        Assert.True(completion.Completed);
    }

    [Fact]
    public void ResetCancelsThePendingCompletion()
    {
        string path = CreateImage(1);
        using IExpansionCard card = CreateCard(JsonSerializer.Serialize(new { imagePath = path, latencyCycles = 100 }));
        byte[] mailbox = NewMailbox();
        var completion = new CompletionProbe();

        card.BeginCommand(DiskCardProtocol.CommandIdentify, mailbox, completion);
        card.Reset();
        card.AdvanceCycles(100);

        Assert.False(completion.Completed);
    }

    [Fact]
    public void DisposeReleasesTheImageFile()
    {
        string path = CreateImage(1);
        IExpansionCard card = CreateCard(JsonSerializer.Serialize(new { imagePath = path }));
        card.Dispose();

        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    private string CreateImage(int sectors, Action<byte[], int>? fill = null)
    {
        string path = Path.Combine(tempDir, "disk.img");
        var data = new byte[sectors * DiskCardProtocol.SectorSize];
        fill?.Invoke(data, sectors);
        File.WriteAllBytes(path, data);
        return path;
    }

    private static IExpansionCard CreateCard(string settingsJson)
    {
        using JsonDocument document = JsonDocument.Parse(settingsJson);
        var plugin = new DiskExpansionCardPlugin();
        return plugin.Create(
            DiskExpansionCardPlugin.CardId,
            new ExpansionCardCreateContext(0),
            document.RootElement.Clone());
    }

    private static byte[] NewMailbox() => new byte[ExpansionCardApi.MailboxSize];

    private static void SetLba(byte[] mailbox, uint lba)
    {
        mailbox[DiskCardProtocol.LbaOffset] = (byte)(lba >> 24);
        mailbox[DiskCardProtocol.LbaOffset + 1] = (byte)(lba >> 16);
        mailbox[DiskCardProtocol.LbaOffset + 2] = (byte)(lba >> 8);
        mailbox[DiskCardProtocol.LbaOffset + 3] = (byte)lba;
    }

    private static ushort ReadStatus(byte[] mailbox) =>
        ReadUInt16(mailbox, DiskCardProtocol.StatusOffset);

    private static ushort ReadUInt16(byte[] mailbox, int offset) =>
        (ushort)((mailbox[offset] << 8) | mailbox[offset + 1]);

    private static uint ReadUInt32(byte[] mailbox, int offset) =>
        ((uint)mailbox[offset] << 24) | ((uint)mailbox[offset + 1] << 16) |
        ((uint)mailbox[offset + 2] << 8) | mailbox[offset + 3];

    private static string ReadAscii(byte[] mailbox, int offset, int count) =>
        System.Text.Encoding.ASCII.GetString(mailbox, offset, count);

    private sealed class CompletionProbe : IExpansionCardCommand
    {
        public bool Completed { get; private set; }

        public void Complete() => Completed = true;
    }
}
