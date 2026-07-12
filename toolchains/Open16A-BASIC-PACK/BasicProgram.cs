using System.Globalization;
using System.Text;

namespace Open16A.BasicPack;

public sealed class BasicPackException(string message) : Exception(message);

public static class BasicProgramFormat
{
    public const int HeaderLength = 10;
    public const int ProgramCapacity = 0x3000;
    public const int MaximumPayloadLength = ProgramCapacity - HeaderLength;
    public const byte Version = 1;
    public const byte AutoRun = 1;

    public const byte FloatLiteral = 0x81;
    public const byte IntegerLiteral = 0x82;
    public const byte StringLiteral = 0x83;
    public const byte Variable = 0x84;

    public const byte TypeFloat = 0x00;
    public const byte TypeInteger = 0x40;
    public const byte TypeString = 0x80;
}

public sealed record BasicProgramLine(ushort Number, byte[] Tokens);

public sealed record BasicProgramImage(IReadOnlyList<BasicProgramLine> Lines, bool AutoRun)
{
    public byte[] ToBytes()
    {
        int payloadLength = Lines.Sum(line => 4 + line.Tokens.Length);
        if (payloadLength > BasicProgramFormat.MaximumPayloadLength)
            throw new BasicPackException($"Program token payload exceeds {BasicProgramFormat.MaximumPayloadLength} bytes.");

        var output = new List<byte>(BasicProgramFormat.HeaderLength + payloadLength);
        output.AddRange("B16P"u8.ToArray());
        output.Add(BasicProgramFormat.Version);
        output.Add(AutoRun ? BasicProgramFormat.AutoRun : (byte)0);
        WriteWord(output, checked((ushort)payloadLength));
        WriteWord(output, checked((ushort)Lines.Count));
        foreach (BasicProgramLine line in Lines)
        {
            WriteWord(output, line.Number);
            WriteWord(output, checked((ushort)line.Tokens.Length));
            output.AddRange(line.Tokens);
        }
        return [.. output];
    }

    private static void WriteWord(List<byte> output, ushort value)
    {
        output.Add((byte)(value >> 8));
        output.Add((byte)value);
    }
}

