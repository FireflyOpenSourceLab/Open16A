import * as fs from "node:fs";
import * as path from "node:path";
import * as vscode from "vscode";
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions
} from "vscode-languageclient/node";

let client: LanguageClient | undefined;
let stackConnectorDecoration: vscode.TextEditorDecorationType | undefined;

interface StackInstruction {
    readonly kind: "PUSH" | "POP";
    readonly register: string;
    readonly line: number;
}

interface StackPair {
    readonly push: StackInstruction;
    readonly pop: StackInstruction;
}

export async function activate(context: vscode.ExtensionContext): Promise<void> {
    const start = async (): Promise<void> => {
        if (client) {
            return;
        }

        const serverPath = findServerPath(context.extensionPath);
        if (!serverPath) {
            void vscode.window.showErrorMessage(
                "Open16A-LSP.dll was not found. Set open16a.languageServer.path or build it in this workspace."
            );
            return;
        }

        const dotnetPath = vscode.workspace.getConfiguration("open16a.languageServer").get<string>("dotnetPath", "dotnet");
        const isManagedAssembly = serverPath.endsWith(".dll");
        const serverOptions: ServerOptions = {
            command: isManagedAssembly ? dotnetPath : serverPath,
            args: isManagedAssembly ? [serverPath] : []
        };
        const clientOptions: LanguageClientOptions = {
            documentSelector: [{ language: "open16a", scheme: "file" }],
            synchronize: {
                configurationSection: "open16a"
            }
        };

        client = new LanguageClient("open16a-lsp", "Open16A Language Server", serverOptions, clientOptions);
        await client.start();
    };

    context.subscriptions.push(vscode.commands.registerCommand("open16a.restartLanguageServer", async () => {
        if (client) {
            await client.stop();
            client = undefined;
        }
        await start();
    }));

    context.subscriptions.push(vscode.commands.registerCommand(
        "open16a.goToStackMatch",
        async (uri: vscode.Uri, line: number) => {
            const document = await vscode.workspace.openTextDocument(uri);
            const editor = await vscode.window.showTextDocument(document);
            const position = new vscode.Position(line, 0);
            editor.selection = new vscode.Selection(position, position);
            editor.revealRange(new vscode.Range(position, position), vscode.TextEditorRevealType.InCenter);
        }
    ));
    context.subscriptions.push(vscode.commands.registerCommand(
        "open16a.showStackWarning",
        (message: string) => vscode.window.showWarningMessage(message)
    ));

    const stackNavigation = new StackNavigationProvider();
    stackConnectorDecoration = vscode.window.createTextEditorDecorationType({
        before: {
            color: new vscode.ThemeColor("editorCodeLens.foreground"),
            margin: "0 0.65em 0 0"
        }
    });
    const refreshStackConnectors = (): void => {
        for (const editor of vscode.window.visibleTextEditors) {
            if (editor.document.languageId === "open16a") {
                editor.setDecorations(stackConnectorDecoration!, createStackConnectorDecorations(editor.document));
            }
        }
    };
    context.subscriptions.push(
        stackNavigation,
        stackConnectorDecoration,
        vscode.languages.registerCodeLensProvider({ language: "open16a", scheme: "file" }, stackNavigation),
        vscode.window.onDidChangeActiveTextEditor(refreshStackConnectors),
        vscode.window.onDidChangeVisibleTextEditors(refreshStackConnectors),
        vscode.workspace.onDidChangeTextDocument(event => {
            for (const editor of vscode.window.visibleTextEditors) {
                if (editor.document === event.document && editor.document.languageId === "open16a") {
                    editor.setDecorations(stackConnectorDecoration!, createStackConnectorDecorations(editor.document));
                }
            }
        })
    );
    refreshStackConnectors();

    context.subscriptions.push({ dispose: () => client?.stop() });
    await start();
}

class StackNavigationProvider implements vscode.CodeLensProvider, vscode.Disposable {
    private readonly changed = new vscode.EventEmitter<void>();
    private readonly changeSubscription = vscode.workspace.onDidChangeTextDocument(event => {
        if (event.document.languageId === "open16a") {
            this.changed.fire();
        }
    });

    public readonly onDidChangeCodeLenses = this.changed.event;

