namespace OldSimulator.VirtualDevices;

public sealed class Memory
{
    public readonly int    InstalledBytes = 1 << 20;
    public readonly byte[] Data           = new byte[1 << 20];

    public byte ReadPhysical(uint addr)
    {
        if (addr >= InstalledBytes)
            throw new ArgumentOutOfRangeException(nameof(addr), "Memory Address Read Out of Range");
        return Data[addr];
    }

    public void WritePhysical(uint addr, byte content)
    {
        if (addr >= InstalledBytes)
            throw new ArgumentOutOfRangeException(nameof(addr), "Memory Address Write Out of Range");
        Data[addr] = content;
    }

    public static uint ToPhysicalAddress(ushort logical, byte sg)
    {
        return logical < 0xC000
            ? logical
            : (uint)((sg << 14) | (logical & 0x3FFF));
    }

    public byte ReadLogical(ushort addr, byte sg)
    {
        return ReadPhysical(ToPhysicalAddress(addr, sg));
    }

    public void WriteLogical(ushort addr, byte sg, byte content)
    {
        WritePhysical(ToPhysicalAddress(addr,sg),content);
    }
}
