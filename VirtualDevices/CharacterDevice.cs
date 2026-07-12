namespace OldSimulator.VirtualDevices;

[Flags]
public enum CharacterMode : ushort
{
    OpaqueBackground = 0,
    TransparentBackground = 1 << 0
}

[Flags]
public enum CharacterStatus : ushort
{
    None = 0,
    UnsupportedVideoMode = 1 << 0,
    CoordinateClamped = 1 << 1
}

public sealed class CharacterDevice
{
    public const ushort PortX = 0x30;
    public const ushort PortY = 0x31;
    public const ushort PortForeground = 0x32;
    public const ushort PortBackground = 0x33;
    public const ushort PortMode = 0x34;
    public const ushort PortPut = 0x35;
    public const ushort PortStatus = 0x36;
    public const ushort PortClear = 0x37;

    public const int CellWidth = 6;
    public const int CellHeight = 8;

    private readonly Memory<byte> videoRam;
    private readonly VideoDevice video;

    public CharacterDevice(Memory<byte> videoRam, VideoDevice video)
    {
        ArgumentNullException.ThrowIfNull(video);

        if (videoRam.Length != VideoDevice.VIDEO_RAM_LENGTH)
            throw new ArgumentException($"Video RAM must be exactly {VideoDevice.VIDEO_RAM_LENGTH} bytes.", nameof(videoRam));

        this.videoRam = videoRam;
        this.video = video;
    }

    public ushort X { get; private set; }

    public ushort Y { get; private set; }

    public ushort Foreground { get; private set; } = 15;

    public ushort Background { get; private set; }

    public CharacterMode Mode { get; private set; }

    public CharacterStatus Status { get; private set; }

    public void Attach(IoBus ioBus)
    {
        ArgumentNullException.ThrowIfNull(ioBus);

        ioBus.RegisterRead(PortX, () => X);
        ioBus.RegisterRead(PortY, () => Y);
        ioBus.RegisterRead(PortForeground, () => Foreground);
        ioBus.RegisterRead(PortBackground, () => Background);
        ioBus.RegisterRead(PortMode, () => (ushort)Mode);
        ioBus.RegisterRead(PortStatus, () => (ushort)Status);

        ioBus.RegisterWrite(PortX, SetX);
        ioBus.RegisterWrite(PortY, SetY);
        ioBus.RegisterWrite(PortForeground, value => Foreground = value);
        ioBus.RegisterWrite(PortBackground, value => Background = value);
        ioBus.RegisterWrite(PortMode, value => Mode = (CharacterMode)(value & 1));
        ioBus.RegisterWrite(PortPut, Put);
        ioBus.RegisterWrite(PortClear, _ => Clear());
    }

    public void Put(ushort value)
    {
        if (!TryGetLayout(out TextLayout layout))
        {
            Status |= CharacterStatus.UnsupportedVideoMode;
            return;
        }

        NormalizeCursor(layout);
        byte character = (byte)value;

        switch (character)
        {
            case (byte)'\r':
                X = 0;
                return;
            case (byte)'\n':
                LineFeed(layout, preserveColumn: true);
                return;
            case (byte)'\b':
                if (X > 0)
                    X--;
                return;
            case (byte)'\t':
                AdvanceTab(layout);
                return;
        }

        DrawGlyph(layout, character is >= 0x20 and <= 0x7E ? character : (byte)'?');
        AdvanceCharacter(layout);
    }

    public void Clear()
    {
        if (!TryGetLayout(out TextLayout layout))
        {
            Status |= CharacterStatus.UnsupportedVideoMode;
            return;
        }

        for (var y = 0; y < layout.PixelHeight; y++)
        {
            for (var x = 0; x < layout.PixelWidth; x++)
                WritePixel(layout, x, y, Background);
        }

        X = 0;
        Y = 0;
    }

    private void SetX(ushort value)
    {
        X = value;
        ClampCursorToMaximum();
    }

    private void SetY(ushort value)
    {
        Y = value;
        ClampCursorToMaximum();
    }

    private void DrawGlyph(TextLayout layout, byte character)
    {
        int originX = X * CellWidth;
        int originY = Y * CellHeight;
        ReadOnlySpan<byte> glyph = Font5x7.GetGlyph(character);

        for (var glyphY = 0; glyphY < 7; glyphY++)
        {
            for (var glyphX = 0; glyphX < 5; glyphX++)
            {
                bool foreground = (glyph[glyphX] & (1 << glyphY)) != 0;
                if (foreground)
                    WritePixel(layout, originX + glyphX, originY + glyphY, Foreground);
                else if (Mode == CharacterMode.OpaqueBackground)
                    WritePixel(layout, originX + glyphX, originY + glyphY, Background);
            }
        }

        if (Mode != CharacterMode.OpaqueBackground)
            return;

        for (var glyphY = 0; glyphY < CellHeight; glyphY++)
            WritePixel(layout, originX + 5, originY + glyphY, Background);

        for (var glyphX = 0; glyphX < 5; glyphX++)
            WritePixel(layout, originX + glyphX, originY + 7, Background);
    }

