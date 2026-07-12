# Open16A 汇编语法手册

本文描述 `Open16A-ASM` **当前实际接受**的源文件语法。指令的运行语义、编码和设备端口见[指令集手册](INSTRUCTION_SET.zh-CN.md)与[系统设备手册](SYSTEM_DEVICES.zh-CN.md)。

汇编器输出 raw `.bin`；每个 16-bit 字按 big-endian 写入：`1234h` 写作两个字节 `12h 34h`。

## 1. 一行源代码

一条有效语句的形状是：

```asm
label:  MNEMONIC operand0, operand1, operand2  ; comment
```

其中标签和指令都可省略。空行、纯注释行均忽略；缩进没有语义。助记符、指令名、寄存器和标签名都不区分大小写。

```asm
start:              ; 只有标签
    li r0, 0        // 指令可用小写
    ADD R1, R0, R0  ; 操作数以逗号分隔
```

每行最多只能定义一个标签，且标签必须在该行的第一个 `:` 之前。实践中不要把冒号写进字符字面量，因为当前解析器会把它当作标签分隔符。

## 2. 注释、标签与空白

### 注释

`;` 和 `//` 都开始一条行尾注释。位于单引号字符字面量内的这两个符号不算注释。

```asm
LI R0, ';'       ; R0 = 003Bh
LI R1, '/'       // R1 = 002Fh
```

### 标签

标签定义以冒号结尾，引用时不带冒号：

```asm
loop:
    ADD R0, R0, R1
    BNE R0, R2, loop
```

名称首字符必须是字母、`_` 或 `.`；其余字符可为字母、数字、`_` 或 `.`。标签大小写不敏感，`Loop:` 与 `loop:` 是重复定义。标签值是它所在位置的 **20-bit 物理输出地址**。

标签可用于立即数、数据、分支目标、`JMPA/CALLA` 目标、`JMPL/CALLL` 目标和长内存地址。

```asm
.org 0300h
entry:
    LI R0, table + 2
    JMPA done
table:
    .word entry, done
done:
    HALT
```

标签可以与指令或指令数据写在同一行。标签不能前向解析成数值表达式以外的东西，例如没有宏、局部数字标签或匿名标签。

## 3. 数值、字符与表达式

### 数值写法

| 写法 | 含义 | 示例 |
|---|---|---|
| 十进制 | 默认基数 | `42`、`-1` |
| `0x` 前缀十六进制 | 不区分 `x` 的大小写 | `0xF4000` |
| `h` 后缀十六进制 | 不区分 `h` 的大小写 | `F4000h`、`00FFh` |
| 单字符字面量 | 一个 UTF-16 字符的数值 | `'A'`、`' '` |
| 标签 | 标签的物理地址 | `loop` |

只有单字符字面量；没有字符串字面量，也没有 `\n`、`\x41` 之类的转义序列。`'A'` 可用，`"ABC"` 与 `'AB'` 不可用。

### 加减表达式

数值和标签可以用 `+`、`-` 组合：

```asm
LI R0, table + 2
.word handler - table
LD.W R1, [R2 + table - base]
```

汇编器会递归计算加减表达式。十进制负数可直接写 `-1`；负十六进制建议写成 `0 - 1h`，不要写 `-1h`。

### 数值范围

| 使用位置 | 接受范围 | 写入方式 |
|---|---:|---|
| `.byte` | `-128` 到 `255` | 低 8 bit |
| `.word`、`LI`、端口、短地址、短位移字 | `-32768` 到 `65535` | 低 16 bit |
| `.org`、`JMPL/CALLL`、长内存地址 | `00000h` 到 `FFFFFh` | 20-bit 物理地址 |
| 分支目标 | 目标必须相对下一条指令为偶地址，且在 `rel16` 范围 | 汇编器自动算字偏移 |

因此 `LI R0, -1` 会发出 `FFFFh`，`.byte -1` 会发出 `FFh`。地址、端口和立即数本身不支持 `65536` 以上的 16-bit 数值；需要 20-bit 地址时使用 `.L` 长指令或 `JMPL/CALLL`。

## 4. 指令与寄存器

指令名不区分大小写。整数指令接受 `R0-R7`，32-bit 指令接受 `FP0-FP7`；`R0` 不是恒为零的寄存器。`PC`、`SP`、`SG`、`SR` 不是一般操作数。

