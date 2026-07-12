namespace OldSimulator.VirtualDevices;

public sealed class Machine
{
    public const uint VIDEO_RAM_ADDRESS      = 0xF4000;
    public const byte VIDEO_INTERRUPT_VECTOR = 0x10;

    public Machine(byte[]? systemRom = null, ulong videoFrameCycles = VideoDevice.DEFAULT_FRAME_CYCLES)
    {
        Memory     = systemRom is null ? new Memory() : new Memory(systemRom);
        IoBus      = new IoBus();
        Interrupts = new InterruptController();
        Cpu        = new Cpu(Memory, IoBus);
        Video = new VideoDevice(
            Interrupts,
            VIDEO_INTERRUPT_VECTOR,
            Memory.GetPhysicalView(VIDEO_RAM_ADDRESS, VideoDevice.VIDEO_RAM_LENGTH),
            videoFrameCycles);
        Character = new CharacterDevice(
            Memory.GetPhysicalView(VIDEO_RAM_ADDRESS, VideoDevice.VIDEO_RAM_LENGTH),
            Video);

        Video.Attach(IoBus, presentPort: 0x20, statusPort: 0x21);
        Character.Attach(IoBus);
    }

    public Memory              Memory     { get; }
    public Cpu                 Cpu        { get; }
    public IoBus               IoBus      { get; }
    public InterruptController Interrupts { get; }
    public VideoDevice         Video      { get; }
    public CharacterDevice     Character  { get; }

    public void AdvanceCycles(ulong budget)
    {
        ulong remaining = budget;

        while (remaining != 0 && !Cpu.Halted)
        {
            ulong cost = Cpu.PeekNextInstructionCost();
            if (cost > remaining)
                break;

            cost = Cpu.ExecuteNextInstruction();
            advanceDevices(cost);
            remaining -= cost;
            acknowledgeInterrupt();
        }

        if (remaining != 0)
        {
            advanceDevices(remaining);
            acknowledgeInterrupt();
        }
    }

    private void advanceDevices(ulong cycles)
    {
        Video.AdvanceCycles(cycles);
    }

    private void acknowledgeInterrupt()
    {
        if (Interrupts.TryAcknowledge(Cpu.InterruptsEnabled, out byte vector))
            Cpu.TryEnterInterrupt(vector);
    }
}
