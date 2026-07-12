namespace OldSimulator.VirtualDevices;

/// <summary>
/// Maintains the four-key keyboard snapshot plus a transition FIFO in low memory.
/// Each snapshot slot is a six-bit row/column scan code. 3Fh denotes an unused slot.
/// </summary>
public sealed class KeyboardDevice
{
    public const uint STATE_ADDRESS        = 0x0210;
    public const int  STATE_LENGTH         = 3;
    public const uint EVENT_HEAD_ADDRESS   = 0x0213;
    public const uint EVENT_TAIL_ADDRESS   = 0x0214;
    public const uint EVENT_FLAGS_ADDRESS  = 0x0215;
    public const uint EVENT_BUFFER_ADDRESS = 0x0216;
    public const int  EVENT_BUFFER_LENGTH  = 32;
    public const int  DEVICE_MEMORY_LENGTH = STATE_LENGTH + 3 + EVENT_BUFFER_LENGTH;
    public const byte EVENT_OVERFLOW        = 1 << 0;
    public const byte EMPTY_SCAN_CODE       = 0x3F;

    public const byte EventDown  = 1 << 6;
    public const byte EventShift = 1 << 7;

    private readonly InterruptController interrupts;
    private readonly byte                interruptVector;
    private readonly PhysicalMemoryView  state;
    private readonly byte[]              pressed = [EMPTY_SCAN_CODE, EMPTY_SCAN_CODE, EMPTY_SCAN_CODE, EMPTY_SCAN_CODE];

    public KeyboardDevice(InterruptController interrupts, byte interruptVector, PhysicalMemoryView state)
    {
        this.interrupts      = interrupts ?? throw new ArgumentNullException(nameof(interrupts));
        this.interruptVector = interruptVector;
        this.state           = state ?? throw new ArgumentNullException(nameof(state));

        if (state.Length != DEVICE_MEMORY_LENGTH)
            throw new ArgumentException($"Keyboard memory must be exactly {DEVICE_MEMORY_LENGTH} bytes.", nameof(state));

        WriteState();
        ClearEvents();
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
        Enqueue((byte)(scanCode | (isDown ? EventDown : 0) | (IsShiftDown() ? EventShift : 0)));
        interrupts.Raise(interruptVector);
        return true;
    }

    /// <summary>
    /// Clears host input without synthesizing a virtual key-release interrupt.
    /// </summary>
    public void Clear()
    {
        if (pressed.Any(scanCode => scanCode != EMPTY_SCAN_CODE))
        {
            Array.Fill(pressed, EMPTY_SCAN_CODE);
            WriteState();
        }
        ClearEvents();
    }

    private bool IsShiftDown() => Array.IndexOf(pressed, (byte)0x2D) >= 0 || Array.IndexOf(pressed, (byte)0x38) >= 0;

    private void Enqueue(byte value)
    {
        byte head = (byte)(state.Read((int)(EVENT_HEAD_ADDRESS - STATE_ADDRESS)) & (EVENT_BUFFER_LENGTH - 1));
        byte tail = (byte)(state.Read((int)(EVENT_TAIL_ADDRESS - STATE_ADDRESS)) & (EVENT_BUFFER_LENGTH - 1));
        byte next = (byte)((head + 1) & (EVENT_BUFFER_LENGTH - 1));
        if (next == tail)
        {
            int flags = (int)(EVENT_FLAGS_ADDRESS - STATE_ADDRESS);
            state.Write(flags, (byte)(state.Read(flags) | EVENT_OVERFLOW));
            return;
        }

        state.Write((int)(EVENT_BUFFER_ADDRESS - STATE_ADDRESS) + head, value);
        state.Write((int)(EVENT_HEAD_ADDRESS - STATE_ADDRESS), next);
    }

    private void ClearEvents()
    {
        state.Write((int)(EVENT_HEAD_ADDRESS - STATE_ADDRESS), 0);
        state.Write((int)(EVENT_TAIL_ADDRESS - STATE_ADDRESS), 0);
        state.Write((int)(EVENT_FLAGS_ADDRESS - STATE_ADDRESS), 0);
        for (var index = 0; index < EVENT_BUFFER_LENGTH; index++)
            state.Write((int)(EVENT_BUFFER_ADDRESS - STATE_ADDRESS) + index, 0);
    }

    private void WriteState()
    {
        state.Write(0, (byte)((pressed[0] << 2) | (pressed[1] >> 4)));
        state.Write(1, (byte)((pressed[1] << 4) | (pressed[2] >> 2)));
        state.Write(2, (byte)((pressed[2] << 6) | pressed[3]));
    }
}
