# Open16A-ASM

Open16A 的命令行汇编器，输出 raw `.bin`，所有 16-bit 字按 big-endian 编码。

```powershell
dotnet run --project toolchains\Open16A-ASM -- examples\hello.asm -o hello.bin
```

在模拟器的 F12 host debugger 中加载：

```text
loadrun "D:\path\hello.bin" 0300
```

## 支持的语法

- 指令集手册中全部当前已实现的整数指令，包括 `EXT` 的分支、长跳转/调用、长内存操作及算术指令。
- 标签：`loop:`；标签可以用于立即数、绝对跳转、长地址和分支目标。
- 指令不区分大小写，寄存器为 `R0-R7`。
- 数字默认十进制；十六进制可写作 `1234h` 或 `0x1234`；字符字面量如 `'A'`。
- `.org physical-address`：仅限源文件开头，用于选择加载基地址。
- `.byte value, ...` 和 `.word value, ...`；`.word` 同样按 big-endian 输出。
- 注释使用 `;` 或 `//`。

常用格式：

```asm
LI R0, 0000h
OUT 0020h, R0
LD.W R1, [R2 + 4]
ST.B R3, [R4 - 1]
BEQ R0, R1, equal
CALLL F4000h
LSTW R0, [F4002h]
```

相对分支直接写目标标签或绝对地址，汇编器自动计算 `rel16` 的字偏移。
