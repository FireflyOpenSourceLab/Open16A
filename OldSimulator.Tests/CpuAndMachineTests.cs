using OldSimulator.VirtualDevices;
using Xunit;

namespace OldSimulator.Tests;

public sealed class CpuAndMachineTests
{
    [Fact]
    public void ExecutesArithmeticBranchesWordsAndIoInLittleEndianOrder()
    {
        var    memory  = new Memory();
        var    bus     = new IoBus();
        ushort written = 0;
        bus.RegisterWrite(0x42, value => written = value);
        bus.RegisterRead(0x43, () => 0xBEEF);
        var cpu = new Cpu(memory, bus);

        WriteProgram(memory,
                     Instruction(2,  rd: 0), 0x1234,        // LI R0, 0x1234
                     Instruction(2,  rd: 1), 2,             // LI R1, 2
                     Instruction(7,  rd: 2, ra: 0, rb: 1),  // ADD R2, R0, R1
                     Instruction(5,  rd: 2, ra: 1), 0x0010, // ST.B R2, [R1 + 0x10]
                     Instruction(3,  rd: 3, ra: 1), 0x0010, // LD.BU R3, [R1 + 0x10]
                     Instruction(16, ra: 2, rb: 3), 0,      // BNE R2, R3, +0 words
                     Instruction(23, ra: 3),        0x0042, // OUT 0x42, R3
                     Instruction(22, rd: 4),        0x0043, // IN R4, 0x43
                     Instruction(29));

        ExecuteUntilHalted(cpu);

        Assert.Equal((ushort)0x1236, cpu.Registers[2]);
        Assert.Equal((ushort)0x36,   cpu.Registers[3]);
        Assert.Equal((ushort)0x36,   written);
        Assert.Equal((ushort)0xBEEF, cpu.Registers[4]);
        Assert.Equal((byte)0x36,     memory.ReadLogical(0x0012, 0));
    }

    [Fact]
    public void SegmentRegisterMapsHighLogicalAddressesAndStackSupportsCallReturn()
    {
        var memory = new Memory();
        var cpu    = new Cpu(memory, new IoBus());

        WriteProgram(memory,
                     Instruction(2,  rd: 0), 1,
                     Instruction(25, ra: 0), // WRSG R0
                     Instruction(2,  rd: 1),        0xC000,
                     Instruction(2,  rd: 2),        0xBEEF,
                     Instruction(6,  rd: 2, ra: 1), 0,
                     Instruction(4,  rd: 3, ra: 1), 0,
                     Instruction(2,  rd: 4),        0x0340,
                     Instruction(18, ra: 4), // CALL R4
                     Instruction(29));
        WriteProgramAt(memory,                0x0340,
                       Instruction(2, rd: 5), 0xCAFE,
                       Instruction(19));

        ExecuteUntilHalted(cpu);

        Assert.Equal((ushort)0xBEEF,            cpu.Registers[3]);
        Assert.Equal((ushort)0xCAFE,            cpu.Registers[5]);
        Assert.Equal((ushort)1,                 cpu.SG);
        Assert.Equal(Cpu.INITIAL_STACK_POINTER, cpu.SP);
        Assert.Equal((byte)0xEF,                memory.ReadPhysical(0x4000));
        Assert.Equal((byte)0xBE,                memory.ReadPhysical(0x4001));
    }

    [Fact]
    public void IllegalInstructionsReservedBitsAndBadStackFaultAtTheInstruction()
    {
        var memory = new Memory();
        var cpu    = new Cpu(memory, new IoBus());
        memory.WriteLogicalWord(Cpu.INITIAL_PROGRAM_COUNTER, 0, Instruction(31));

        cpu.ExecuteNextInstruction();

        Assert.True(cpu.Halted, $"PC={cpu.PC:X4} SG={cpu.SG:X2} SP={cpu.SP:X4} fault={cpu.FaultCode}");
        Assert.Equal(CpuFaultCode.IllegalOpcode,  cpu.FaultCode);
        Assert.Equal(Cpu.INITIAL_PROGRAM_COUNTER, cpu.FaultingPc);

        cpu.Reset();
        memory.WriteLogicalWord(Cpu.INITIAL_PROGRAM_COUNTER, 0, (ushort)(Instruction(0) | 1));
        cpu.ExecuteNextInstruction();
        Assert.Equal(CpuFaultCode.ReservedBits, cpu.FaultCode);

        cpu.Reset();
        memory.WriteLogicalWord(Cpu.INITIAL_PROGRAM_COUNTER, 0, Instruction(21, rd: 0));
        cpu.ExecuteNextInstruction();
        Assert.Equal(CpuFaultCode.StackAccess, cpu.FaultCode);
    }

