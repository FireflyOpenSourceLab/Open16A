using OldSimulator.Expansion;

namespace OldSimulator.VirtualDevices;

public sealed class Machine : IDisposable
{
    public const uint VIDEO_RAM_ADDRESS      = 0xF4000;
    public const byte VIDEO_INTERRUPT_VECTOR = 0x10;
    public const byte KEYBOARD_INTERRUPT_VECTOR = 0x11;
    public const byte EXPANSION_INTERRUPT_VECTOR = 0x12;

    private readonly HashSet<uint> breakpoints = [];
    private bool skipCurrentBreakpoint;

    public Machine(
        byte[]? systemRom = null,
        ulong videoFrameCycles = VideoDevice.DEFAULT_FRAME_CYCLES,
        IEnumerable<ExpansionCardInstallation>? expansionCards = null)
    {
        Memory     = systemRom is null ? new Memory() : new Memory(systemRom);
        IoBus      = new IoBus();
        Interrupts = new InterruptController();
        Cpu        = new Cpu(Memory, IoBus);
        Video = new VideoDevice(
            Interrupts,
            VIDEO_INTERRUPT_VECTOR,
            Memory.CreatePhysicalView(VIDEO_RAM_ADDRESS, VideoDevice.VIDEO_RAM_LENGTH),
            videoFrameCycles);
        Character = new CharacterDevice(
            Memory.CreatePhysicalView(VIDEO_RAM_ADDRESS, VideoDevice.VIDEO_RAM_LENGTH),
            Video);
        Keyboard = new KeyboardDevice(
            Interrupts,
            KEYBOARD_INTERRUPT_VECTOR,
            Memory.CreatePhysicalView(KeyboardDevice.STATE_ADDRESS, KeyboardDevice.DEVICE_MEMORY_LENGTH));
        Expansion = new ExpansionBus(
            Memory,
            IoBus,
            Interrupts,
            EXPANSION_INTERRUPT_VECTOR,
            expansionCards);

        Video.Attach(IoBus, VideoDevice.PortPresent, VideoDevice.PortStatus);
        Character.Attach(IoBus);
    }

    public Memory              Memory     { get; }
    public Cpu                 Cpu        { get; }
    public IoBus               IoBus      { get; }
    public InterruptController Interrupts { get; }
    public VideoDevice         Video      { get; }
    public CharacterDevice     Character  { get; }
    public KeyboardDevice      Keyboard   { get; }
    public ExpansionBus        Expansion  { get; }

    public bool Paused { get; private set; }

    public IReadOnlyCollection<uint> Breakpoints => breakpoints;

    public uint CurrentPhysicalProgramCounter => Memory.ToPhysicalAddress(Cpu.PC, Cpu.SG);

    public void AdvanceCycles(ulong budget)
    {
        if (Paused)
            return;

        ulong remaining = budget;

        while (remaining != 0 && !Cpu.Halted)
        {
            if (shouldPauseAtBreakpoint())
            {
                Paused = true;
                break;
            }

            ulong cost = Cpu.PeekNextInstructionCost();
            if (cost > remaining)
                break;

            cost = Cpu.ExecuteNextInstruction();
            advanceDevices(cost);
            remaining -= cost;
            acknowledgeInterrupt();
        }

        if (remaining != 0 && !Paused)
        {
            advanceDevices(remaining);
            acknowledgeInterrupt();
        }
    }

    public void Pause() => Paused = true;

    public void Resume()
    {
        skipCurrentBreakpoint = breakpoints.Contains(CurrentPhysicalProgramCounter);
        Paused = false;
    }

    public bool AddBreakpoint(uint physicalAddress)
    {
        if (physicalAddress >= Memory.INSTALLED_BYTES)
            throw new ArgumentOutOfRangeException(nameof(physicalAddress));

        return breakpoints.Add(physicalAddress);
    }

    public bool RemoveBreakpoint(uint physicalAddress) => breakpoints.Remove(physicalAddress);

    public void ClearBreakpoints() => breakpoints.Clear();

    public void Reset()
    {
        Cpu.Reset();
        Keyboard.Clear();
        Expansion.Reset();
        Paused = false;
        skipCurrentBreakpoint = false;
    }

    public void Dispose()
    {
        Expansion.Dispose();
    }

    public ulong StepInstruction()
    {
        Paused = true;

        if (Cpu.Halted)
            return 0;

        ulong cycles = Cpu.ExecuteNextInstruction();
        advanceDevices(cycles);
        acknowledgeInterrupt();
        return cycles;
    }

    private void advanceDevices(ulong cycles)
    {
        Video.AdvanceCycles(cycles);
        Expansion.AdvanceCycles(cycles);
    }

    private void acknowledgeInterrupt()
    {
        if (Interrupts.TryGetPending(Cpu.InterruptsEnabled, out byte vector) && Cpu.TryEnterInterrupt(vector))
            Interrupts.Clear(vector);
    }

    private bool shouldPauseAtBreakpoint()
    {
        uint address = CurrentPhysicalProgramCounter;
        if (skipCurrentBreakpoint && breakpoints.Contains(address))
        {
            skipCurrentBreakpoint = false;
            return false;
        }

        skipCurrentBreakpoint = false;
        return breakpoints.Contains(address);
    }
}
