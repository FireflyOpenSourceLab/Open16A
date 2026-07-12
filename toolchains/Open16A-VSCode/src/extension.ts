import * as fs from "node:fs";
import * as path from "node:path";
import * as vscode from "vscode";
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions
} from "vscode-languageclient/node";

let client: LanguageClient | undefined;

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
        const serverOptions: ServerOptions = {
            command: serverPath.endsWith(".exe") ? serverPath : dotnetPath,
            args: serverPath.endsWith(".exe") ? [] : [serverPath]
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

    context.subscriptions.push({ dispose: () => client?.stop() });
    await start();
}

export async function deactivate(): Promise<void> {
    if (client) {
        await client.stop();
        client = undefined;
    }
}

function findServerPath(extensionPath: string): string | undefined {
    const configured = vscode.workspace.getConfiguration("open16a.languageServer").get<string>("path", "").trim();
    const candidates = [
        configured,
        process.env.OPEN16A_LSP_PATH,
        path.join(extensionPath, "server", "Open16A-LSP.exe"),
        ...vscode.workspace.workspaceFolders?.flatMap(folder => [
            path.join(folder.uri.fsPath, "Open16A-LSP.dll"),
            path.join(folder.uri.fsPath, "toolchains", "Open16A-LSP", "bin", "Debug", "net10.0", "Open16A-LSP.dll")
        ]) ?? []
    ];
    return candidates.find(candidate => candidate && fs.existsSync(candidate));
}
