using OldSimulator.Expansion;

namespace OldSimulator;

public sealed record SimulatorStartupOptions(
    bool ShowHelp,
    string ConfigurationPath,
    bool ConfigurationWasExplicit,
    SimulatorProgramLoad? ProgramLoad);

public sealed record SimulatorProgramLoad(string Path, uint BaseAddress);

public static class SimulatorStartup
{
    public const string Usage = "Usage: OldSimulator [--config <simulator.json>] [--load <file.bin:physical-base>]";

    public static SimulatorStartupOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 1 && args[0] is "-h" or "--help")
            return new SimulatorStartupOptions(true, string.Empty, false, null);

        string configurationPath = Path.Combine(AppContext.BaseDirectory, "simulator.json");
        bool configurationWasExplicit = false;
        SimulatorProgramLoad? programLoad = null;
        for (var index = 0; index < args.Length; index++)
        {
            string option = args[index];
            if (option == "--config" && index + 1 < args.Length && !string.IsNullOrWhiteSpace(args[index + 1]) && !configurationWasExplicit)
            {
                configurationPath = Path.GetFullPath(args[++index]);
                configurationWasExplicit = true;
                continue;
            }
            if (option == "--load" && index + 1 < args.Length && programLoad is null)
            {
                programLoad = ParseProgramLoad(args[++index]);
                continue;
            }

            throw new ArgumentException(Usage);
        }

        return new SimulatorStartupOptions(false, configurationPath, configurationWasExplicit, programLoad);
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

    private static SimulatorProgramLoad ParseProgramLoad(string value)
    {
        int separator = value.LastIndexOf(':');
        if (separator <= 0 || separator == value.Length - 1)
            throw new ArgumentException(Usage);

        string path = value[..separator];
        string address = value[(separator + 1)..];
        string digits = address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? address[2..]
            : address.EndsWith("h", StringComparison.OrdinalIgnoreCase)
                ? address[..^1]
                : address;
        if (!uint.TryParse(digits, System.Globalization.NumberStyles.AllowHexSpecifier,
                           System.Globalization.CultureInfo.InvariantCulture, out uint baseAddress)
            || baseAddress >= VirtualDevices.Memory.INSTALLED_BYTES)
        {
            throw new ArgumentException(Usage);
        }

        return new SimulatorProgramLoad(Path.GetFullPath(path), baseAddress);
    }
}
