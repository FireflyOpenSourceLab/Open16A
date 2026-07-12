# Open16A-LD

Open16A-LD 有两种链接模式：固定地址 raw `.bin` 合并，以及带跨模块符号重定位的 `.o16o` 对象链接。输出始终是一份 raw `.bin` 物理映像。

## 固定地址 raw 模块

```powershell
dotnet run --project toolchains\Open16A-LD -- `
  boot.bin@0300h driver.bin@1800h app.bin@4000h `
  --base 0300h -o system.bin --map system.map
```

- 每个输入使用 `<文件>@<20-bit 物理地址>`；地址可写作 `F4000h`、`0xF4000` 或十进制。
- `--base` 决定输出 raw `.bin` 的第一个物理地址；未指定时取最低模块地址。
- 模块之间的空洞以 `00h` 填充。
- `--map` 输出模块地址和长度，便于 loader 或 debugger 的 `loadrun` 使用。

此模式适用于固定地址的 boot、ROM 和常驻映像。

## 可重定位对象模块

先以 `-c` 汇编每个源文件，再按所给顺序链接；模块从 `--base` 起连续排列，每个模块起点自动按字对齐。

```powershell
dotnet run --project toolchains\Open16A-ASM -- main.asm -c -o main.o16o
dotnet run --project toolchains\Open16A-ASM -- console.asm -c -o console.o16o
dotnet run --project toolchains\Open16A-LD -- `
  main.o16o console.o16o --base 0300h -o app.bin --map app.map
```

对象源文件使用 `.global name` 导出标签，使用 `.extern name` 引用另一个对象导出的标签。链接器不允许同名全局符号、未定义外部符号、损坏的重定位记录，或在同一次链接中混用 raw `.bin` 与 `.o16o`。

链接器支持以下回填类型：

- `ABS16`：`.word`、`LI`、端口/短控制地址及短内存位移。最终值必须落在 16-bit 字范围。
- `ABS20`：`JMPL`、`CALLL` 与长内存指令的 20-bit 物理地址。
- `REL16`：指向外部符号的条件分支；目标必须在分支的 word-relative 范围内。

对象链接同样可写 `--map`。映像由 simulator host debugger 的 `load` 或 `loadrun` 一次加载到 `--base`，不会自动生成 16 KiB overlay 或磁盘加载器；那是外部存储设备的软件协议层。