    private void AdvanceCharacter(TextLayout layout)
    {
        X++;
        if (X < layout.Columns)
            return;

        X = 0;
        LineFeed(layout, preserveColumn: false);
    }

    private void AdvanceTab(TextLayout layout)
    {
        ushort target = (ushort)((X + 4) & ~3);
        if (target < layout.Columns)
        {
            X = target;
            return;
        }

        X = 0;
        LineFeed(layout, preserveColumn: false);
    }

    private void LineFeed(TextLayout layout, bool preserveColumn)
    {
        if (!preserveColumn)
            X = 0;

        if (Y + 1 < layout.Rows)
        {
            Y++;
            return;
        }

        Scroll(layout);
        Y = (ushort)(layout.Rows - 1);
    }

    private void Scroll(TextLayout layout)
    {
        for (var y = 0; y < layout.PixelHeight - CellHeight; y++)
        {
            for (var x = 0; x < layout.PixelWidth; x++)
                WritePixel(layout, x, y, ReadPixel(layout, x, y + CellHeight));
        }

        for (var y = layout.PixelHeight - CellHeight; y < layout.PixelHeight; y++)
        {
            for (var x = 0; x < layout.PixelWidth; x++)
                WritePixel(layout, x, y, Background);
        }
    }

    private void NormalizeCursor(TextLayout layout)
    {
        if (X >= layout.Columns)
        {
            X = (ushort)(layout.Columns - 1);
            Status |= CharacterStatus.CoordinateClamped;
        }

        if (Y < layout.Rows)
            return;

        Y = (ushort)(layout.Rows - 1);
        Status |= CharacterStatus.CoordinateClamped;
    }

    private void ClampCursorToMaximum()
    {
        if (X > 79)
        {
            X = 79;
            Status |= CharacterStatus.CoordinateClamped;
        }

        if (Y <= 47)
            return;

        Y = 47;
        Status |= CharacterStatus.CoordinateClamped;
    }

    private bool TryGetLayout(out TextLayout layout)
    {
        layout = video.WriteMode switch
        {
            VideoMode.Indexed256 => new TextLayout(40, 24, 240, 192, false),
            VideoMode.Indexed4 => new TextLayout(80, 48, 480, 384, true),
            _ => default
        };

        return layout.Columns != 0;
    }

    private byte ReadPixel(TextLayout layout, int x, int y)
    {
        ReadOnlySpan<byte> vram = videoRam.Span;

        if (!layout.Packed2Bpp)
            return vram[y * 256 + x];

        int address = y * (512 / 4) + x / 4;
        int shift = (3 - x % 4) * 2;
        return (byte)((vram[address] >> shift) & 3);
    }

    private void WritePixel(TextLayout layout, int x, int y, ushort color)
    {
        Span<byte> vram = videoRam.Span;

        if (!layout.Packed2Bpp)
        {
            vram[y * 256 + x] = (byte)color;
            return;
        }

        int address = y * (512 / 4) + x / 4;
        int shift = (3 - x % 4) * 2;
        byte mask = (byte)(3 << shift);
        vram[address] = (byte)((vram[address] & ~mask) | (((byte)color & 3) << shift));
    }

    private readonly record struct TextLayout(
        int Columns,
        int Rows,
        int PixelWidth,
        int PixelHeight,
        bool Packed2Bpp);
}

