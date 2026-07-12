# Open16A VS Code 扩展

该扩展为 `.o16a` 和 `.asm` 提供 Open16A 语法高亮，并通过 `Open16A-LSP` 提供诊断、补全、悬停、标签定义跳转和文档符号。

## 开发与安装

先构建 LSP，再安装扩展依赖和打包：

```powershell
cd D:\sim\OldSimulator
dotnet build toolchains\Open16A-LSP
cd toolchains\Open16A-VSCode
npm install
npm run compile
npm run package
```

在 VS Code 的 Extensions 面板选择 `Install from VSIX...`，选择生成的 `open16a-asm-0.1.0.vsix`。

扩展会按以下顺序寻找服务器：

1. `open16a.languageServer.path` 设置。
2. 环境变量 `OPEN16A_LSP_PATH`。
3. 当前工作区下的 `toolchains/Open16A-LSP/bin/Debug/net10.0/Open16A-LSP.dll`。

默认服务器以 `dotnet <Open16A-LSP.dll>` 启动。`open16a.languageServer.dotnetPath` 可改为 `dotnet` 的绝对路径。

命令面板中的 `Open16A: Restart Language Server` 可在更新 LSP 后重启连接。
