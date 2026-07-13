using System.Text.Json;
using OldSimulator.VirtualDevices;

namespace OldSimulator.Expansion.EmbeddedAsm;

public static class EmbeddedAsmCardLayout
{
    public const int    AddressSpaceBytes = 1 << 16;
    public const ushort ProgramAddress    = Cpu.INITIAL_PROGRAM_COUNTER;
    public const ushort StackAddress      = Cpu.INITIAL_STACK_POINTER;
    public const ushort MailboxAddress    = 0xFC00;
    public const byte   ExternalVector    = 0;
}

public sealed class EmbeddedAsmExpansionCardPlugin : IExpansionCardPlugin
{
    public const string CardId = "open16a.embedded-asm";
    private const string FirmwareResourceName = "Open16A.EmbeddedAsm.firmware.bin";

    private static readonly IReadOnlyList<ExpansionCardDescriptor> CardDescriptors =
        Array.AsReadOnly([
            new ExpansionCardDescriptor(CardId, "Open16A Embedded ASM Coprocessor", 1)
        ]);

    public int ApiVersion => ExpansionCardApi.Version;

    public IReadOnlyList<ExpansionCardDescriptor> Cards => CardDescriptors;

    public IExpansionCard Create(
        string cardId,
        ExpansionCardCreateContext context,
        JsonElement settings)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!string.Equals(cardId, CardId, StringComparison.Ordinal))
            throw new ArgumentException($"Unknown expansion card ID '{cardId}'.", nameof(cardId));
        if (settings.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Embedded ASM card settings must be a JSON object.", nameof(settings));
        if (settings.TryGetProperty("firmwareBase64", out _))
        {
            throw new ArgumentException(
                "firmwareBase64 is not supported; rebuild the plugin to change embedded firmware.",
                nameof(settings));
        }

        return new EmbeddedAsmExpansionCard(ReadEmbeddedFirmware());
    }

    private static byte[] ReadEmbeddedFirmware()
    {
        using Stream stream = typeof(EmbeddedAsmExpansionCardPlugin).Assembly
            .GetManifestResourceStream(FirmwareResourceName) ??
            throw new InvalidOperationException(
                $"Embedded ASM firmware resource '{FirmwareResourceName}' is missing.");
        using var output = new MemoryStream();
        stream.CopyTo(output);
        byte[] firmware = output.ToArray();

        if (firmware.Length == 0 || firmware.Length > EmbeddedAsmCardLayout.MailboxAddress - EmbeddedAsmCardLayout.ProgramAddress)
        {
            throw new InvalidOperationException(
                $"Embedded ASM firmware must contain 1-{EmbeddedAsmCardLayout.MailboxAddress - EmbeddedAsmCardLayout.ProgramAddress} bytes.");
        }

        return firmware;
    }
}

internal sealed class EmbeddedAsmExpansionCard : IExpansionCard
{
    private readonly byte[] firmware;
    private Memory memory = null!;
    private IoBus ioBus = null!;
    private InterruptController interrupts = null!;
    private Cpu cpu = null!;
    private PhysicalMemoryView mailbox = null!;
    private Memory<byte> externalMailbox;
    private IExpansionCardCommand? completion;
    private bool disposed;

    public EmbeddedAsmExpansionCard(byte[] firmware)
    {
        this.firmware = firmware ?? throw new ArgumentNullException(nameof(firmware));
        initializeProcessor();
    }

    public void BeginCommand(ushort command, Memory<byte> mailbox, IExpansionCardCommand completion)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(completion);
        if (mailbox.Length != ExpansionCardApi.MailboxSize)
            throw new ArgumentException($"Mailbox must be exactly {ExpansionCardApi.MailboxSize} bytes.", nameof(mailbox));
        if (this.completion is not null)
            throw new InvalidOperationException("The embedded ASM processor already has a command in progress.");
        throwIfFaulted();

        this.mailbox.CopyFrom(mailbox.Span);
        externalMailbox = mailbox;
        this.completion = completion;
        cpu.Registers[0] = command;
        interrupts.Raise(EmbeddedAsmCardLayout.ExternalVector);
    }

    public void AdvanceCycles(ulong cycles)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (completion is null)
            return;

        acknowledgeInterrupt();
        ulong remaining = cycles;
        while (remaining != 0 && !cpu.Halted)
        {
            ulong cost = cpu.PeekNextInstructionCost();
            if (cost > remaining)
                break;

            cost = cpu.ExecuteNextInstruction();
            remaining -= cost;
            throwIfFaulted();
            acknowledgeInterrupt();
        }

        throwIfFaulted();
        if (cpu.Halted && !interrupts.IsPending(EmbeddedAsmCardLayout.ExternalVector))
            completeCommand();
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        completion = null;
        externalMailbox = default;
        initializeProcessor();
    }

    public void Dispose()
    {
        completion = null;
        externalMailbox = default;
        disposed = true;
    }

    private void initializeProcessor()
    {
        memory = new Memory(EmbeddedAsmCardLayout.AddressSpaceBytes, flatLogicalAddressing: true);
        ioBus = new IoBus();
        interrupts = new InterruptController();
        cpu = new Cpu(memory, ioBus);
        mailbox = memory.CreatePhysicalView(EmbeddedAsmCardLayout.MailboxAddress, ExpansionCardApi.MailboxSize);
        for (var index = 0; index < firmware.Length; index++)
            memory.WritePhysical((uint)(EmbeddedAsmCardLayout.ProgramAddress + index), firmware[index]);
    }

    private void acknowledgeInterrupt()
    {
        if (interrupts.TryGetPending(cpu.InterruptsEnabled, out byte vector) && cpu.TryEnterInterrupt(vector))
            interrupts.Clear(vector);
    }

    private void completeCommand()
    {
        IExpansionCardCommand commandCompletion = completion!;
        mailbox.CopyTo(externalMailbox.Span);
        completion = null;
        externalMailbox = default;
        commandCompletion.Complete();
    }

    private void throwIfFaulted()
    {
        if (cpu.FaultCode != CpuFaultCode.None)
        {
            throw new InvalidOperationException(
                $"Embedded ASM processor faulted with {cpu.FaultCode} at {cpu.FaultingPc:X4}h.");
        }
    }
}