internal static class Font5x7
{
    // Public-domain classic GLCD font. ASCII U+0020 through U+007E; each
    // glyph occupies five columns and bit 0 is the top pixel of a column.
    private static readonly byte[] Data =
    [
        0x00,0x00,0x00,0x00,0x00, 0x00,0x00,0x5F,0x00,0x00, 0x00,0x07,0x00,0x07,0x00, 0x14,0x7F,0x14,0x7F,0x14,
        0x24,0x2A,0x7F,0x2A,0x12, 0x23,0x13,0x08,0x64,0x62, 0x36,0x49,0x55,0x22,0x50, 0x00,0x05,0x03,0x00,0x00,
        0x00,0x1C,0x22,0x41,0x00, 0x00,0x41,0x22,0x1C,0x00, 0x14,0x08,0x3E,0x08,0x14, 0x08,0x08,0x3E,0x08,0x08,
        0x00,0x50,0x30,0x00,0x00, 0x08,0x08,0x08,0x08,0x08, 0x00,0x60,0x60,0x00,0x00, 0x20,0x10,0x08,0x04,0x02,
        0x3E,0x51,0x49,0x45,0x3E, 0x00,0x42,0x7F,0x40,0x00, 0x42,0x61,0x51,0x49,0x46, 0x21,0x41,0x45,0x4B,0x31,
        0x18,0x14,0x12,0x7F,0x10, 0x27,0x45,0x45,0x45,0x39, 0x3C,0x4A,0x49,0x49,0x30, 0x01,0x71,0x09,0x05,0x03,
        0x36,0x49,0x49,0x49,0x36, 0x06,0x49,0x49,0x29,0x1E, 0x00,0x36,0x36,0x00,0x00, 0x00,0x56,0x36,0x00,0x00,
        0x08,0x14,0x22,0x41,0x00, 0x14,0x14,0x14,0x14,0x14, 0x00,0x41,0x22,0x14,0x08, 0x02,0x01,0x51,0x09,0x06,
        0x32,0x49,0x79,0x41,0x3E, 0x7E,0x11,0x11,0x11,0x7E, 0x7F,0x49,0x49,0x49,0x36, 0x3E,0x41,0x41,0x41,0x22,
        0x7F,0x41,0x41,0x22,0x1C, 0x7F,0x49,0x49,0x49,0x41, 0x7F,0x09,0x09,0x09,0x01, 0x3E,0x41,0x49,0x49,0x7A,
        0x7F,0x08,0x08,0x08,0x7F, 0x00,0x41,0x7F,0x41,0x00, 0x20,0x40,0x41,0x3F,0x01, 0x7F,0x08,0x14,0x22,0x41,
        0x7F,0x40,0x40,0x40,0x40, 0x7F,0x02,0x0C,0x02,0x7F, 0x7F,0x04,0x08,0x10,0x7F, 0x3E,0x41,0x41,0x41,0x3E,
        0x7F,0x09,0x09,0x09,0x06, 0x3E,0x41,0x51,0x21,0x5E, 0x7F,0x09,0x19,0x29,0x46, 0x46,0x49,0x49,0x49,0x31,
        0x01,0x01,0x7F,0x01,0x01, 0x3F,0x40,0x40,0x40,0x3F, 0x1F,0x20,0x40,0x20,0x1F, 0x3F,0x40,0x38,0x40,0x3F,
        0x63,0x14,0x08,0x14,0x63, 0x07,0x08,0x70,0x08,0x07, 0x61,0x51,0x49,0x45,0x43, 0x00,0x7F,0x41,0x41,0x00,
        0x02,0x04,0x08,0x10,0x20, 0x00,0x41,0x41,0x7F,0x00, 0x04,0x02,0x01,0x02,0x04, 0x40,0x40,0x40,0x40,0x40,
        0x00,0x01,0x02,0x04,0x00, 0x20,0x54,0x54,0x54,0x78, 0x7F,0x48,0x44,0x44,0x38, 0x38,0x44,0x44,0x44,0x20,
        0x38,0x44,0x44,0x48,0x7F, 0x38,0x54,0x54,0x54,0x18, 0x08,0x7E,0x09,0x01,0x02, 0x0C,0x52,0x52,0x52,0x3E,
        0x7F,0x08,0x04,0x04,0x78, 0x00,0x44,0x7D,0x40,0x00, 0x20,0x40,0x44,0x3D,0x00, 0x7F,0x10,0x28,0x44,0x00,
        0x00,0x41,0x7F,0x40,0x00, 0x7C,0x04,0x18,0x04,0x78, 0x7C,0x08,0x04,0x04,0x78, 0x38,0x44,0x44,0x44,0x38,
        0x7C,0x14,0x14,0x14,0x08, 0x08,0x14,0x14,0x18,0x7C, 0x7C,0x08,0x04,0x04,0x08, 0x48,0x54,0x54,0x54,0x20,
        0x04,0x3F,0x44,0x40,0x20, 0x3C,0x40,0x40,0x20,0x7C, 0x1C,0x20,0x40,0x20,0x1C, 0x3C,0x40,0x30,0x40,0x3C,
        0x44,0x28,0x10,0x28,0x44, 0x0C,0x50,0x50,0x50,0x3C, 0x44,0x64,0x54,0x4C,0x44, 0x00,0x08,0x36,0x41,0x00,
        0x00,0x00,0x7F,0x00,0x00, 0x00,0x41,0x36,0x08,0x00, 0x08,0x04,0x08,0x10,0x08
    ];

    public static ReadOnlySpan<byte> GetGlyph(byte character)
    {
        int index = (character - 0x20) * 5;
        return Data.AsSpan(index, 5);
    }
}
