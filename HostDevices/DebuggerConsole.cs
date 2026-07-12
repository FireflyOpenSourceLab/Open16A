using System.Globalization;
using Raylib_cs;
using OldSimulator.VirtualDevices;

namespace OldSimulator.HostDevices;

/// <summary>
/// Host-side debugger. It is intentionally outside the emulated I/O and timing model.
/// </summary>
public sealed class DebuggerConsole
{
    private const int PanelX = 16;
    private const int PanelY = 16;
    private const int PanelWidth = 700;
    private const int PanelHeight = 520;
    private const int FontSize = 18;
    private const int LineHeight = 22;
    private const int MaximumInputLength = 120;
    private const int MaximumHistoryLines = 19;

    private readonly Machine machine;
    private readonly List<string> history = [];
    private string input = string.Empty;

    public DebuggerConsole(Machine machine)
    {
        this.machine = machine ?? throw new ArgumentNullException(nameof(machine));
        WriteLine("OPEN1620 host debugger. Type help.");
    }

    public bool IsOpen { get; private set; }

    public IReadOnlyList<string> History => history;

    public void UpdateInput()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.F12))
        {
            IsOpen = !IsOpen;
            if (IsOpen)
                machine.Pause();
        }

        if (!IsOpen)
            return;

        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            IsOpen = false;
            return;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && input.Length != 0)
            input = input[..^1];

        if (Raylib.IsKeyPressed(KeyboardKey.Enter))
        {
            if (input.Length != 0)
            {
                WriteLine($"> {input}");
                Execute(input);
                input = string.Empty;
            }

            return;
        }

        for (int character = Raylib.GetCharPressed(); character != 0; character = Raylib.GetCharPressed())
        {
            if (character is >= 0x20 and <= 0x7E && input.Length < MaximumInputLength)
                input += (char)character;
        }
    }

    public void Draw()
    {
        if (!IsOpen)
            return;

        Raylib.DrawRectangle(PanelX, PanelY, PanelWidth, PanelHeight, new Color(8, 12, 16, 238));
        Raylib.DrawRectangleLines(PanelX, PanelY, PanelWidth, PanelHeight, new Color(72, 212, 175, 255));
        Raylib.DrawText("HOST DEBUGGER  F12 close", PanelX + 12, PanelY + 10, FontSize, new Color(72, 212, 175, 255));

        int y = PanelY + 40;
        foreach (string line in history)
        {
            Raylib.DrawText(line, PanelX + 12, y, FontSize, Color.LightGray);
            y += LineHeight;
        }

        Raylib.DrawText($"> {input}", PanelX + 12, PanelY + PanelHeight - 30, FontSize, Color.White);
    }

    public string Execute(string command)
    {
        string[] parts = command.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return string.Empty;

        try
        {
            string result = parts[0].ToLowerInvariant() switch
            {
                "help" => "help regs status pause run step [n] mem <addr> [len] break <addr> clear <addr>|all breaks set <reg> <value> reset",
                "regs" => FormatRegisters(),
                "status" => FormatStatus(),
                "pause" => Pause(),
                "run" or "continue" => Resume(),
                "step" or "s" => Step(parts),
                "mem" or "m" => DumpMemory(parts),
                "break" or "b" => AddBreakpoint(parts),
                "clear" => ClearBreakpoint(parts),
                "breaks" => ListBreakpoints(),
                "set" => SetRegister(parts),
                "reset" => Reset(),
                _ => $"Unknown command: {parts[0]}"
            };

            if (!string.IsNullOrEmpty(result))
                WriteLine(result);
            return result;
        }
        catch (ArgumentException exception)
        {
            WriteLine($"Error: {exception.Message}");
            return exception.Message;
        }
    }

    private string FormatRegisters()
    {
        var values = new List<string>(8);
        for (var index = 0; index < machine.Cpu.Registers.Length; index++)
            values.Add($"R{index}={machine.Cpu.Registers[index]:X4}");

        return string.Join(" ", values);
    }

    private string FormatStatus()
    {
        Cpu cpu = machine.Cpu;
        string state = machine.Paused ? "paused" : cpu.Halted ? "halted" : "running";
        return $"{state} PC={cpu.PC:X4} PHYS={machine.CurrentPhysicalProgramCounter:X5} SP={cpu.SP:X4} SG={cpu.SG:X2} SR={cpu.SR:X4} IF={(cpu.InterruptsEnabled ? 1 : 0)} FAULT={cpu.FaultCode}";
    }

    private string Pause()
    {
        machine.Pause();
        return "Paused.";
    }

    private string Resume()
    {
        machine.Resume();
        IsOpen = false;
        return "Running.";
    }

    private string Step(string[] parts)
    {
        int count = parts.Length == 1 ? 1 : ParseInt(parts[1]);
        if (count is < 1 or > 10_000)
            throw new ArgumentException("Step count must be between 1 and 10000.");

        ulong cycles = 0;
        for (var index = 0; index < count; index++)
        {
            ulong used = machine.StepInstruction();
            cycles += used;
            if (used == 0 || machine.Cpu.Halted)
                break;
        }

        return $"Stepped {count} instruction(s), {cycles} cycle(s). {FormatStatus()}";
    }

    private string DumpMemory(string[] parts)
    {
        if (parts.Length is < 2 or > 3)
            throw new ArgumentException("Usage: mem <physical-address> [length]");

        uint address = ParseAddress(parts[1]);
        int length = parts.Length == 3 ? ParseInt(parts[2]) : 64;
        if (length is < 1 or > 256 || (ulong)address + (uint)length > Memory.INSTALLED_BYTES)
            throw new ArgumentException("Memory range must be within physical RAM and at most 256 bytes.");

        var lines = new List<string>();
        for (var offset = 0; offset < length; offset += 16)
        {
            int rowLength = Math.Min(16, length - offset);
            var values = new string[rowLength];
            for (var column = 0; column < rowLength; column++)
                values[column] = machine.Memory.ReadPhysical(address + (uint)(offset + column)).ToString("X2");

            lines.Add($"{address + (uint)offset:X5}: {string.Join(' ', values)}");
        }

        foreach (string line in lines)
            WriteLine(line);
        return string.Empty;
    }

    private string AddBreakpoint(string[] parts)
    {
        if (parts.Length != 2)
            throw new ArgumentException("Usage: break <physical-address>");

        uint address = ParseAddress(parts[1]);
        bool added = machine.AddBreakpoint(address);
        return added ? $"Breakpoint set at {address:X5}." : $"Breakpoint already exists at {address:X5}.";
    }

    private string ClearBreakpoint(string[] parts)
    {
        if (parts.Length != 2)
            throw new ArgumentException("Usage: clear <physical-address>|all");

        if (string.Equals(parts[1], "all", StringComparison.OrdinalIgnoreCase))
        {
            machine.ClearBreakpoints();
            return "All breakpoints cleared.";
        }

        uint address = ParseAddress(parts[1]);
        return machine.RemoveBreakpoint(address) ? $"Breakpoint cleared at {address:X5}." : $"No breakpoint at {address:X5}.";
    }

    private string ListBreakpoints()
    {
        return machine.Breakpoints.Count == 0
            ? "No breakpoints."
            : string.Join(" ", machine.Breakpoints.Order().Select(address => address.ToString("X5")));
    }

    private string SetRegister(string[] parts)
    {
        if (parts.Length != 3)
            throw new ArgumentException("Usage: set <r0-r7|pc|sp|sg|sr> <value>");

        ushort value = checked((ushort)ParseInt(parts[2]));
        string target = parts[1].ToLowerInvariant();

        if (target.Length == 2 && target[0] == 'r' && target[1] is >= '0' and <= '7')
            machine.Cpu.Registers[target[1] - '0'] = value;
        else
        {
            switch (target)
            {
                case "pc": machine.Cpu.PC = value; break;
                case "sp": machine.Cpu.SP = value; break;
                case "sg": machine.Cpu.SG = (byte)value; break;
                case "sr": machine.Cpu.SR = value; break;
                default: throw new ArgumentException("Unknown register.");
            }
        }

        return $"{target.ToUpperInvariant()}={value:X4}";
    }

    private string Reset()
    {
        machine.Reset();
        return "Machine reset.";
    }

    private void WriteLine(string line)
    {
        history.Add(line);
        while (history.Count > MaximumHistoryLines)
            history.RemoveAt(0);
    }

    private static int ParseInt(string value)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.Parse(value[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        if (value.EndsWith("h", StringComparison.OrdinalIgnoreCase))
            return int.Parse(value[..^1], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    private static uint ParseAddress(string value)
    {
        int parsed = ParseInt(value);
        if (parsed < 0 || parsed >= Memory.INSTALLED_BYTES)
            throw new ArgumentException("Physical address must be within 00000-FFFFF.");
        return (uint)parsed;
    }
}
