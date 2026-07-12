using Open16A.Ld;
using Open16A.Asm;
using System.Text.Json;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: open16a-ld <file.bin>@<physical-address> ... -o <output.bin> [--base <physical-address>] [--map <output.map>]\n       open16a-ld <module.o16o> ... -o <output.bin> --base <physical-address> [--map <output.map>]");
    return;
}

var inputs = new List<LinkInput>();
string? output = null;
string? map = null;
uint? baseAddress = null;

try
{
    for (var index = 0; index < args.Length; index++)
    {
        switch (args[index])
        {
            case "-o" when index + 1 < args.Length:
                output = args[++index];
                break;
            case "--base" when index + 1 < args.Length:
                baseAddress = Linker.ParseAddress(args[++index]);
                break;
            case "--map" when index + 1 < args.Length:
                map = args[++index];
                break;
            default:
                inputs.Add(args[index].EndsWith(".o16o", StringComparison.OrdinalIgnoreCase)
                    ? new LinkInput(args[index], 0)
                    : Linker.ParseInput(args[index]));
                break;
        }
    }

    if (output is null)
        throw new LinkException("Missing required -o <output.bin>.");

    var linker = new Linker();
    bool objectMode = inputs.Count != 0 && inputs.All(input => Path.GetExtension(input.Path).Equals(".o16o", StringComparison.OrdinalIgnoreCase));
    if (inputs.Any(input => Path.GetExtension(input.Path).Equals(".o16o", StringComparison.OrdinalIgnoreCase)) && !objectMode)
        throw new LinkException("Raw binaries and .o16o objects cannot be mixed in one link.");
    if (objectMode && baseAddress is null)
        throw new LinkException("Relocatable object links require --base <physical-address>.");

    LinkResult result = objectMode
        ? linker.LinkObjects(inputs.Select(input => JsonSerializer.Deserialize<ObjectModule>(File.ReadAllText(input.Path)) ?? throw new LinkException($"Invalid object module '{input.Path}'.")), baseAddress!.Value)
        : linker.Link(inputs, baseAddress);
    File.WriteAllBytes(output, result.Bytes);
    if (map is not null)
    {
        var lines = new List<string> { $"origin {result.Origin:X5}h", $"length {result.Bytes.Length:X}" };
        lines.AddRange(result.Entries.Select(entry => $"{entry.Address:X5}h {entry.Length:X4}h {entry.Path}"));
        File.WriteAllLines(map, lines);
    }
    Console.WriteLine($"Linked {result.Entries.Count} module(s), {result.Bytes.Length} byte(s), origin {result.Origin:X5}h -> {output}");
}
catch (Exception exception) when (exception is LinkException or IOException or UnauthorizedAccessException or JsonException)
{
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 1;
}