操作数中的 `Rd` 是目的寄存器，`Ra`、`Rb` 是源寄存器，`Rs` 是存储时的源寄存器。

### 基本运算与栈

| 写法 | 说明 |
|---|---|
| `NOP` | 不做任何事。 |
| `MOV Rd, Ra` | 复制寄存器。 |
| `LI Rd, imm16` | 装入 16-bit 常量或标签值。 |
| `ADD/SUB/AND/OR/XOR Rd, Ra, Rb` | 三寄存器算术或位操作。 |
| `SHL/SHR/SAR Rd, Ra, Rb` | 移位；移位量取 `Rb & 0Fh`。 |
| `PUSH Ra` | 压入一个字。 |
| `POP Rd` | 弹出一个字。 |
| `MUL/DIV/DIVU/MOD/MODU Rd, Ra, Rb` | 扩展整数运算。 |
| `NEG/NOT Rd, Ra` | 一元二补码取负或按位取反。 |
| `ROL/ROR Rd, Ra, Rb` | 循环移位。 |

### 短逻辑地址内存访问

`LD.*` / `ST.*` 的地址是 16-bit **逻辑地址**，会经过 `SG` 映射。内存操作数必须带方括号：

```asm
LD.BU R0, [R1]          ; R0 = zero_extend8(memory[R1])
LD.W  R2, [R3 + 4]      ; 读取一个 big-endian 字
ST.B  R4, [R5 - 1]      ; 写入 R4 的低字节
ST.W  R6, [R7 + offset] ; 写入一个字
```

| 写法 | 说明 |
|---|---|
| `LD.BU Rd, [Ra + disp16]` | 读取字节并零扩展。 |
| `LD.W Rd, [Ra + disp16]` | 读取 16-bit big-endian 字。 |
| `ST.B Rs, [Ra + disp16]` | 写入 `Rs` 的低字节。 |
| `ST.W Rs, [Ra + disp16]` | 写入一个字。 |

`[Ra]` 等价于 `[Ra + 0]`。`disp16` 是带符号位移，可写数字、标签或加减表达式；范围必须是 `-32768` 到 `32767`。方括号中第一个部分必须是 `R0-R7`，不能直接写绝对地址。

### 20-bit 长内存访问

下列 `L...` 指令绕开逻辑地址和 `SG`，直接使用物理 `p20`。标准写法用方括号：

```asm
LDBS R0, [F4000h]
LDBU R1, [F4001h]
LDW  R2, [F4002h]
LSTB R3, [F4004h]
LSTW R4, [F4006h]
```

| 写法 | 说明 |
|---|---|
| `LDBS Rd, [p20]` | 读字节并符号扩展。 |
| `LDBU Rd, [p20]` | 读字节并零扩展。 |
| `LDW Rd, [p20]` | 读 big-endian 16-bit 字。 |
| `LSTB Rs, [p20]` | 写 `Rs` 的低字节。 |
| `LSTW Rs, [p20]` | 写一个字。 |

汇编器也接受不带方括号的 `LDW R0, F4000h`，但建议始终写方括号，明确这是内存地址而不是立即数。

### 分支、调用与返回

分支第三个操作数写目标标签或目标地址，**不要手算 `rel16`**：

```asm
    BEQ R0, R1, equal
    BLT R2, R3, signed_less
    BLO R4, R5, unsigned_lower
```

| 写法 | 说明 |
|---|---|
| `BEQ/BNE Ra, Rb, target` | 相等/不等分支。 |
| `BLT/BGE/BLE/BGT Ra, Rb, target` | 有符号比较分支。 |
| `BLO/BHS Ra, Rb, target` | 无符号比较分支。 |
| `JMP Ra` / `CALL Ra` | 跳转或调用寄存器中的 16-bit 逻辑地址。 |
| `JMPA addr16` / `CALLA addr16` | 跳转或调用 16-bit 逻辑立即地址。 |
| `JMPL p20` / `CALLL p20` | 跳转或调用 20-bit 物理地址，并由 CPU 转换 `SG` 与 `PC`。 |
| `RET` / `RETL` / `IRET` | 分别对应短调用、长调用和中断返回。 |

