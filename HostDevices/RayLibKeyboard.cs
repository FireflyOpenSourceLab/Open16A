using OldSimulator.VirtualDevices;
using Raylib_cs;

namespace OldSimulator.HostDevices;

/// <summary>
/// Host input adapter for the Open16A compact 63-key matrix.
/// F12 is deliberately omitted because it belongs to the host debugger.
/// </summary>
public sealed class RayLibKeyboard
{
    private static readonly KeyBinding[] bindings =
    [
        new(KeyboardKey.Grave,        0x00), new(KeyboardKey.One,          0x01),
        new(KeyboardKey.Two,          0x02), new(KeyboardKey.Three,        0x03),
        new(KeyboardKey.Four,         0x04), new(KeyboardKey.Five,         0x05),
        new(KeyboardKey.Six,          0x06), new(KeyboardKey.Seven,        0x07),
        new(KeyboardKey.Eight,        0x08), new(KeyboardKey.Nine,         0x09),
        new(KeyboardKey.Zero,         0x0A), new(KeyboardKey.Minus,        0x0B),
        new(KeyboardKey.Equal,        0x0C), new(KeyboardKey.Backspace,    0x0D),
        new(KeyboardKey.Escape,       0x0E), new(KeyboardKey.Up,           0x0F),

        new(KeyboardKey.Tab,          0x10), new(KeyboardKey.Q,            0x11),
        new(KeyboardKey.W,            0x12), new(KeyboardKey.E,            0x13),
        new(KeyboardKey.R,            0x14), new(KeyboardKey.T,            0x15),
        new(KeyboardKey.Y,            0x16), new(KeyboardKey.U,            0x17),
        new(KeyboardKey.I,            0x18), new(KeyboardKey.O,            0x19),
        new(KeyboardKey.P,            0x1A), new(KeyboardKey.LeftBracket,  0x1B),
        new(KeyboardKey.RightBracket, 0x1C), new(KeyboardKey.Backslash,     0x1D),
        new(KeyboardKey.Delete,       0x1E), new(KeyboardKey.End,          0x1F),

        new(KeyboardKey.CapsLock,     0x20), new(KeyboardKey.A,            0x21),
        new(KeyboardKey.S,            0x22), new(KeyboardKey.D,            0x23),
        new(KeyboardKey.F,            0x24), new(KeyboardKey.G,            0x25),
        new(KeyboardKey.H,            0x26), new(KeyboardKey.J,            0x27),
        new(KeyboardKey.K,            0x28), new(KeyboardKey.L,            0x29),
        new(KeyboardKey.Semicolon,    0x2A), new(KeyboardKey.Apostrophe,   0x2B),
        new(KeyboardKey.Enter,        0x2C), new(KeyboardKey.LeftShift,    0x2D),
        new(KeyboardKey.Z,            0x2E), new(KeyboardKey.X,            0x2F),

        new(KeyboardKey.C,            0x30), new(KeyboardKey.V,            0x31),
        new(KeyboardKey.B,            0x32), new(KeyboardKey.N,            0x33),
        new(KeyboardKey.M,            0x34), new(KeyboardKey.Comma,        0x35),
        new(KeyboardKey.Period,       0x36), new(KeyboardKey.Slash,        0x37),
        new(KeyboardKey.RightShift,   0x38), new(KeyboardKey.LeftControl,  0x39),
        new(KeyboardKey.LeftAlt,      0x3A), new(KeyboardKey.Space,        0x3B),
        new(KeyboardKey.Left,         0x3C), new(KeyboardKey.Down,         0x3D),
        new(KeyboardKey.Right,        0x3E)
    ];

    private readonly KeyboardDevice keyboard;

    public RayLibKeyboard(KeyboardDevice keyboard)
    {
        this.keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
    }

    public void Update()
    {
        foreach (KeyBinding binding in bindings)
        {
            if (Raylib.IsKeyPressed(binding.HostKey))
                keyboard.SetKeyState(binding.ScanCode, true);
            if (Raylib.IsKeyReleased(binding.HostKey))
                keyboard.SetKeyState(binding.ScanCode, false);
        }
    }

    private readonly record struct KeyBinding(KeyboardKey HostKey, byte ScanCode);
}
