namespace OldSimulator.VirtualDevices;

public sealed class IoBus
{
    private readonly Dictionary<ushort, Func<ushort>>   reads  = [];
    private readonly Dictionary<ushort, Action<ushort>> writes = [];

    public void RegisterRead(ushort port, Func<ushort> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (!reads.TryAdd(port, handler))
            throw new InvalidOperationException($"I/O port 0x{port:X4} already has a read handler.");
    }

    public void RegisterWrite(ushort port, Action<ushort> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (!writes.TryAdd(port, handler))
            throw new InvalidOperationException($"I/O port 0x{port:X4} already has a write handler.");
    }

    public ushort Read(ushort port)
    {
        return reads.TryGetValue(port, out Func<ushort>? handler) ? handler() : (ushort)0;
    }

    public void Write(ushort port, ushort value)
    {
        if (writes.TryGetValue(port, out Action<ushort>? handler))
            handler(value);
    }

    public bool IsReadMapped(ushort port) => reads.ContainsKey(port);

    public bool IsWriteMapped(ushort port) => writes.ContainsKey(port);
}
