using Open16A.Asm;
using System.Globalization;

namespace Open16A.Lsp;

public sealed record TextPosition(int Line, int Character);
public sealed record TextRange(TextPosition Start, TextPosition End);
public sealed record DiagnosticInfo(TextRange Range, string Message, int Severity = 1);
public sealed record LabelInfo(string Name, TextRange Range, int Address);

public sealed class LanguageDocument
{
    private static readonly Dictionary<string, string> Documentation = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LI"] = "`LI Rd, imm16` - Load a 16-bit immediate value.",
        ["OUT"] = "`OUT port16, Ra` - Write a 16-bit register value to an I/O port.",
        ["IN"] = "`IN Rd, port16` - Read a 16-bit I/O port value.",
        ["LD.W"] = "`LD.W Rd, [Ra + disp16]` - Load a big-endian word.",
        ["ST.W"] = "`ST.W Rd, [Ra + disp16]` - Store a big-endian word.",
        ["CALLL"] = "`CALLL p20` - Call a 20-bit physical address and save SG and PC.",
        ["RETL"] = "`RETL` - Restore PC and SG saved by CALLL.",
        ["JMPL"] = "`JMPL p20` - Jump to a 20-bit physical address.",
        ["PRESENT"] = "Write `0000h-0002h` to video port `0020h` to submit a frame."
    };

    public static readonly string[] Mnemonics =
    [
        "NOP", "MOV", "LI", "LD.BU", "LD.W", "ST.B", "ST.W", "ADD", "SUB", "AND", "OR", "XOR", "SHL", "SHR", "SAR",
        "BEQ", "BNE", "JMP", "CALL", "RET", "PUSH", "POP", "IN", "OUT", "RDSG", "WRSG", "WSGI", "EI", "DI", "HALT", "IRET",
        "BLT", "BGE", "BLO", "BHS", "BLE", "BGT", "JMPA", "CALLA", "JMPL", "CALLL", "RETL", "LDBS", "LDBU", "LDW", "LSTB", "LSTW",
        "MUL", "DIV", "DIVU", "MOD", "MODU", "NEG", "NOT", "ROL", "ROR"
        , "FLI", "FMOV", "FLD", "FST", "FADD", "FSUB", "FMUL", "FDIV", "FNEG", "FABS", "FCMP"
        , "IFPLI", "IFPADD", "IFPSUB", "IFPAND", "IFPOR", "IFPXOR", "IFPNOT", "IFPSHL", "IFPSHR", "IFPSAR", "IFPROL", "IFPROR"
    ];

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
        if (Mnemonics.Contains(token, StringComparer.OrdinalIgnoreCase))
            return $"`{token.ToUpperInvariant()}` Open16A instruction.";
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
