using OldSimulator.Expansion;
using OldSimulator.VirtualDevices;
using Xunit;

namespace OldSimulator.Tests;

public sealed class ExpansionBusTests
{
    private static readonly ExpansionCardDescriptor Descriptor =
        new("test.card", "Test card", 1);

    [Fact]
    public void CommandUsesAPrivateMailboxSnapshotAndPublishesItOnlyOnCompletion()
    {
        var card = new TestCard();
        using var fixture = new Fixture((2, card));
        uint mailbox = ExpansionBus.MailboxAddress(2);
        fixture.Memory.WritePhysical(mailbox, 0x11);
        fixture.Memory.WritePhysical(mailbox + 1, 0x22);
        fixture.Memory.WritePhysical(mailbox + ExpansionCardApi.MailboxSize - 1, 0x33);

        fixture.IoBus.Write((ushort)(ExpansionBus.CommandPortBase + 2), 0xCAFE);

        Assert.Equal((ushort)0xCAFE, card.LastCommand);
        Assert.Equal((byte)0x11, card.Mailbox.Span[0]);
        Assert.Equal((byte)0x22, card.Mailbox.Span[1]);
        Assert.Equal(ExpansionCardApi.MailboxSize, card.Mailbox.Length);
        Assert.Equal((byte)0x33, card.Mailbox.Span[^1]);
        Assert.Equal(ExpansionCardStatus.Present | ExpansionCardStatus.Busy,
                     fixture.Expansion.GetSlotState(2).Status);

        fixture.Memory.WritePhysical(mailbox, 0x99);
        card.Mailbox.Span[1] = 0x44;
        card.Mailbox.Span[^1] = 0x55;
        card.Complete();

        Assert.Equal((byte)0x11, fixture.Memory.ReadPhysical(mailbox));
        Assert.Equal((byte)0x44, fixture.Memory.ReadPhysical(mailbox + 1));
        Assert.Equal((byte)0x55,
                     fixture.Memory.ReadPhysical(mailbox + ExpansionCardApi.MailboxSize - 1));
        Assert.Equal(ExpansionCardStatus.Present | ExpansionCardStatus.Done,
                     fixture.Expansion.GetSlotState(2).Status);
        Assert.Equal((byte)(1 << 2), fixture.Expansion.PendingMask);
        Assert.True(fixture.Interrupts.IsPending(Machine.EXPANSION_INTERRUPT_VECTOR));
        Assert.Throws<InvalidOperationException>(card.Complete);
    }

    [Fact]
    public void BusyAndUnacknowledgedCommandsAreRejectedUntilAck()
    {
        var card = new TestCard();
        using var fixture = new Fixture((0, card));

        fixture.IoBus.Write(ExpansionBus.CommandPortBase, 1);
        fixture.IoBus.Write(ExpansionBus.CommandPortBase, 2);

        Assert.Equal(1, card.BeginCount);
        Assert.Equal(ExpansionCardStatus.Present | ExpansionCardStatus.Busy | ExpansionCardStatus.Rejected,
                     fixture.Expansion.GetSlotState(0).Status);

        card.Complete();
        fixture.IoBus.Write(ExpansionBus.CommandPortBase, 3);

        Assert.Equal(1, card.BeginCount);
        Assert.Equal(ExpansionCardStatus.Present | ExpansionCardStatus.Done | ExpansionCardStatus.Rejected,
                     fixture.Expansion.GetSlotState(0).Status);

        fixture.IoBus.Write(ExpansionBus.PendingPort, 1);
        fixture.IoBus.Write(ExpansionBus.CommandPortBase, 4);

        Assert.Equal(2, card.BeginCount);
        Assert.Equal((ushort)4, card.LastCommand);
        Assert.Equal(ExpansionCardStatus.Present | ExpansionCardStatus.Busy,
                     fixture.Expansion.GetSlotState(0).Status);
    }

