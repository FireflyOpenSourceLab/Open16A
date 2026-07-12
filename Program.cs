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

Raylib.InitWindow(1024, 768, "The OPEN1620 Simulator");
const ulong cpu_hz = 16_934_400;

long  previousTimestamp = Stopwatch.GetTimestamp();
ulong cycleRemainder    = 0;

var machine = new Machine();

Random.Shared.NextBytes(machine.Memory.GetPhysicalView(Machine.VIDEO_RAM_ADDRESS, VideoDevice.VIDEO_RAM_LENGTH).Span);

for (int i = 0; i < 4; i++)
{
    machine.Video.SetPaletteEntry(
        (byte)i,
        new Rgb24(
            (byte)Random.Shared.Next(256),
            (byte)Random.Shared.Next(256),
            (byte)Random.Shared.Next(256)));
}

machine.IoBus.Write(0x20, (ushort)VideoMode.Indexed4);

try
{
    using var screen = new RayLibScreen();
    while (!Raylib.WindowShouldClose())
    {
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
        Raylib.EndDrawing();
    }
}
finally
{
    Raylib.CloseWindow();
}
