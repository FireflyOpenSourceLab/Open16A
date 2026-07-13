using System.Text.Json;
using OldSimulator.Expansion;
using Xunit;

namespace OldSimulator.Tests;

public sealed class ExpansionConfigurationTests
{
    [Fact]
    public void LoadValidatesAndResolvesSlotConfiguration()
    {
        using var directory = new TemporaryDirectory();
        string configPath = directory.WriteJson(
            "simulator.json",
            new
            {
                version = 1,
                slots = new object[]
                {
                    new
                    {
                        slot = 3,
                        assembly = "plugins/example.dll",
                        cardId = "example.card",
                        settings = new { latencyCycles = 17 }
                    },
                    new
                    {
                        slot = 1,
                        assembly = "plugins/other.dll",
                        cardId = "other.card"
                    }
                }
            });

        ExpansionConfiguration configuration = ExpansionConfiguration.Load(configPath);

        Assert.Equal(ExpansionCardApi.Version, configuration.Version);
        Assert.Equal(2, configuration.Slots.Count);
        Assert.Equal(3, configuration.Slots[0].Slot);
        Assert.Equal(Path.Combine(directory.Path, "plugins", "example.dll"),
                     configuration.Slots[0].AssemblyPath);
        Assert.Equal("example.card", configuration.Slots[0].CardId);
        Assert.Equal(17, configuration.Slots[0].Settings.GetProperty("latencyCycles").GetInt32());
        Assert.Equal(JsonValueKind.Object, configuration.Slots[1].Settings.ValueKind);
        Assert.Empty(configuration.Slots[1].Settings.EnumerateObject());
    }

    [Theory]
    [InlineData("{\"version\":2,\"slots\":[]}", "version")]
    [InlineData("{\"version\":1,\"slots\":[{\"slot\":8,\"assembly\":\"a.dll\",\"cardId\":\"a\"}]}", "slot")]
    [InlineData("{\"version\":1,\"slots\":[{\"slot\":0,\"assembly\":\"a.dll\",\"cardId\":\"a\"},{\"slot\":0,\"assembly\":\"b.dll\",\"cardId\":\"b\"}]}", "duplicate")]
    [InlineData("{\"version\":1,\"slots\":[{\"slot\":0,\"assembly\":\"a.dll\",\"cardId\":\"a\",\"settings\":[]}]}", "settings")]
    public void LoadRejectsInvalidConfiguration(string json, string messageFragment)
    {
        using var directory = new TemporaryDirectory();
        string configPath = directory.WriteText("simulator.json", json);

        var error = Assert.Throws<ExpansionConfigurationException>(
            () => ExpansionConfiguration.Load(configPath));

        Assert.Contains(messageFragment, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadReportsMalformedJsonWithTheConfigurationPath()
    {
        using var directory = new TemporaryDirectory();
        string configPath = directory.WriteText("broken.json", "{\"version\":1,");

        var error = Assert.Throws<ExpansionConfigurationException>(
            () => ExpansionConfiguration.Load(configPath));

        Assert.Contains(Path.GetFullPath(configPath), error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<JsonException>(error.InnerException);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"OldSimulator-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string WriteJson(string name, object value)
        {
            return WriteText(name, JsonSerializer.Serialize(value));
        }

        public string WriteText(string name, string contents)
        {
            string path = System.IO.Path.Combine(Path, name);
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
