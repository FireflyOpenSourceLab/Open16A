using OldSimulator.VirtualDevices;
using Open16A.Asm;
using Xunit;

namespace OldSimulator.Tests;

public sealed class BasicInterpreterTests
{
    [Fact]
    public void AssembledInterpreterRunsAnAutoRunB16PProgramAndWaitsForKeyboard()
    {
        Machine machine = StartInterpreter();
        WriteAutoRunProgram(machine.Memory);
        machine.AdvanceCycles(100_000);

        Assert.Equal(CpuFaultCode.None, machine.Cpu.FaultCode);
        Assert.True(machine.Cpu.Halted);
        Assert.Equal((byte)'B', machine.Memory.ReadPhysical(0x4000));
        Assert.Equal((byte)0x91, machine.Memory.ReadPhysical(0x400E));
    }

    [Fact]
    public void ReplInsertsSortsReplacesAndDeletesGuestProgramLines()
    {
        Machine machine = StartInterpreter();
        machine.AdvanceCycles(100_000);

        SendLine(machine, "10 end");
        SendLine(machine, "20 goto 10");

        Assert.Equal((ushort)2, machine.Memory.ReadPhysicalWord(0x4008));
        Assert.Equal((ushort)10, machine.Memory.ReadPhysicalWord(0x400A));
        Assert.Equal((ushort)20, machine.Memory.ReadPhysicalWord(0x400F));
        Assert.Equal((ushort)4, machine.Memory.ReadPhysicalWord(0x4011));
        Assert.Equal((byte)0x95, machine.Memory.ReadPhysical(0x4013));
        Assert.Equal((byte)0x82, machine.Memory.ReadPhysical(0x4014));
        Assert.Equal((ushort)10, machine.Memory.ReadPhysicalWord(0x4015));

        SendLine(machine, "10 end");
        Assert.Equal((ushort)2, machine.Memory.ReadPhysicalWord(0x4008));

        SendLine(machine, "20");
        Assert.Equal((ushort)1, machine.Memory.ReadPhysicalWord(0x4008));
        Assert.Equal((ushort)10, machine.Memory.ReadPhysicalWord(0x400A));
        Assert.True(machine.Cpu.Halted);
        Assert.Equal(CpuFaultCode.None, machine.Cpu.FaultCode);
    }

    private static Machine StartInterpreter()
    {
        string root = FindProjectRoot();
        string source = File.ReadAllText(Path.Combine(root, "toolchains", "Open16A-BASIC", "basic.asm"));
        AssemblyResult image = new Assembler().Assemble(source);
        var machine = new Machine();

        for (var index = 0; index < image.Bytes.Length; index++)
            machine.Memory.WritePhysical(image.Origin + (uint)index, image.Bytes[index]);

        machine.Cpu.PC = checked((ushort)image.Origin);
        machine.Cpu.SG = 0;
        return machine;
    }

    private static void SendLine(Machine machine, string text)
    {
        foreach (char character in text)
            Press(machine, ScanCode(character));
        Press(machine, 0x2C); // Enter
    }

    private static void Press(Machine machine, byte scanCode)
    {
        machine.Keyboard.SetKeyState(scanCode, true);
        machine.AdvanceCycles(256);
        machine.Keyboard.SetKeyState(scanCode, false);
        machine.AdvanceCycles(256);
    }

    private static byte ScanCode(char character) => character switch
    {
        '0' => 0x0A, '1' => 0x01, '2' => 0x02, '3' => 0x03, '4' => 0x04,
        '5' => 0x05, '6' => 0x06, '7' => 0x07, '8' => 0x08, '9' => 0x09,
        ' ' => 0x3B, 'd' => 0x23, 'e' => 0x13, 'g' => 0x25, 'n' => 0x33,
        'o' => 0x19, 't' => 0x15,
        _ => throw new ArgumentOutOfRangeException(nameof(character), character, "No virtual scan-code mapping."),
    };

    private static void WriteAutoRunProgram(Memory memory)
    {
        byte[] image =
        [
            (byte)'B', (byte)'1', (byte)'6', (byte)'P', 1, 1, 0, 13, 0, 2,
            0, 10, 0, 4, 0x91, 0x83, 1, (byte)'X',
            0, 20, 0, 1, 0x9D
        ];

        for (var index = 0; index < image.Length; index++)
            memory.WritePhysical(0x4000u + (uint)index, image[index]);
    }

    private static string FindProjectRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OldSimulator.csproj")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not locate the OldSimulator project root.");
    }

}
