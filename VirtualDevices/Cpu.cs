namespace OldSimulator.VirtualDevices;

public enum CpuFaultCode
{
    None,
    OddProgramCounter,
    IllegalOpcode,
    ReservedBits,
    StackAccess
}

public sealed class Cpu
{
    public const ushort INTERRUPT_VECTOR_TABLE  = 0x0010;
    public const ushort INITIAL_PROGRAM_COUNTER = 0x0300;
    public const ushort INITIAL_STACK_POINTER   = 0xBFFF;
    public const ushort INTERRUPT_ENABLE_FLAG   = 1;

    private readonly Memory memory;
    private readonly IoBus  ioBus;
    private          byte   sg;

    public Cpu(Memory memory, IoBus ioBus)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        this.ioBus  = ioBus ?? throw new ArgumentNullException(nameof(ioBus));
        Reset();
    }

    public ushort[] Registers { get; } = new ushort[8];

    // These registers are storage only until floating-point instructions are defined.
    public uint[] FloatingPointRegisters { get; } = new uint[4];

    public ushort PC { get; set; }
    public ushort SP { get; set; }

    public byte SG
    {
        get => sg;
        set => sg = (byte)(value & 0x3F);
    }

    public ushort       SR                { get; set; }
    public bool         Halted            { get; private set; }
    public CpuFaultCode FaultCode         { get; private set; }
    public ushort       FaultingPc        { get; private set; }
    public bool         HasFault          => FaultCode != CpuFaultCode.None;
    public bool         InterruptsEnabled => (SR & INTERRUPT_ENABLE_FLAG) != 0;

    public void Reset()
    {
        Array.Clear(Registers);
        Array.Clear(FloatingPointRegisters);
        PC         = INITIAL_PROGRAM_COUNTER;
        SP         = INITIAL_STACK_POINTER;
        SG         = 0;
        SR         = 0;
        Halted     = false;
        FaultCode  = CpuFaultCode.None;
        FaultingPc = 0;
    }

    public ulong PeekNextInstructionCost()
    {
        if (Halted || (PC & 1) != 0)
            return 1;

        ushort word   = memory.ReadLogicalWord(PC, SG);
        int    opcode = word >> 11;
        return opcode is 3 or 4 or 5 or 6 or 20 or 21 or 22 or 23 ? 2UL : 1UL;
    }

    public ulong ExecuteNextInstruction()
    {
        if (Halted)
            return 0;

        ushort instructionAddress = PC;
        if ((instructionAddress & 1) != 0)
            return fault(CpuFaultCode.OddProgramCounter, instructionAddress);

        ushort instruction = fetchWord();
        if ((instruction & 3) != 0)
            return fault(CpuFaultCode.ReservedBits, instructionAddress);

        int   opcode = instruction >> 11;
        int   rd     = (instruction >> 8) & 7;
        int   ra     = (instruction >> 5) & 7;
        int   rb     = (instruction >> 2) & 7;
        ulong cost   = opcode is 3 or 4 or 5 or 6 or 20 or 21 or 22 or 23 ? 2UL : 1UL;

        switch (opcode)
        {
            case 0: // NOP
                break;
            case 1: // MOV
                Registers[rd] = Registers[ra];
                break;
            case 2: // LI
                Registers[rd] = fetchWord();
                break;
            case 3: // LD.BU
                Registers[rd] = memory.ReadLogical(addressWithOffset(Registers[ra], fetchWord()), SG);
                break;
            case 4: // LD.W
                Registers[rd] = memory.ReadLogicalWord(addressWithOffset(Registers[ra], fetchWord()), SG);
                break;
            case 5: // ST.B
                memory.WriteLogical(addressWithOffset(Registers[ra], fetchWord()), SG, (byte)Registers[rd]);
                break;
            case 6: // ST.W
                memory.WriteLogicalWord(addressWithOffset(Registers[ra], fetchWord()), SG, Registers[rd]);
                break;
            case 7:
                Registers[rd] = unchecked((ushort)(Registers[ra] + Registers[rb]));
                break;
            case 8:
                Registers[rd] = unchecked((ushort)(Registers[ra] - Registers[rb]));
                break;
            case 9:
                Registers[rd] = (ushort)(Registers[ra] & Registers[rb]);
                break;
            case 10:
                Registers[rd] = (ushort)(Registers[ra] | Registers[rb]);
                break;
            case 11:
                Registers[rd] = (ushort)(Registers[ra] ^ Registers[rb]);
                break;
            case 12:
                Registers[rd] = unchecked((ushort)(Registers[ra] << (Registers[rb] & 0xF)));
                break;
            case 13:
                Registers[rd] = (ushort)(Registers[ra] >> (Registers[rb] & 0xF));
                break;
            case 14:
                Registers[rd] = unchecked((ushort)((short)Registers[ra] >> (Registers[rb] & 0xF)));
                break;
            case 15: // BEQ
                branch(Registers[ra] == Registers[rb], fetchWord());
                break;
            case 16: // BNE
                branch(Registers[ra] != Registers[rb], fetchWord());
                break;
            case 17: // JMP
                PC = Registers[ra];
                break;
            case 18: // CALL
                if (!pushWord(PC))
                    return cost;
                PC = Registers[ra];
                break;
            case 19: // RET
                if (!tryPopWord(out ushort returnAddress))
                    return cost;
                PC = returnAddress;
                break;
            case 20: // PUSH
                pushWord(Registers[ra]);
                break;
            case 21: // POP
                if (tryPopWord(out ushort popped))
                    Registers[rd] = popped;
                break;
            case 22: // IN
                Registers[rd] = ioBus.Read(fetchWord());
                break;
            case 23: // OUT
                ioBus.Write(fetchWord(), Registers[ra]);
                break;
            case 24: // RDSG
                Registers[rd] = SG;
                break;
            case 25: // WRSG
                SG = (byte)(Registers[ra] & 0x3F);
                break;
            case 26: // WSGI
                SG = (byte)(fetchWord() & 0x3F);
                break;
            case 27: // EI
                SR |= INTERRUPT_ENABLE_FLAG;
                break;
            case 28: // DI
                SR &= unchecked((ushort)~INTERRUPT_ENABLE_FLAG);
                break;
            case 29: // HALT
                Halted = true;
                break;
            case 30: // IRET
                if (!canPopWords(3))
                {
                    fault(CpuFaultCode.StackAccess, PC);
                    return cost;
                }

                tryPopWord(out ushort restoredSg);
                tryPopWord(out ushort restoredSr);
                tryPopWord(out ushort restoredPc);
                SG = (byte)(restoredSg & 0x3F);
                SR = restoredSr;
                PC = restoredPc;
                break;
            default:
                return fault(CpuFaultCode.IllegalOpcode, instructionAddress);
        }

        return cost;
    }

    public bool TryEnterInterrupt(byte vector)
    {
        if (!InterruptsEnabled)
            return false;

        // Preflight avoids leaving a partial interrupt frame on a stack fault.
        if (!canPushWords(3))
        {
            fault(CpuFaultCode.StackAccess, PC);
            return false;
        }

        pushWord(PC);
        pushWord(SR);
        pushWord(SG);
        SR     &= unchecked((ushort)~INTERRUPT_ENABLE_FLAG);
        PC     =  memory.ReadPhysicalWord((uint)(INTERRUPT_VECTOR_TABLE + vector * sizeof(ushort)));
        Halted =  false;
        return true;
    }

    private ushort fetchWord()
    {
        ushort value = memory.ReadLogicalWord(PC, SG);
        PC = unchecked((ushort)(PC + sizeof(ushort)));
        return value;
    }

    private void branch(bool condition, ushort rawOffset)
    {
        if (condition)
            PC = unchecked((ushort)(PC + ((short)rawOffset * sizeof(ushort))));
    }

    private static ushort addressWithOffset(ushort address, ushort rawOffset)
    {
        return unchecked((ushort)(address + (short)rawOffset));
    }

    private bool pushWord(ushort value)
    {
        if (!canPushWords(1))
        {
            fault(CpuFaultCode.StackAccess, PC);
            return false;
        }

        SP = unchecked((ushort)(SP - sizeof(ushort)));
        memory.WritePhysicalWord(SP, value);
        return true;
    }

    private bool tryPopWord(out ushort value)
    {
        if (SP > INITIAL_STACK_POINTER - 1)
        {
            value = 0;
            fault(CpuFaultCode.StackAccess, PC);
            return false;
        }

        value = memory.ReadPhysicalWord(SP);
        SP    = unchecked((ushort)(SP + sizeof(ushort)));
        return true;
    }

    private bool canPushWords(int count)
    {
        int destination = SP - count * sizeof(ushort);
        return destination >= 0 && destination + sizeof(ushort) - 1 <= INITIAL_STACK_POINTER;
    }

    private bool canPopWords(int count)
    {
        return SP <= INITIAL_STACK_POINTER - 1 && SP + count * sizeof(ushort) - 1 <= INITIAL_STACK_POINTER;
    }

    private ulong fault(CpuFaultCode code, ushort faultingPc)
    {
        FaultCode  = code;
        FaultingPc = faultingPc;
        Halted     = true;
        return 1;
    }
}
