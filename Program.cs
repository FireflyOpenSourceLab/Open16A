using System.Diagnostics;
using OldSimulator.HostDevices;
using OldSimulator.VirtualDevices;
using Raylib_cs;

const int min_window_width  = 1024;
const int min_window_height = 768;

Raylib.SetConfigFlags(
    ConfigFlags.ResizableWindow |
    ConfigFlags.VSyncHint
);

Raylib.InitWindow(1024, 768, "The Open16A Simulator");
Raylib.SetWindowMinSize(min_window_width, min_window_height);
const ulong cpu_hz = 16_934_400;

long  previousTimestamp = Stopwatch.GetTimestamp();
ulong cycleRemainder    = 0;

var machine = new Machine();
var debugger = new DebuggerConsole(machine);
var keyboard = new RayLibKeyboard(machine.Keyboard);

try
{
    using var screen = new RayLibScreen();
    using var debuggerScope = debugger;
    while (!Raylib.WindowShouldClose())
    {
        debugger.UpdateInput();
        if (debugger.IsOpen)
            machine.Keyboard.Clear();
        else
            keyboard.Update();

        long now          = Stopwatch.GetTimestamp();
        long elapsedTicks = now - previousTimestamp;
        previousTimestamp = now;

        // 可在这里将超长停顿限制为 100 ms，避免断点恢复后疯狂追帧。
        elapsedTicks = Math.Min(elapsedTicks, Stopwatch.Frequency / 10);

        UInt128 scaled = (UInt128)elapsedTicks * cpu_hz + cycleRemainder;
        ulong   cycles = (ulong)(scaled / (ulong)Stopwatch.Frequency);
        cycleRemainder = (ulong)(scaled % (ulong)Stopwatch.Frequency);

        machine.AdvanceCycles(cycles);
        screen.Sync(machine.Video.CurrentFrame);

        Raylib.BeginDrawing();
        // 绘制 Screen 纹理和调试界面
        Raylib.ClearBackground(Color.Black);
        screen.Draw();
        debugger.Draw();
        Raylib.EndDrawing();
    }
}
finally
{
    Raylib.CloseWindow();
}
