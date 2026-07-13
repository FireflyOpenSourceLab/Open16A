using OldSimulator.VirtualDevices;
using Open16A.Asm;
using Open16A.BasicPack;
using Xunit;

namespace OldSimulator.Tests;

public sealed class BasicInterpreterTests
{
    [Fact]
    public void InterpreterStaysWithinBasic11SizeBudget()
    {
        AssemblyResult image = AssembleInterpreter();

        Assert.True(image.Bytes.Length <= 10_000,
            $"BASIC 1.1 interpreter is {image.Bytes.Length} bytes; budget is 10000 bytes.");
        Assert.True(image.Origin + image.Bytes.Length <= 0x4000,
            "Interpreter must not overlap the B16P program store at 4000h.");
    }

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

    [Fact]
    public void ControlCInterruptsAnInfiniteBasicLoop()
    {
        Machine machine = StartInterpreter();
        WriteInfiniteLoopProgram(machine.Memory);
        machine.AdvanceCycles(10_000);

        Assert.False(machine.Cpu.Halted);
        machine.Keyboard.SetKeyState(0x39, true); // LeftControl
        machine.AdvanceCycles(256);
        machine.Keyboard.SetKeyState(0x30, true); // C
        machine.AdvanceCycles(10_000);

        Assert.True(machine.Cpu.Halted);
        Assert.Equal(CpuFaultCode.None, machine.Cpu.FaultCode);
    }

    [Fact]
    public void ReplUsesTheSharedTokenFormatForLetExpressions()
    {
        Machine machine = StartInterpreter();
        machine.AdvanceCycles(100_000);

        SendLine(machine, "10 let a=5");

        Assert.Equal((ushort)1, machine.Memory.ReadPhysicalWord(0x4008));
        Assert.Equal((ushort)7, machine.Memory.ReadPhysicalWord(0x400C));
        Assert.Equal(new byte[] { 0x90, 0x84, 0x00, (byte)'=', 0x82, 0x00, 0x05 },
            Enumerable.Range(0, 7).Select(index => machine.Memory.ReadPhysical(0x400Eu + (uint)index)).ToArray());
    }

    [Fact]
    public void LowercaseListDirectCommandListsTheProgram()
    {
        Machine machine = StartInterpreter();
        machine.AdvanceCycles(100_000);
        SendLine(machine, "10 end");

        SendLine(machine, "list");

        // LIST writes line number '1' at the first cell of text row 2. A
        // syntax error would write '?', whose first column has pixel y+1 set.
        Assert.Equal((byte)0, machine.Memory.ReadPhysical(0xF4000u + 17u * 256u));
        Assert.True(machine.Cpu.Halted);
        Assert.Equal(CpuFaultCode.None, machine.Cpu.FaultCode);
    }

    [Fact]
    public void DirectCommandsUseTheCaseInsensitiveTokenizer()
    {
        Machine machine = StartInterpreter();
        machine.AdvanceCycles(100_000);
        SendLine(machine, "10 end");

        SendLine(machine, "NEW");
        machine.AdvanceCycles(10_000);

        Assert.Equal((byte)0, machine.Memory.ReadPhysical(0x4000));
        Assert.True(machine.Cpu.Halted);
        Assert.Equal(CpuFaultCode.None, machine.Cpu.FaultCode);
    }

    [Fact]
    public void ExecutesIntegerExpressionsWithMicrosoftBasicPrecedence()
    {
        Machine machine = StartInterpreter();
        WritePackedProgram(machine.Memory, "10 LET A=2+3*4\n20 POKE 16000,A\n30 END");

        machine.AdvanceCycles(100_000);

        Assert.Equal((byte)14, machine.Memory.ReadPhysical(16000));
        Assert.True(machine.Cpu.Halted);
        Assert.Equal(CpuFaultCode.None, machine.Cpu.FaultCode);
    }

    [Fact]
    public void ExecutesGosubAndReturnUsingABasicControlStack()
    {
        Machine machine = StartInterpreter();
        WritePackedProgram(machine.Memory,
            "10 GOSUB 100\n20 POKE 9000,A\n30 END\n100 LET A=7\n110 RETURN");

        machine.AdvanceCycles(100_000);

        Assert.Equal((byte)7, machine.Memory.ReadPhysical(9000));
        Assert.True(machine.Cpu.Halted);
        Assert.Equal(CpuFaultCode.None, machine.Cpu.FaultCode);
    }

    [Fact]
    public void ExecutesForNextWithToAndStepFrames()
    {
        Machine machine = StartInterpreter();
        WritePackedProgram(machine.Memory,
            "10 LET A=0\n20 FOR I=1 TO 5\n30 LET A=A+I\n40 NEXT I\n50 POKE 9100,A\n60 END");

        machine.AdvanceCycles(200_000);

        Assert.Equal((byte)15, machine.Memory.ReadPhysical(9100));
        Assert.True(machine.Cpu.Halted);
        Assert.Equal(CpuFaultCode.None, machine.Cpu.FaultCode);
    }

