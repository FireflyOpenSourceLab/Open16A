namespace OldSimulator.VirtualDevices;

public interface IClockedDevice
{
    void AdvanceCycles(ulong cycles);
}
