# Open16A BASIC

这是运行在 Open16A guest 内的 BASIC 解释器。映像入口是 `1300h`，避免可选 system ROM 的 `0300h-12FFh` 范围。

解释器使用字符卡 REPL 和键盘 FIFO 行输入。程序区始终采用与
`Open16A-BASIC-PACK` 相同的 `B16P` v1 格式，位于 `4000h`；因此宿主预置的
`.bas` 程序和 guest 内编辑的行可以互换。

当前 guest REPL 支持：

- 输入带行号的 `PRINT "text"`、`GOTO line` 或 `END` 行；相同行号会替换，
  只有行号会删除，行按号码排序保存。保存程序行后会直接接受下一行，不重复输出
  `READY.`。
- 直接命令 `RUN`、`CLS`、`NEW` 与 `LIST`。`LIST` 可反解 REPL 支持的行类型，
  来自新版宿主打包器但尚未具备 guest 反解器的语句会显示为 `?`。

预置程序执行目前包含如下整数控制子集：

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

`Open16A-BASIC-PACK` 同时为后续解释器版本编码完整 Microsoft BASIC 核心的
词法面：浮点/整数/字符串变量、表达式、`FOR/NEXT`、`GOSUB/RETURN`、数组、
`INPUT`、`DATA/READ/RESTORE` 与 `CONT`。其中新增的 `DATA`、`READ`、`RESTORE`
和 `CONT` token 分别固定为 `B6h` 到 `B9h`，不会改变现有程序映像的 ABI。尚未由
guest 执行核心处理的 token 会报告语法错误，而不会静默误执行。

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
