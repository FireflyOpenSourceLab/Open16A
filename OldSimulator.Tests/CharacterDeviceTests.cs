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
        Assert.Equal((byte)2,   fixture.Vram[0]);
        Assert.Equal((byte)9,   fixture.Vram[256]);
        Assert.Equal((byte)2,   fixture.Vram[7 * 256]);
        Assert.Equal((byte)2,   fixture.Vram[5]);
        Assert.Equal((ushort)1, fixture.Device.X);
    }

    [Fact]
    public void TransparentModePreservesGlyphBackground()
    {
        var fixture = new Fixture(VideoMode.Indexed256);
        fixture.Vram.AsSpan(0, 6 * 8 * 256).Fill(7);
        fixture.Bus.Write(CharacterDevice.PortMode,       (ushort)CharacterMode.TransparentBackground);
        fixture.Bus.Write(CharacterDevice.PortForeground, 3);
        fixture.Bus.Write(CharacterDevice.PortPut,        (ushort)'A');

        Assert.Equal((byte)7, fixture.Vram[0]);
        Assert.Equal((byte)3, fixture.Vram[256]);
        Assert.Equal((byte)7, fixture.Vram[5]);
    }

    [Fact]
    public void PutPreservesNeighborPixelsWhenWritingPackedIndexed4VideoRam()
    {
        var fixture = new Fixture(VideoMode.Indexed4);
        fixture.Vram[0] = 0b_01_10_11_00;
        fixture.Bus.Write(CharacterDevice.PortForeground, 2);
        fixture.Bus.Write(CharacterDevice.PortBackground, 1);
        fixture.Bus.Write(CharacterDevice.PortPut,        (ushort)'A');

        // The first row of 'A' has foreground pixels in its middle columns.
        Assert.Equal((byte)0b_01_10_10_10, fixture.Vram[0]);
        // Pixel 4 and the inter-character column are background.
        Assert.Equal((byte)0b_01_01_00_00, fixture.Vram[1]);
        // Row 1 has foreground pixels in glyph column 0, then backgrounds.
        Assert.Equal((byte)0b_10_01_01_01, fixture.Vram[128]);
    }

    [Fact]
    public void PutRejectsRgbaModeWithoutChangingVideoRam()
    {
        var fixture = new Fixture(VideoMode.Rgba8888);
        fixture.Vram[0] = 0xA5;
        fixture.Bus.Write(CharacterDevice.PortPut, (ushort)'A');

        Assert.Equal((byte)0xA5,                                   fixture.Vram[0]);
        Assert.Equal((ushort)CharacterStatus.UnsupportedVideoMode, fixture.Bus.Read(CharacterDevice.PortStatus));
    }

    [Fact]
    public void LineFeedAtBottomScrollsAndClearsLastTextRow()
    {
        var fixture = new Fixture(VideoMode.Indexed256);
        fixture.Bus.Write(CharacterDevice.PortBackground, 4);
        fixture.Vram.AsSpan(0, 256 * 192).Fill(1);
        fixture.Bus.Write(CharacterDevice.PortY,   23);
        fixture.Bus.Write(CharacterDevice.PortPut, (ushort)'\n');

        Assert.Equal((byte)1,    fixture.Vram[0]);
        Assert.Equal((byte)4,    fixture.Vram[(192 - CharacterDevice.CellHeight) * 256]);
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
        Assert.Equal((byte)0b_00_10_10_10, fixture.Vram[0]);
        Assert.Equal((byte)0b_10_00_00_00, fixture.Vram[128]);
    }

    private sealed class Fixture
    {
        public Fixture(VideoMode mode)
        {
            Vram  = new byte[VideoDevice.VIDEO_RAM_LENGTH];
            Bus   = new IoBus();
            Video = new VideoDevice(new InterruptController(), 0x10, Vram);
            Video.Attach(Bus, presentPort: 0x20, statusPort: 0x21);
            Video.TryPresent(mode);
            if (mode != VideoMode.Indexed256)
                Video.AdvanceCycles(VideoDevice.DEFAULT_FRAME_CYCLES);

            Device = new CharacterDevice(Vram, Video);
            Device.Attach(Bus);
        }

        public byte[]          Vram   { get; }
        public IoBus           Bus    { get; }
        public VideoDevice     Video  { get; }
        public CharacterDevice Device { get; }
    }
}
