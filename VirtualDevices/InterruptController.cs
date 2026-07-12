namespace OldSimulator.VirtualDevices;

public sealed class InterruptController
{
    private readonly bool[] pending = new bool[256];
    private          int    pendingCount;

    public bool HasPending => pendingCount != 0;

    public void Raise(byte vector)
    {
        if (pending[vector])
            return;

        pending[vector] = true;
        pendingCount++;
    }

    public bool IsPending(byte vector) => pending[vector];

    public void Clear(byte vector)
    {
        if (!pending[vector])
            return;

        pending[vector] = false;
        pendingCount--;
    }

    public bool TryAcknowledge(bool interruptsEnabled, out byte vector)
    {
        if (!interruptsEnabled || pendingCount == 0)
        {
            vector = 0;
            return false;
        }

        for (var index = 0; index < pending.Length; index++)
        {
            if (!pending[index])
                continue;

            vector         = (byte)index;
            pending[index] = false;
            pendingCount--;
            return true;
        }

        throw new InvalidOperationException("Interrupt pending count is inconsistent with its vector table.");
    }
}
