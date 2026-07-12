using OldSimulator.VirtualDevices;
using Xunit;

namespace OldSimulator.Tests;

public sealed class KeyboardDeviceTests
{
    [Fact]
    public void PressesArePackedIntoThreeBytesInPressOrder()
    {
        var fixture = new Fixture();

        fixture.Keyboard.SetKeyState(0x01, true);
        fixture.Keyboard.SetKeyState(0x12, true);
        fixture.Keyboard.SetKeyState(0x23, true);
        fixture.Keyboard.SetKeyState(0x3E, true);

        Assert.Equal((byte)0x05, fixture.Memory.ReadPhysical(KeyboardDevice.STATE_ADDRESS));
        Assert.Equal((byte)0x28, fixture.Memory.ReadPhysical(KeyboardDevice.STATE_ADDRESS + 1));
        Assert.Equal((byte)0xFE, fixture.Memory.ReadPhysical(KeyboardDevice.STATE_ADDRESS + 2));
        Assert.True(fixture.Interrupts.IsPending(Machine.KEYBOARD_INTERRUPT_VECTOR));
    }

    [Fact]
    public void FifthKeyIsIgnoredAndReleasingAKeyCompactsTheState()
    {
        var fixture = new Fixture();
        fixture.Keyboard.SetKeyState(0x01, true);
        fixture.Keyboard.SetKeyState(0x12, true);
        fixture.Keyboard.SetKeyState(0x23, true);
        fixture.Keyboard.SetKeyState(0x3E, true);
        fixture.Interrupts.Clear(Machine.KEYBOARD_INTERRUPT_VECTOR);

        Assert.False(fixture.Keyboard.SetKeyState(0x02, true));
        Assert.False(fixture.Interrupts.IsPending(Machine.KEYBOARD_INTERRUPT_VECTOR));

        Assert.True(fixture.Keyboard.SetKeyState(0x12, false));
        Assert.Equal((byte)0x06, fixture.Memory.ReadPhysical(KeyboardDevice.STATE_ADDRESS));
        Assert.Equal((byte)0x3F, fixture.Memory.ReadPhysical(KeyboardDevice.STATE_ADDRESS + 1));
        Assert.Equal((byte)0xBF, fixture.Memory.ReadPhysical(KeyboardDevice.STATE_ADDRESS + 2));
        Assert.True(fixture.Interrupts.IsPending(Machine.KEYBOARD_INTERRUPT_VECTOR));
    }

    [Fact]
    public void EmptySlotsUseTheReservedScanCodeAndInvalidCodesAreRejected()
    {
        var fixture = new Fixture();

        Assert.Equal((byte)0xFF, fixture.Memory.ReadPhysical(KeyboardDevice.STATE_ADDRESS));
        Assert.Equal((byte)0xFF, fixture.Memory.ReadPhysical(KeyboardDevice.STATE_ADDRESS + 1));
        Assert.Equal((byte)0xFF, fixture.Memory.ReadPhysical(KeyboardDevice.STATE_ADDRESS + 2));

        Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Keyboard.SetKeyState(KeyboardDevice.EMPTY_SCAN_CODE, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Keyboard.SetKeyState(0x40, true));
    }

    [Fact]
    public void KeyboardInterruptWakesHaltedCpuAndUsesItsOwnVector()
    {
        var machine = new Machine();
        machine.Memory.WritePhysicalWord(Cpu.INITIAL_PROGRAM_COUNTER, Instruction(27));
        machine.Memory.WritePhysicalWord(Cpu.INITIAL_PROGRAM_COUNTER + 2, Instruction(29));
        machine.Memory.WritePhysicalWord((uint)(Cpu.INTERRUPT_VECTOR_TABLE + Machine.KEYBOARD_INTERRUPT_VECTOR * 2), 0x0040);
        machine.Memory.WritePhysicalWord(0x0040, Instruction(30));

        machine.AdvanceCycles(2);
        machine.Keyboard.SetKeyState(0x21, true);
        machine.AdvanceCycles(1);

        Assert.False(machine.Cpu.Halted);
        Assert.Equal((ushort)0x0040, machine.Cpu.PC);
        Assert.Equal((byte)0x87, machine.Memory.ReadPhysical(KeyboardDevice.STATE_ADDRESS));
        Assert.Equal((byte)0xFF, machine.Memory.ReadPhysical(KeyboardDevice.STATE_ADDRESS + 1));
        Assert.Equal((byte)0xFF, machine.Memory.ReadPhysical(KeyboardDevice.STATE_ADDRESS + 2));

        machine.AdvanceCycles(1);

        Assert.Equal((ushort)(Cpu.INITIAL_PROGRAM_COUNTER + 4), machine.Cpu.PC);
        Assert.True(machine.Cpu.InterruptsEnabled);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Memory = new Memory();
            Interrupts = new InterruptController();
            Keyboard = new KeyboardDevice(
                Interrupts,
                Machine.KEYBOARD_INTERRUPT_VECTOR,
                Memory.CreatePhysicalView(KeyboardDevice.STATE_ADDRESS, KeyboardDevice.STATE_LENGTH));
        }

        public Memory Memory { get; }
        public InterruptController Interrupts { get; }
        public KeyboardDevice Keyboard { get; }
    }

    private static ushort Instruction(int opcode)
    {
        return (ushort)(opcode << 11);
    }
}
