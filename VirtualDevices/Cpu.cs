namespace OldSimulator.VirtualDevices;

public enum CpuFaultCode
{
    None,
    OddProgramCounter,
    IllegalOpcode,
    ReservedBits,
    StackAccess,
    DivisionByZero
}

public enum ExtendedOpcode : ushort
{
    Branch = 0,
    JumpAbsolute = 1,
    CallAbsolute = 2,
    LongJump = 3,
    LongCall = 4,
    LongReturn = 5,
    LongLoadByteSigned = 6,
    LongLoadByteUnsigned = 7,
    LongLoadWord = 8,
    LongStoreByte = 9,
    LongStoreWord = 10,
    Multiply = 11,
    DivideSigned = 12,
    DivideUnsigned = 13,
    ModuloSigned = 14,
    ModuloUnsigned = 15,
    Negate = 16,
    BitwiseNot = 17,
    RotateLeft = 18,
    RotateRight = 19
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
        if (opcode == 31)
            return GetExtendedInstructionCost((ushort)(word & 0x07FF));

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
        int opcode = instruction >> 11;
        if (opcode != 31 && (instruction & 3) != 0)
            return fault(CpuFaultCode.ReservedBits, instructionAddress);

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
            case 31:
                return ExecuteExtended((ushort)(instruction & 0x07FF), instructionAddress);
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

