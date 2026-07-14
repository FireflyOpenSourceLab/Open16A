using OldSimulator.VirtualDevices;
using Xunit;

namespace OldSimulator.Tests;

public sealed class ProgramImageLoaderTests
{
    [Fact]
    public void LoadResetsTheMachineSetsThePhysicalEntryPointAndLeavesItRunnable()
    {
        var machine = new Machine();
        machine.Pause();

        int length = ProgramImageLoader.Load(machine, [0xE8, 0x00], 0x23456);

        Assert.Equal(2, length);
        Assert.False(machine.Paused);
        Assert.Equal((byte)8, machine.Cpu.SG);
        Assert.Equal((ushort)0xF456, machine.Cpu.PC);
        Assert.Equal((byte)0xE8, machine.Memory.ReadPhysical(0x23456));
    }

    [Fact]
    public void LoadRejectsOddAddressesAndProtectedRomOverlap()
    {
        var machine = new Machine();
        Assert.Throws<ArgumentException>(() => ProgramImageLoader.Load(machine, [0], 0x301));

        var romMachine = new Machine(new byte[Memory.SYSTEM_ROM_LENGTH]);
        Assert.Throws<ArgumentException>(() => ProgramImageLoader.Load(romMachine, [0], Memory.SYSTEM_ROM_START));
    }
}
