using System.Numerics;
using Raylib_cs;

namespace OldSimulator.HostDevices;
using VirtualDevices;

public sealed class RayLibScreen : IDisposable
{
    private Texture2D texture;
    private Color[]   pixels          = [];
    private VideoMode currentMode    = VideoMode.Indexed256;
    private ulong     displayedSerial = ulong.MaxValue;
    private bool      disposed;

    private void EnsureSurface(VideoMode requestedMode)
    {
        (int width, int height) = requestedMode switch
        {
            VideoMode.Indexed256 => (256, 192),
            VideoMode.Indexed4   => (512, 384),
            VideoMode.Rgba8888   => (128, 96),
            _                    => throw new ArgumentOutOfRangeException(nameof(requestedMode))
        };

        if (texture.Width == width && texture.Height == height)
            return;

        if (texture.Id != 0)
            Raylib.UnloadTexture(texture);

        Image image = Raylib.GenImageColor(width, height, Color.Black);

        try
        {
            texture = Raylib.LoadTextureFromImage(image);
            Raylib.SetTextureFilter(texture, TextureFilter.Point);
        }
        finally
        {
            Raylib.UnloadImage(image);
        }

        pixels = new Color[width * height];
        currentMode   = requestedMode;
    }

    public void Draw()
    {
        if (texture.Id == 0)
            return;

        int width  = Raylib.GetScreenWidth();
        int height = Raylib.GetScreenHeight();

        int scale = Math.Max(
            1,
            Math.Min(
                width / texture.Width,
                height / texture.Height
            )
        );

        float drawWidth  = texture.Width * scale;
        float drawHeight = texture.Height * scale;
        float drawX      = (width - drawWidth) / 2f;
        float drawY      = (height - drawHeight) / 2f;

        Raylib.DrawTexturePro(
            texture,
            source: new Rectangle(0,   0,     texture.Width, texture.Height),
            dest: new Rectangle(drawX, drawY, drawWidth,     drawHeight),
            Vector2.Zero,
            rotation: 0,
            tint: Color.White
        );
    }



    private void decode(VideoFrame frame)
    {
        ReadOnlySpan<byte>  vram    = frame.Vram.Span;
        ReadOnlySpan<Rgb24> palette = frame.Palette.Span;

        static Color toColor(Rgb24 color) =>
            new Color(color.Red, color.Green, color.Blue, (byte)255);

        switch (frame.Mode)
        {
            case VideoMode.Indexed256:
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = toColor(palette[vram[i]]);
                break;

            case VideoMode.Indexed4:
                const int width    = 512;
                const int rowBytes = width / 4; // 128

                for (int y = 0; y < 384; y++)
                {
                    for (int sourceX = 0; sourceX < rowBytes; sourceX++)
                    {
                        byte packed     = vram[y * rowBytes + sourceX];
                        int  pixelStart = y * width + sourceX * 4;

                        pixels[pixelStart + 0] = toColor(palette[(packed >> 6) & 3]);
                        pixels[pixelStart + 1] = toColor(palette[(packed >> 4) & 3]);
                        pixels[pixelStart + 2] = toColor(palette[(packed >> 2) & 3]);
                        pixels[pixelStart + 3] = toColor(palette[packed & 3]);
                    }
                }
                break;

            case VideoMode.Rgba8888:
                for (int pixel = 0; pixel < pixels.Length; pixel++)
                {
                    int source = pixel * 4;
                    pixels[pixel] = new Color(
                        vram[source],
                        vram[source + 1],
                        vram[source + 2],
                        vram[source + 3]);
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(frame.Mode));
        }
    }

    public void Sync(VideoFrame frame)
    {
        if (frame.Serial == displayedSerial)
            return;
        EnsureSurface(frame.Mode);
        decode(frame);
        Raylib.UpdateTexture(texture,pixels);
        displayedSerial = frame.Serial;
    }
    public void Dispose()
    {
        if (disposed)
            return;
        Raylib.UnloadTexture(texture);
        disposed = true;
    }
}
