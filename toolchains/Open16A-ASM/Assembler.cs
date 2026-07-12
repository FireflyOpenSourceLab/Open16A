using System.Globalization;
using System.Text;

namespace Open16A.Asm;

public sealed record AssemblyResult(uint Origin, byte[] Bytes);

public sealed class AssemblyException : Exception
{
    public AssemblyException(int line, string message) : base($"Line {line}: {message}") { }
}

public sealed class Assembler
{
    private readonly Dictionary<string, uint> labels = new(StringComparer.OrdinalIgnoreCase);

    public AssemblyResult Assemble(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        labels.Clear();
        List<Line> lines = Parse(source);
        uint origin = 0;
        uint address = 0;
        bool hasOutput = false;

        foreach (Line line in lines)
        {
            if (line.Label is not null && !labels.TryAdd(line.Label, address))
                throw Error(line, $"Duplicate label '{line.Label}'.");
            if (line.Body is null)
                continue;

            if (Mnemonic(line.Body) == ".ORG")
            {
                if (hasOutput)
                    throw Error(line, ".org must appear before output.");
                address = origin = Physical(Value(Arguments(line), 0, line), line);
                continue;
            }

            int length = Length(line);
            if ((ulong)address + (uint)length > 0x1_00000)
                throw Error(line, "Output exceeds the 1 MiB physical address space.");
            address += (uint)length;
            hasOutput = true;
        }

        var bytes = new List<byte>(checked((int)(address - origin)));
        address = origin;
        foreach (Line line in lines)
        {
            if (line.Body is null || Mnemonic(line.Body) == ".ORG")
                continue;
            Emit(line, address, bytes);
            address = origin + (uint)bytes.Count;
        }

        return new AssemblyResult(origin, [.. bytes]);
    }

