using OldSimulator.HostDevices;
using OldSimulator.Expansion;
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
        console.Execute("mem not-an-address");
        Assert.Contains(console.History, line => line.StartsWith("Error:", StringComparison.Ordinal));
        Assert.Contains("Unterminated", console.Execute("load \"unfinished"));
    }

    [Fact]
    public void PokeAndFillWritePhysicalMemoryButRespectRomProtection()
    {
        byte[] rom = new byte[Memory.SYSTEM_ROM_LENGTH];
        rom[0] = 0xA5;
        var machine = new Machine(rom);
        var console = new DebuggerConsole(machine);

        Assert.Contains("F4000 <- FF", console.Execute("poke F4000 FFh"));
        Assert.Equal((byte)0xFF, machine.Memory.ReadPhysical(0xF4000));

        Assert.Contains("Filled 4", console.Execute("fill F4010 4 7Eh"));
        Assert.Equal((byte)0x7E, machine.Memory.ReadPhysical(0xF4010));
        Assert.Equal((byte)0x7E, machine.Memory.ReadPhysical(0xF4013));

        console.Execute("poke 00300 00");
        Assert.Equal((byte)0xA5, machine.Memory.ReadPhysical(Memory.SYSTEM_ROM_START));
    }

    [Fact]
    public void InAndOutUseTheVirtualIoBus()
    {
        var machine = new Machine();
        var console = new DebuggerConsole(machine);

        Assert.Equal("0020 <- 0001", console.Execute("out 20 1"));
        Assert.Equal(VideoMode.Indexed4, machine.Video.WriteMode);
        Assert.Equal("0021 -> 0001", console.Execute("in 21"));

        Assert.Contains("Usage: out", console.Execute("out 20"));
        Assert.Contains("I/O port", console.Execute("in 10000"));
    }

    [Fact]
    public void CardsListsInstalledAndEmptyExpansionSlots()
    {
        var installation = new ExpansionCardInstallation(
            2,
            new ExpansionCardDescriptor("test.card", "Test card", 1),
            new IdleExpansionCard());
        using var machine = new Machine(expansionCards: [installation]);
        var console = new DebuggerConsole(machine);

        string result = console.Execute("cards");

        Assert.Contains("0: empty", result);
        Assert.Contains("2: test.card Present", result);
        Assert.Contains("7: empty", result);
    }

    [Fact]
    public void LoadCopiesBinaryToPhysicalMemoryAndConfiguresAHighPageEntryPoint()
    {
        string path = Path.Combine(Path.GetTempPath(), $"open16a loader {Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, [0xE8, 0x00]); // HALT, big-endian

        try
        {
            var machine = new Machine();
            var console = new DebuggerConsole(machine);
            machine.Cpu.Registers[0] = 0xBEEF;

            Assert.Contains("23456", console.Execute($"load \"{path}\" 23456"));
            Assert.True(machine.Paused);
            Assert.Equal((ushort)0, machine.Cpu.Registers[0]);
            Assert.Equal((byte)8, machine.Cpu.SG);
            Assert.Equal((ushort)0xF456, machine.Cpu.PC);
            Assert.Equal((byte)0xE8, machine.Memory.ReadPhysical(0x23456));
            Assert.Equal((byte)0x00, machine.Memory.ReadPhysical(0x23457));

            console.Execute("run");
            machine.AdvanceCycles(1);
            Assert.True(machine.Cpu.Halted);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadRunStartsImmediatelyAndRefusesToOverwriteSystemRom()
    {
        string path = Path.Combine(Path.GetTempPath(), $"open16a-loader-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, [0xE8, 0x00]);

        try
        {
            var machine = new Machine();
            var console = new DebuggerConsole(machine);

            Assert.Contains("Running", console.Execute($"loadrun \"{path}\" 0400"));
            Assert.False(machine.Paused);
            Assert.Equal((ushort)0x0400, machine.Cpu.PC);

            var romConsole = new DebuggerConsole(new Machine(new byte[Memory.SYSTEM_ROM_LENGTH]));
            Assert.Contains("system ROM", romConsole.Execute($"load \"{path}\" 0300"));
        }
        finally
        {
            File.Delete(path);
        }
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

    private sealed class IdleExpansionCard : IExpansionCard
    {
        public void BeginCommand(ushort command, Memory<byte> mailbox, IExpansionCardCommand completion)
        {
        }

        public void AdvanceCycles(ulong cycles)
        {
        }

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }
}
