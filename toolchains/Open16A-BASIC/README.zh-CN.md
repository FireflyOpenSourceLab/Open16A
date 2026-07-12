# Open16A BASIC

这是运行在 Open16A guest 内的 BASIC 解释器。映像入口是 `1300h`，避免可选 system ROM 的 `0300h-12FFh` 范围。

当前解释器提供字符卡 REPL、键盘 FIFO 行输入、直接命令 `RUN` / `CLS`，以及预置程序中的整数控制子集：

```basic
10 LET A%=1
20 IF A%=1 THEN 50 ELSE 40
30 PRINT "unreached"
40 END
50 POKE 8192,65
60 PRINT PEEK(8192)
70 GOTO 90
90 END
```

已实现的程序语句是 `LET A%=integer`、`PRINT "text"`、`PRINT integer`、`PRINT A%`、`PRINT PEEK(address)`、`CLS`、`POKE address,value`、`IF left (=|<|>) right THEN line [ELSE line]`、`GOTO line`、`REM`、`END` 与 `STOP`。整数操作数可为 `-32768..32767` 字面量或 `A%-Z%`；`PEEK/POKE` 使用当前 `SG` 的逻辑内存地址，`POKE` 写入低 8 bit。

默认 FP32 变量、整数表达式、`FOR/NEXT`、`GOSUB/RETURN`、数组、字符串变量和 `INPUT` 尚未实现，遇到这些 token 会报告语法错误。

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
