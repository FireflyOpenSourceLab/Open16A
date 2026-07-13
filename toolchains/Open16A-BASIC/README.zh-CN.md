# Open16A BASIC

这是运行在 Open16A guest 内的 BASIC 解释器。映像入口是 `1300h`，避免可选 system ROM 的 `0300h-12FFh` 范围。

解释器使用字符卡 REPL 和键盘 FIFO 行输入。程序区始终采用与
`Open16A-BASIC-PACK` 相同的 `B16P` v1 格式，位于 `4000h`；因此宿主预置的
`.bas` 程序和 guest 内编辑的行可以互换。

## 语言与 REPL

Open16A BASIC 1.1 是 Microsoft BASIC 风格的 16-bit 整数子集。默认数值变量
`A-Z` 与显式整数变量 `A%-Z%` 都保存带符号 16-bit 值；字符串变量 `A$-Z$`
每个最多保存 31 个 ASCII 字符。一维数值数组每个变量固定提供 16 个元素，
`DIM A(n)` 用于声明和范围检查。CPU 的 `FP0-FP7` 可由 IFP 指令作为 32-bit
整数暂存器使用；BASIC 1.1 的图形执行器仍按视频设备的 `SG` 分页 ABI 访问
`F4000h-FFFFFh`，不会把物理地址截成 16 bit。

REPL 与 PACK 使用同一套大小写不敏感 tokenizer。输入带行号的任意支持语句会按
行号插入或替换；只输入行号会删除该行。编辑后立即接受下一行，不重复输出
`READY.`。直接命令为 `RUN`、`LIST`、`NEW`、`CONT` 和 `CLS`；`LIST` 从 token
流完整反解关键字、变量、整数、字符串和运算符。

支持的语句和函数：

- `LET`（可省略）、`PRINT`、`INPUT`、`IF/THEN/ELSE`、
  `GOTO`、`GOSUB/RETURN`、`FOR/TO/STEP/NEXT`、`DIM`、`DATA/READ/RESTORE`、
  `REM`、`STOP/CONT`、`END`。
- `CLS`、`COLOR foreground[,background]`、`LOCATE row,column`、
  `PEEK(address)` 与 `POKE address,value`。
- `INP(port)` 与 `OUT port,value` 直接访问完整的 16-bit Open16A I/O 端口空间；
  未映射端口保持机器定义的“读零、忽略写入”行为。
- `SCREEN mode`、`PSET (x,y),color`、`PRESET (x,y)[,color]`、
  `LINE (x1,y1)-(x2,y2),color`、`CIRCLE (x,y),radius,color`、
  `POINT(x,y)`、`PALETTE index,r,g,b` 和 `PRESENT`。
- 带括号和 Microsoft BASIC 优先级的 `+ - * / AND OR NOT`，以及
  `= < > <= >= <>` 条件；函数 `ABS`、`INT`、`SGN`、`LEN`、`VAL`。
- 字符串字面量、字符串变量赋值/复制、`PRINT` 和 `INPUT`。

示例：

```basic
10 DIM A(10)
20 FOR I=0 TO 10
30 LET A(I)=I*2
40 NEXT I
50 INPUT "VALUE"; N
60 IF N>=0 THEN 80 ELSE 70
70 STOP
80 PRINT "A(7)="; A(7)
90 END
```

`Ctrl+C` 通过虚拟键盘 IRQ 请求中断，在下一条 BASIC 语句边界打印 `BREAK` 并
返回输入等待。模拟器还限制每个宿主帧的 guest cycle 预算，紧密 BASIC 循环不会
长期阻塞窗口消息与键盘采样。

图形模式与视频设备一一对应：`SCREEN 0` 为 256 x 192 的 8 bpp 索引色，
`SCREEN 1` 为 512 x 384 的 2 bpp 索引色，`SCREEN 2` 为 128 x 96 RGBA。
模式 2 的 BASIC 颜色是单个 16-bit `RGBA4444` 位模式，例如十进制 `4660`
（`1234h`）写成 `11h,22h,33h,44h`。`SCREEN` 选择模式并清空 48 KiB VRAM，
越界图元被裁剪；`PRESENT` 按视频设备协议异步快照 VRAM 和调色板。

本子集仍不包含 FP32 BASIC 算术、字符串数组、磁盘/文件命令或 GW-BASIC
硬件扩展。`PEEK/POKE` 使用当前 `SG` 的逻辑地址，`POKE` 写入低 8 bit。

单独构建默认解释器，不带预置程序：

```powershell
.\toolchains\Open16A-BASIC\build.ps1 -Output .\open16a-basic.bin
```

构建附带示例：

```powershell
.\toolchains\Open16A-BASIC\build.ps1 `
  -Program .\toolchains\Open16A-BASIC\examples\hello.bas `
  -Output .\basic-hello.bin -AutoRun
```

然后在 F12 host debugger 中执行：

```text
loadrun "D:\sim\OldSimulator\basic-hello.bin" 1300h
```

`Open16A-BASIC-PACK` 接受带行号的 ASCII `.bas` 并产生放置在 `4000h` 的 token 程序段。提供 `-Program` 时，最终映像通过现有 linker 将解释器置于 `1300h`、程序段置于 `4000h`；省略它时，输出就是可单独加载的默认解释器。
