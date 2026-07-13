using Xunit;

namespace OldSimulator.Tests;

public sealed class SimulatorStartupTests
{
    [Fact]
    public void NoArgumentsSelectsTheConfigurationBesideTheExecutable()
    {
        SimulatorStartupOptions options = SimulatorStartup.Parse([]);

        Assert.False(options.ShowHelp);
        Assert.False(options.ConfigurationWasExplicit);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "simulator.json"), options.ConfigurationPath);
    }

    [Fact]
    public void ExplicitConfigurationAndHelpAreParsedWithoutStartingTheHost()
    {
        SimulatorStartupOptions configured = SimulatorStartup.Parse(["--config", "machine.json"]);
        SimulatorStartupOptions help = SimulatorStartup.Parse(["--help"]);

        Assert.True(configured.ConfigurationWasExplicit);
        Assert.Equal(Path.GetFullPath("machine.json"), configured.ConfigurationPath);
        Assert.True(help.ShowHelp);
    }

    [Theory]
    [InlineData("--config")]
    [InlineData("--unknown")]
    [InlineData("--help extra")]
    public void InvalidArgumentsAreRejected(string commandLine)
    {
        Assert.Throws<ArgumentException>(() => SimulatorStartup.Parse(commandLine.Split(' ')));
    }

    [Fact]
    public void MissingDefaultConfigurationMeansNoExpansionCards()
    {
        string path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");
        var options = new SimulatorStartupOptions(false, path, false);

        Assert.Empty(SimulatorStartup.LoadExpansionCards(options));
    }

    [Fact]
    public void MissingExplicitConfigurationIsAnError()
    {
        string path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");
        var options = new SimulatorStartupOptions(false, path, true);

        FileNotFoundException error = Assert.Throws<FileNotFoundException>(
            () => SimulatorStartup.LoadExpansionCards(options));

        Assert.Equal(path, error.FileName);
    }
}
