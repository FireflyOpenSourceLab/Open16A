import * as fs from "node:fs";
import * as path from "node:path";
import * as vscode from "vscode";
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions
} from "vscode-languageclient/node";

let client: LanguageClient | undefined;
let stoppingClient: Promise<void> | undefined;

interface StackInstruction {
    readonly kind: "PUSH" | "POP";
    readonly register: string;
    readonly line: number;
}

interface StackPair {
    readonly push: StackInstruction;
    readonly pop: StackInstruction;
}

interface StackAnalysis {
    readonly pairs: readonly StackPair[];
    readonly unmatchedPushes: readonly StackInstruction[];
    readonly unmatchedPops: readonly StackInstruction[];
    readonly depthLimited: readonly StackInstruction[];
    readonly stateLimited: boolean;
}

interface CachedStackAnalysis {
    readonly version: number;
    readonly analysis: StackAnalysis;
}

interface ParsedLine {
    readonly instruction: StackInstruction | undefined;
    readonly successors: readonly number[];
}

interface StackFrame {
    readonly id: number;
    readonly depth: number;
    readonly push: StackInstruction;
    readonly previous: StackFrame | undefined;
}

const MAX_STACK_DEPTH = 64;
const MAX_ANALYSIS_STATES = 25_000;
const ANALYSIS_YIELD_INTERVAL = 256;

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
        await stopClient();
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

    context.subscriptions.push({ dispose: () => { void stopClient(); } });
    await start();
}

async function stopClient(): Promise<void> {
    if (stoppingClient) {
        return stoppingClient;
    }

    const active = client;
    client = undefined;
    if (!active) {
        return;
    }

    stoppingClient = active.stop().catch(error => {
        // VS Code may already have destroyed the stdio transport during shutdown.
        console.warn("Open16A language server stop completed with a closed transport.", error);
    }).finally(() => {
        stoppingClient = undefined;
    });
    return stoppingClient;
}

class StackNavigationProvider implements vscode.CodeLensProvider, vscode.Disposable {
    private readonly changed = new vscode.EventEmitter<void>();
    private readonly analysisCache = new Map<string, CachedStackAnalysis>();
    private readonly pendingAnalyses = new Map<string, Promise<StackAnalysis>>();
    private readonly changeSubscription = vscode.workspace.onDidChangeTextDocument(event => {
        if (event.document.languageId === "open16a") {
            this.analysisCache.delete(event.document.uri.toString());
            this.changed.fire();
        }
    });
    private readonly configurationSubscription = vscode.workspace.onDidChangeConfiguration(event => {
        if (event.affectsConfiguration("open16a.stackNavigation.enabled")) {
            this.changed.fire();
        }
    });

    public readonly onDidChangeCodeLenses = this.changed.event;

    public async provideCodeLenses(document: vscode.TextDocument, token: vscode.CancellationToken): Promise<vscode.CodeLens[]> {
        if (!stackNavigationEnabled()) {
            return [];
        }

        const analysis = await this.getAnalysis(document);
        if (token.isCancellationRequested) {
            return [];
        }

        const lenses: vscode.CodeLens[] = [];
        for (const pair of analysis.pairs) {
            lenses.push(stackLens(document, pair.push, pair.pop, "↓"));
            lenses.push(stackLens(document, pair.pop, pair.push, "↑"));
        }
        for (const push of analysis.unmatchedPushes) {
            lenses.push(stackWarningLens(push, `no matching POP ${push.register}`));
        }
        for (const pop of analysis.unmatchedPops) {
            lenses.push(stackWarningLens(pop, `no matching PUSH ${pop.register}`));
        }
        for (const push of analysis.depthLimited) {
            lenses.push(stackWarningLens(push, "analysis stopped at the stack-depth limit"));
        }
        if (analysis.stateLimited) {
            lenses.push(analysisLimitLens());
        }

        return lenses;
    }

    private async getAnalysis(document: vscode.TextDocument): Promise<StackAnalysis> {
        const uri = document.uri.toString();
        const cached = this.analysisCache.get(uri);
        if (cached?.version === document.version) {
            return cached.analysis;
        }

        const version = document.version;
        const key = `${uri}@${version}`;
        let pending = this.pendingAnalyses.get(key);
        if (!pending) {
            const lines = document.getText().split(/\r?\n/);
            pending = analyzeStackAsync(lines);
            this.pendingAnalyses.set(key, pending);
            void pending.then(
                () => this.pendingAnalyses.delete(key),
                () => this.pendingAnalyses.delete(key)
            );
        }

        const analysis = await pending;
        if (document.version === version) {
            this.analysisCache.set(uri, { version, analysis });
        }
        return analysis;
    }

