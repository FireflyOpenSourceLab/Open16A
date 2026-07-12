using Open16A.BasicPack;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: open16a-basic-pack <program.bas> -o <program.bin> [--autorun]");
    return;
}

string input = args[0];
string? output = null;
bool autoRun = false;
for (var index = 1; index < args.Length; index++)
{
    if (args[index] == "-o" && index + 1 < args.Length)
        output = args[++index];
    else if (args[index] == "--autorun")
        autoRun = true;
    else
    {
        Console.Error.WriteLine("Usage: open16a-basic-pack <program.bas> -o <program.bin> [--autorun]");
        Environment.ExitCode = 2;
        return;
    }
}

if (output is null)
{
    Console.Error.WriteLine("Missing required -o <program.bin>.");
    Environment.ExitCode = 2;
    return;
}

try
{
    BasicProgramImage image = BasicTokenizer.ParseProgram(File.ReadAllText(input), autoRun);
    byte[] bytes = image.ToBytes();
    File.WriteAllBytes(output, bytes);
    Console.WriteLine($"Packed {image.Lines.Count} BASIC line(s), {bytes.Length} byte(s) -> {output}");
}
catch (Exception exception) when (exception is BasicPackException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 1;
}
