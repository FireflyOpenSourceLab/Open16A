using Open16A.Asm;
using System.Globalization;

namespace Open16A.Lsp;

public sealed record TextPosition(int Line, int Character);
public sealed record TextRange(TextPosition Start, TextPosition End);
public sealed record DiagnosticInfo(TextRange Range, string Message, int Severity = 1);
public sealed record LabelInfo(string Name, TextRange Range, int Address);

public sealed class LanguageDocument
{
    public static readonly string[] Mnemonics =
    [
        "NOP", "MOV", "LI", "LD.BU", "LD.W", "ST.B", "ST.W", "ADD", "SUB", "AND", "OR", "XOR", "SHL", "SHR", "SAR",
        "BEQ", "BNE", "JMP", "CALL", "RET", "PUSH", "POP", "IN", "OUT", "RDSG", "WRSG", "WSGI", "EI", "DI", "HALT", "IRET",
        "BLT", "BGE", "BLO", "BHS", "BLE", "BGT", "JMPA", "CALLA", "JMPL", "CALLL", "RETL", "LDBS", "LDBU", "LDW", "LSTB", "LSTW",
        "MUL", "DIV", "DIVU", "MOD", "MODU", "NEG", "NOT", "ROL", "ROR"
        , "FLI", "FMOV", "FLD", "FST", "FADD", "FSUB", "FMUL", "FDIV", "FNEG", "FABS", "FCMP"
        , "IFPLI", "IFPADD", "IFPSUB", "IFPAND", "IFPOR", "IFPXOR", "IFPNOT", "IFPSHL", "IFPSHR", "IFPSAR", "IFPROL", "IFPROR"
    ];

    private static readonly IReadOnlyDictionary<string, string> Documentation = Mnemonics.ToDictionary(
        mnemonic => mnemonic,
        DescribeInstruction,
        StringComparer.OrdinalIgnoreCase);

    public LanguageDocument(string uri, string text)
    {
        Uri = uri;
        Text = text;
        Lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        Labels = ScanLabels();
        Diagnostics = ScanDiagnostics();
    }

    public string Uri { get; }
    public string Text { get; }
    public IReadOnlyList<string> Lines { get; }
    public IReadOnlyDictionary<string, LabelInfo> Labels { get; }
    public IReadOnlyList<DiagnosticInfo> Diagnostics { get; }

    public string? TokenAt(TextPosition position)
    {
        if (position.Line < 0 || position.Line >= Lines.Count)
            return null;
        string line = Lines[position.Line];
        if (line.Length == 0)
            return null;
        int index = Math.Clamp(position.Character, 0, line.Length - 1);
        if (!IsTokenCharacter(line[index]) && index > 0 && IsTokenCharacter(line[index - 1]))
            index--;
        if (!IsTokenCharacter(line[index]))
            return null;

        int start = index;
        int end = index + 1;
        while (start > 0 && IsTokenCharacter(line[start - 1])) start--;
        while (end < line.Length && IsTokenCharacter(line[end])) end++;
        return line[start..end];
    }

    public TextRange? Definition(string token)
    {
        return Labels.TryGetValue(token, out LabelInfo? label) ? label.Range : null;
    }

    public string? Hover(string token)
    {
        if (Documentation.TryGetValue(token, out string? description))
            return description;
        if (string.Equals(token, "PRESENT", StringComparison.OrdinalIgnoreCase))
            return "`PRESENT` - Write `0000h-0002h` to video port `0020h` to submit a frame.";
        if (token.Length == 2 && token[0] is 'R' or 'r' && token[1] is >= '0' and <= '7')
            return $"`{token.ToUpperInvariant()}` - 16-bit general-purpose register.";
        if (token.Length == 3 && token[0] is 'F' or 'f' && token[1] is 'P' or 'p' && token[2] is >= '0' and <= '7')
            return $"`{token.ToUpperInvariant()}` - 32-bit IEEE-754 / integer-overlay register.";
        if (Labels.TryGetValue(token, out LabelInfo? label))
            return $"`{label.Name}` - label at physical `{label.Address:X5}h`.";
        return null;
    }

