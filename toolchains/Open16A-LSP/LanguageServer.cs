using System.Text;
using System.Text.Json;
using System.Globalization;

namespace Open16A.Lsp;

public sealed class LanguageServer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Stream input;
    private readonly Stream output;
    private readonly Dictionary<string, LanguageDocument> documents = new(StringComparer.Ordinal);
    private bool shutdownRequested;
    private bool exitRequested;

    public int ExitCode => shutdownRequested ? 0 : 1;

    public LanguageServer(Stream input, Stream output)
    {
        this.input = input;
        this.output = output;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (!exitRequested && !cancellationToken.IsCancellationRequested)
        {
            using JsonDocument? message = await ReadMessageAsync(cancellationToken);
            if (message is null)
                return;
            await HandleAsync(message.RootElement, cancellationToken);
        }
    }

    private async Task HandleAsync(JsonElement message, CancellationToken cancellationToken)
    {
        if (!message.TryGetProperty("method", out JsonElement methodElement))
            return;

        string method = methodElement.GetString() ?? string.Empty;
        JsonElement parameters = message.TryGetProperty("params", out JsonElement foundParameters) ? foundParameters : default;
        bool request = message.TryGetProperty("id", out JsonElement id);

        object? result = method switch
        {
            "initialize" => Initialize(),
            "shutdown" => Shutdown(),
            "textDocument/completion" => Completion(),
            "textDocument/hover" => Hover(parameters),
            "textDocument/definition" => Definition(parameters),
            "textDocument/documentSymbol" => DocumentSymbols(parameters),
            "textDocument/didOpen" => await DidOpenAsync(parameters, cancellationToken),
            "textDocument/didChange" => await DidChangeAsync(parameters, cancellationToken),
            "textDocument/didClose" => await DidCloseAsync(parameters, cancellationToken),
            "initialized" => null,
            "exit" => Exit(),
            _ => null
        };

        if (!request)
            return;

        if (method is "initialize" or "shutdown" or "textDocument/completion" or "textDocument/hover" or "textDocument/definition" or "textDocument/documentSymbol")
            await SendAsync(new { jsonrpc = "2.0", id, result }, cancellationToken);
        else if (method != "exit")
            await SendAsync(new { jsonrpc = "2.0", id, error = new { code = -32601, message = $"Method not found: {method}" } }, cancellationToken);
    }

    private object Initialize() => new
    {
        capabilities = new
        {
            textDocumentSync = 1,
            completionProvider = new { triggerCharacters = new[] { " ", ".", "," } },
            hoverProvider = true,
            definitionProvider = true,
            documentSymbolProvider = true
        },
        serverInfo = new { name = "Open16A-LSP", version = "0.1.0" }
    };

    private object? Shutdown()
    {
        shutdownRequested = true;
        return null;
    }

    private object Exit()
    {
        exitRequested = true;
        return new { };
    }

    private object Completion()
    {
        IEnumerable<object> instructions = LanguageDocument.Mnemonics.Select(mnemonic => new
        {
            label = mnemonic,
            kind = 14,
            detail = "Open16A instruction"
        });
        IEnumerable<object> directives = new[] { ".org", ".byte", ".word" }.Select(directive => new
        {
            label = directive,
            kind = 14,
            detail = "Open16A assembler directive"
        });
        IEnumerable<object> registers = Enumerable.Range(0, 8).Select(index => new
        {
            label = $"R{index}",
            kind = 6,
            detail = "16-bit general-purpose register"
        });
        IEnumerable<object> floatingPointRegisters = Enumerable.Range(0, 8).Select(index => new
        {
            label = $"FP{index}",
            kind = 6,
            detail = "32-bit floating-point register"
        });
        return new { isIncomplete = false, items = instructions.Concat(directives).Concat(registers).Concat(floatingPointRegisters) };
    }

    private object? Hover(JsonElement parameters)
    {
        if (!TryDocumentPosition(parameters, out LanguageDocument document, out TextPosition position))
            return null;
        string? token = document.TokenAt(position);
        string? text = token is null ? null : document.Hover(token);
        return text is null ? null : new { contents = new { kind = "markdown", value = text } };
    }

    private object? Definition(JsonElement parameters)
    {
        if (!TryDocumentPosition(parameters, out LanguageDocument document, out TextPosition position))
            return null;
        string? token = document.TokenAt(position);
        TextRange? range = token is null ? null : document.Definition(token);
        return range is null ? null : new { uri = document.Uri, range = ToProtocolRange(range) };
    }

    private object DocumentSymbols(JsonElement parameters)
    {
        if (!TryUri(parameters, out string uri) || !documents.TryGetValue(uri, out LanguageDocument? document))
            return Array.Empty<object>();
        return document.Labels.Values.Select(label => new
        {
            name = label.Name,
            kind = 13,
            range = ToProtocolRange(label.Range),
            selectionRange = ToProtocolRange(label.Range),
            detail = $"{label.Address:X5}h"
        });
    }

    private async Task<object?> DidOpenAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryTextDocument(parameters, out string uri, out JsonElement textDocument))
            return null;
        await UpsertAsync(uri, textDocument.TryGetProperty("text", out JsonElement text) ? text.GetString() ?? string.Empty : string.Empty, cancellationToken);
        return null;
    }

    private async Task<object?> DidChangeAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryTextDocument(parameters, out string uri, out _) || !parameters.TryGetProperty("contentChanges", out JsonElement changes) || changes.ValueKind != JsonValueKind.Array || changes.GetArrayLength() == 0)
            return null;
        JsonElement lastChange = changes[changes.GetArrayLength() - 1];
        string text = lastChange.TryGetProperty("text", out JsonElement content) ? content.GetString() ?? string.Empty : string.Empty;
        await UpsertAsync(uri, text, cancellationToken);
        return null;
    }

    private async Task<object?> DidCloseAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryTextDocument(parameters, out string uri, out _))
            return null;
        documents.Remove(uri);
        await PublishDiagnosticsAsync(uri, Array.Empty<DiagnosticInfo>(), cancellationToken);
        return null;
    }

    private async Task UpsertAsync(string uri, string text, CancellationToken cancellationToken)
    {
        var document = new LanguageDocument(uri, text);
        documents[uri] = document;
        await PublishDiagnosticsAsync(uri, document.Diagnostics, cancellationToken);
    }

    private Task PublishDiagnosticsAsync(string uri, IReadOnlyList<DiagnosticInfo> diagnostics, CancellationToken cancellationToken)
    {
        return SendAsync(new
        {
            jsonrpc = "2.0",
            method = "textDocument/publishDiagnostics",
            @params = new
            {
                uri,
                diagnostics = diagnostics.Select(diagnostic => new
                {
                    range = ToProtocolRange(diagnostic.Range),
                    severity = diagnostic.Severity,
                    source = "open16a-asm",
                    message = diagnostic.Message
                })
            }
        }, cancellationToken);
    }

    private bool TryDocumentPosition(JsonElement parameters, out LanguageDocument document, out TextPosition position)
    {
        document = null!;
        position = new TextPosition(0, 0);
        if (!TryUri(parameters, out string uri) || !documents.TryGetValue(uri, out LanguageDocument? foundDocument) || foundDocument is null)
            return false;
        if (!parameters.TryGetProperty("position", out JsonElement raw) || raw.ValueKind != JsonValueKind.Object || !raw.TryGetProperty("line", out JsonElement line) || !raw.TryGetProperty("character", out JsonElement character))
            return false;
        document = foundDocument;
        position = new TextPosition(line.GetInt32(), character.GetInt32());
        return true;
    }

    private static bool TryUri(JsonElement parameters, out string uri)
    {
        uri = string.Empty;
        if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty("textDocument", out JsonElement textDocument) || textDocument.ValueKind != JsonValueKind.Object || !textDocument.TryGetProperty("uri", out JsonElement value))
            return false;
        uri = value.GetString() ?? string.Empty;
        return uri.Length != 0;
    }

    private static bool TryTextDocument(JsonElement parameters, out string uri, out JsonElement textDocument)
    {
        textDocument = default;
        if (!TryUri(parameters, out uri) || !parameters.TryGetProperty("textDocument", out textDocument))
            return false;
        return true;
    }

    private static object ToProtocolRange(TextRange range) => new
    {
        start = new { line = range.Start.Line, character = range.Start.Character },
        end = new { line = range.End.Line, character = range.End.Character }
    };

    private async Task SendAsync(object value, CancellationToken cancellationToken)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await output.WriteAsync(header, cancellationToken);
        await output.WriteAsync(body, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private async Task<JsonDocument?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var header = new List<byte>();
        var previous = new Queue<byte>(4);
        var buffer = new byte[1];
        while (true)
        {
            int read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                return header.Count == 0 ? null : throw new InvalidDataException("Unexpected end of LSP header.");
            header.Add(buffer[0]);
            previous.Enqueue(buffer[0]);
            if (previous.Count > 4) previous.Dequeue();
            if (previous.Count == 4 && previous.SequenceEqual("\r\n\r\n"u8.ToArray()))
                break;
        }

        string headers = Encoding.ASCII.GetString([.. header]);
        int contentLength = headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Where(value => value.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            .Select(value => int.Parse(value["Content-Length:".Length..].Trim(), CultureInfo.InvariantCulture))
            .SingleOrDefault();
        if (contentLength <= 0)
            throw new InvalidDataException("LSP message has no Content-Length.");

        byte[] body = new byte[contentLength];
        int offset = 0;
        while (offset < body.Length)
        {
            int read = await input.ReadAsync(body.AsMemory(offset), cancellationToken);
            if (read == 0)
                throw new InvalidDataException("Unexpected end of LSP body.");
            offset += read;
        }
        return JsonDocument.Parse(body);
    }
}
