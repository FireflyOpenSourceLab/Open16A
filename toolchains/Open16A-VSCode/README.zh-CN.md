# Open16A VS Code 扩展

该扩展为 `.o16a` 和 `.asm` 提供 Open16A 语法高亮，并通过 `Open16A-LSP` 提供诊断、指令/寄存器/标签补全、悬停、标签定义跳转和文档符号。补全无论以 Enter 或 Tab 提交，都会替换光标所在的完整标识符。实验性 `PUSH Rn`/`POP Rn` 栈配对导航默认关闭；启用后会显示可点击箭头，并以严格 LIFO 规则标记未配对的保存或恢复。

## 开发与安装

安装扩展依赖并打包。仓库根目录的 `build-toolchains.ps1` 会生成 Windows、Linux 和 macOS Apple Silicon 的 VSIX，并为每个包嵌入对应的原生 LSP；安装后的扩展不需要工作区内的 LSP 文件，也不需要本机 `dotnet`。

```powershell
cd D:\sim\OldSimulator\toolchains\Open16A-VSCode
npm install
npm run package
```

在 VS Code 的 Extensions 面板选择 `Install from VSIX...`，选择生成的 `open16a-asm-0.1.0.vsix`。

扩展会按以下顺序寻找服务器：

1. `open16a.languageServer.path` 设置。
2. 环境变量 `OPEN16A_LSP_PATH`。
3. VSIX 内置的当前平台 AOT `Open16A-LSP`。
4. 当前工作区根目录的 `Open16A-LSP.dll`。
5. 当前工作区下的 `toolchains/Open16A-LSP/bin/Debug/net10.0/Open16A-LSP.dll`。

内置服务器直接启动。使用外部 DLL 时，`open16a.languageServer.dotnetPath` 可改为 `dotnet` 的绝对路径。

命令面板中的 `Open16A: Restart Language Server` 可在更新 LSP 后重启连接。需要试用栈配对时，设置 `open16a.stackNavigation.enabled` 为 `true`；点击栈配对箭头可跳至对应的 `PUSH` 或 `POP`。该功能进行控制流分析，较大的文件可能影响扩展宿主响应，因此默认保持关闭。