    private ulong ExecuteExtended(ushort operation, ushort instructionAddress)
    {
        switch ((ExtendedOpcode)operation)
        {
            case ExtendedOpcode.Branch:
                if (!TryFetchRegisterOperands(out int condition, out int branchLeft, out int branchRight, instructionAddress))
                    return 1;

                ushort branchOffset = fetchWord();
                if (condition is < 0 or > 5)
                    return fault(CpuFaultCode.ReservedBits, instructionAddress);
                if (ShouldBranch(condition, Registers[branchLeft], Registers[branchRight]))
                    PC = unchecked((ushort)(PC + (short)branchOffset * sizeof(ushort)));
                return 3;

            case ExtendedOpcode.JumpAbsolute:
                PC = fetchWord();
                return 2;

            case ExtendedOpcode.CallAbsolute:
                ushort callTarget = fetchWord();
                if (!canPushWords(1))
                    return fault(CpuFaultCode.StackAccess, instructionAddress);
                pushWord(PC);
                PC = callTarget;
                return 2;

            case ExtendedOpcode.LongJump:
                if (!TryFetchPhysicalAddress(out uint jumpTarget, instructionAddress))
                    return 1;
                SetLongProgramCounter(jumpTarget);
                return 3;

            case ExtendedOpcode.LongCall:
                if (!TryFetchPhysicalAddress(out uint longCallTarget, instructionAddress))
                    return 1;
                if (!canPushWords(2))
                    return fault(CpuFaultCode.StackAccess, instructionAddress);
                pushWord(SG);
                pushWord(PC);
                SetLongProgramCounter(longCallTarget);
                return 3;

            case ExtendedOpcode.LongReturn:
                if (!canPopWords(2))
                    return fault(CpuFaultCode.StackAccess, instructionAddress);
                tryPopWord(out ushort longReturnPc);
                tryPopWord(out ushort longReturnSg);
                PC = longReturnPc;
                SG = (byte)(longReturnSg & 0x3F);
                return 1;

            case ExtendedOpcode.LongLoadByteSigned:
                if (!TryFetchRegisterOperand(out int signedByteDestination, instructionAddress)
                    || !TryFetchPhysicalAddress(out uint signedByteAddress, instructionAddress))
                    return 1;
                Registers[signedByteDestination] = unchecked((ushort)(short)(sbyte)memory.ReadPhysical(signedByteAddress));
                return 4;

            case ExtendedOpcode.LongLoadByteUnsigned:
                if (!TryFetchRegisterOperand(out int unsignedByteDestination, instructionAddress)
                    || !TryFetchPhysicalAddress(out uint unsignedByteAddress, instructionAddress))
                    return 1;
                Registers[unsignedByteDestination] = memory.ReadPhysical(unsignedByteAddress);
                return 4;

            case ExtendedOpcode.LongLoadWord:
                if (!TryFetchRegisterOperand(out int wordDestination, instructionAddress)
                    || !TryFetchPhysicalAddress(out uint wordAddress, instructionAddress))
                    return 1;
                Registers[wordDestination] = memory.ReadPhysicalWord(wordAddress);
                return 4;

            case ExtendedOpcode.LongStoreByte:
                if (!TryFetchRegisterOperand(out int byteSource, instructionAddress)
                    || !TryFetchPhysicalAddress(out uint byteStoreAddress, instructionAddress))
                    return 1;
                memory.WritePhysical(byteStoreAddress, (byte)Registers[byteSource]);
                return 4;

            case ExtendedOpcode.LongStoreWord:
                if (!TryFetchRegisterOperand(out int wordSource, instructionAddress)
                    || !TryFetchPhysicalAddress(out uint wordStoreAddress, instructionAddress))
                    return 1;
                memory.WritePhysicalWord(wordStoreAddress, Registers[wordSource]);
                return 4;

            case ExtendedOpcode.Multiply:
                if (!TryFetchRegisterOperands(out int multiplyDestination, out int multiplyLeft, out int multiplyRight, instructionAddress))
                    return 1;
                Registers[multiplyDestination] = unchecked((ushort)((short)Registers[multiplyLeft] * (short)Registers[multiplyRight]));
                return 2;

            case ExtendedOpcode.DivideSigned:
                if (!TryFetchRegisterOperands(out int signedDivideDestination, out int signedDividend, out int signedDivisor, instructionAddress))
                    return 1;
                if (Registers[signedDivisor] == 0)
                    return fault(CpuFaultCode.DivisionByZero, instructionAddress);
                Registers[signedDivideDestination] = unchecked((ushort)((short)Registers[signedDividend] / (short)Registers[signedDivisor]));
                return 2;

            case ExtendedOpcode.DivideUnsigned:
                if (!TryFetchRegisterOperands(out int unsignedDivideDestination, out int unsignedDividend, out int unsignedDivisor, instructionAddress))
                    return 1;
                if (Registers[unsignedDivisor] == 0)
                    return fault(CpuFaultCode.DivisionByZero, instructionAddress);
                Registers[unsignedDivideDestination] = (ushort)(Registers[unsignedDividend] / Registers[unsignedDivisor]);
                return 2;

            case ExtendedOpcode.ModuloSigned:
                if (!TryFetchRegisterOperands(out int signedModuloDestination, out int signedModuloDividend, out int signedModuloDivisor, instructionAddress))
                    return 1;
                if (Registers[signedModuloDivisor] == 0)
                    return fault(CpuFaultCode.DivisionByZero, instructionAddress);
                Registers[signedModuloDestination] = unchecked((ushort)((short)Registers[signedModuloDividend] % (short)Registers[signedModuloDivisor]));
                return 2;

            case ExtendedOpcode.ModuloUnsigned:
                if (!TryFetchRegisterOperands(out int unsignedModuloDestination, out int unsignedModuloDividend, out int unsignedModuloDivisor, instructionAddress))
                    return 1;
                if (Registers[unsignedModuloDivisor] == 0)
                    return fault(CpuFaultCode.DivisionByZero, instructionAddress);
                Registers[unsignedModuloDestination] = (ushort)(Registers[unsignedModuloDividend] % Registers[unsignedModuloDivisor]);
                return 2;

            case ExtendedOpcode.Negate:
                if (!TryFetchUnaryOperands(out int negateDestination, out int negateSource, instructionAddress))
                    return 1;
                Registers[negateDestination] = unchecked((ushort)-(short)Registers[negateSource]);
                return 2;

            case ExtendedOpcode.BitwiseNot:
                if (!TryFetchUnaryOperands(out int notDestination, out int notSource, instructionAddress))
                    return 1;
                Registers[notDestination] = unchecked((ushort)~Registers[notSource]);
                return 2;

            case ExtendedOpcode.RotateLeft:
                if (!TryFetchRegisterOperands(out int leftRotateDestination, out int leftRotateSource, out int leftRotateAmount, instructionAddress))
                    return 1;
                Registers[leftRotateDestination] = RotateLeft(Registers[leftRotateSource], Registers[leftRotateAmount]);
                return 2;

            case ExtendedOpcode.RotateRight:
                if (!TryFetchRegisterOperands(out int rightRotateDestination, out int rightRotateSource, out int rightRotateAmount, instructionAddress))
                    return 1;
                Registers[rightRotateDestination] = RotateRight(Registers[rightRotateSource], Registers[rightRotateAmount]);
                return 2;

            default:
                return fault(CpuFaultCode.IllegalOpcode, instructionAddress);
        }
    }

