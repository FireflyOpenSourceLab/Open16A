namespace OldSimulator.VirtualDevices;

public enum VideoMode : ushort
{
    Indexed256 = 0,
    Indexed4   = 1,
    Rgba8888   = 2
}

[Flags]
public enum VideoStatus : ushort
{
    None            = 0,
    PresentBusy     = 1 << 0,
    PresentRejected = 1 << 1
}

public readonly record struct Rgb24(byte Red, byte Green, byte Blue);

public readonly record struct VideoFrame(
    ulong                 Serial,
    VideoMode             Mode,
    ReadOnlyMemory<byte>  Vram,
    ReadOnlyMemory<Rgb24> Palette,
    Rgb24                 Backdrop);

public sealed class VideoDevice : IClockedDevice
{
    public const int   VIDEO_RAM_LENGTH     = 0xC000;
    public const ulong DEFAULT_FRAME_CYCLES = 282_240;

    private readonly InterruptController interrupts;
    private readonly byte                interruptVector;
    private readonly Memory<byte>        videoRam;
    private readonly byte[]              pendingVram      = new byte[VIDEO_RAM_LENGTH];
    private readonly byte[]              displayedVram    = new byte[VIDEO_RAM_LENGTH];
    private readonly Rgb24[]             palette          = new Rgb24[256];
    private readonly Rgb24[]             pendingPalette   = new Rgb24[256];
    private readonly Rgb24[]             displayedPalette = new Rgb24[256];
    private readonly ulong               frameCycles;

    private ulong     cyclesUntilVBlank;
    private bool      presentPending;
    private VideoMode pendingMode;
    private Rgb24     pendingBackdrop;
    private Rgb24     displayedBackdrop;

    public VideoDevice(
        InterruptController interrupts,
        byte                interruptVector,
        Memory<byte>        videoRam,
        ulong               frameCycles = DEFAULT_FRAME_CYCLES)
    {
        ArgumentNullException.ThrowIfNull(interrupts);

        if (videoRam.Length != VIDEO_RAM_LENGTH)
            throw new ArgumentException($"Video RAM must be exactly {VIDEO_RAM_LENGTH} bytes.", nameof(videoRam));

        if (frameCycles == 0)
            throw new ArgumentOutOfRangeException(nameof(frameCycles));

        this.interrupts      = interrupts;
        this.interruptVector = interruptVector;
        this.videoRam        = videoRam;
        this.frameCycles     = frameCycles;
        cyclesUntilVBlank    = frameCycles;
        CurrentMode          = VideoMode.Indexed256;
        WriteMode            = VideoMode.Indexed256;
        displayedBackdrop    = Backdrop;
    }

    public VideoMode CurrentMode { get; private set; }

    public VideoMode WriteMode { get; private set; }

    public Rgb24 Backdrop { get; set; }

    public VideoStatus Status { get; private set; }

    public ulong FrameSerial { get; private set; }

    public VideoFrame CurrentFrame => new(
        FrameSerial,
        CurrentMode,
        displayedVram,
        displayedPalette,
        displayedBackdrop);

    public void Attach(IoBus ioBus, ushort presentPort, ushort statusPort)
    {
        ArgumentNullException.ThrowIfNull(ioBus);
        ioBus.RegisterWrite(presentPort, WritePresent);
        ioBus.RegisterRead(statusPort, ReadStatus);
    }

    public void SetPaletteEntry(byte index, Rgb24 color)
    {
        palette[index] = color;
    }

    public Rgb24 GetPaletteEntry(byte index) => palette[index];

    public bool TryPresent(VideoMode mode)
    {
        if (!IsSupportedMode(mode))
        {
            Status |= VideoStatus.PresentRejected;
            return false;
        }

        if (presentPending)
        {
            Status |= VideoStatus.PresentRejected;
            return false;
        }

        videoRam.Span.CopyTo(pendingVram);
        palette.CopyTo(pendingPalette, 0);
        pendingMode     =  mode;
        WriteMode       =  mode;
        pendingBackdrop =  Backdrop;
        presentPending  =  true;
        Status          &= ~VideoStatus.PresentRejected;
        Status          |= VideoStatus.PresentBusy;
        return true;
    }

    public void AdvanceCycles(ulong cycles)
    {
        while (cycles >= cyclesUntilVBlank)
        {
            cycles            -= cyclesUntilVBlank;
            cyclesUntilVBlank =  frameCycles;
            OnVBlank();
        }

        cyclesUntilVBlank -= cycles;
    }

    private void WritePresent(ushort rawMode)
    {
        TryPresent((VideoMode)rawMode);
    }

    private ushort ReadStatus() => (ushort)Status;

    private void OnVBlank()
    {
        if (!presentPending)
            return;

        pendingVram.CopyTo(displayedVram, 0);
        pendingPalette.CopyTo(displayedPalette, 0);
        CurrentMode       =  pendingMode;
        displayedBackdrop =  pendingBackdrop;
        presentPending    =  false;
        Status            &= ~VideoStatus.PresentBusy;
        FrameSerial++;
        interrupts.Raise(interruptVector);
    }

    private static bool IsSupportedMode(VideoMode mode)
    {
        return mode is VideoMode.Indexed256 or VideoMode.Indexed4 or VideoMode.Rgba8888;
    }
}
