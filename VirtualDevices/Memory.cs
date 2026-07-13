namespace OldSimulator.VirtualDevices;

public sealed class Memory
{
    public const int    INSTALLED_BYTES   = 1 << 20;
    public const ushort SYSTEM_ROM_START  = 0x0300;
    public const int    SYSTEM_ROM_LENGTH = 0x1000;

    private readonly byte[] data;
    private readonly bool   flatLogicalAddressing;

    private bool systemRomLoaded;

    public Memory() : this(INSTALLED_BYTES)
    {
    }

    public Memory(int installedBytes, bool flatLogicalAddressing = false)
    {
        if (installedBytes is < 1 or > INSTALLED_BYTES)
            throw new ArgumentOutOfRangeException(nameof(installedBytes));
        if (flatLogicalAddressing && installedBytes != 1 << 16)
        {
            throw new ArgumentException(
                "Flat logical addressing requires exactly 64 KiB of installed memory.",
                nameof(installedBytes));
        }

        data                       = new byte[installedBytes];
        InstalledBytes             = installedBytes;
        this.flatLogicalAddressing = flatLogicalAddressing;
    }

    public Memory(byte[] systemRom) : this(INSTALLED_BYTES)
    {
        ArgumentNullException.ThrowIfNull(systemRom);
        LoadSystemRom(systemRom);
    }

    public bool HasSystemRom => systemRomLoaded;

    public int InstalledBytes { get; }

    public ReadOnlyMemory<byte> Data => data;

    public void LoadSystemRom(ReadOnlySpan<byte> systemRom)
    {
        if (systemRom.Length != SYSTEM_ROM_LENGTH)
        {
            throw new ArgumentException(
                $"System ROM must be exactly {SYSTEM_ROM_LENGTH} bytes.",
                nameof(systemRom));
        }

        systemRom.CopyTo(data.AsSpan(SYSTEM_ROM_START, SYSTEM_ROM_LENGTH));
        systemRomLoaded = true;
    }

    public byte ReadPhysical(uint addr)
    {
        if (addr >= InstalledBytes)
            throw new ArgumentOutOfRangeException(nameof(addr), "Memory Address Read Out of Range");
        return data[addr];
    }

    public void WritePhysical(uint addr, byte content)
    {
        if (addr >= InstalledBytes)
            throw new ArgumentOutOfRangeException(nameof(addr), "Memory Address Write Out of Range");

        if (isSystemRomAddress(addr))
            return;

        data[addr] = content;
    }

    public ushort ReadPhysicalWord(uint addr)
    {
        ensurePhysicalRange(addr, sizeof(ushort));
        return (ushort)((ReadPhysical(addr) << 8) | ReadPhysical(addr + 1));
    }

    public void WritePhysicalWord(uint addr, ushort content)
    {
        ensurePhysicalRange(addr, sizeof(ushort));
        WritePhysical(addr,     (byte)(content >> 8));
        WritePhysical(addr + 1, (byte)content);
    }

    public ReadOnlyMemory<byte> GetPhysicalReadOnlyView(uint addr, int length)
    {
        ensurePhysicalRange(addr, length);
        return data.AsMemory((int)addr, length);
    }

    public PhysicalMemoryView CreatePhysicalView(uint addr, int length)
    {
        ensurePhysicalRange(addr, length);
        return new PhysicalMemoryView(this, addr, length);
    }

    public static uint ToPhysicalAddress(ushort logical, byte sg)
    {
        return logical < 0xC000
            ? logical
            : (uint)((sg << 14) | (logical & 0x3FFF));
    }

    public byte ReadLogical(ushort addr, byte sg)
    {
        return ReadPhysical(toPhysicalAddress(addr, sg));
    }

    public void WriteLogical(ushort addr, byte sg, byte content)
    {
        WritePhysical(toPhysicalAddress(addr, sg), content);
    }

    public ushort ReadLogicalWord(ushort addr, byte sg)
    {
        ushort next = unchecked((ushort)(addr + 1));
        return (ushort)((ReadLogical(addr, sg) << 8) | ReadLogical(next, sg));
    }

    public void WriteLogicalWord(ushort addr, byte sg, ushort content)
    {
        ushort next = unchecked((ushort)(addr + 1));
        WriteLogical(addr, sg, (byte)(content >> 8));
        WriteLogical(next, sg, (byte)content);
    }

    private bool isSystemRomAddress(uint addr)
    {
        return systemRomLoaded && addr is >= SYSTEM_ROM_START and < SYSTEM_ROM_START + SYSTEM_ROM_LENGTH;
    }

    private uint toPhysicalAddress(ushort logical, byte sg)
    {
        return flatLogicalAddressing ? logical : ToPhysicalAddress(logical, sg);
    }

    private void ensurePhysicalRange(uint addr, int length)
    {
        if (length < 0 || addr > (uint)InstalledBytes || (ulong)addr + (uint)length > (uint)InstalledBytes)
            throw new ArgumentOutOfRangeException(nameof(addr), "Physical memory range is out of bounds.");
    }

}

/// <summary>
/// A bounded physical-memory window for DMA and debugger code. Its operations
/// use <see cref="Memory"/> accessors, so writes to system ROM are ignored.
/// </summary>
public sealed class PhysicalMemoryView
{
    private readonly Memory memory;
    private readonly uint   start;

    internal PhysicalMemoryView(Memory memory, uint start, int length)
    {
        this.memory = memory;
        this.start  = start;
        Length      = length;
    }

    public int Length { get; }

    public byte Read(int offset)
    {
        return memory.ReadPhysical(addressAt(offset));
    }

    public void Write(int offset, byte value)
    {
        memory.WritePhysical(addressAt(offset), value);
    }

    public ushort ReadWord(int offset)
    {
        ensureOffset(offset, sizeof(ushort));
        return memory.ReadPhysicalWord(start + (uint)offset);
    }

    public void WriteWord(int offset, ushort value)
    {
        ensureOffset(offset, sizeof(ushort));
        memory.WritePhysicalWord(start + (uint)offset, value);
    }

    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length != Length)
            throw new ArgumentException("Destination length must match the physical view length.", nameof(destination));

        for (var offset = 0; offset < Length; offset++)
            destination[offset] = memory.ReadPhysical(start + (uint)offset);
    }

    public void CopyFrom(ReadOnlySpan<byte> source)
    {
        if (source.Length != Length)
            throw new ArgumentException("Source length must match the physical view length.", nameof(source));

        for (var offset = 0; offset < Length; offset++)
            memory.WritePhysical(start + (uint)offset, source[offset]);
    }

    private uint addressAt(int offset)
    {
        ensureOffset(offset, 1);
        return start + (uint)offset;
    }

    private void ensureOffset(int offset, int count)
    {
        if (offset < 0 || count > Length - offset)
            throw new ArgumentOutOfRangeException(nameof(offset), "Physical view offset is out of bounds.");
    }
}