    public provideCodeLenses(document: vscode.TextDocument): vscode.CodeLens[] {
        const { pairs, stack, unmatchedPops } = analyzeStack(document);

        const lenses: vscode.CodeLens[] = [];
        for (const pair of pairs) {
            lenses.push(stackLens(document, pair.push, pair.pop, "↓"));
            lenses.push(stackLens(document, pair.pop, pair.push, "↑"));
        }
        for (const push of stack) {
            lenses.push(stackWarningLens(push, `no matching POP ${push.register}`));
        }
        for (const pop of unmatchedPops) {
            lenses.push(stackWarningLens(pop, `no matching PUSH ${pop.register}`));
        }

        return lenses;
    }

    public dispose(): void {
        this.changeSubscription.dispose();
        this.changed.dispose();
    }
}

function createStackConnectorDecorations(document: vscode.TextDocument): vscode.DecorationOptions[] {
    const decorations: vscode.DecorationOptions[] = [];
    for (const pair of analyzeStack(document).pairs) {
        for (let line = pair.push.line; line <= pair.pop.line; line++) {
            const glyph = line === pair.push.line ? "┌" : line === pair.pop.line ? "╰" : "│";
            decorations.push({
                range: new vscode.Range(line, 0, line, 0),
                renderOptions: { before: { contentText: glyph } }
            });
        }
    }
    return decorations;
}

function analyzeStack(document: vscode.TextDocument): {
    pairs: StackPair[];
    stack: StackInstruction[];
    unmatchedPops: StackInstruction[];
} {
    const pairs: StackPair[] = [];
    const stack: StackInstruction[] = [];
    const unmatchedPops: StackInstruction[] = [];

    for (let line = 0; line < document.lineCount; line++) {
        const instruction = parseStackInstruction(document.lineAt(line).text, line);
        if (!instruction) {
            continue;
        }
        if (instruction.kind === "PUSH") {
            stack.push(instruction);
        } else if (stack.at(-1)?.register === instruction.register) {
            pairs.push({ push: stack.pop()!, pop: instruction });
        } else {
            unmatchedPops.push(instruction);
        }
    }

    return { pairs, stack, unmatchedPops };
}

function stackWarningLens(source: StackInstruction, message: string): vscode.CodeLens {
    return new vscode.CodeLens(
        new vscode.Range(source.line, 0, source.line, 0),
        {
            title: `$(warning) ${message}`,
            command: "open16a.showStackWarning",
            arguments: [`Open16A stack balance: ${message}.`]
        }
    );
}

function stackLens(
    document: vscode.TextDocument,
    source: StackInstruction,
    target: StackInstruction,
    arrow: "↓" | "↑"
): vscode.CodeLens {
    return new vscode.CodeLens(
        new vscode.Range(source.line, 0, source.line, 0),
        {
            title: `${arrow} ${target.kind} ${target.register}: line ${target.line + 1}`,
            command: "open16a.goToStackMatch",
            arguments: [document.uri, target.line]
        }
    );
}

function parseStackInstruction(text: string, line: number): StackInstruction | undefined {
    const code = text.split(";", 1)[0];
    const match = /^\s*(?:[A-Za-z_.][A-Za-z0-9_.]*\s*:\s*)?(PUSH|POP)\s+(R[0-7])\b/i.exec(code);
    if (!match) {
        return undefined;
    }

    return { kind: match[1].toUpperCase() as "PUSH" | "POP", register: match[2].toUpperCase(), line };
}

export async function deactivate(): Promise<void> {
    if (client) {
        await client.stop();
        client = undefined;
    }
}

function findServerPath(extensionPath: string): string | undefined {
    const configured = vscode.workspace.getConfiguration("open16a.languageServer").get<string>("path", "").trim();
    const bundledServer = process.platform === "win32"
        ? path.join(extensionPath, "server", "win-x64", "Open16A-LSP.exe")
        : process.platform === "linux"
            ? path.join(extensionPath, "server", "linux-x64", "Open16A-LSP")
            : process.platform === "darwin"
                ? path.join(extensionPath, "server", "osx-arm64", "Open16A-LSP")
                : "";
    const candidates = [
        configured,
        process.env.OPEN16A_LSP_PATH,
        bundledServer,
        ...vscode.workspace.workspaceFolders?.flatMap(folder => [
            path.join(folder.uri.fsPath, "Open16A-LSP.dll"),
            path.join(folder.uri.fsPath, "toolchains", "Open16A-LSP", "bin", "Debug", "net10.0", "Open16A-LSP.dll")
        ]) ?? []
    ];
    return candidates.find(candidate => candidate && fs.existsSync(candidate));
}