    public dispose(): void {
        this.changeSubscription.dispose();
        this.configurationSubscription.dispose();
        this.changed.dispose();
    }
}

function stackNavigationEnabled(): boolean {
    return vscode.workspace.getConfiguration("open16a.stackNavigation").get<boolean>("enabled", false);
}

async function analyzeStackAsync(lines: readonly string[]): Promise<StackAnalysis> {
    if (lines.length === 0) {
        return { pairs: [], unmatchedPushes: [], unmatchedPops: [], depthLimited: [], stateLimited: false };
    }

    const labelLines = new Map<string, number>();
    const code = new Array<string>(lines.length);

    for (let line = 0; line < lines.length; line++) {
        code[line] = withoutComment(lines[line]);
        const match = /^\s*([A-Za-z_.][A-Za-z0-9_.]*)\s*:/.exec(code[line]);
        if (match) {
            labelLines.set(match[1].toUpperCase(), line);
        }
        if ((line + 1) % ANALYSIS_YIELD_INTERVAL === 0) {
            await yieldToExtensionHost();
        }
    }

    const parsed = new Array<ParsedLine>(lines.length);
    for (let line = 0; line < lines.length; line++) {
        parsed[line] = {
            instruction: parseStackInstruction(code[line], line),
            successors: successors(code[line], line, lines.length, labelLines)
        };
        if ((line + 1) % ANALYSIS_YIELD_INTERVAL === 0) {
            await yieldToExtensionHost();
        }
    }

    const pairs = new Map<string, StackPair>();
    const unmatchedPushes = new Map<number, StackInstruction>();
    const unmatchedPops = new Map<number, StackInstruction>();
    const depthLimited = new Map<number, StackInstruction>();
    const frames = new Map<string, StackFrame>();
    const visited = new Map<number, Set<number>>();
    const pending: Array<{ line: number; stack: StackFrame | undefined }> = [];
    const roots = new Set<number>([0]);
    for (const line of code) {
        const call = /\bCALLA\s+([A-Za-z_.][A-Za-z0-9_.]*)\b/i.exec(line);
        if (call) {
            const target = labelLines.get(call[1].toUpperCase());
            if (target !== undefined) {
                roots.add(target);
            }
        }
    }
    let nextFrameId = 1;
    let visitedStates = 0;
    let stateLimited = false;

    for (const line of roots) {
        pending.push({ line, stack: undefined });
    }
    // Seed every save as well as known entry points so unreferenced or indirectly-called routines still get navigation.
    for (const line of parsed) {
        if (line.instruction?.kind === "PUSH") {
            pending.push({ line: line.instruction.line, stack: undefined });
        }
    }

    while (pending.length !== 0) {
        if (visitedStates >= MAX_ANALYSIS_STATES) {
            stateLimited = true;
            break;
        }

        const state = pending.pop()!;
        const stackId = state.stack?.id ?? 0;
        let statesAtLine = visited.get(state.line);
        if (!statesAtLine) {
            statesAtLine = new Set<number>();
            visited.set(state.line, statesAtLine);
        }
        if (statesAtLine.has(stackId)) {
            continue;
        }
        statesAtLine.add(stackId);
        visitedStates++;

        const current = parsed[state.line];
        let stack = state.stack;
        if (current.instruction?.kind === "PUSH") {
            if (stack && stack.depth >= MAX_STACK_DEPTH) {
                depthLimited.set(current.instruction.line, current.instruction);
                continue;
            }
            const frameKey = `${stack?.id ?? 0}:${current.instruction.line}`;
            stack = frames.get(frameKey);
            if (!stack) {
                stack = {
                    id: nextFrameId++,
                    depth: (state.stack?.depth ?? 0) + 1,
                    push: current.instruction,
                    previous: state.stack
                };
                frames.set(frameKey, stack);
            }
        } else if (current.instruction?.kind === "POP") {
            if (!stack || stack.push.register !== current.instruction.register) {
                unmatchedPops.set(current.instruction.line, current.instruction);
                continue;
            }
            pairs.set(`${stack.push.line}:${current.instruction.line}`, { push: stack.push, pop: current.instruction });
            unmatchedPops.delete(current.instruction.line);
            stack = stack.previous;
        }

        if (current.successors.length === 0) {
            recordUnmatchedStack(stack, unmatchedPushes);
            continue;
        }
        for (const nextLine of current.successors) {
            pending.push({ line: nextLine, stack });
        }

        if (visitedStates % ANALYSIS_YIELD_INTERVAL === 0) {
            await yieldToExtensionHost();
        }
    }

    // A POP that is not the strict LIFO mate of any PUSH is always unbalanced, even in an unreachable block.
    foreach: for (const line of parsed) {
        if (line.instruction?.kind !== "POP") {
            continue;
        }
        for (const pair of pairs.values()) {
            if (pair.pop.line === line.instruction.line) {
                continue foreach;
            }
        }
        unmatchedPops.set(line.instruction.line, line.instruction);
    }

    return {
        pairs: [...pairs.values()],
        unmatchedPushes: [...unmatchedPushes.values()],
        unmatchedPops: [...unmatchedPops.values()],
        depthLimited: [...depthLimited.values()],
        stateLimited
    };
}

