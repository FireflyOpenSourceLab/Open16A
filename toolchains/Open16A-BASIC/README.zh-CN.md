# Open16A BASIC

这是运行在 Open16A guest 内的 BASIC 解释器。映像入口是 `1300h`，避免可选 system ROM 的 `0300h-12FFh` 范围。

当前 bootstrap 核已经提供字符卡 REPL、键盘 FIFO 行输入、直接命令 `RUN` / `CLS`，以及 token 程序中的 `PRINT "..."` 和 `END`。宿主 packer 已稳定定义 v1 token 格式，后续语句会在同一执行分派器上逐步加入。

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