`CALLL` 与 `RETL` 必须配对；`IRET` 只用于中断处理程序。`JMPL/CALLL` 的 `p20` 能直接写 `F4000h` 或标签。

### I/O、段与中断控制

I/O 端口不是内存操作数，不能写方括号：

```asm
IN  R0, 0021h       ; 先写目的寄存器，再写端口
OUT 0020h, R0       ; 先写端口，再写源寄存器
```

| 写法 | 说明 |
|---|---|
| `IN Rd, port16` | 从端口读一个 16-bit 值。 |
| `OUT port16, Ra` | 向端口写寄存器值。 |
| `RDSG Rd` | 读取当前 `SG`。 |
| `WRSG Ra` | 以寄存器低 6 bit 写 `SG`。 |
| `WSGI imm16` | 以立即数低 6 bit 写 `SG`。 |
| `EI` / `DI` | 开启/关闭可屏蔽中断。 |
| `HALT` | 停机，直到可接收中断到来。 |

例如，给字符设备写 `H` 并提交一帧：

```asm
LI R0, 'H'
OUT 0035h, R0
OUT 0020h, R0
```

`OUT 0020h, R0` 的第二个操作数仍必须是寄存器；若只需要触发提交，常见做法是先把 `R0` 清零。

### 32-bit 浮点与 IFP 指令

```asm
FLI   FP0, 1.5
FLI   FP1, 2.25
FADD  FP2, FP0, FP1
FST   FP2, [R0]
IFPLI FP3, 80000000h
IFPSAR FP4, FP3, FP5
```

`FLI` 接受 IEEE-754 single 十进制字面量；`IFPLI` 接受 raw 32-bit 位模式。`FLD/FST` 使用 `[Ra + disp16]` 逻辑寻址并读写四字节。`FADD`、`FSUB`、`FMUL`、`FDIV`、`FNEG`、`FABS` 做单精度浮点运算；`IFP...` 指令仅把同一组寄存器当作 32-bit 整数。

## 5. 汇编指令

### `.org p20`

设定 raw `.bin` 的加载基地址和后续标签的起始物理地址：

```asm
.org 0300h
entry:
    HALT
```

`.org` 只能出现在任何指令或数据输出之前，且地址必须在 `00000h-Fffffh`。把它作为文件第一条非注释、非空白语句最清晰；若在 `.org` 前定义标签，该标签仍会是 `00000h`，不会自动改成 `.org` 的值。

### `.byte value, ...`

逐个输出字节：

```asm
.byte 0, 1, 2, 0FFh, 'A', -1
```

每个值必须落在 `-128..255`。没有字符串展开功能，文本必须逐字符写：

```asm
.byte 'H', 'E', 'L', 'L', 'O', 0
```

### `.word value, ...`

逐个输出 16-bit big-endian 字：

```asm
.word 1234h, -1, entry
```

上例在 `.bin` 中依次输出 `12 34 FF FF` 和 `entry` 地址的高字节、低字节。

## 6. 完整例子

这个程序把 `R0` 从零累加到五，并在等于五时停机。分支直接引用标签，汇编器会计算偏移。

```asm
; count.o16a
.org 0300h

start:
    LI R0, 0
    LI R1, 1
    LI R2, 5

loop:
    ADD R0, R0, R1
    BNE R0, R2, loop
    HALT
```

在仓库根目录汇编：

```powershell
dotnet run --project toolchains\Open16A-ASM -- count.o16a -o count.bin
```

然后可在模拟器的 F12 debugger 中执行：

```text
loadrun "D:\path\count.bin" 0300h
```

这里的 `0300h` 必须与 `.org 0300h` 一致。

## 7. 当前不支持的 MASM 功能

Open16A-ASM 是一个有意保持小巧的两遍汇编器，目前没有：

- 宏、`include`、条件汇编、结构体、过程声明或段声明。
- 字符串字面量、转义字符、多字符常量或双精度浮点数常量。
- 寄存器别名、局部匿名标签、表达式中的乘除/括号。
- 直接书写 `EXT` 原始 selector；请写对应助记符，如 `CALLL`、`MUL`。

遇到错误时，汇编器以 `Line N:` 开头报告源代码行号。编辑器中的 Open16A LSP 使用同一个汇编器，因此诊断内容与命令行一致。
