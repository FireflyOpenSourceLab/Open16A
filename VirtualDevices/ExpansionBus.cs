using OldSimulator.Expansion;

namespace OldSimulator.VirtualDevices;

[Flags]
public enum ExpansionCardStatus : ushort
{
    None        = 0,
    Present     = 1 << 0,
    Busy        = 1 << 1,
    Done        = 1 << 2,
    Rejected    = 1 << 3,
    PluginFault = 1 << 4
}

public readonly record struct ExpansionSlotState(
    int Slot,
    ExpansionCardDescriptor? Descriptor,
    ExpansionCardStatus Status,
    string? LastError);

public sealed class ExpansionBus : IClockedDevice, IDisposable
{
    public const int SlotCount = 8;
    public const uint MailboxBaseAddress = 0xF0000;
    public const ushort CommandPortBase = 0x40;
    public const ushort PendingPort = 0x48;

    private readonly InterruptController interrupts;
    private readonly byte interruptVector;
    private readonly Slot[] slots;

    private byte pendingMask;
    private bool disposed;

    public ExpansionBus(
        Memory memory,
        IoBus ioBus,
        InterruptController interrupts,
        byte interruptVector,
        IEnumerable<ExpansionCardInstallation>? installations = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(ioBus);
        ArgumentNullException.ThrowIfNull(interrupts);

        ExpansionCardInstallation?[] installedBySlot = validateInstallations(installations);
        this.interrupts = interrupts;
        this.interruptVector = interruptVector;
        slots = new Slot[SlotCount];

        for (var index = 0; index < SlotCount; index++)
        {
            slots[index] = new Slot(
                index,
                memory.CreatePhysicalView(MailboxAddress(index), ExpansionCardApi.MailboxSize),
                installedBySlot[index]);

            int capturedIndex = index;
            ushort port = (ushort)(CommandPortBase + index);
            ioBus.RegisterRead(port, () => (ushort)slots[capturedIndex].Status);
            ioBus.RegisterWrite(port, command => submit(capturedIndex, command));
        }

        ioBus.RegisterRead(PendingPort, () => pendingMask);
        ioBus.RegisterWrite(PendingPort, acknowledge);
    }

    public byte PendingMask => pendingMask;

    public IReadOnlyList<ExpansionSlotState> Slots
    {
        get
        {
            var result = new ExpansionSlotState[SlotCount];
            for (var index = 0; index < result.Length; index++)
                result[index] = stateOf(slots[index]);
            return result;
        }
    }

    public static uint MailboxAddress(int slot)
    {
        ensureSlot(slot);
        return MailboxBaseAddress + (uint)(slot * ExpansionCardApi.MailboxSize);
    }

    public ExpansionSlotState GetSlotState(int slot)
    {
        ensureSlot(slot);
        return stateOf(slots[slot]);
    }

