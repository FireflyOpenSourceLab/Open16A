# Open16A-BASIC-PACK

将带行号的 7-bit ASCII BASIC 源文件编码为 Open16A BASIC 1.1 使用的 `B16P` v1 token 程序段。输出不是独立可执行文件，而是应由 `Open16A-LD` 放到物理 `4000h`，与位于 `1300h` 的 guest 解释器合并。1.1 保持 v1 映像格式和既有 token 值兼容。

```powershell
dotnet run --project toolchains\Open16A-BASIC-PACK -- `
  program.bas -o program.bin --autorun
```

程序头为 `B16P`、版本、flags、payload 长度和行数，全部多字节字段均是 big-endian。`--autorun` 设置 flags bit `0h`，解释器启动后自动执行预置程序。

词法规则：行号为 `1-65535` 的十进制数，行会按号码排序；关键字不区分大小写；变量仅为 `A-Z`、`A%-Z%`、`A$-Z$`；字符串和 `REM` 文本只能使用可显示 ASCII，单个字符串字面量最多 255 字节。数字字面量限于十进制 `-32768..32767` 的 `INT16`；小数、指数和越界整数会在打包时明确拒绝。PACK 与 guest REPL 共享 BASIC 1.1 的语句、函数及图形/I/O token 表。
