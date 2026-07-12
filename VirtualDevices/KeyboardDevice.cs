namespace OldSimulator.VirtualDevices;

/// <summary>
/// Maintains the four-key keyboard snapshot exposed at 0210h-0212h.
/// Each slot is a six-bit row/column scan code. 3Fh denotes an unused slot.
/// </summary>
public sealed class KeyboardDevice
{
    public const uint STATE_ADDRESS   = 0x0210;
    public const int  STATE_LENGTH    = 3;
    public const byte EMPTY_SCAN_CODE = 0x3F;

    private readonly InterruptController interrupts;
    private readonly byte                interruptVector;
    private readonly PhysicalMemoryView  state;
    private readonly byte[]              pressed = [EMPTY_SCAN_CODE, EMPTY_SCAN_CODE, EMPTY_SCAN_CODE, EMPTY_SCAN_CODE];

    public KeyboardDevice(InterruptController interrupts, byte interruptVector, PhysicalMemoryView state)
    {
        this.interrupts      = interrupts ?? throw new ArgumentNullException(nameof(interrupts));
        this.interruptVector = interruptVector;
        this.state           = state ?? throw new ArgumentNullException(nameof(state));

        if (state.Length != STATE_LENGTH)
            throw new ArgumentException($"Keyboard state must be exactly {STATE_LENGTH} bytes.", nameof(state));

        WriteState();
    }

    /// <summary>
    /// Applies one host key transition. The fifth simultaneously held key is ignored.
    /// </summary>
    public bool SetKeyState(byte scanCode, bool isDown)
    {
        if (scanCode >= EMPTY_SCAN_CODE)
            throw new ArgumentOutOfRangeException(nameof(scanCode), "Scan code must be within 00-3E.");

        int index = Array.IndexOf(pressed, scanCode);
        if (isDown)
        {
            if (index >= 0)
                return false;

            index = Array.IndexOf(pressed, EMPTY_SCAN_CODE);
            if (index < 0)
                return false;

            pressed[index] = scanCode;
        }
        else
        {
            if (index < 0)
                return false;

            Array.Copy(pressed, index + 1, pressed, index, pressed.Length - index - 1);
            pressed[^1] = EMPTY_SCAN_CODE;
        }

        WriteState();
        interrupts.Raise(interruptVector);
        return true;
    }

    /// <summary>
    /// Clears host input without synthesizing a virtual key-release interrupt.
    /// </summary>
    public void Clear()
    {
        if (pressed.All(scanCode => scanCode == EMPTY_SCAN_CODE))
            return;

        Array.Fill(pressed, EMPTY_SCAN_CODE);
        WriteState();
    }

    private void WriteState()
    {
        state.Write(0, (byte)((pressed[0] << 2) | (pressed[1] >> 4)));
        state.Write(1, (byte)((pressed[1] << 4) | (pressed[2] >> 2)));
        state.Write(2, (byte)((pressed[2] << 6) | pressed[3]));
    }
}