    [Fact]
    public void InputSuspendsForGuestKeyboardAndResumesTheProgram()
    {
        Machine machine = StartInterpreter();
        WritePackedProgram(machine.Memory, "10 INPUT \"VALUE\"; A\n20 POKE 9200,A\n30 END");
        machine.AdvanceCycles(100_000);

        Assert.True(machine.Cpu.Halted);
        SendLine(machine, "42");

        Assert.Equal((byte)42, machine.Memory.ReadPhysical(9200));
        Assert.True(machine.Cpu.Halted);
        Assert.Equal(CpuFaultCode.None, machine.Cpu.FaultCode);
    }

    [Fact]
    public void DataReadAndRestoreShareAProgramDataCursor()
    {
        Machine machine = StartInterpreter();
        WritePackedProgram(machine.Memory,
            "10 DATA 3,4\n20 READ A,B\n30 POKE 9300,A*10+B\n40 RESTORE\n50 READ C\n60 POKE 9301,C\n70 END");

        machine.AdvanceCycles(200_000);

        Assert.Equal((byte)34, machine.Memory.ReadPhysical(9300));
        Assert.Equal((byte)3, machine.Memory.ReadPhysical(9301));
        Assert.True(machine.Cpu.Halted);
    }

    [Fact]
    public void StoresCopiesAndMeasuresStringVariables()
    {
        Machine machine = StartInterpreter();
        WritePackedProgram(machine.Memory,
            "10 LET A$=\"HELLO\"\n20 POKE 9400,LEN(A$)\n30 LET B$=A$\n40 POKE 9401,LEN(B$)\n50 END");

        machine.AdvanceCycles(200_000);

        Assert.Equal((byte)5, machine.Memory.ReadPhysical(9400));
        Assert.Equal((byte)5, machine.Memory.ReadPhysical(9401));
        Assert.True(machine.Cpu.Halted);
    }

    [Fact]
    public void DimCreatesOneDimensionalNumericArrayStorage()
    {
        Machine machine = StartInterpreter();
        WritePackedProgram(machine.Memory,
            "10 DIM A(10)\n20 FOR I=0 TO 10\n30 LET A(I)=I*2\n40 NEXT I\n50 POKE 9500,A(7)\n60 END");

        machine.AdvanceCycles(1_000_000);
        machine.AdvanceCycles(1_000);

        Assert.Equal((byte)14, machine.Memory.ReadPhysical(9500));
        Assert.True(machine.Cpu.Halted);
    }

    [Fact]
    public void InpAndOutAccessTheOpen16aIoBus()
    {
        Machine machine = StartInterpreter();
        ushort written = 0;
        machine.IoBus.RegisterRead(0x50, () => 0x1234);
        machine.IoBus.RegisterWrite(0x51, value => written = value);
        WritePackedProgram(machine.Memory,
            "10 LET A=INP(80)\n20 OUT 81,A+1\n30 POKE 9600,A\n40 END");

        machine.AdvanceCycles(300_000);
        machine.AdvanceCycles(10_000);

        Assert.Equal((ushort)0x1235, written);
        Assert.Equal((byte)0x34, machine.Memory.ReadPhysical(9600));
        Assert.True(machine.Cpu.Halted);
        Assert.Equal(CpuFaultCode.None, machine.Cpu.FaultCode);
    }

    [Fact]
    public void GraphicsCommandsDrawIndexedPixelsLinesAndCircles()
    {
        Machine machine = StartInterpreter();
        WritePackedProgram(machine.Memory,
            "10 SCREEN 0\n20 PSET (5,3),12\n30 LINE (1,1)-(4,4),9\n" +
            "40 CIRCLE (100,100),3,7\n50 PRESENT\n60 END");

        machine.AdvanceCycles(2_000_000);
        machine.AdvanceCycles(10_000);

        Assert.True(machine.Cpu.Halted, $"PC={machine.Cpu.PC:X4}, fault={machine.Cpu.FaultCode}");
        Assert.Equal((byte)12, machine.Memory.ReadPhysical(0xF4000u + 3u * 256u + 5u));
        Assert.Equal((byte)9, machine.Memory.ReadPhysical(0xF4000u + 2u * 256u + 2u));
        Assert.Equal((byte)7, machine.Memory.ReadPhysical(0xF4000u + 100u * 256u + 103u));
        Assert.Equal((byte)7, machine.Memory.ReadPhysical(0xF4000u + 103u * 256u + 100u));
        Assert.Equal(CpuFaultCode.None, machine.Cpu.FaultCode);
    }

