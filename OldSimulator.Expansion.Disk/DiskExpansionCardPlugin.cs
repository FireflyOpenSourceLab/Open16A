using System.Text.Json;

namespace OldSimulator.Expansion.Disk;

public sealed class DiskExpansionCardPlugin : IExpansionCardPlugin
{
    public const string CardId = "open16a.disk";

    private static readonly IReadOnlyList<ExpansionCardDescriptor> CardDescriptors =
        Array.AsReadOnly([
            new ExpansionCardDescriptor(CardId, "Open16A Disk Image Drive", DiskCardProtocol.ProtocolVersion)
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

        return new DiskExpansionCard(DiskCardSettings.Read(settings));
    }
}

internal sealed record DiskCardSettings(
    string ImagePath,
    bool ReadOnly,
    ulong LatencyCycles)
{
    public const ulong DefaultLatencyCycles = 512;

    public static DiskCardSettings Read(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Disk card settings must be a JSON object.", nameof(settings));
        }

        return new DiskCardSettings(
            readImagePath(settings),
            readReadOnly(settings),
            readLatencyCycles(settings));
    }

    private static string readImagePath(JsonElement settings)
    {
        if (!settings.TryGetProperty("imagePath", out JsonElement value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new ArgumentException(
                "Disk card setting 'imagePath' must be a non-empty string.",
                nameof(settings));
        }

        string path = value.GetString()!;
        if (!Path.IsPathRooted(path))
        {
            throw new ArgumentException(
                "Disk card setting 'imagePath' must be an absolute path.",
                nameof(settings));
        }

        return path;
    }

    private static bool readReadOnly(JsonElement settings)
    {
        if (!settings.TryGetProperty("readOnly", out JsonElement value))
        {
            return false;
        }

        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new ArgumentException(
                "Disk card setting 'readOnly' must be a boolean.",
                nameof(settings));
        }

        return value.GetBoolean();
    }

    private static ulong readLatencyCycles(JsonElement settings)
    {
        if (!settings.TryGetProperty("latencyCycles", out JsonElement value))
        {
            return DefaultLatencyCycles;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt64(out ulong cycles))
        {
            throw new ArgumentException(
                "Disk card setting 'latencyCycles' must be a non-negative integer.",
                nameof(settings));
        }

        return cycles;
    }
}