function recordUnmatchedStack(stack: StackFrame | undefined, unmatchedPushes: Map<number, StackInstruction>): void {
    for (let frame = stack; frame; frame = frame.previous) {
        unmatchedPushes.set(frame.push.line, frame.push);
    }
}

function successors(code: string, line: number, lineCount: number, labelLines: ReadonlyMap<string, number>): number[] {
    const fallthrough = line + 1 < lineCount ? [line + 1] : [];
    const jump = /\bJMPA\s+([A-Za-z_.][A-Za-z0-9_.]*)\b/i.exec(code);
    if (jump) {
        const target = labelLines.get(jump[1].toUpperCase());
        return target === undefined ? [] : [target];
    }
    if (/\b(?:JMP|JMPL|RET|RETL|IRET|HALT)\b/i.test(code)) {
        return [];
    }
    const branch = /\b(?:BEQ|BNE|BLT|BGE|BLO|BHS|BLE|BGT)\s+R[0-7]\s*,\s*R[0-7]\s*,\s*([A-Za-z_.][A-Za-z0-9_.]*)\b/i.exec(code);
    if (!branch) {
        return fallthrough;
    }
    const target = labelLines.get(branch[1].toUpperCase());
    return target === undefined ? fallthrough : [...fallthrough, target];
}

function yieldToExtensionHost(): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, 0));
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

function analysisLimitLens(): vscode.CodeLens {
    return new vscode.CodeLens(
        new vscode.Range(0, 0, 0, 0),
        {
            title: "$(warning) stack analysis stopped at the control-flow state limit",
            command: "open16a.showStackWarning",
            arguments: ["Open16A stack balance: analysis stopped at the control-flow state limit."]
        }
    );
}

function withoutComment(text: string): string {
    for (let index = 0; index < text.length; index++) {
        if (text[index] === ";" || (text[index] === "/" && text[index + 1] === "/")) {
            return text.slice(0, index);
        }
    }
    return text;
}

export async function deactivate(): Promise<void> {
    await stopClient();
}

function findServerPath(extensionPath: string): string | undefined {
    const configured = vscode.workspace.getConfiguration("open16a.languageServer").get<string>("path", "").trim();
    const bundledNativeServer = process.platform === "win32"
        ? path.join(extensionPath, "server", "win-x64", "Open16A-LSP.exe")
        : process.platform === "linux"
            ? path.join(extensionPath, "server", "linux-x64", "Open16A-LSP")
            : process.platform === "darwin"
                ? path.join(extensionPath, "server", "osx-arm64", "Open16A-LSP")
                : "";
    const bundledManagedServer = path.join(extensionPath, "server", "Open16A-LSP.dll");
    const candidates = [
        configured,
        process.env.OPEN16A_LSP_PATH,
        bundledNativeServer,
        bundledManagedServer,
        ...vscode.workspace.workspaceFolders?.flatMap(folder => [
            path.join(folder.uri.fsPath, "Open16A-LSP.dll"),
            path.join(folder.uri.fsPath, "toolchains", "Open16A-LSP", "bin", "Debug", "net10.0", "Open16A-LSP.dll")
        ]) ?? []
    ];
    return candidates.find(candidate => candidate && fs.existsSync(candidate));
}
