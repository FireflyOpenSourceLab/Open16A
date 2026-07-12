# Open16A-LD

Open16A 的第一阶段静态 linker。它把已定位的 raw `.bin` 模块合并为一份连续物理映像，填充模块间空洞并拒绝重叠；不改变模块中的指令或地址，因此还不包含跨模块符号重定位。

```powershell
dotnet run --project toolchains\Open16A-LD -- `
  boot.bin@0300h driver.bin@1800h app.bin@4000h `
  --base 0300h -o system.bin --map system.map
```

- 每个输入使用 `<文件>@<20-bit 物理地址>`；地址可写作 `F4000h`、`0xF4000` 或十进制。
- `--base` 决定输出 raw `.bin` 的第一个物理地址；未指定时取最低模块地址。
- 模块之间的空洞以 `00h` 填充。
- `--map` 输出模块地址和长度，便于 loader 或 debugger 的 `loadrun` 使用。

这个阶段适用于固定地址的 boot、ROM 和常驻映像。下一阶段将引入 `.o16o` 可重定位对象、`.global/.extern`、`ABS16/ABS20/REL16` 重定位，以及为外部存储 loader 生成的 16 KiB overlay 包。
