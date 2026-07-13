using System.Text.Json;

namespace OldSimulator.Expansion.Loopback;

public sealed class LoopbackExpansionCardPlugin : IExpansionCardPlugin
{
    public const string CardId = "open16a.loopback";

    private const ulong DefaultLatencyCycles = 32;

    private static readonly IReadOnlyList<ExpansionCardDescriptor> CardDescriptors =
        Array.AsReadOnly([
            new ExpansionCardDescriptor(CardId, "Open16A Loopback Diagnostic Card", 1)
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
        {
            throw new ArgumentException($"Unknown expansion card ID '{cardId}'.", nameof(cardId));
        }

        return new LoopbackExpansionCard(ReadLatencyCycles(settings));
    }

    private static ulong ReadLatencyCycles(JsonElement settings)
    {
        if (settings.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return DefaultLatencyCycles;
        }

        if (settings.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Loopback card settings must be a JSON object.", nameof(settings));
        }

        if (!settings.TryGetProperty("latencyCycles", out JsonElement latency))
        {
            return DefaultLatencyCycles;
        }

        if (latency.ValueKind != JsonValueKind.Number || !latency.TryGetUInt64(out ulong value))
        {
            throw new ArgumentException(
                "Loopback setting 'latencyCycles' must be a non-negative integer.",
                nameof(settings));
        }

        return value;
    }
}

internal sealed class LoopbackExpansionCard(ulong latencyCycles) : IExpansionCard
{
    private IExpansionCardCommand? _pendingCommand;
    private ulong _remainingCycles;
    private bool _disposed;

    public void BeginCommand(ushort command, Memory<byte> mailbox, IExpansionCardCommand completion)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(completion);

        if (mailbox.Length != ExpansionCardApi.MailboxSize)
        {
            throw new ArgumentException(
                $"Mailbox must be exactly {ExpansionCardApi.MailboxSize} bytes.",
                nameof(mailbox));
        }

        if (_pendingCommand is not null)
        {
            throw new InvalidOperationException("The loopback card already has a command in progress.");
        }

        if (command != 0)
        {
            mailbox.Span[0] = 0xFF;
            mailbox.Span[1] = 0xFF;
        }

        _pendingCommand = completion;
        _remainingCycles = latencyCycles;

        if (_remainingCycles == 0)
        {
            CompletePendingCommand();
        }
    }

    public void AdvanceCycles(ulong cycles)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_pendingCommand is null)
        {
            return;
        }

        if (cycles < _remainingCycles)
        {
            _remainingCycles -= cycles;
            return;
        }

        CompletePendingCommand();
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelPendingCommand();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CancelPendingCommand();
        _disposed = true;
    }

    private void CompletePendingCommand()
    {
        IExpansionCardCommand completion = _pendingCommand!;
        CancelPendingCommand();
        completion.Complete();
    }

    private void CancelPendingCommand()
    {
        _pendingCommand = null;
        _remainingCycles = 0;
    }
}
