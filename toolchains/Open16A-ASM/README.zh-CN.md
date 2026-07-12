# Open16A-ASM

Open16A 的命令行汇编器，默认输出 raw `.bin`；传入 `-c` 时输出可重定位 `.o16o` 对象文件。所有 16-bit 字按 big-endian 编码。

```powershell
dotnet run --project toolchains\Open16A-ASM -- examples\hello.asm -o hello.bin
```

生成供 `Open16A-LD` 跨模块链接的对象文件：

```powershell
dotnet run --project toolchains\Open16A-ASM -- main.asm -c -o main.o16o
```

在模拟器的 F12 host debugger 中加载：

```text
loadrun "D:\path\hello.bin" 0300
```

## 支持的语法

完整的源代码写法、操作数规则和可用/不可用功能见[汇编语法手册](../../docs/ASSEMBLY_SYNTAX.zh-CN.md)。

- 指令集手册中全部当前已实现的整数指令，包括 `EXT` 的分支、长跳转/调用、长内存操作及算术指令。
- 标签：`loop:`；标签可以用于立即数、绝对跳转、长地址和分支目标。
- 指令不区分大小写，寄存器为 `R0-R7`。
- 数字默认十进制；十六进制可写作 `1234h` 或 `0x1234`；字符字面量如 `'A'`。
- `.org physical-address`：仅限源文件开头，用于选择加载基地址。
- `.global symbol, ...`：在 `-c` 模式导出当前模块标签；`.extern symbol, ...`：声明由其他模块提供的符号。
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

## 可重定位对象

`-c` 生成 JSON 格式的 `.o16o`。对象内的标签从 `00000h` 起计算，因此只能省略 `.org` 或写 `.org 0`；非零 `.org` 属于固定地址映像，应继续生成 raw `.bin`。

```asm
; main.asm
.global main
.extern putchar

main:
    LI R0, 'A'
    CALLA putchar
    HALT
```

对象模式会为标签构成的短立即数/位移、`JMPA/CALLA`、`JMPL/CALLL`、长内存地址和外部相对分支生成重定位记录。链接器会在最终放置模块后回填它们。短形式只可装入 `0000h-FFFFh`；跨越该范围的代码入口应使用 `CALLL/JMPL`。表达式的可重定位写法为 `symbol`、`symbol + constant` 或 `symbol - constant`；短内存位移中的符号必须写在 `Ra + symbol` 一侧，不能写为 `Ra - symbol`。