    [Fact]
    public void SystemRomIsVisibleAndIgnoresProtectedPhysicalLogicalAndDmaWrites()
    {
        byte[] rom = new byte[Memory.SYSTEM_ROM_LENGTH];
        rom[0] = 0xA5;
        var memory = new Memory(rom);

        memory.WritePhysical(Memory.SYSTEM_ROM_START, 0x11);
        memory.WriteLogical(Memory.SYSTEM_ROM_START, 0, 0x22);
        memory.CreatePhysicalView(Memory.SYSTEM_ROM_START, 1).Write(0, 0x33);

        Assert.Equal((byte)0xA5, memory.ReadPhysical(Memory.SYSTEM_ROM_START));
        Assert.Equal((byte)0xA5, memory.ReadLogical(Memory.SYSTEM_ROM_START, 0));
        Assert.Equal((byte)0xA5, memory.CreatePhysicalView(Memory.SYSTEM_ROM_START, 1).Read(0));
    }

    [Fact]
    public void VideoInterruptWakesHaltedCpuAndIretRestoresTheFrame()
    {
        var machine = new Machine(videoFrameCycles: 13);
        WriteProgram(machine.Memory,
                     Instruction(27),           // EI
                     Instruction(26),        3, // WSGI 3
                     Instruction(2,  rd: 0), (ushort)'A',
                     Instruction(23, ra: 0), CharacterDevice.PortPut,
                     Instruction(2,  rd: 0), (ushort)VideoMode.Indexed256,
                     Instruction(23, ra: 0), 0x20, // OUT present port, R0
                     Instruction(29));
        machine.Memory.WritePhysicalWord((uint)(Cpu.INTERRUPT_VECTOR_TABLE + Machine.VIDEO_INTERRUPT_VECTOR * 2),
                                         0x0040);
        machine.Memory.WritePhysicalWord(0x0040, Instruction(30));

        machine.AdvanceCycles(9);

        Assert.True(machine.Cpu.Halted);
        Assert.True(machine.Cpu.InterruptsEnabled);
        Assert.False(machine.Interrupts.IsPending(Machine.VIDEO_INTERRUPT_VECTOR));
        Assert.Equal((ushort)1, machine.Character.X);

        machine.AdvanceCycles(4);

        Assert.False(machine.Cpu.Halted);
        Assert.Equal((ushort)0x0040, machine.Cpu.PC);
        Assert.False(machine.Cpu.InterruptsEnabled);

        machine.AdvanceCycles(1);

        Assert.Equal((ushort)0x0318, machine.Cpu.PC);
        Assert.True(machine.Cpu.InterruptsEnabled);
        Assert.Equal((byte)3,                   machine.Cpu.SG);
        Assert.Equal(Cpu.INITIAL_STACK_POINTER, machine.Cpu.SP);
        Assert.Equal((ulong)1,                  machine.Video.FrameSerial);
    }

    [Fact]
    public void ExtendedBranchesAndDirectCallProvideCompleteIntegerControlFlow()
    {
        var memory = new Memory();
        var cpu = new Cpu(memory, new IoBus());

        WriteProgram(memory,
                     Instruction(2, rd: 0), 0xFFFF,
                     Instruction(2, rd: 1), 1,
                     Extended(0), RegisterOperands(rd: 0, ra: 0, rb: 1), 2, // BLT R0, R1, +2 words
                     Instruction(2, rd: 2), 0xDEAD,
                     Instruction(2, rd: 2), 0xBEEF,
                     Extended(2), 0x0340, // CALLA 0340h
                     Instruction(29));
        WriteProgramAt(memory,
                       0x0340,
                       Instruction(2, rd: 3), 0xCAFE,
                       Instruction(19));

        ExecuteUntilHalted(cpu);

        Assert.Equal((ushort)0xBEEF, cpu.Registers[2]);
        Assert.Equal((ushort)0xCAFE, cpu.Registers[3]);
        Assert.Equal(Cpu.INITIAL_STACK_POINTER, cpu.SP);
    }

