using OldSimulator.VirtualDevices;
using Xunit;

namespace OldSimulator.Tests;

public sealed class VideoDeviceTests
{
    [Fact]
    public void StartsWithTheOpen16aVgaStylePalette()
    {
        var fixture = new Fixture();

        Assert.Equal(new Rgb24(0x00, 0x00, 0x00), fixture.Video.CurrentFrame.Palette.Span[0x00]);
        Assert.Equal(new Rgb24(0x00, 0x00, 0xAA), fixture.Video.CurrentFrame.Palette.Span[0x01]);
        Assert.Equal(new Rgb24(0xFF, 0xFF, 0x55), fixture.Video.CurrentFrame.Palette.Span[0x0E]);
        Assert.Equal(new Rgb24(0x00, 0x00, 0x00), fixture.Video.CurrentFrame.Palette.Span[0x10]);
        Assert.Equal(new Rgb24(0xFF, 0xFF, 0xFF), fixture.Video.CurrentFrame.Palette.Span[0xE7]);
        Assert.Equal(new Rgb24(0x08, 0x08, 0x08), fixture.Video.CurrentFrame.Palette.Span[0xE8]);
        Assert.Equal(new Rgb24(0xEE, 0xEE, 0xEE), fixture.Video.CurrentFrame.Palette.Span[0xFF]);
    }

    [Fact]
    public void PalettePortsTransferRgbBytesAndAutoIncrementTheIndex()
    {
        var fixture = new Fixture();

        fixture.Bus.Write(VideoDevice.PortPaletteIndex, 0x2A);
        fixture.Bus.Write(VideoDevice.PortPaletteData, 0x12);
        fixture.Bus.Write(VideoDevice.PortPaletteData, 0x34);
        fixture.Bus.Write(VideoDevice.PortPaletteData, 0x56);

        Assert.Equal(new Rgb24(0x12, 0x34, 0x56), fixture.Video.GetPaletteEntry(0x2A));
        Assert.Equal((ushort)0x2B, fixture.Bus.Read(VideoDevice.PortPaletteIndex));

        fixture.Bus.Write(VideoDevice.PortPaletteIndex, 0x2A);
        Assert.Equal((ushort)0x12, fixture.Bus.Read(VideoDevice.PortPaletteData));
        Assert.Equal((ushort)0x34, fixture.Bus.Read(VideoDevice.PortPaletteData));
        Assert.Equal((ushort)0x56, fixture.Bus.Read(VideoDevice.PortPaletteData));
        Assert.Equal((ushort)0x2B, fixture.Bus.Read(VideoDevice.PortPaletteIndex));
    }

    [Fact]
    public void PresentSnapshotsThePaletteUntilVBlank()
    {
        var fixture = new Fixture();
        fixture.Video.SetPaletteEntry(3, new Rgb24(1, 2, 3));

        fixture.Bus.Write(VideoDevice.PortPresent, (ushort)VideoMode.Indexed256);
        fixture.Video.SetPaletteEntry(3, new Rgb24(4, 5, 6));
        fixture.Video.AdvanceCycles(VideoDevice.DEFAULT_FRAME_CYCLES);

        Assert.Equal(new Rgb24(1, 2, 3), fixture.Video.CurrentFrame.Palette.Span[3]);
        Assert.Equal(new Rgb24(4, 5, 6), fixture.Video.GetPaletteEntry(3));
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            var memory = new Memory();
            Bus = new IoBus();
            Video = new VideoDevice(
                new InterruptController(),
                Machine.VIDEO_INTERRUPT_VECTOR,
                memory.CreatePhysicalView(Machine.VIDEO_RAM_ADDRESS, VideoDevice.VIDEO_RAM_LENGTH));
            Video.Attach(Bus, VideoDevice.PortPresent, VideoDevice.PortStatus);
        }

        public IoBus Bus { get; }
        public VideoDevice Video { get; }
    }
}