    private List<Line> Parse(string source)
    {
        var lines = new List<Line>();
        string[] rawLines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var number = 1; number <= rawLines.Length; number++)
        {
            string text = WithoutComment(rawLines[number - 1]).Trim();
            if (text.Length == 0)
                continue;
            string? label = null;
            int separator = text.IndexOf(':');
            if (separator >= 0)
            {
                label = text[..separator].Trim();
                if (!Identifier(label))
                    throw new AssemblyException(number, $"Invalid label '{label}'.");
                text = text[(separator + 1)..].Trim();
            }
            lines.Add(new Line(number, label, text.Length == 0 ? null : text));
        }
        return lines;
    }

    private int Length(Line line)
    {
        string mnemonic = Mnemonic(line.Body!);
        List<string> operands = Arguments(line);
        return mnemonic switch
        {
            ".BYTE" => operands.Count,
            ".WORD" => checked(operands.Count * 2),
            "NOP" or "MOV" or "ADD" or "SUB" or "AND" or "OR" or "XOR" or "SHL" or "SHR" or "SAR"
                or "JMP" or "CALL" or "RET" or "PUSH" or "POP" or "RDSG" or "WRSG" or "EI" or "DI"
                or "HALT" or "IRET" or "RETL" => 2,
            "LI" or "LD.BU" or "LD.W" or "ST.B" or "ST.W" or "BEQ" or "BNE" or "IN" or "OUT" or "WSGI"
                or "JMPA" or "CALLA" or "MUL" or "DIV" or "DIVU" or "MOD" or "MODU" or "NEG" or "NOT"
                or "ROL" or "ROR" => 4,
            "BLT" or "BGE" or "BLO" or "BHS" or "BLE" or "BGT" or "JMPL" or "CALLL" => 6,
            "LDBS" or "LDBU" or "LDW" or "LSTB" or "LSTW" => 8,
            _ => throw Error(line, $"Unknown instruction or directive '{mnemonic}'.")
        };
    }

    private void Emit(Line line, uint address, List<byte> output)
    {
        string mnemonic = Mnemonic(line.Body!);
        List<string> op = Arguments(line);
        if (mnemonic == ".BYTE")
        {
            foreach (string value in op) Byte(output, ByteValue(Resolve(value, line), line));
            return;
        }
        if (mnemonic == ".WORD")
        {
            foreach (string value in op) Word(output, WordValue(Resolve(value, line), line));
            return;
        }

        switch (mnemonic)
        {
            case "NOP": Basic(output, 0); break;
            case "MOV": Basic(output, 1, Rd(op, line), Ra(op, line)); break;
            case "LI": Basic(output, 2, Rd(op, line)); Word(output, WordValue(Value(op, 1, line), line)); break;
            case "LD.BU": Memory(output, 3, op, line); break;
            case "LD.W": Memory(output, 4, op, line); break;
            case "ST.B": Memory(output, 5, op, line); break;
            case "ST.W": Memory(output, 6, op, line); break;
            case "ADD": Basic(output, 7, Rd(op, line), Ra(op, line), Rb(op, line)); break;
            case "SUB": Basic(output, 8, Rd(op, line), Ra(op, line), Rb(op, line)); break;
            case "AND": Basic(output, 9, Rd(op, line), Ra(op, line), Rb(op, line)); break;
            case "OR": Basic(output, 10, Rd(op, line), Ra(op, line), Rb(op, line)); break;
            case "XOR": Basic(output, 11, Rd(op, line), Ra(op, line), Rb(op, line)); break;
            case "SHL": Basic(output, 12, Rd(op, line), Ra(op, line), Rb(op, line)); break;
            case "SHR": Basic(output, 13, Rd(op, line), Ra(op, line), Rb(op, line)); break;
            case "SAR": Basic(output, 14, Rd(op, line), Ra(op, line), Rb(op, line)); break;
            case "BEQ": Branch(output, 15, op, address, line); break;
            case "BNE": Branch(output, 16, op, address, line); break;
            case "JMP": Basic(output, 17, ra: Ra(op, line)); break;
            case "CALL": Basic(output, 18, ra: Ra(op, line)); break;
            case "RET": Basic(output, 19); break;
            case "PUSH": Basic(output, 20, ra: Ra(op, line)); break;
            case "POP": Basic(output, 21, rd: Rd(op, line)); break;
            case "IN": Basic(output, 22, rd: Rd(op, line)); Word(output, WordValue(Value(op, 1, line), line)); break;
            case "OUT": Basic(output, 23, ra: Register(Operand(op, 1, line), line)); Word(output, WordValue(Value(op, 0, line), line)); break;
            case "RDSG": Basic(output, 24, rd: Rd(op, line)); break;
            case "WRSG": Basic(output, 25, ra: Ra(op, line)); break;
            case "WSGI": Basic(output, 26); Word(output, WordValue(Value(op, 0, line), line)); break;
            case "EI": Basic(output, 27); break;
            case "DI": Basic(output, 28); break;
            case "HALT": Basic(output, 29); break;
            case "IRET": Basic(output, 30); break;
            case "BLT": ExtendedBranch(output, 0, op, address, line); break;
            case "BGE": ExtendedBranch(output, 1, op, address, line); break;
            case "BLO": ExtendedBranch(output, 2, op, address, line); break;
            case "BHS": ExtendedBranch(output, 3, op, address, line); break;
            case "BLE": ExtendedBranch(output, 4, op, address, line); break;
            case "BGT": ExtendedBranch(output, 5, op, address, line); break;
            case "JMPA": Extended(output, 1); Word(output, WordValue(Value(op, 0, line), line)); break;
            case "CALLA": Extended(output, 2); Word(output, WordValue(Value(op, 0, line), line)); break;
            case "JMPL": LongControl(output, 3, op, line); break;
            case "CALLL": LongControl(output, 4, op, line); break;
            case "RETL": Extended(output, 5); break;
            case "LDBS": LongMemory(output, 6, op, line); break;
            case "LDBU": LongMemory(output, 7, op, line); break;
            case "LDW": LongMemory(output, 8, op, line); break;
            case "LSTB": LongMemory(output, 9, op, line); break;
            case "LSTW": LongMemory(output, 10, op, line); break;
            case "MUL": ExtendedRegisters(output, 11, op, line); break;
            case "DIV": ExtendedRegisters(output, 12, op, line); break;
            case "DIVU": ExtendedRegisters(output, 13, op, line); break;
            case "MOD": ExtendedRegisters(output, 14, op, line); break;
            case "MODU": ExtendedRegisters(output, 15, op, line); break;
            case "NEG": ExtendedRegisters(output, 16, op, line, true); break;
            case "NOT": ExtendedRegisters(output, 17, op, line, true); break;
            case "ROL": ExtendedRegisters(output, 18, op, line); break;
            case "ROR": ExtendedRegisters(output, 19, op, line); break;
            default: throw Error(line, $"Unknown instruction '{mnemonic}'.");
        }
    }

    private void Memory(List<byte> output, int opcode, List<string> op, Line line)
    {
        (int ra, long offset) = MemoryOperand(Operand(op, 1, line), line);
        if (offset is < short.MinValue or > short.MaxValue)
            throw Error(line, "Memory displacement must fit signed 16 bits.");
        Basic(output, opcode, Rd(op, line), ra);
        Word(output, unchecked((ushort)(short)offset));
    }

    private void Branch(List<byte> output, int opcode, List<string> op, uint address, Line line)
    {
        Basic(output, opcode, ra: Register(Operand(op, 0, line), line), rb: Register(Operand(op, 1, line), line));
        Word(output, Relative(Value(op, 2, line), address + 4, line));
    }

    private void ExtendedBranch(List<byte> output, int condition, List<string> op, uint address, Line line)
    {
        Extended(output, 0);
        Descriptor(output, condition, Register(Operand(op, 0, line), line), Register(Operand(op, 1, line), line));
        Word(output, Relative(Value(op, 2, line), address + 6, line));
    }

    private void LongControl(List<byte> output, int selector, List<string> op, Line line)
    {
        Extended(output, selector);
        PhysicalAddress(output, Value(op, 0, line), line);
    }

    private void LongMemory(List<byte> output, int selector, List<string> op, Line line)
    {
        Extended(output, selector);
        Descriptor(output, Rd(op, line), 0, 0);
        string operand = Operand(op, 1, line).Trim();
        if (operand.StartsWith('[') && operand.EndsWith(']')) operand = operand[1..^1];
        PhysicalAddress(output, Resolve(operand, line), line);
    }

    private static void ExtendedRegisters(List<byte> output, int selector, List<string> op, Line line, bool unary = false)
    {
        Extended(output, selector);
        Descriptor(output, Rd(op, line), Ra(op, line), unary ? 0 : Rb(op, line));
    }

    private static void Basic(List<byte> output, int opcode, int rd = 0, int ra = 0, int rb = 0) =>
        Word(output, (ushort)((opcode << 11) | (rd << 8) | (ra << 5) | (rb << 2)));
    private static void Extended(List<byte> output, int selector) => Word(output, (ushort)(0xF800 | selector));
    private static void Descriptor(List<byte> output, int rd, int ra, int rb) => Word(output, (ushort)((rd << 8) | (ra << 5) | (rb << 2)));
    private static void Word(List<byte> output, ushort value) { output.Add((byte)(value >> 8)); output.Add((byte)value); }
    private static void Byte(List<byte> output, byte value) => output.Add(value);

    private void PhysicalAddress(List<byte> output, long value, Line line)
    {
        uint address = Physical(value, line);
        Word(output, (ushort)address);
        Word(output, (ushort)(address >> 16));
    }

    private static ushort Relative(long target, uint next, Line line)
    {
        long delta = target - next;
        if ((delta & 1) != 0 || delta is < -65536 or > 65534)
            throw Error(line, "Branch target must be an even address within rel16 range.");
        return unchecked((ushort)(short)(delta / 2));
    }

    private long Value(List<string> operands, int index, Line line) => Resolve(Operand(operands, index, line), line);

    private long Resolve(string text, Line line)
    {
        text = text.Trim();
        int operation = AddSubtract(text);
        if (operation > 0)
        {
            long left = Resolve(text[..operation], line);
            long right = Resolve(text[(operation + 1)..], line);
            return text[operation] == '+' ? left + right : left - right;
        }
        if (text.Length == 3 && text[0] == '\'' && text[2] == '\'') return text[1];
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return Parse(text[2..], NumberStyles.AllowHexSpecifier, line);
        if (text.EndsWith("h", StringComparison.OrdinalIgnoreCase)) return Parse(text[..^1], NumberStyles.AllowHexSpecifier, line);
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long decimalValue)) return decimalValue;
        if (labels.TryGetValue(text, out uint label)) return label;
        throw Error(line, $"Unknown symbol or invalid value '{text}'.");
    }

    private static long Parse(string text, NumberStyles style, Line line)
    {
        if (long.TryParse(text, style, CultureInfo.InvariantCulture, out long value)) return value;
        throw Error(line, $"Invalid numeric value '{text}'.");
    }

    private (int Register, long Offset) MemoryOperand(string text, Line line)
    {
        text = text.Trim();
        if (!text.StartsWith('[') || !text.EndsWith(']')) throw Error(line, "Memory operand must be [Ra + disp16].");
        text = text[1..^1].Trim();
        int operation = AddSubtract(text);
        if (operation < 0) return (Register(text, line), 0);
        int register = Register(text[..operation], line);
        string offset = text[(operation + 1)..];
        long value = Resolve(offset, line);
        return (register, text[operation] == '-' ? -value : value);
    }

    private static int AddSubtract(string text)
    {
        bool character = false;
        for (var index = 1; index < text.Length; index++)
        {
            if (text[index] == '\'') character = !character;
            if (!character && text[index] is '+' or '-') return index;
        }
        return -1;
    }

    private static int Rd(List<string> op, Line line) => Register(Operand(op, 0, line), line);
    private static int Ra(List<string> op, Line line) => Register(Operand(op, op.Count == 1 ? 0 : 1, line), line);
    private static int Rb(List<string> op, Line line) => Register(Operand(op, 2, line), line);
    private static string Operand(List<string> op, int index, Line line) => index < op.Count ? op[index] : throw Error(line, "Not enough operands.");

    private static int Register(string text, Line line)
    {
        text = text.Trim();
        if (text.Length == 2 && text[0] is 'R' or 'r' && text[1] is >= '0' and <= '7') return text[1] - '0';
        throw Error(line, $"Expected R0-R7, got '{text}'.");
    }

    private static ushort WordValue(long value, Line line)
    {
        if (value is < short.MinValue or > ushort.MaxValue) throw Error(line, "Value must fit a 16-bit word.");
        return unchecked((ushort)value);
    }
    private static byte ByteValue(long value, Line line)
    {
        if (value is < sbyte.MinValue or > byte.MaxValue) throw Error(line, "Value must fit a byte.");
        return unchecked((byte)value);
    }
    private static uint Physical(long value, Line line)
    {
        if (value is < 0 or >= 0x1_00000) throw Error(line, "Physical address must be within 00000h-FFFFFh.");
        return (uint)value;
    }

    private static string Mnemonic(string body)
    {
        int space = body.IndexOfAny([' ', '\t']);
        return (space < 0 ? body : body[..space]).ToUpperInvariant();
    }
    private static List<string> Arguments(Line line) => Split(Operands(line.Body!));
    private static string Operands(string body)
    {
        int space = body.IndexOfAny([' ', '\t']);
        return space < 0 ? string.Empty : body[(space + 1)..].Trim();
    }
    private static List<string> Split(string text)
    {
        var result = new List<string>(); var value = new StringBuilder(); int brackets = 0; bool character = false;
        foreach (char c in text)
        {
            if (c == '\'') character = !character;
            if (!character && c == '[') brackets++;
            if (!character && c == ']') brackets--;
            if (c == ',' && !character && brackets == 0) { result.Add(value.ToString().Trim()); value.Clear(); }
            else value.Append(c);
        }
        if (value.Length != 0) result.Add(value.ToString().Trim());
        return result;
    }
    private static string WithoutComment(string text)
    {
        bool character = false;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\'') character = !character;
            if (!character && text[index] == ';') return text[..index];
            if (!character && text[index] == '/' && index + 1 < text.Length && text[index + 1] == '/') return text[..index];
        }
        return text;
    }
    private static bool Identifier(string value) => value.Length != 0 && (char.IsLetter(value[0]) || value[0] is '_' or '.') && value.All(c => char.IsLetterOrDigit(c) || c is '_' or '.');
    private static AssemblyException Error(Line line, string message) => new(line.Number, message);
    private sealed record Line(int Number, string? Label, string? Body);
}
