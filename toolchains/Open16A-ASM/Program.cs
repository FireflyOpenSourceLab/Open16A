using Open16A.Asm;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: open16a-asm <input.asm> [-o <output.bin>]");
    return;
}

string input = args[0];
string output = Path.ChangeExtension(input, ".bin");
if (args.Length == 3 && args[1] == "-o")
    output = args[2];
else if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: open16a-asm <input.asm> [-o <output.bin>]");
    Environment.ExitCode = 2;
    return;
}

try
{
    AssemblyResult result = new Assembler().Assemble(File.ReadAllText(input));
    File.WriteAllBytes(output, result.Bytes);
    Console.WriteLine($"Wrote {result.Bytes.Length} byte(s) to {output}; load at {result.Origin:X5}h.");
}
catch (Exception exception) when (exception is AssemblyException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 1;
}