    [Fact]
    public void AckClearsSelectedCompletionsAndReraisesTheSharedInterruptForTheRest()
    {
        var card0 = new TestCard(completeImmediately: true);
        var card3 = new TestCard(completeImmediately: true);
        using var fixture = new Fixture((3, card3), (0, card0));

        fixture.IoBus.Write(ExpansionBus.CommandPortBase, 1);
        fixture.IoBus.Write((ushort)(ExpansionBus.CommandPortBase + 3), 2);

        Assert.Equal((ushort)0b1001, fixture.IoBus.Read(ExpansionBus.PendingPort));
        fixture.Interrupts.Clear(Machine.EXPANSION_INTERRUPT_VECTOR);

        fixture.IoBus.Write(ExpansionBus.PendingPort, 1);

        Assert.Equal((byte)0b1000, fixture.Expansion.PendingMask);
        Assert.Equal(ExpansionCardStatus.Present, fixture.Expansion.GetSlotState(0).Status);
        Assert.True(fixture.Interrupts.IsPending(Machine.EXPANSION_INTERRUPT_VECTOR));

        fixture.IoBus.Write(ExpansionBus.PendingPort, 1 << 3);

        Assert.Equal((byte)0, fixture.Expansion.PendingMask);
        Assert.False(fixture.Interrupts.IsPending(Machine.EXPANSION_INTERRUPT_VECTOR));
    }

    [Fact]
    public void OnlyBusyCardsAdvanceInSlotOrderAndPluginFailuresAreIsolated()
    {
        var calls = new List<int>();
        var idle = new TestCard(onAdvance: _ => calls.Add(0));
        var later = new TestCard(onAdvance: _ =>
        {
            calls.Add(5);
            throw new InvalidOperationException("broken card");
        });
        var earlier = new TestCard(onAdvance: cycles => calls.Add(1));
        using var fixture = new Fixture((5, later), (0, idle), (1, earlier));
        fixture.IoBus.Write((ushort)(ExpansionBus.CommandPortBase + 5), 1);
        fixture.IoBus.Write((ushort)(ExpansionBus.CommandPortBase + 1), 2);

        fixture.Expansion.AdvanceCycles(17);

        Assert.Equal([1, 5], calls);
        Assert.Equal([17UL], earlier.AdvanceCalls);
        Assert.Equal(ExpansionCardStatus.Present | ExpansionCardStatus.Busy,
                     fixture.Expansion.GetSlotState(1).Status);
        ExpansionSlotState failed = fixture.Expansion.GetSlotState(5);
        Assert.Equal(ExpansionCardStatus.Present | ExpansionCardStatus.Done | ExpansionCardStatus.PluginFault,
                     failed.Status);
        Assert.Contains("broken card", failed.LastError);
        Assert.Equal((byte)(1 << 5), fixture.Expansion.PendingMask);
    }

    [Fact]
    public void ResetCancelsOldCompletionsPreservesMailboxAndResetsEveryCard()
    {
        var card = new TestCard();
        using var fixture = new Fixture((4, card));
        uint mailbox = ExpansionBus.MailboxAddress(4);
        fixture.Memory.WritePhysical(mailbox, 0x11);
        fixture.IoBus.Write((ushort)(ExpansionBus.CommandPortBase + 4), 7);
        System.Memory<byte> staleMailbox = card.Mailbox;
        IExpansionCardCommand staleCompletion = card.Completion!;
        fixture.Memory.WritePhysical(mailbox, 0x77);

        fixture.Expansion.Reset();
        staleMailbox.Span[0] = 0xEE;
        staleCompletion.Complete();

        Assert.Equal(1, card.ResetCount);
        Assert.Equal((byte)0x77, fixture.Memory.ReadPhysical(mailbox));
        Assert.Equal(ExpansionCardStatus.Present, fixture.Expansion.GetSlotState(4).Status);
        Assert.Equal((byte)0, fixture.Expansion.PendingMask);
        Assert.False(fixture.Interrupts.IsPending(Machine.EXPANSION_INTERRUPT_VECTOR));
    }

    [Fact]
    public void EmptySlotsReadZeroAndIgnoreCommands()
    {
        using var fixture = new Fixture();

        fixture.IoBus.Write((ushort)(ExpansionBus.CommandPortBase + 7), 0xFFFF);

        Assert.Equal((ushort)0, fixture.IoBus.Read((ushort)(ExpansionBus.CommandPortBase + 7)));
        Assert.Equal(ExpansionCardStatus.None, fixture.Expansion.GetSlotState(7).Status);
        Assert.Equal((byte)0, fixture.Expansion.PendingMask);
    }

