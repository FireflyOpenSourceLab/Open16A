using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Open16A.Lsp;

public sealed class LanguageServer
{
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

        JsonNode? result = method switch
        {
            "initialize" => Initialize(),
            "shutdown" => Shutdown(),
            "textDocument/completion" => Completion(parameters),
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
            await SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = JsonNode.Parse(id.GetRawText()), ["result"] = result }, cancellationToken);
        else if (method != "exit")
            await SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = JsonNode.Parse(id.GetRawText()), ["error"] = new JsonObject { ["code"] = -32601, ["message"] = $"Method not found: {method}" } }, cancellationToken);
    }

    private static JsonObject Initialize() => new()
    {
        ["capabilities"] = new JsonObject
        {
            ["textDocumentSync"] = 1,
            ["completionProvider"] = new JsonObject { ["triggerCharacters"] = new JsonArray(" ", ".", ",") },
            ["hoverProvider"] = true,
            ["definitionProvider"] = true,
            ["documentSymbolProvider"] = true
        },
        ["serverInfo"] = new JsonObject { ["name"] = "Open16A-LSP", ["version"] = "0.1.0" }
    };

    private JsonNode? Shutdown()
    {
        shutdownRequested = true;
        return null;
    }

    private JsonObject Exit()
    {
        exitRequested = true;
        return new JsonObject();
    }

    private JsonObject Completion(JsonElement parameters)
    {
        var items = new JsonArray();
        foreach (string mnemonic in LanguageDocument.Mnemonics)
            items.Add(new JsonObject { ["label"] = mnemonic, ["kind"] = 14, ["detail"] = "Open16A instruction" });
        foreach (string directive in new[] { ".org", ".byte", ".word" })
            items.Add(new JsonObject { ["label"] = directive, ["kind"] = 14, ["detail"] = "Open16A assembler directive", ["textEdit"] = DirectiveTextEdit(parameters, directive) });
        for (var index = 0; index < 8; index++)
        {
            items.Add(new JsonObject { ["label"] = $"R{index}", ["kind"] = 6, ["detail"] = "16-bit general-purpose register" });
            items.Add(new JsonObject { ["label"] = $"FP{index}", ["kind"] = 6, ["detail"] = "32-bit floating-point register" });
        }
        return new JsonObject { ["isIncomplete"] = false, ["items"] = items };
    }

    private JsonNode? Hover(JsonElement parameters)
    {
        if (!TryDocumentPosition(parameters, out LanguageDocument document, out TextPosition position))
            return null;
        string? token = document.TokenAt(position);
        string? text = token is null ? null : document.Hover(token);
        return text is null ? null : new JsonObject { ["contents"] = new JsonObject { ["kind"] = "markdown", ["value"] = text } };
    }

    private JsonNode? Definition(JsonElement parameters)
    {
        if (!TryDocumentPosition(parameters, out LanguageDocument document, out TextPosition position))
            return null;
        string? token = document.TokenAt(position);
        TextRange? range = token is null ? null : document.Definition(token);
        return range is null ? null : new JsonObject { ["uri"] = document.Uri, ["range"] = ToProtocolRange(range) };
    }

    private JsonArray DocumentSymbols(JsonElement parameters)
    {
        if (!TryUri(parameters, out string uri) || !documents.TryGetValue(uri, out LanguageDocument? document))
            return new JsonArray();
        var symbols = new JsonArray();
        foreach (LabelInfo label in document.Labels.Values)
            symbols.Add(new JsonObject { ["name"] = label.Name, ["kind"] = 13, ["range"] = ToProtocolRange(label.Range), ["selectionRange"] = ToProtocolRange(label.Range), ["detail"] = $"{label.Address:X5}h" });
        return symbols;
    }

    private async Task<JsonNode?> DidOpenAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryTextDocument(parameters, out string uri, out JsonElement textDocument))
            return null;
        await UpsertAsync(uri, textDocument.TryGetProperty("text", out JsonElement text) ? text.GetString() ?? string.Empty : string.Empty, cancellationToken);
        return null;
    }

    private async Task<JsonNode?> DidChangeAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryTextDocument(parameters, out string uri, out _) || !parameters.TryGetProperty("contentChanges", out JsonElement changes) || changes.ValueKind != JsonValueKind.Array || changes.GetArrayLength() == 0)
            return null;
        JsonElement lastChange = changes[changes.GetArrayLength() - 1];
        string text = lastChange.TryGetProperty("text", out JsonElement content) ? content.GetString() ?? string.Empty : string.Empty;
        await UpsertAsync(uri, text, cancellationToken);
        return null;
    }

    private async Task<JsonNode?> DidCloseAsync(JsonElement parameters, CancellationToken cancellationToken)
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
        var values = new JsonArray();
        foreach (DiagnosticInfo diagnostic in diagnostics)
            values.Add(new JsonObject { ["range"] = ToProtocolRange(diagnostic.Range), ["severity"] = diagnostic.Severity, ["source"] = "open16a-asm", ["message"] = diagnostic.Message });
        return SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "textDocument/publishDiagnostics", ["params"] = new JsonObject { ["uri"] = uri, ["diagnostics"] = values } }, cancellationToken);
    }

    private JsonObject DirectiveTextEdit(JsonElement parameters, string directive)
    {
        if (!TryDocumentPosition(parameters, out LanguageDocument document, out TextPosition position))
            return new JsonObject { ["newText"] = directive, ["range"] = ToProtocolRange(new TextRange(position, position)) };
        string line = document.Lines[position.Line];
        int start = Math.Clamp(position.Character, 0, line.Length);
        while (start > 0 && (char.IsLetterOrDigit(line[start - 1]) || line[start - 1] is '_' or '.')) start--;
        return new JsonObject
        {
            ["newText"] = directive,
            ["range"] = ToProtocolRange(new TextRange(new TextPosition(position.Line, start), position))
        };
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

    private static JsonObject ToProtocolRange(TextRange range) => new()
    {
        ["start"] = new JsonObject { ["line"] = range.Start.Line, ["character"] = range.Start.Character },
        ["end"] = new JsonObject { ["line"] = range.End.Line, ["character"] = range.End.Character }
    };

    private async Task SendAsync(JsonNode value, CancellationToken cancellationToken)
    {
        byte[] body = Encoding.UTF8.GetBytes(value.ToJsonString());
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