    [Fact]
    public void PsetAndPointSupportPackedAndRgbaVideoModes()
    {
        Machine packed = StartInterpreter();
        WritePackedProgram(packed.Memory,
            "10 SCREEN 1\n20 PSET (7,2),3\n25 PRESET (8,2),2\n" +
            "30 POKE 9700,POINT(7,2)\n35 POKE 9702,POINT(8,2)\n40 END");
        packed.AdvanceCycles(1_000_000);

        Assert.Equal((byte)3, packed.Memory.ReadPhysical(9700));
        Assert.Equal((byte)2, packed.Memory.ReadPhysical(9702));
        Assert.Equal((byte)0x03, packed.Memory.ReadPhysical(0xF4000u + 2u * 128u + 1u));

        Machine rgba = StartInterpreter();
        WritePackedProgram(rgba.Memory,
            "10 SCREEN 2\n20 PSET (2,1),4660\n30 A=POINT(2,1)\n40 POKE 9701,A\n50 END");
        rgba.AdvanceCycles(1_000_000);
        rgba.AdvanceCycles(10_000);

        uint address = 0xF4000u + 1u * 512u + 2u * 4u;
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44 },
            Enumerable.Range(0, 4).Select(offset => rgba.Memory.ReadPhysical(address + (uint)offset)).ToArray());
        Assert.Equal((byte)0x34, rgba.Memory.ReadPhysical(9701));
        Assert.True(rgba.Cpu.Halted);
        Assert.Equal(CpuFaultCode.None, rgba.Cpu.FaultCode);
    }

    [Fact]
    public void PaletteCommandProgramsTheVideoDac()
    {
        Machine machine = StartInterpreter();
        WritePackedProgram(machine.Memory, "10 PALETTE 42,18,52,86\n20 END");

        machine.AdvanceCycles(200_000);

        Assert.Equal(new Rgb24(18, 52, 86), machine.Video.GetPaletteEntry(42));
        Assert.True(machine.Cpu.Halted);
    }

    private static Machine StartInterpreter()
    {
        AssemblyResult image = AssembleInterpreter();
        var machine = new Machine();

        for (var index = 0; index < image.Bytes.Length; index++)
            machine.Memory.WritePhysical(image.Origin + (uint)index, image.Bytes[index]);

        machine.Cpu.PC = checked((ushort)image.Origin);
        machine.Cpu.SG = 0;
        return machine;
    }

    private static AssemblyResult AssembleInterpreter()
    {
        string root = FindProjectRoot();
        string source = File.ReadAllText(Path.Combine(root, "toolchains", "Open16A-BASIC", "basic.asm"));
        return new Assembler().Assemble(source);
    }

    private static void SendLine(Machine machine, string text)
    {
        foreach (char character in text)
        {
            if (char.IsAsciiLetterUpper(character))
            {
                machine.Keyboard.SetKeyState(0x2D, true); // LeftShift
                machine.AdvanceCycles(128);
                Press(machine, ScanCode(char.ToLowerInvariant(character)));
                machine.Keyboard.SetKeyState(0x2D, false);
                machine.AdvanceCycles(128);
            }
            else
            {
                Press(machine, ScanCode(character));
            }
        }
        Press(machine, 0x2C); // Enter
    }

    private static void Press(Machine machine, byte scanCode)
    {
        machine.Keyboard.SetKeyState(scanCode, true);
        machine.AdvanceCycles(2_048);
        machine.Keyboard.SetKeyState(scanCode, false);
        machine.AdvanceCycles(2_048);
    }

    private static byte ScanCode(char character) => character switch
    {
        '0' => 0x0A, '1' => 0x01, '2' => 0x02, '3' => 0x03, '4' => 0x04,
        '5' => 0x05, '6' => 0x06, '7' => 0x07, '8' => 0x08, '9' => 0x09,
        ' ' => 0x3B, '=' => 0x0C, 'a' => 0x21, 'd' => 0x23, 'e' => 0x13,
        'g' => 0x25, 'i' => 0x18, 'l' => 0x29, 'n' => 0x33, 'o' => 0x19,
        's' => 0x22, 't' => 0x15, 'w' => 0x12,
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

    private static void WriteInfiniteLoopProgram(Memory memory)
    {
        byte[] image =
        [
            (byte)'B', (byte)'1', (byte)'6', (byte)'P', 1, 1, 0, 8, 0, 1,
            0, 10, 0, 4, 0x95, 0x82, 0, 10
        ];

        for (var index = 0; index < image.Length; index++)
            memory.WritePhysical(0x4000u + (uint)index, image[index]);
    }

    private static void WritePackedProgram(Memory memory, string source)
    {
        byte[] image = BasicTokenizer.ParseProgram(source, autoRun: true).ToBytes();
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