public static class BasicTokenizer
{
    private static readonly Dictionary<string, byte> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LET"] = 0x90, ["PRINT"] = 0x91, ["INPUT"] = 0x92, ["IF"] = 0x93,
        ["THEN"] = 0x94, ["GOTO"] = 0x95, ["GOSUB"] = 0x96, ["RETURN"] = 0x97,
        ["FOR"] = 0x98, ["TO"] = 0x99, ["STEP"] = 0x9A, ["NEXT"] = 0x9B,
        ["REM"] = 0x9C, ["END"] = 0x9D, ["STOP"] = 0x9E, ["DIM"] = 0x9F,
        ["CLS"] = 0xA0, ["COLOR"] = 0xA1, ["LOCATE"] = 0xA2,
        ["ABS"] = 0xA3, ["INT"] = 0xA4, ["SGN"] = 0xA5, ["LEN"] = 0xA6,
        ["LEFT$"] = 0xA7, ["RIGHT$"] = 0xA8, ["MID$"] = 0xA9, ["CHR$"] = 0xAA,
        ["STR$"] = 0xAB, ["VAL"] = 0xAC, ["AND"] = 0xAD, ["OR"] = 0xAE,
        ["NOT"] = 0xAF, ["RUN"] = 0xB0, ["LIST"] = 0xB1, ["NEW"] = 0xB2
    };

    public static BasicProgramImage ParseProgram(string source, bool autoRun = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        var lines = new List<BasicProgramLine>();
        var usedNumbers = new HashSet<ushort>();
        string[] sourceLines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < sourceLines.Length; index++)
        {
            string text = sourceLines[index].Trim();
            if (text.Length == 0)
                continue;
            lines.Add(ParseLine(text, index + 1, usedNumbers));
        }

        lines.Sort((left, right) => left.Number.CompareTo(right.Number));
        return new BasicProgramImage(lines, autoRun);
    }

    public static BasicProgramLine ParseLine(string text, int sourceLine = 1, ISet<ushort>? usedNumbers = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        int index = 0;
        SkipWhitespace(text, ref index);
        int numberStart = index;
        while (index < text.Length && char.IsAsciiDigit(text[index])) index++;
        if (numberStart == index || !ushort.TryParse(text[numberStart..index], NumberStyles.None, CultureInfo.InvariantCulture, out ushort number) || number == 0)
            throw Error(sourceLine, "Program lines must begin with a decimal line number in 1-65535.");
        if (usedNumbers is not null && !usedNumbers.Add(number))
            throw Error(sourceLine, $"Duplicate program line {number}.");

        SkipWhitespace(text, ref index);
        if (index == text.Length)
            throw Error(sourceLine, "Program line must contain a statement.");
        return new BasicProgramLine(number, Tokenize(text[index..], sourceLine));
    }

    public static byte[] Tokenize(string text, int sourceLine = 1)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = new List<byte>();
        for (var index = 0; index < text.Length;)
        {
            char current = text[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }
            if (current == '"')
            {
                AddString(tokens, text, ref index, sourceLine);
                continue;
            }
            if (char.IsAsciiLetter(current))
            {
                AddWord(tokens, text, ref index, sourceLine);
                continue;
            }
            if (char.IsAsciiDigit(current) || (current == '.' && index + 1 < text.Length && char.IsAsciiDigit(text[index + 1])))
            {
                AddNumber(tokens, text, ref index, sourceLine);
                continue;
            }
            if (current is '+' or '-' or '*' or '/' or '=' or '<' or '>' or '(' or ')' or ',' or ';' or ':')
            {
                tokens.Add((byte)current);
                index++;
                continue;
            }
            throw Error(sourceLine, $"Unsupported character U+{(int)current:X4}.");
        }
        return [.. tokens];
    }

    private static void AddString(List<byte> tokens, string text, ref int index, int sourceLine)
    {
        index++;
        int start = index;
        while (index < text.Length && text[index] != '"')
        {
            if (text[index] is < ' ' or > '~')
                throw Error(sourceLine, "String literals are limited to printable 7-bit ASCII.");
            index++;
        }
        if (index == text.Length)
            throw Error(sourceLine, "Unterminated string literal.");
        int length = index - start;
        if (length > byte.MaxValue)
            throw Error(sourceLine, "String literals are limited to 255 bytes.");
        tokens.Add(BasicProgramFormat.StringLiteral);
        tokens.Add((byte)length);
        for (var character = start; character < index; character++)
            tokens.Add((byte)text[character]);
        index++;
    }

    private static void AddWord(List<byte> tokens, string text, ref int index, int sourceLine)
    {
        int start = index;
        while (index < text.Length && char.IsAsciiLetter(text[index])) index++;
        if (index < text.Length && text[index] is '%' or '$') index++;
        string word = text[start..index];
        if (Keywords.TryGetValue(word, out byte keyword))
        {
            tokens.Add(keyword);
            if (keyword == 0x9C)
            {
                while (index < text.Length)
                {
                    char character = text[index++];
                    if (character is < ' ' or > '~')
                        throw Error(sourceLine, "REM text is limited to printable 7-bit ASCII.");
                    tokens.Add((byte)character);
                }
            }
            return;
        }

        if (word.Length is < 1 or > 2 || !char.IsAsciiLetter(word[0]))
            throw Error(sourceLine, $"Expected a BASIC keyword or one-letter variable, got '{word}'.");
        byte type = word.Length == 1 ? BasicProgramFormat.TypeFloat : word[1] switch
        {
            '%' => BasicProgramFormat.TypeInteger,
            '$' => BasicProgramFormat.TypeString,
            _ => throw Error(sourceLine, $"Invalid variable '{word}'.")
        };
        tokens.Add(BasicProgramFormat.Variable);
        tokens.Add((byte)(type | (char.ToUpperInvariant(word[0]) - 'A')));
    }

    private static void AddNumber(List<byte> tokens, string text, ref int index, int sourceLine)
    {
        int start = index;
        while (index < text.Length && char.IsAsciiDigit(text[index])) index++;
        if (index < text.Length && text[index] == '.')
        {
            index++;
            while (index < text.Length && char.IsAsciiDigit(text[index])) index++;
        }
        if (index < text.Length && text[index] is 'e' or 'E')
        {
            index++;
            if (index < text.Length && text[index] is '+' or '-') index++;
            int exponentStart = index;
            while (index < text.Length && char.IsAsciiDigit(text[index])) index++;
            if (exponentStart == index)
                throw Error(sourceLine, "Exponent requires decimal digits.");
        }
        string literal = text[start..index];
        if (!literal.ContainsAny('.', 'e', 'E')
            && short.TryParse(literal, NumberStyles.Integer, CultureInfo.InvariantCulture, out short integerValue))
        {
            tokens.Add(BasicProgramFormat.IntegerLiteral);
            tokens.Add((byte)((ushort)integerValue >> 8));
            tokens.Add((byte)integerValue);
            return;
        }
        if (!float.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || float.IsInfinity(value) || float.IsNaN(value))
            throw Error(sourceLine, $"Invalid finite FP32 literal '{literal}'.");
        uint bits = BitConverter.SingleToUInt32Bits(value);
        tokens.Add(BasicProgramFormat.FloatLiteral);
        tokens.Add((byte)(bits >> 24));
        tokens.Add((byte)(bits >> 16));
        tokens.Add((byte)(bits >> 8));
        tokens.Add((byte)bits);
    }

    private static void SkipWhitespace(string text, ref int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
    }

    private static BasicPackException Error(int line, string message) => new($"Line {line}: {message}");
}