    [Fact]
    public void ExtendedFarInstructionsAccessPhysicalMemoryAndRestoreTheCallerSegment()
    {
        var memory = new Memory();
        var cpu = new Cpu(memory, new IoBus());
        const uint target = 0x23456;
        const uint data = 0xA4000;

        memory.WritePhysical(data, 0xF0);
        WriteProgram(memory,
                     Instruction(26), 3, // WSGI 3
                     Extended(4), (ushort)target, (ushort)(target >> 16), // LCALL 23456h
                     Instruction(2, rd: 4), 0xBEEF,
                     Extended(6), RegisterOperands(rd: 5), (ushort)data, (ushort)(data >> 16), // LDBS R5, A4000h
                     Instruction(2, rd: 6), 0xCAFE,
                     Extended(10), RegisterOperands(rd: 6), (ushort)(data + 2), (ushort)(data >> 16), // LSTW R6, A4002h
                     Instruction(29));
        WriteProgramAt(memory,
                       target,
                       Instruction(2, rd: 0), 0x1234,
                       Extended(5)); // LRET

        ExecuteUntilHalted(cpu);

        Assert.Equal((ushort)0x1234, cpu.Registers[0]);
        Assert.Equal((byte)3, cpu.SG);
        Assert.Equal((ushort)0xBEEF, cpu.Registers[4]);
        Assert.Equal((ushort)0xFFF0, cpu.Registers[5]);
        Assert.Equal((ushort)0xCAFE, memory.ReadPhysicalWord(data + 2));
        Assert.Equal(Cpu.INITIAL_STACK_POINTER, cpu.SP);
    }

    [Fact]
    public void ExtendedIntegerOperationsHaveDefinedSignedAndUnsignedSemantics()
    {
        var memory = new Memory();
        var cpu = new Cpu(memory, new IoBus());

        WriteProgram(memory,
                     Instruction(2, rd: 0), 0xFFF9, // -7
                     Instruction(2, rd: 1), 3,
                     Extended(11), RegisterOperands(rd: 2, ra: 0, rb: 1), // MUL
                     Extended(12), RegisterOperands(rd: 3, ra: 0, rb: 1), // DIV
                     Extended(13), RegisterOperands(rd: 4, ra: 0, rb: 1), // DIVU
                     Extended(14), RegisterOperands(rd: 5, ra: 0, rb: 1), // MOD
                     Extended(16), RegisterOperands(rd: 6, ra: 0),        // NEG
                     Extended(18), RegisterOperands(rd: 7, ra: 1, rb: 1), // ROL
                     Instruction(29));

        ExecuteUntilHalted(cpu);

        Assert.Equal((ushort)0xFFEB, cpu.Registers[2]);
        Assert.Equal((ushort)0xFFFE, cpu.Registers[3]);
        Assert.Equal((ushort)0x5553, cpu.Registers[4]);
        Assert.Equal((ushort)0xFFFF, cpu.Registers[5]);
        Assert.Equal((ushort)7, cpu.Registers[6]);
        Assert.Equal((ushort)0x0018, cpu.Registers[7]);
    }

    [Fact]
    public void DivisionByZeroFaultsTheExtendedInstruction()
    {
        var memory = new Memory();
        var cpu = new Cpu(memory, new IoBus());
        WriteProgram(memory,
                     Instruction(2, rd: 0), 1,
                     Instruction(2, rd: 1), 0,
                     Extended(12), RegisterOperands(rd: 2, ra: 0, rb: 1));

        for (var i = 0; i < 8 && !cpu.Halted; i++)
            cpu.ExecuteNextInstruction();

        Assert.Equal(CpuFaultCode.DivisionByZero, cpu.FaultCode);
        Assert.Equal((ushort)0x0308, cpu.FaultingPc);
    }

    private static ushort Instruction(int opcode, int rd = 0, int ra = 0, int rb = 0)
    {
        return (ushort)((opcode << 11) | (rd << 8) | (ra << 5) | (rb << 2));
    }

    private static ushort Extended(int operation)
    {
        return (ushort)((31 << 11) | operation);
    }

    private static ushort RegisterOperands(int rd = 0, int ra = 0, int rb = 0)
    {
        return (ushort)((rd << 8) | (ra << 5) | (rb << 2));
    }

    private static void WriteProgram(Memory memory, params ushort[] words)
    {
        WriteProgramAt(memory, Cpu.INITIAL_PROGRAM_COUNTER, words);
    }

    private static void WriteProgramAt(Memory memory, uint address, params ushort[] words)
    {
        uint current = address;
        foreach (ushort word in words)
        {
            memory.WritePhysicalWord(current, word);
            current += sizeof(ushort);
        }
    }

    private static void ExecuteUntilHalted(Cpu cpu)
    {
        for (var i = 0; i < 64 && !cpu.Halted; i++)
            cpu.ExecuteNextInstruction();

        Assert.True(cpu.Halted);
        Assert.Equal(CpuFaultCode.None, cpu.FaultCode);
    }
}
