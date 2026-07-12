using Open16A.Lsp;

var server = new LanguageServer(Console.OpenStandardInput(), Console.OpenStandardOutput());
await server.RunAsync();
Environment.ExitCode = server.ExitCode;
