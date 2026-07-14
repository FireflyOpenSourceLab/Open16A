namespace OldSimulator.VirtualDevices;

public static class ProgramImageLoader
{
    public static int Load(Machine machine, string path, uint baseAddress)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        byte[] program = File.ReadAllBytes(path);
        return Load(machine, program, baseAddress);
    }

    public static int Load(Machine machine, ReadOnlySpan<byte> program, uint baseAddress)
    {
        ArgumentNullException.ThrowIfNull(machine);
        if (program.Length == 0)
            throw new ArgumentException("Program file is empty.", nameof(program));
        if ((baseAddress & 1) != 0)
            throw new ArgumentException("Program base address must be even.", nameof(baseAddress));
        if ((ulong)baseAddress + (uint)program.Length > Memory.INSTALLED_BYTES)
            throw new ArgumentException("Program does not fit within physical memory.", nameof(program));

        ulong endAddress = (ulong)baseAddress + (uint)program.Length;
        if (machine.Memory.HasSystemRom
            && baseAddress < Memory.SYSTEM_ROM_START + Memory.SYSTEM_ROM_LENGTH
            && endAddress > Memory.SYSTEM_ROM_START)
        {
            throw new ArgumentException("Program range overlaps protected system ROM.", nameof(baseAddress));
        }

        for (var offset = 0; offset < program.Length; offset++)
            machine.Memory.WritePhysical(baseAddress + (uint)offset, program[offset]);

        machine.Reset();
        SetEntryPoint(machine.Cpu, baseAddress);
        return program.Length;
    }

    public static void SetEntryPoint(Cpu cpu, uint physicalAddress)
    {
        ArgumentNullException.ThrowIfNull(cpu);
        if (physicalAddress >= Memory.INSTALLED_BYTES)
            throw new ArgumentOutOfRangeException(nameof(physicalAddress));

        if (physicalAddress < 0xC000)
        {
            cpu.SG = 0;
            cpu.PC = (ushort)physicalAddress;
            return;
        }

        cpu.SG = (byte)(physicalAddress >> 14);
        cpu.PC = (ushort)(0xC000 | (physicalAddress & 0x3FFF));
    }
}
