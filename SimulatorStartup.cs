using OldSimulator.Expansion;

namespace OldSimulator;

public sealed record SimulatorStartupOptions(
    bool ShowHelp,
    string ConfigurationPath,
    bool ConfigurationWasExplicit);

public static class SimulatorStartup
{
    public const string Usage = "Usage: OldSimulator [--config <simulator.json>]";

    public static SimulatorStartupOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            return new SimulatorStartupOptions(
                false,
                Path.Combine(AppContext.BaseDirectory, "simulator.json"),
                false);
        }

        if (args.Length == 1 && args[0] is "-h" or "--help")
            return new SimulatorStartupOptions(true, string.Empty, false);

        if (args.Length == 2 && args[0] == "--config" && !string.IsNullOrWhiteSpace(args[1]))
            return new SimulatorStartupOptions(false, Path.GetFullPath(args[1]), true);

        throw new ArgumentException(Usage);
    }

    public static IReadOnlyList<ExpansionCardInstallation> LoadExpansionCards(
        SimulatorStartupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!File.Exists(options.ConfigurationPath))
        {
            if (options.ConfigurationWasExplicit)
                throw new FileNotFoundException("The simulator configuration file does not exist.", options.ConfigurationPath);

            return [];
        }

        return ExpansionPluginLoader.Load(options.ConfigurationPath);
    }
}
