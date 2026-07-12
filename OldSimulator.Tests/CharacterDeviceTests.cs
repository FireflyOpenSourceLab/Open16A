using OldSimulator.VirtualDevices;
using Xunit;

namespace OldSimulator.Tests;

public sealed class CharacterDeviceTests
{
    [Fact]
    public void PutDrawsOpaqueGlyphIntoIndexed256VideoRam()
    {
        var fixture = new Fixture(VideoMode.Indexed256);
        fixture.Bus.Write(CharacterDevice.PortForeground, 9);
        fixture.Bus.Write(CharacterDevice.PortBackground, 2);
        fixture.Bus.Write(CharacterDevice.PortPut,        (ushort)'A');

        // 'A' first column is 0b1111110: rows 1 through 6 are foreground.
        Assert.Equal((byte)2, fixture.ReadVram(0));
        Assert.Equal((byte)9, fixture.ReadVram(256));
        Assert.Equal((byte)2, fixture.ReadVram(7 * 256));
        Assert.Equal((byte)2, fixture.ReadVram(5));
        Assert.Equal((ushort)1, fixture.Device.X);
    }

    [Fact]
    public void TransparentModePreservesGlyphBackground()
    {
        var fixture = new Fixture(VideoMode.Indexed256);
        fixture.FillVram(0, 6 * 8 * 256, 7);
        fixture.Bus.Write(CharacterDevice.PortMode,       (ushort)CharacterMode.TransparentBackground);
        fixture.Bus.Write(CharacterDevice.PortForeground, 3);
        fixture.Bus.Write(CharacterDevice.PortPut,        (ushort)'A');

        Assert.Equal((byte)7, fixture.ReadVram(0));
        Assert.Equal((byte)3, fixture.ReadVram(256));
        Assert.Equal((byte)7, fixture.ReadVram(5));
    }

    [Fact]
    public void PutPreservesNeighborPixelsWhenWritingPackedIndexed4VideoRam()
    {
        var fixture = new Fixture(VideoMode.Indexed4);
        fixture.WriteVram(0, 0b_01_10_11_00);
        fixture.Bus.Write(CharacterDevice.PortForeground, 2);
        fixture.Bus.Write(CharacterDevice.PortBackground, 1);
        fixture.Bus.Write(CharacterDevice.PortPut,        (ushort)'A');

        // The first row of 'A' has foreground pixels in its middle columns.
        Assert.Equal((byte)0b_01_10_10_10, fixture.ReadVram(0));
        // Pixel 4 and the inter-character column are background.
        Assert.Equal((byte)0b_01_01_00_00, fixture.ReadVram(1));
        // Row 1 has foreground pixels in glyph column 0, then backgrounds.
        Assert.Equal((byte)0b_10_01_01_01, fixture.ReadVram(128));
    }

    [Fact]
    public void PutRejectsRgbaModeWithoutChangingVideoRam()
    {
        var fixture = new Fixture(VideoMode.Rgba8888);
        fixture.WriteVram(0, 0xA5);
        fixture.Bus.Write(CharacterDevice.PortPut, (ushort)'A');

        Assert.Equal((byte)0xA5, fixture.ReadVram(0));
        Assert.Equal((ushort)CharacterStatus.UnsupportedVideoMode, fixture.Bus.Read(CharacterDevice.PortStatus));
    }

    [Fact]
    public void LineFeedAtBottomScrollsAndClearsLastTextRow()
    {
        var fixture = new Fixture(VideoMode.Indexed256);
        fixture.Bus.Write(CharacterDevice.PortBackground, 4);
        fixture.FillVram(0, 256 * 192, 1);
        fixture.Bus.Write(CharacterDevice.PortY,   23);
        fixture.Bus.Write(CharacterDevice.PortPut, (ushort)'\n');

        Assert.Equal((byte)1, fixture.ReadVram(0));
        Assert.Equal((byte)4, fixture.ReadVram((192 - CharacterDevice.CellHeight) * 256));
        Assert.Equal((ushort)23, fixture.Device.Y);
    }

    [Fact]
    public void PresentImmediatelyChangesCharacterWriteFormat()
    {
        var fixture = new Fixture(VideoMode.Indexed256);
        fixture.Video.AdvanceCycles(VideoDevice.DEFAULT_FRAME_CYCLES);
        fixture.Bus.Write(0x20,                           (ushort)VideoMode.Indexed4);
        fixture.Bus.Write(CharacterDevice.PortForeground, 2);
        fixture.Bus.Write(CharacterDevice.PortPut,        (ushort)'A');

        Assert.Equal(VideoMode.Indexed4,   fixture.Video.WriteMode);
        Assert.Equal((byte)0b_00_10_10_10, fixture.ReadVram(0));
        Assert.Equal((byte)0b_10_00_00_00, fixture.ReadVram(128));
    }

    private sealed class Fixture
    {
        public Fixture(VideoMode mode)
        {
            Memory = new Memory();
            Bus = new IoBus();
            VideoRam = Memory.CreatePhysicalView(0xF4000, VideoDevice.VIDEO_RAM_LENGTH);
            Video = new VideoDevice(new InterruptController(), 0x10, VideoRam);
            Video.Attach(Bus, presentPort: 0x20, statusPort: 0x21);
            Video.TryPresent(mode);
            if (mode != VideoMode.Indexed256)
                Video.AdvanceCycles(VideoDevice.DEFAULT_FRAME_CYCLES);

            Device = new CharacterDevice(VideoRam, Video);
            Device.Attach(Bus);
        }

        public Memory Memory { get; }
        public PhysicalMemoryView VideoRam { get; }
        public IoBus           Bus    { get; }
        public VideoDevice     Video  { get; }
        public CharacterDevice Device { get; }

        public byte ReadVram(int offset) => VideoRam.Read(offset);

        public void WriteVram(int offset, byte value) => VideoRam.Write(offset, value);

        public void FillVram(int offset, int length, byte value)
        {
            for (var i = 0; i < length; i++)
                VideoRam.Write(offset + i, value);
        }
    }
}