    [Fact]
    public void MachineAdvancesVideoBeforeExpansionAndDisposesCards()
    {
        var card = new TestCard();
        using (var machine = new Machine(
                   videoFrameCycles: 1,
                   expansionCards: [new ExpansionCardInstallation(0, Descriptor, card)]))
        {
            machine.Video.TryPresent(VideoMode.Indexed256);
            card.OnAdvance = _ => Assert.Equal((ulong)1, machine.Video.FrameSerial);
            machine.IoBus.Write(ExpansionBus.CommandPortBase, 1);

            machine.AdvanceCycles(1);

            Assert.Same(machine.Expansion, machine.Expansion);
            Assert.Equal([1UL], card.AdvanceCalls);
        }

        Assert.Equal(1, card.DisposeCount);
    }

    [Fact]
    public void ExpansionInterruptWakesAHaltedCpu()
    {
        var card = new TestCard();
        using var machine = new Machine(
            expansionCards: [new ExpansionCardInstallation(0, Descriptor, card)]);
        ulong elapsed = 0;
        card.OnAdvance = cycles =>
        {
            elapsed += cycles;
            if (elapsed >= 10)
                card.Complete();
        };
        WriteProgram(
            machine.Memory,
            Instruction(27),                         // EI
            Instruction(2, rd: 0), 0x1234,          // LI R0, 1234h
            Instruction(23, ra: 0), ExpansionBus.CommandPortBase,
            Instruction(29));                        // HALT
        machine.Memory.WritePhysicalWord(
            (uint)(Cpu.INTERRUPT_VECTOR_TABLE + Machine.EXPANSION_INTERRUPT_VECTOR * 2),
            0x0040);
        machine.Memory.WritePhysicalWord(0x0040, Instruction(30)); // IRET

        machine.StepInstruction();
        machine.StepInstruction();
        machine.StepInstruction();
        machine.StepInstruction();

        Assert.True(machine.Cpu.Halted);
        machine.Resume();
        machine.AdvanceCycles(10);

        Assert.False(machine.Cpu.Halted);
        Assert.Equal((ushort)0x0040, machine.Cpu.PC);
        Assert.False(machine.Interrupts.IsPending(Machine.EXPANSION_INTERRUPT_VECTOR));
        Assert.Equal((byte)1, machine.Expansion.PendingMask);
    }

    private static ushort Instruction(int opcode, int rd = 0, int ra = 0, int rb = 0)
    {
        return (ushort)((opcode << 11) | (rd << 8) | (ra << 5) | (rb << 2));
    }

    private static void WriteProgram(Memory memory, params ushort[] words)
    {
        uint address = Cpu.INITIAL_PROGRAM_COUNTER;
        foreach (ushort word in words)
        {
            memory.WritePhysicalWord(address, word);
            address += sizeof(ushort);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(params (int Slot, TestCard Card)[] cards)
        {
            Memory = new Memory();
            IoBus = new IoBus();
            Interrupts = new InterruptController();
            ExpansionCardInstallation[] installations = cards
                .Select(item => new ExpansionCardInstallation(item.Slot, Descriptor, item.Card))
                .ToArray();
            Expansion = new ExpansionBus(
                Memory,
                IoBus,
                Interrupts,
                Machine.EXPANSION_INTERRUPT_VECTOR,
                installations);
        }

        public Memory Memory { get; }
        public IoBus IoBus { get; }
        public InterruptController Interrupts { get; }
        public ExpansionBus Expansion { get; }

        public void Dispose() => Expansion.Dispose();
    }

    private sealed class TestCard : IExpansionCard
    {
        private readonly bool completeImmediately;

        public TestCard(bool completeImmediately = false, Action<ulong>? onAdvance = null)
        {
            this.completeImmediately = completeImmediately;
            OnAdvance = onAdvance;
        }

        public int BeginCount { get; private set; }
        public ushort LastCommand { get; private set; }
        public System.Memory<byte> Mailbox { get; private set; }
        public IExpansionCardCommand? Completion { get; private set; }
        public List<ulong> AdvanceCalls { get; } = [];
        public Action<ulong>? OnAdvance { get; set; }
        public int ResetCount { get; private set; }
        public int DisposeCount { get; private set; }

        public void BeginCommand(
            ushort command,
            System.Memory<byte> mailbox,
            IExpansionCardCommand completion)
        {
            BeginCount++;
            LastCommand = command;
            Mailbox = mailbox;
            Completion = completion;
            if (completeImmediately)
                completion.Complete();
        }

        public void AdvanceCycles(ulong cycles)
        {
            AdvanceCalls.Add(cycles);
            OnAdvance?.Invoke(cycles);
        }

        public void Complete() => Completion!.Complete();

        public void Reset() => ResetCount++;

        public void Dispose() => DisposeCount++;
    }
}
