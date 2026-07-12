using OldSimulator.HostDevices;
using OldSimulator.VirtualDevices;
using Xunit;

namespace OldSimulator.Tests;

public sealed class DebuggerConsoleTests
{
    [Fact]
    public void CommandsControlExecutionAndExposeMachineState()
    {
        var machine = new Machine();
        var console = new DebuggerConsole(machine);

        Assert.Contains("R0=0000", console.Execute("regs"));
        Assert.Contains("PC=0300", console.Execute("status"));

        console.Execute("set r3 BEEFh");
        console.Execute("set sg 3");
        Assert.Equal((ushort)0xBEEF, machine.Cpu.Registers[3]);
        Assert.Equal((byte)3, machine.Cpu.SG);

        Assert.Equal("Paused.", console.Execute("pause"));
        Assert.True(machine.Paused);
        Assert.Contains("Stepped", console.Execute("step"));
        Assert.True(machine.Paused);
    }

    [Fact]
    public void BreakpointsPauseBeforeExecutingTheirPhysicalAddress()
    {
        var machine = new Machine();
        var console = new DebuggerConsole(machine);
        machine.Memory.WritePhysicalWord(Cpu.INITIAL_PROGRAM_COUNTER, Instruction(0));
        machine.Memory.WritePhysicalWord(Cpu.INITIAL_PROGRAM_COUNTER + 2, Instruction(0));

        Assert.Contains("set", console.Execute("break 00302h"), StringComparison.OrdinalIgnoreCase);
        console.Execute("run");
        machine.AdvanceCycles(2);

        Assert.True(machine.Paused);
        Assert.Equal((ushort)(Cpu.INITIAL_PROGRAM_COUNTER + 2), machine.Cpu.PC);
    }

    [Fact]
    public void BreakpointDoesNotAdvanceVirtualDevicesAfterPausing()
    {
        var machine = new Machine(videoFrameCycles: 10);
        var console = new DebuggerConsole(machine);
        machine.Memory.WritePhysicalWord(Cpu.INITIAL_PROGRAM_COUNTER, Instruction(0));
        machine.Memory.WritePhysicalWord(Cpu.INITIAL_PROGRAM_COUNTER + 2, Instruction(0));
        console.Execute("break 00302h");
        console.Execute("run");

        machine.AdvanceCycles(20);

        Assert.True(machine.Paused);
        Assert.Equal((ulong)0, machine.Video.FrameSerial);
    }

    [Fact]
    public void MemoryDumpUsesPhysicalAddressing()
    {
        var machine = new Machine();
        var console = new DebuggerConsole(machine);
        machine.Memory.WritePhysical(0xA3456, 0x5A);

        console.Execute("mem A3456h 1");
        Assert.Contains(console.History, line => line.Contains("A3456: 5A", StringComparison.Ordinal));
    }

    [Fact]
    public void BareDebuggerAddressesAreHexadecimalAndInvalidInputDoesNotEscape()
    {
        var console = new DebuggerConsole(new Machine());

        Assert.Contains("No breakpoint at F4000", console.Execute("clear F4000"));
        Assert.Contains("Error:", console.Execute("mem not-an-address"));
    }

    [Fact]
    public void ProtectedViewsRespectRomWrites()
    {
        byte[] rom = new byte[Memory.SYSTEM_ROM_LENGTH];
        rom[0] = 0xA5;
        var memory = new Memory(rom);
        var view = memory.CreatePhysicalView(Memory.SYSTEM_ROM_START, 1);

        view.CopyFrom([0x00]);

        Assert.Equal((byte)0xA5, view.Read(0));
    }

    private static ushort Instruction(int opcode)
    {
        return (ushort)(opcode << 11);
    }
}