    private bool TryFetchRegisterOperands(out int rd, out int ra, out int rb, ushort instructionAddress)
    {
        ushort operands = fetchWord();
        rd = (operands >> 8) & 7;
        ra = (operands >> 5) & 7;
        rb = (operands >> 2) & 7;
        if ((operands & 3) == 0)
            return true;

        fault(CpuFaultCode.ReservedBits, instructionAddress);
        return false;
    }

    private bool TryFetchUnaryOperands(out int rd, out int ra, ushort instructionAddress)
    {
        if (!TryFetchRegisterOperands(out rd, out ra, out int rb, instructionAddress))
            return false;
        if (rb == 0)
            return true;

        fault(CpuFaultCode.ReservedBits, instructionAddress);
        return false;
    }

    private bool TryFetchRegisterOperand(out int register, ushort instructionAddress)
    {
        ushort operands = fetchWord();
        register = (operands >> 8) & 7;
        if ((operands & 0xFF) == 0)
            return true;

        fault(CpuFaultCode.ReservedBits, instructionAddress);
        return false;
    }

    private bool TryFetchPhysicalAddress(out uint address, ushort instructionAddress)
    {
        ushort low = fetchWord();
        ushort high = fetchWord();
        if ((high & 0xFFF0) == 0)
        {
            address = (uint)(low | (high << 16));
            return true;
        }

        address = 0;
        fault(CpuFaultCode.ReservedBits, instructionAddress);
        return false;
    }

    private void SetLongProgramCounter(uint physicalAddress)
    {
        SG = (byte)(physicalAddress >> 14);
        PC = (ushort)(0xC000 | (physicalAddress & 0x3FFF));
    }

    private static bool ShouldBranch(int condition, ushort left, ushort right)
    {
        return condition switch
        {
            0 => (short)left < (short)right,
            1 => (short)left >= (short)right,
            2 => left < right,
            3 => left >= right,
            4 => (short)left <= (short)right,
            5 => (short)left > (short)right,
            _ => false
        };
    }

    private static ushort RotateLeft(ushort value, ushort amount)
    {
        int count = amount & 0xF;
        return count == 0 ? value : (ushort)((value << count) | (value >> (16 - count)));
    }

    private static ushort RotateRight(ushort value, ushort amount)
    {
        int count = amount & 0xF;
        return count == 0 ? value : (ushort)((value >> count) | (value << (16 - count)));
    }

    private static ulong GetExtendedInstructionCost(ushort operation)
    {
        return (ExtendedOpcode)operation switch
        {
            ExtendedOpcode.Branch => 3,
            ExtendedOpcode.JumpAbsolute or ExtendedOpcode.CallAbsolute => 2,
            ExtendedOpcode.LongJump or ExtendedOpcode.LongCall => 3,
            ExtendedOpcode.LongReturn => 1,
            ExtendedOpcode.LongLoadByteSigned or ExtendedOpcode.LongLoadByteUnsigned or ExtendedOpcode.LongLoadWord
                or ExtendedOpcode.LongStoreByte or ExtendedOpcode.LongStoreWord => 4,
            ExtendedOpcode.Multiply or ExtendedOpcode.DivideSigned or ExtendedOpcode.DivideUnsigned
                or ExtendedOpcode.ModuloSigned or ExtendedOpcode.ModuloUnsigned or ExtendedOpcode.Negate
                or ExtendedOpcode.BitwiseNot or ExtendedOpcode.RotateLeft or ExtendedOpcode.RotateRight => 2,
            _ => 1
        };
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
