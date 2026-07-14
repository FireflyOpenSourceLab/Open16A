import * as fs from "node:fs";
import * as path from "node:path";
import * as vscode from "vscode";
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions
} from "vscode-languageclient/node";

let client: LanguageClient | undefined;

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
    context.subscriptions.push(
        stackNavigation,
        vscode.languages.registerCodeLensProvider({ language: "open16a", scheme: "file" }, stackNavigation)
    );

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

function analyzeStack(document: vscode.TextDocument): {
    pairs: StackPair[];
    stack: StackInstruction[];
    unmatchedPops: StackInstruction[];
} {
    const pairs: StackPair[] = [];
    const pushes: StackInstruction[] = [];
    const pops: StackInstruction[] = [];
    const labelLines = findLabelLines(document);

    for (let line = 0; line < document.lineCount; line++) {
        const instruction = parseStackInstruction(document.lineAt(line).text, line);
        if (instruction?.kind === "PUSH") {
            pushes.push(instruction);
        } else if (instruction?.kind === "POP") {
            pops.push(instruction);
        }
    }

    const matchedPushes = new Set<number>();
    const matchedPops = new Set<number>();
    for (const push of pushes) {
        for (const pop of findControlFlowPops(document, push, labelLines)) {
            pairs.push({ push, pop });
            matchedPushes.add(push.line);
            matchedPops.add(pop.line);
        }
    }

    return {
        pairs,
        stack: pushes.filter(push => !matchedPushes.has(push.line)),
        unmatchedPops: pops.filter(pop => !matchedPops.has(pop.line))
    };
}

function findLabelLines(document: vscode.TextDocument): Map<string, number> {
    const labels = new Map<string, number>();
    for (let line = 0; line < document.lineCount; line++) {
        const match = /^\s*([A-Za-z_.][A-Za-z0-9_.]*)\s*:/.exec(withoutComment(document.lineAt(line).text));
        if (match) {
            labels.set(match[1].toUpperCase(), line);
        }
    }
    return labels;
}

function findControlFlowPops(
    document: vscode.TextDocument,
    push: StackInstruction,
    labelLines: ReadonlyMap<string, number>
): StackInstruction[] {
    const candidates: StackInstruction[] = [];
    const pending = successors(document, push.line, labelLines).map(line => ({ line, stack: [push.register] }));
    const visited = new Set<string>();

    while (pending.length !== 0) {
        const state = pending.pop()!;
        const key = `${state.line}:${state.stack.join(",")}`;
        if (!visited.add(key)) {
            continue;
        }

        const instruction = parseStackInstruction(document.lineAt(state.line).text, state.line);
        let stack = state.stack;
        if (instruction) {
            if (instruction.kind === "PUSH") {
                if (stack.length >= 32) {
                    continue;
                }
                stack = [...stack, instruction.register];
            } else if (stack.at(-1) === instruction.register) {
                stack = stack.slice(0, -1);
                if (stack.length === 0) {
                    candidates.push(instruction);
                    continue;
                }
            } else {
                continue;
            }
        }

        for (const nextLine of successors(document, state.line, labelLines)) {
            pending.push({ line: nextLine, stack });
        }
    }

    return candidates;
}

function successors(document: vscode.TextDocument, line: number, labelLines: ReadonlyMap<string, number>): number[] {
    const code = withoutComment(document.lineAt(line).text);
    const jump = /\bJMPA\s+([A-Za-z_.][A-Za-z0-9_.]*)\b/i.exec(code);
    if (jump) {
        const target = labelLines.get(jump[1].toUpperCase());
        return target === undefined ? [] : [target];
    }
    if (/\b(?:RET|IRET|HALT)\b/i.test(code)) {
        return [];
    }
    const fallthrough = line + 1 < document.lineCount ? [line + 1] : [];
    const branch = /\b(?:BEQ|BNE|BLT|BGE|BLO|BHS|BLE|BGT)\b[^;]*,\s*([A-Za-z_.][A-Za-z0-9_.]*)\s*$/i.exec(code);
    if (!branch) {
        return fallthrough;
    }
    const target = labelLines.get(branch[1].toUpperCase());
    return target === undefined ? fallthrough : [...fallthrough, target];
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
    const code = withoutComment(text);
    const match = /^\s*(?:[A-Za-z_.][A-Za-z0-9_.]*\s*:\s*)?(PUSH|POP)\s+(R[0-7])\b/i.exec(code);
    if (!match) {
        return undefined;
    }

    return { kind: match[1].toUpperCase() as "PUSH" | "POP", register: match[2].toUpperCase(), line };
}

function withoutComment(text: string): string {
    return text.split(";", 1)[0];
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