    private IReadOnlyDictionary<string, LabelInfo> ScanLabels()
    {
        var labels = new Dictionary<string, LabelInfo>(StringComparer.OrdinalIgnoreCase);
        var addresses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int address = 0;

        for (var index = 0; index < Lines.Count; index++)
        {
            string code = StripComment(Lines[index]);
            int colon = code.IndexOf(':');
            if (colon >= 0)
            {
                string name = code[..colon].Trim();
                int start = code.IndexOf(name, StringComparison.Ordinal);
                if (Identifier(name) && !addresses.ContainsKey(name))
                {
                    addresses[name] = address;
                    labels[name] = new LabelInfo(name, new TextRange(new TextPosition(index, start), new TextPosition(index, start + name.Length)), address);
                }
            }

            string body = colon >= 0 ? code[(colon + 1)..].Trim() : code.Trim();
            if (body.StartsWith(".org", StringComparison.OrdinalIgnoreCase) && address == 0)
            {
                if (TryParseOrigin(body, out int origin))
                    address = origin;
                continue;
            }
            address += InstructionLength(body);
        }

        return labels;
    }

    private IReadOnlyList<DiagnosticInfo> ScanDiagnostics()
    {
        try
        {
            _ = new Assembler().Assemble(Text);
            return [];
        }
        catch (AssemblyException exception)
        {
            int line = Math.Clamp(exception.Line - 1, 0, Math.Max(0, Lines.Count - 1));
            return [new DiagnosticInfo(new TextRange(new TextPosition(line, 0), new TextPosition(line, Lines[line].Length)), exception.Message)];
        }
    }

    private static int InstructionLength(string body)
    {
        string mnemonic = body.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToUpperInvariant() ?? string.Empty;
        return mnemonic switch
        {
            ".BYTE" => Math.Max(1, body.Count(character => character == ',') + 1),
            ".WORD" => Math.Max(1, body.Count(character => character == ',') + 1) * 2,
            "LI" or "LD.BU" or "LD.W" or "ST.B" or "ST.W" or "BEQ" or "BNE" or "IN" or "OUT" or "WSGI"
                or "JMPA" or "CALLA" or "MUL" or "DIV" or "DIVU" or "MOD" or "MODU" or "NEG" or "NOT" or "ROL" or "ROR" => 4,
            "BLT" or "BGE" or "BLO" or "BHS" or "BLE" or "BGT" or "JMPL" or "CALLL" => 6,
            "LDBS" or "LDBU" or "LDW" or "LSTB" or "LSTW" => 8,
            "FLI" or "IFPLI" => 8,
            "FLD" or "FST" => 6,
            "FMOV" or "FADD" or "FSUB" or "FMUL" or "FDIV" or "FNEG" or "FABS" or "FCMP"
                or "IFPADD" or "IFPSUB" or "IFPAND" or "IFPOR" or "IFPXOR" or "IFPNOT"
                or "IFPSHL" or "IFPSHR" or "IFPSAR" or "IFPROL" or "IFPROR" => 4,
            _ => string.IsNullOrEmpty(mnemonic) ? 0 : 2
        };
    }

