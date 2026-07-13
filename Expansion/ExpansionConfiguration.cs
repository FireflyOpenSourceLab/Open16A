using System.Text.Json;
using System.Text.Json.Serialization;

namespace OldSimulator.Expansion;

public sealed class ExpansionConfigurationException : Exception
{
    public ExpansionConfigurationException(string message) : base(message)
    {
    }

    public ExpansionConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed record ExpansionSlotConfiguration(
    int Slot,
    string AssemblyPath,
    string CardId,
    JsonElement Settings);

public sealed class ExpansionConfiguration
{
    private const int SlotCount = 8;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy     = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling  = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly JsonElement EmptySettings = JsonSerializer.SerializeToElement(new { });

    private ExpansionConfiguration(IReadOnlyList<ExpansionSlotConfiguration> slots)
    {
        Version = ExpansionCardApi.Version;
        Slots   = slots;
    }

    public int Version { get; }

    public IReadOnlyList<ExpansionSlotConfiguration> Slots { get; }

    public static ExpansionConfiguration Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        ConfigurationDocument? document;
        try
        {
            using FileStream stream = File.OpenRead(fullPath);
            document = JsonSerializer.Deserialize<ConfigurationDocument>(stream, JsonOptions);
        }
        catch (JsonException error)
        {
            throw new ExpansionConfigurationException(
                $"Expansion configuration '{fullPath}' is not valid JSON: {error.Message}",
                error);
        }
        catch (NotSupportedException error)
        {
            throw new ExpansionConfigurationException(
                $"Expansion configuration '{fullPath}' could not be read: {error.Message}",
                error);
        }

        if (document is null)
            throw new ExpansionConfigurationException($"Expansion configuration '{fullPath}' must contain an object.");

        if (document.Version is null)
            throw new ExpansionConfigurationException($"Expansion configuration '{fullPath}' is missing version.");
        if (document.Version != ExpansionCardApi.Version)
        {
            throw new ExpansionConfigurationException(
                $"Expansion configuration '{fullPath}' uses unsupported version {document.Version}; " +
                $"expected {ExpansionCardApi.Version}.");
        }

        if (document.Slots is null)
            throw new ExpansionConfigurationException($"Expansion configuration '{fullPath}' is missing slots.");

        string baseDirectory = Path.GetDirectoryName(fullPath)!;
        var configuredSlots = new List<ExpansionSlotConfiguration>(document.Slots.Count);
        var usedSlots = new HashSet<int>();

        foreach (SlotDocument? slot in document.Slots)
        {
            if (slot is null)
                throw new ExpansionConfigurationException($"Expansion configuration '{fullPath}' contains a null slot.");
            if (slot.Slot is null)
                throw new ExpansionConfigurationException($"Expansion configuration '{fullPath}' contains a slot without slot number.");
            if (slot.Slot is < 0 or >= SlotCount)
            {
                throw new ExpansionConfigurationException(
                    $"Expansion configuration '{fullPath}' slot {slot.Slot} is outside the valid range 0-{SlotCount - 1}.");
            }
            if (!usedSlots.Add(slot.Slot.Value))
            {
                throw new ExpansionConfigurationException(
                    $"Expansion configuration '{fullPath}' contains duplicate slot {slot.Slot}.");
            }
            if (string.IsNullOrWhiteSpace(slot.Assembly))
                throw new ExpansionConfigurationException($"Expansion configuration '{fullPath}' slot {slot.Slot} is missing assembly.");
            if (string.IsNullOrWhiteSpace(slot.CardId))
                throw new ExpansionConfigurationException($"Expansion configuration '{fullPath}' slot {slot.Slot} is missing cardId.");

            JsonElement settings = slot.Settings.ValueKind switch
            {
                JsonValueKind.Undefined => EmptySettings,
                JsonValueKind.Object    => slot.Settings,
                _ => throw new ExpansionConfigurationException(
                    $"Expansion configuration '{fullPath}' slot {slot.Slot} settings must be a JSON object.")
            };

            string assemblyPath;
            try
            {
                assemblyPath = Path.GetFullPath(slot.Assembly, baseDirectory);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new ExpansionConfigurationException(
                    $"Expansion configuration '{fullPath}' slot {slot.Slot} has an invalid assembly path: {error.Message}",
                    error);
            }

            configuredSlots.Add(new ExpansionSlotConfiguration(
                                    slot.Slot.Value,
                                    assemblyPath,
                                    slot.CardId,
                                    settings.Clone()));
        }

        return new ExpansionConfiguration(configuredSlots.AsReadOnly());
    }

    private sealed class ConfigurationDocument
    {
        public int? Version { get; init; }

        public List<SlotDocument?>? Slots { get; init; }
    }

    private sealed class SlotDocument
    {
        public int? Slot { get; init; }

        public string? Assembly { get; init; }

        public string? CardId { get; init; }

        public JsonElement Settings { get; init; }
    }
}
