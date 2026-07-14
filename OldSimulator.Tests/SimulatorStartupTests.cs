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
        Assert.Null(options.ProgramLoad);
    }

    [Fact]
    public void ExplicitConfigurationAndHelpAreParsedWithoutStartingTheHost()
    {
        SimulatorStartupOptions configured = SimulatorStartup.Parse(["--load", "program.bin:0x0300", "--config", "machine.json"]);
        SimulatorStartupOptions help = SimulatorStartup.Parse(["--help"]);

        Assert.True(configured.ConfigurationWasExplicit);
        Assert.Equal(Path.GetFullPath("machine.json"), configured.ConfigurationPath);
        Assert.Equal(Path.GetFullPath("program.bin"), configured.ProgramLoad!.Path);
        Assert.Equal((uint)0x0300, configured.ProgramLoad.BaseAddress);
        Assert.True(help.ShowHelp);
    }

    [Theory]
    [InlineData("--config")]
    [InlineData("--load")]
    [InlineData("--load program.bin")]
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
        var options = new SimulatorStartupOptions(false, path, false, null);

        Assert.Empty(SimulatorStartup.LoadExpansionCards(options));
    }

    [Fact]
    public void MissingExplicitConfigurationIsAnError()
    {
        string path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");
        var options = new SimulatorStartupOptions(false, path, true, null);

        FileNotFoundException error = Assert.Throws<FileNotFoundException>(
            () => SimulatorStartup.LoadExpansionCards(options));

        Assert.Equal(path, error.FileName);
    }
}