    private static string DescribeInstruction(string mnemonic) => mnemonic switch
    {
        "NOP" => "`NOP` - Do nothing and continue with the next instruction.",
        "MOV" => "`MOV Rd, Ra` - Copy the 16-bit value in `Ra` to `Rd`.",
        "LI" => "`LI Rd, imm16` - Load a 16-bit immediate value.",
        "LD.BU" => "`LD.BU Rd, [Ra + disp16]` - Load a byte from logical memory and zero-extend it to `Rd`.",
        "LD.W" => "`LD.W Rd, [Ra + disp16]` - Load a 16-bit big-endian word from logical memory.",
        "ST.B" => "`ST.B Rs, [Ra + disp16]` - Store the low byte of `Rs` to logical memory.",
        "ST.W" => "`ST.W Rs, [Ra + disp16]` - Store a 16-bit big-endian word to logical memory.",
        "ADD" => "`ADD Rd, Ra, Rb` - Add two 16-bit values; the low 16 bits are written to `Rd`.",
        "SUB" => "`SUB Rd, Ra, Rb` - Subtract `Rb` from `Ra`; the low 16 bits are written to `Rd`.",
        "AND" => "`AND Rd, Ra, Rb` - Bitwise AND two 16-bit values.",
        "OR" => "`OR Rd, Ra, Rb` - Bitwise OR two 16-bit values.",
        "XOR" => "`XOR Rd, Ra, Rb` - Bitwise exclusive OR two 16-bit values.",
        "SHL" => "`SHL Rd, Ra, Rb` - Shift `Ra` left by `Rb & 0Fh` bits.",
        "SHR" => "`SHR Rd, Ra, Rb` - Logically shift `Ra` right by `Rb & 0Fh` bits.",
        "SAR" => "`SAR Rd, Ra, Rb` - Arithmetically shift signed `Ra` right by `Rb & 0Fh` bits.",
        "BEQ" => "`BEQ Ra, Rb, rel16` - Branch when `Ra` equals `Rb`.",
        "BNE" => "`BNE Ra, Rb, rel16` - Branch when `Ra` does not equal `Rb`.",
        "JMP" => "`JMP Ra` - Jump to the logical address in `Ra` without changing `SG`.",
        "CALL" => "`CALL Ra` - Push the return PC, then call the logical address in `Ra`.",
        "RET" => "`RET` - Pop the return PC saved by `CALL` or `CALLA`.",
        "PUSH" => "`PUSH Ra` - Push a 16-bit register value onto the physical stack.",
        "POP" => "`POP Rd` - Pop a 16-bit value from the physical stack into `Rd`.",
        "IN" => "`IN Rd, port16` - Read a 16-bit value from an I/O port; unmapped ports read as zero.",
        "OUT" => "`OUT port16, Ra` - Write a 16-bit register value to an I/O port.",
        "RDSG" => "`RDSG Rd` - Read the current segment register into `Rd` with zero extension.",
        "WRSG" => "`WRSG Ra` - Set `SG` to `Ra & 003Fh`.",
        "WSGI" => "`WSGI imm16` - Set `SG` to `imm16 & 003Fh`.",
        "EI" => "`EI` - Enable maskable interrupts by setting `SR.IE`.",
        "DI" => "`DI` - Disable maskable interrupts by clearing `SR.IE`.",
        "HALT" => "`HALT` - Stop fetching instructions until an acceptable interrupt arrives.",
        "IRET" => "`IRET` - Restore the interrupt frame: `SG`, `SR`, then `PC`.",
        "BLT" => "`BLT Ra, Rb, rel16` - Branch when signed `Ra` is less than signed `Rb`.",
        "BGE" => "`BGE Ra, Rb, rel16` - Branch when signed `Ra` is greater than or equal to signed `Rb`.",
        "BLO" => "`BLO Ra, Rb, rel16` - Branch when unsigned `Ra` is below unsigned `Rb`.",
        "BHS" => "`BHS Ra, Rb, rel16` - Branch when unsigned `Ra` is higher than or equal to unsigned `Rb`.",
        "BLE" => "`BLE Ra, Rb, rel16` - Branch when signed `Ra` is less than or equal to signed `Rb`.",
        "BGT" => "`BGT Ra, Rb, rel16` - Branch when signed `Ra` is greater than signed `Rb`.",
        "JMPA" => "`JMPA addr16` - Jump to a logical absolute address without changing `SG`.",
        "CALLA" => "`CALLA addr16` - Push the return PC, then call a logical absolute address.",
        "JMPL" => "`JMPL p20` - Jump to a 20-bit physical address and derive `SG` and `PC` from it.",
        "CALLL" => "`CALLL p20` - Call a 20-bit physical address, saving `SG` and the return PC.",
        "RETL" => "`RETL` - Restore the PC and SG saved by `CALLL`.",
        "LDBS" => "`LDBS Rd, [p20]` - Load a byte from physical memory and sign-extend it to `Rd`.",
        "LDBU" => "`LDBU Rd, [p20]` - Load a byte from physical memory and zero-extend it to `Rd`.",
        "LDW" => "`LDW Rd, [p20]` - Load a 16-bit big-endian word from physical memory.",
        "LSTB" => "`LSTB Rs, [p20]` - Store the low byte of `Rs` to physical memory.",
        "LSTW" => "`LSTW Rs, [p20]` - Store a 16-bit big-endian word to physical memory.",
        "MUL" => "`MUL Rd, Ra, Rb` - Signed multiply; write the low 16 bits to `Rd`.",
        "DIV" => "`DIV Rd, Ra, Rb` - Signed division with truncation toward zero; division by zero faults.",
        "DIVU" => "`DIVU Rd, Ra, Rb` - Unsigned division; division by zero faults.",
        "MOD" => "`MOD Rd, Ra, Rb` - Signed remainder; division by zero faults.",
        "MODU" => "`MODU Rd, Ra, Rb` - Unsigned remainder; division by zero faults.",
        "NEG" => "`NEG Rd, Ra` - Two's-complement negate `Ra`.",
        "NOT" => "`NOT Rd, Ra` - Bitwise complement `Ra`.",
        "ROL" => "`ROL Rd, Ra, Rb` - Rotate `Ra` left by `Rb & 0Fh` bits.",
        "ROR" => "`ROR Rd, Ra, Rb` - Rotate `Ra` right by `Rb & 0Fh` bits.",
        "FLI" => "`FLI FPd, f32` - Load an IEEE-754 single-precision literal into `FPd`.",
        "FMOV" => "`FMOV FPd, FPa` - Copy the raw 32-bit value of a floating-point register.",
        "FLD" => "`FLD FPd, [Ra + disp16]` - Load a 32-bit big-endian value from logical memory.",
        "FST" => "`FST FPs, [Ra + disp16]` - Store a 32-bit big-endian value to logical memory.",
        "FADD" => "`FADD FPd, FPa, FPb` - IEEE-754 single-precision addition.",
        "FSUB" => "`FSUB FPd, FPa, FPb` - IEEE-754 single-precision subtraction.",
        "FMUL" => "`FMUL FPd, FPa, FPb` - IEEE-754 single-precision multiplication.",
        "FDIV" => "`FDIV FPd, FPa, FPb` - IEEE-754 single-precision division.",
        "FNEG" => "`FNEG FPd, FPa` - Negate a single-precision floating-point value.",
        "FABS" => "`FABS FPd, FPa` - Clear the sign bit of a floating-point value.",
        "FCMP" => "`FCMP Rd, FPa, FPb` - Compare floats and write the Open16A comparison code to `Rd`.",
        "IFPLI" => "`IFPLI FPd, imm32` - Load a raw 32-bit integer-overlay immediate into `FPd`.",
        "IFPADD" => "`IFPADD FPd, FPa, FPb` - Add raw 32-bit integer-overlay values.",
        "IFPSUB" => "`IFPSUB FPd, FPa, FPb` - Subtract raw 32-bit integer-overlay values.",
        "IFPAND" => "`IFPAND FPd, FPa, FPb` - Bitwise AND raw 32-bit integer-overlay values.",
        "IFPOR" => "`IFPOR FPd, FPa, FPb` - Bitwise OR raw 32-bit integer-overlay values.",
        "IFPXOR" => "`IFPXOR FPd, FPa, FPb` - Bitwise XOR raw 32-bit integer-overlay values.",
        "IFPNOT" => "`IFPNOT FPd, FPa` - Bitwise complement a raw 32-bit integer-overlay value.",
        "IFPSHL" => "`IFPSHL FPd, FPa, FPb` - Shift left by `FPb & 1Fh` bits in the integer-overlay layer.",
        "IFPSHR" => "`IFPSHR FPd, FPa, FPb` - Logically shift right by `FPb & 1Fh` bits in the integer-overlay layer.",
        "IFPSAR" => "`IFPSAR FPd, FPa, FPb` - Arithmetically shift right by `FPb & 1Fh` bits in the integer-overlay layer.",
        "IFPROL" => "`IFPROL FPd, FPa, FPb` - Rotate left by `FPb & 1Fh` bits in the integer-overlay layer.",
        "IFPROR" => "`IFPROR FPd, FPa, FPb` - Rotate right by `FPb & 1Fh` bits in the integer-overlay layer.",
        _ => throw new ArgumentOutOfRangeException(nameof(mnemonic), mnemonic, "Unknown Open16A instruction.")
    };

    private static bool TryParseOrigin(string body, out int origin)
    {
        string[] tokens = body.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        string value = tokens.Length > 1 ? tokens[1] : string.Empty;
        if (value.EndsWith("h", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(value[..^1], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out origin);
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(value[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out origin);
        return int.TryParse(value, CultureInfo.InvariantCulture, out origin);
    }

    private static string StripComment(string line)
    {
        int semicolon = line.IndexOf(';');
        int slash = line.IndexOf("//", StringComparison.Ordinal);
        int cut = new[] { semicolon, slash }.Where(index => index >= 0).DefaultIfEmpty(line.Length).Min();
        return line[..cut];
    }

    private static bool Identifier(string value) =>
        value.Length != 0 && (char.IsLetter(value[0]) || value[0] is '_' or '.')
        && value.All(character => char.IsLetterOrDigit(character) || character is '_' or '.');

    private static bool IsTokenCharacter(char value) => char.IsLetterOrDigit(value) || value is '_' or '.';
}
