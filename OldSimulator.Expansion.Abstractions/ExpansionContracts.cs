using System.Text.Json;

namespace OldSimulator.Expansion;

public static class ExpansionCardApi
{
    public const int Version     = 1;
    public const int MailboxSize = 0x400;
}

public sealed record ExpansionCardDescriptor(string Id, string DisplayName, int ProtocolVersion);

public sealed record ExpansionCardCreateContext(int Slot);

public interface IExpansionCardPlugin
{
    int ApiVersion { get; }

    IReadOnlyList<ExpansionCardDescriptor> Cards { get; }

    IExpansionCard Create(string cardId, ExpansionCardCreateContext context, JsonElement settings);
}

public interface IExpansionCard : IDisposable
{
    void BeginCommand(ushort command, Memory<byte> mailbox, IExpansionCardCommand completion);

    void AdvanceCycles(ulong cycles);

    void Reset();
}

public interface IExpansionCardCommand
{
    void Complete();
}
