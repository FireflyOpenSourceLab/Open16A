using Open16A.Lsp;

var server = new LanguageServer(Console.OpenStandardInput(), Console.OpenStandardOutput());
try
{
    await server.RunAsync();
    Environment.ExitCode = server.ExitCode;
}
catch (IOException)
{
    // The editor can destroy the stdio transport while stopping the server.
    Environment.ExitCode = 0;
}
catch (ObjectDisposedException)
{
    Environment.ExitCode = 0;
}