    public void AdvanceCycles(ulong cycles)
    {
        if (disposed)
            return;

        for (var index = 0; index < slots.Length; index++)
        {
            Slot slot = slots[index];
            if ((slot.Status & ExpansionCardStatus.Busy) == 0)
                continue;

            ulong generation = slot.Generation;
            try
            {
                slot.Installation!.Card.AdvanceCycles(cycles);
            }
            catch (Exception exception)
            {
                fault(slot, generation, exception);
            }
        }
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        pendingMask = 0;
        interrupts.Clear(interruptVector);

        foreach (Slot slot in slots)
        {
            slot.Generation++;
            slot.ActiveMailbox = null;
            slot.LastError = null;
            slot.Status = slot.Installation is null
                ? ExpansionCardStatus.None
                : ExpansionCardStatus.Present;

            if (slot.Installation is null)
                continue;

            try
            {
                slot.Installation.Card.Reset();
            }
            catch (Exception exception)
            {
                slot.Status |= ExpansionCardStatus.PluginFault;
                slot.LastError = describe(exception);
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        pendingMask = 0;
        interrupts.Clear(interruptVector);

        foreach (Slot slot in slots)
        {
            slot.Generation++;
            slot.ActiveMailbox = null;

            if (slot.Installation is null)
                continue;

            try
            {
                slot.Installation.Card.Dispose();
            }
            catch (Exception exception)
            {
                slot.LastError = describe(exception);
            }
        }
    }

    private void submit(int slotIndex, ushort command)
    {
        if (disposed)
            return;

        Slot slot = slots[slotIndex];
        if (slot.Installation is null)
            return;

        const ExpansionCardStatus unavailable =
            ExpansionCardStatus.Busy | ExpansionCardStatus.Done | ExpansionCardStatus.PluginFault;
        if ((slot.Status & unavailable) != 0)
        {
            slot.Status |= ExpansionCardStatus.Rejected;
            return;
        }

        var mailbox = new byte[ExpansionCardApi.MailboxSize];
        slot.Mailbox.CopyTo(mailbox);
        slot.Generation++;
        ulong generation = slot.Generation;
        slot.ActiveMailbox = mailbox;
        slot.Status &= ~ExpansionCardStatus.Rejected;
        slot.Status |= ExpansionCardStatus.Busy;
        var completion = new CommandCompletion(this, slotIndex, generation, mailbox);

        try
        {
            slot.Installation.Card.BeginCommand(command, mailbox, completion);
        }
        catch (Exception exception)
        {
            fault(slot, generation, exception);
        }
    }

    private void complete(int slotIndex, ulong generation, byte[] mailbox)
    {
        if (disposed)
            return;

        Slot slot = slots[slotIndex];
        if (slot.Generation != generation ||
            (slot.Status & ExpansionCardStatus.Busy) == 0 ||
            !ReferenceEquals(slot.ActiveMailbox, mailbox))
        {
            return;
        }

        slot.Mailbox.CopyFrom(mailbox);
        slot.ActiveMailbox = null;
        slot.Status &= ~ExpansionCardStatus.Busy;
        slot.Status |= ExpansionCardStatus.Done;
        pendingMask |= (byte)(1 << slotIndex);
        interrupts.Raise(interruptVector);
    }

    private void fault(Slot slot, ulong generation, Exception exception)
    {
        slot.LastError = describe(exception);
        slot.Status |= ExpansionCardStatus.PluginFault;

        if (slot.Generation != generation)
            return;

        if ((slot.Status & ExpansionCardStatus.Busy) != 0)
        {
            slot.ActiveMailbox = null;
            slot.Status &= ~ExpansionCardStatus.Busy;
            slot.Status |= ExpansionCardStatus.Done;
        }

        if ((slot.Status & ExpansionCardStatus.Done) == 0)
            return;

        pendingMask |= (byte)(1 << slot.Index);
        interrupts.Raise(interruptVector);
    }

    private void acknowledge(ushort rawMask)
    {
        if (disposed)
            return;

        byte mask = (byte)rawMask;
        pendingMask &= (byte)~mask;
        for (var index = 0; index < slots.Length; index++)
        {
            if ((mask & (1 << index)) != 0)
                slots[index].Status &= ~(ExpansionCardStatus.Done | ExpansionCardStatus.Rejected);
        }

        interrupts.Clear(interruptVector);
        if (pendingMask != 0)
            interrupts.Raise(interruptVector);
    }

    private static ExpansionSlotState stateOf(Slot slot)
    {
        return new ExpansionSlotState(
            slot.Index,
            slot.Installation?.Descriptor,
            slot.Status,
            slot.LastError);
    }

    private static ExpansionCardInstallation?[] validateInstallations(
        IEnumerable<ExpansionCardInstallation>? installations)
    {
        var result = new ExpansionCardInstallation?[SlotCount];
        if (installations is null)
            return result;

        foreach (ExpansionCardInstallation installation in installations)
        {
            ArgumentNullException.ThrowIfNull(installation);
            ensureSlot(installation.Slot);
            ArgumentNullException.ThrowIfNull(installation.Descriptor);
            ArgumentNullException.ThrowIfNull(installation.Card);

            if (result[installation.Slot] is not null)
                throw new ArgumentException($"Expansion slot {installation.Slot} is configured more than once.", nameof(installations));

            result[installation.Slot] = installation;
        }

        return result;
    }

    private static void ensureSlot(int slot)
    {
        if ((uint)slot >= SlotCount)
            throw new ArgumentOutOfRangeException(nameof(slot), $"Expansion slot must be between 0 and {SlotCount - 1}.");
    }

    private static string describe(Exception exception) =>
        $"{exception.GetType().Name}: {exception.Message}";

    private sealed class Slot
    {
        public Slot(int index, PhysicalMemoryView mailbox, ExpansionCardInstallation? installation)
        {
            Index = index;
            Mailbox = mailbox;
            Installation = installation;
            Status = installation is null ? ExpansionCardStatus.None : ExpansionCardStatus.Present;
        }

        public int Index { get; }
        public PhysicalMemoryView Mailbox { get; }
        public ExpansionCardInstallation? Installation { get; }
        public ExpansionCardStatus Status { get; set; }
        public ulong Generation { get; set; }
        public byte[]? ActiveMailbox { get; set; }
        public string? LastError { get; set; }
    }

    private sealed class CommandCompletion : IExpansionCardCommand
    {
        private readonly ExpansionBus owner;
        private readonly int slot;
        private readonly ulong generation;
        private readonly byte[] mailbox;
        private bool completed;

        public CommandCompletion(ExpansionBus owner, int slot, ulong generation, byte[] mailbox)
        {
            this.owner = owner;
            this.slot = slot;
            this.generation = generation;
            this.mailbox = mailbox;
        }

        public void Complete()
        {
            if (completed)
                throw new InvalidOperationException("An expansion-card command can only be completed once.");

            completed = true;
            owner.complete(slot, generation, mailbox);
        }
    }
}
