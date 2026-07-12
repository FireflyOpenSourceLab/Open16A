# Open16A-LSP

Open16A 汇编的 stdio Language Server Protocol 实现。启动后只通过标准输入/输出传输 `Content-Length` JSON-RPC，日志和错误不写入 stdout。

```powershell
dotnet build toolchains\Open16A-LSP
dotnet toolchains\Open16A-LSP\bin\Debug\net10.0\Open16A-LSP.dll
```

支持：

- `textDocument/publishDiagnostics`：使用实际 `Open16A-ASM` 汇编器报告行级错误。
- `textDocument/completion`：指令、`.org/.byte/.word` 和 `R0-R7`。
- `textDocument/hover`：常用指令、寄存器和标签地址说明。
- `textDocument/definition`：跳至同文件标签定义。
- `textDocument/documentSymbol`：标签大纲。
- `didOpen`、全量同步 `didChange`、`didClose`。

语言标识建议使用 `open16a-asm`，文件扩展名建议为 `.o16a` 或 `.asm`。客户端应以 stdio 启动该程序，并声明/处理全量文本同步。

VS Code 的通用 LSP 客户端配置示意：

```json
{
  "command": "dotnet",
  "args": ["D:\\sim\\OldSimulator\\toolchains\\Open16A-LSP\\bin\\Debug\\net10.0\\Open16A-LSP.dll"]
}
```
