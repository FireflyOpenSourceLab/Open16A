using Open16A.Asm;
using System.Text.Json;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: open16a-asm <input.asm> [-o <output.bin>] [-c]");
    return;
}

string input = args[0];
string output = Path.ChangeExtension(input, ".bin");
bool objectMode = false;
for (var index = 1; index < args.Length; index++)
{
    if (args[index] == "-c")
        objectMode = true;
    else if (args[index] == "-o" && index + 1 < args.Length)
        output = args[++index];
    else
    {
        Console.Error.WriteLine("Usage: open16a-asm <input.asm> [-o <output.bin>] [-c]");
        Environment.ExitCode = 2;
        return;
    }
}

if (objectMode && Path.GetExtension(output).Equals(".bin", StringComparison.OrdinalIgnoreCase))
    output = Path.ChangeExtension(output, ".o16o");

try
{
    var assembler = new Assembler();
    if (objectMode)
    {
        ObjectModule module = assembler.AssembleObject(File.ReadAllText(input));
        File.WriteAllText(output, JsonSerializer.Serialize(module));
        Console.WriteLine($"Wrote relocatable object with {module.Bytes.Length} byte(s), {module.Symbols.Count} symbol(s), and {module.Relocations.Count} relocation(s) to {output}.");
    }
    else
    {
        AssemblyResult result = assembler.Assemble(File.ReadAllText(input));
        File.WriteAllBytes(output, result.Bytes);
        Console.WriteLine($"Wrote {result.Bytes.Length} byte(s) to {output}; load at {result.Origin:X5}h.");
    }
}
catch (Exception exception) when (exception is AssemblyException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 1;
}
