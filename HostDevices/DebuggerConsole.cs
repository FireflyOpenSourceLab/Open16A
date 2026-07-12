using System.Globalization;
using System.Numerics;
using Raylib_cs;
using OldSimulator.VirtualDevices;

namespace OldSimulator.HostDevices;

/// <summary>
/// Host-side debugger. It is intentionally outside the emulated I/O and timing model.
/// </summary>
public sealed class DebuggerConsole : IDisposable
{
    private const int PanelMargin = 20;
    private const int PanelMaximumWidth = 920;
    private const int PanelMaximumHeight = 560;
    private const int FontSize = 16;
    private const int LineHeight = 21;
    private const int PanelPadding = 16;
    private const int MaximumInputLength = 120;
    private const int MaximumHistoryLines = 19;

    private readonly Machine machine;
    private readonly List<string> history = [];
    private Font font;
    private bool ownsFont;
    private bool fontLoaded;
    private string input = string.Empty;
    private bool disposed;

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

        EnsureFont();

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

        int panelWidth = Math.Min(PanelMaximumWidth, Raylib.GetScreenWidth() - PanelMargin * 2);
        int panelHeight = Math.Min(PanelMaximumHeight, Raylib.GetScreenHeight() - PanelMargin * 2);
        int panelX = Math.Max(PanelMargin, (Raylib.GetScreenWidth() - panelWidth) / 2);
        int panelY = Math.Max(PanelMargin, (Raylib.GetScreenHeight() - panelHeight) / 2);
        int textWidth = panelWidth - PanelPadding * 2;

        Raylib.DrawRectangle(panelX, panelY, panelWidth, panelHeight, new Color(10, 15, 20, 246));
        Raylib.DrawRectangleLines(panelX, panelY, panelWidth, panelHeight, new Color(65, 204, 168, 255));
        Raylib.DrawRectangle(panelX, panelY, panelWidth, 36, new Color(19, 42, 48, 255));
        DrawText("HOST DEBUGGER", panelX + PanelPadding, panelY + 10, new Color(103, 242, 202, 255));
        DrawText("F12 close  ESC hide", panelX + panelWidth - PanelPadding - TextWidth("F12 close  ESC hide"), panelY + 10, new Color(177, 200, 206, 255));

        int historyTop = panelY + 50;
        int inputY = panelY + panelHeight - 34;
        int visibleLines = Math.Max(1, (inputY - historyTop - 8) / LineHeight);
        List<string> visualHistory = WrapHistory(textWidth);
        int firstLine = Math.Max(0, visualHistory.Count - visibleLines);
        int y = historyTop;
        for (var index = firstLine; index < visualHistory.Count; index++)
        {
            Color color = visualHistory[index].StartsWith("> ", StringComparison.Ordinal)
                ? new Color(103, 242, 202, 255)
                : new Color(218, 226, 230, 255);
            DrawText(visualHistory[index], panelX + PanelPadding, y, color);
            y += LineHeight;
        }

        Raylib.DrawRectangle(panelX + 1, inputY - 8, panelWidth - 2, 1, new Color(50, 80, 87, 255));
        string prompt = $"> {input}";
        DrawText(TruncateToWidth(prompt, textWidth), panelX + PanelPadding, inputY, Color.White);
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
                "help" => "Commands: regs, status, pause, run, step [n], mem <addr> [len], break <addr>, clear <addr>|all, breaks, set <reg> <value>, reset",
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
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
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

    private List<string> WrapHistory(int width)
    {
        var lines = new List<string>();
        foreach (string line in history)
            lines.AddRange(WrapLine(line, width));
        return lines;
    }

    private IEnumerable<string> WrapLine(string value, int width)
    {
        if (string.IsNullOrEmpty(value))
        {
            yield return string.Empty;
            yield break;
        }

        var line = string.Empty;
        foreach (string word in value.Split(' ', StringSplitOptions.None))
        {
            string candidate = line.Length == 0 ? word : $"{line} {word}";
            if (line.Length != 0 && TextWidth(candidate) > width)
            {
                yield return line;
                line = word;
            }
            else
            {
                line = candidate;
            }
        }

        while (TextWidth(line) > width)
        {
            int count = Math.Max(1, line.Length - 1);
            while (count > 1 && TextWidth(line[..count]) > width)
                count--;
            yield return line[..count];
            line = line[count..];
        }

        yield return line;
    }

    private string TruncateToWidth(string value, int width)
    {
        if (TextWidth(value) <= width)
            return value;

        const string Ellipsis = "...";
        int count = value.Length;
        while (count > 0 && TextWidth(value[..count] + Ellipsis) > width)
            count--;
        return value[..count] + Ellipsis;
    }

    private void DrawText(string value, int x, int y, Color color)
    {
        Raylib.DrawTextEx(font, value, new Vector2(x, y), FontSize, 0, color);
    }

    private int TextWidth(string value)
    {
        return (int)MathF.Ceiling(Raylib.MeasureTextEx(font, value, FontSize, 0).X);
    }

    private void EnsureFont()
    {
        if (fontLoaded)
            return;

        const string CascadiaMonoPath = @"C:\Windows\Fonts\CascadiaMono.ttf";
        ownsFont = File.Exists(CascadiaMonoPath);
        font = ownsFont ? Raylib.LoadFont(CascadiaMonoPath) : Raylib.GetFontDefault();
        fontLoaded = true;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        if (fontLoaded && ownsFont)
            Raylib.UnloadFont(font);
        disposed = true;
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
        string digits = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? value[2..]
            : value.EndsWith("h", StringComparison.OrdinalIgnoreCase)
                ? value[..^1]
                : value;

        if (!uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint parsed)
            || parsed >= Memory.INSTALLED_BYTES)
            throw new ArgumentException("Physical address must be within 00000-FFFFF.");
        return parsed;
    }
}
