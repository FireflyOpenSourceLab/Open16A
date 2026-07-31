# Open16A

一套完整的复古计算机模拟器:自研 16-bit 指令集,从 CPU、设备、模拟器宿主,到汇编器、链接器、BASIC 解释器、语言服务器与编辑器插件,全部包含在同一个解决方案里。

## 特性

- **自研 CPU 与指令集**:16-bit 逻辑地址、8 个通用寄存器 `R0-R7`、32-bit 暂存器 `FP0-FP7`、`EXT` 扩展指令,指令手册见 [docs/INSTRUCTION_SET.zh-CN.md](docs/INSTRUCTION_SET.zh-CN.md)
- **1 MiB 物理内存**:`SG` 分页映射 16-bit 逻辑地址,受保护的统一内存写入规则
- **视频设备**:三种显示模式(256x192 8bpp 索引色 / 512x384 2bpp / 128x96 RGBA)、256 色调色板、VBlank 帧同步与视频中断
- **键盘与中断**:扫描码快照 + FIFO 行输入,设备直接向中断控制器抬起固定向量
- **宿主调试器**:F12 打开,支持 `loadrun`、`mem`、`poke`、`fill`、`load` 等命令
- **扩展卡框架**:受信任 .NET 插件,独立 `AssemblyLoadContext` 装载,1 KiB mailbox 快照/写回协议;内置 EmbeddedAsm(嵌入汇编协处理器卡)与 Loopback 卡
- **完整工具链**:汇编器、静态链接器、guest 内 BASIC 解释器、LSP 语言服务器、VS Code 与 Neovim 插件
- **可重定位对象**:`-c` 输出带重定位记录的 `.o16o`,由链接器跨模块回填

## 仓库结构

```
OldSimulator.sln            解决方案(模拟器核心 + 扩展 + 测试)
OldSimulator/               模拟器宿主(raylib 窗口、键盘、调试器)
VirtualDevices/             CPU、内存、I/O 总线、视频、键盘、中断控制器
HostDevices/                宿主侧设备(屏幕、键盘、调试器控制台)
Expansion/                  扩展卡装载与配置
OldSimulator.Expansion.*    扩展卡框架与内置卡
docs/                       指令集、系统设备、扩展插件、汇编语法手册
toolchains/Open16A-ASM      命令行汇编器
toolchains/Open16A-LD       静态链接器
toolchains/Open16A-BASIC    运行在 guest 内的 BASIC 解释器(1.0/1.1)
toolchains/Open16A-BASIC-PACK  .bas -> B16P token 程序打包器
toolchains/Open16A-LSP      汇编语言服务器(stdio LSP)
toolchains/Open16A-VSCode   VS Code 扩展
toolchains/Open16A-Nvim     Neovim 插件(内置三平台自包含 LSP)
```

## 快速开始

需要 .NET 10 SDK。构建并运行模拟器:

```powershell
dotnet build OldSimulator.sln
dotnet run --project OldSimulator
```

运行后按 `F12` 打开宿主调试器,加载程序:

```text
loadrun "D:\path\hello.bin" 0300
```

模拟器默认读取 `simulator.json` 配置扩展卡,可用 `--config <path>` 指定其他文件;参考 [simulator.example.json](simulator.example.json)。

汇编并生成可加载的二进制:

```powershell
dotnet run --project toolchains\Open16A-ASM -- examples\hello.asm -o hello.bin
```

运行测试:

```powershell
dotnet test OldSimulator.Tests
```

## 工具链

各工具链的用法与示例见各自目录下的 `README.zh-CN.md`。发布版 `Open16A-Nvim.zip` 内置 Windows x64、Linux x64 与 macOS Apple Silicon 的自包含 LSP 和 VS Code 扩展,重新生成:

```powershell
.\build-toolchains.ps1
```

## 文档

- [指令集手册](docs/INSTRUCTION_SET.zh-CN.md)
- [系统设备手册](docs/SYSTEM_DEVICES.zh-CN.md)
- [扩展卡插件开发手册](docs/EXPANSION_PLUGINS.zh-CN.md)
- [汇编语法手册](docs/ASSEMBLY_SYNTAX.zh-CN.md)

## 许可证

[GPL-3.0](LICENSE)

Copyright (c) 2026 chenjintang-shrimp
