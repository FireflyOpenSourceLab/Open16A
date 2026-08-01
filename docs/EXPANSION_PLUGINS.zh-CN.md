# Open16A 扩展卡插件开发手册

本文面向扩展卡插件作者，描述 `OldSimulator.Expansion.Abstractions` API v1 的装载规则、生命周期和执行约束。Guest 可见的端口与 mailbox ABI 见[系统设备手册](SYSTEM_DEVICES.zh-CN.md)。

## 1. 运行与信任模型

扩展卡插件是受信任的 .NET DLL，在模拟器启动时由独立的 `AssemblyLoadContext` 装载。模拟器以普通 JIT 模式发布；插件不是 guest 代码，也不在安全沙箱中运行，因此插件能够获得其进程权限范围内的全部 .NET 能力。只应装载来源可信的 DLL。

每个配置的插件入口 DLL 必须恰好包含一个公开、可无参构造且实现 `IExpansionCardPlugin` 的类型。模拟器为入口 DLL 使用 `AssemblyDependencyResolver` 解析同目录依赖，并强制复用宿主提供的 `OldSimulator.Expansion.Abstractions`，避免契约类型被重复装载。

插件只在启动时装载，槽位随后保持固定。v1 不支持热插拔、卸载后重新装载或运行时修改配置。

## 2. 契约概览

契约 namespace 是 `OldSimulator.Expansion`，当前 `ExpansionCardApi.Version` 为 `1`，mailbox 大小 `ExpansionCardApi.MailboxSize` 为 `400h`（1024）字节。

```csharp
public interface IExpansionCardPlugin
{
    int ApiVersion { get; }
    IReadOnlyList<ExpansionCardDescriptor> Cards { get; }
    IExpansionCard Create(
        string cardId,
        ExpansionCardCreateContext context,
        JsonElement settings);
}

public interface IExpansionCard : IDisposable
{
    void BeginCommand(ushort command, Memory<byte> mailbox, IExpansionCardCommand completion);
    void AdvanceCycles(ulong cycles);
    void Reset();
}

public interface IExpansionCardCommand
{
    void Complete();
}
```

`ExpansionCardDescriptor` 包含稳定的 `Id`、供宿主显示的 `DisplayName` 和卡自身的 `ProtocolVersion`。插件的 `ApiVersion` 必须与宿主 API 完全一致；一份 DLL 可在 `Cards` 中声明多个卡型号，`Create` 根据配置中的 `cardId` 创建其中一个实例。`ExpansionCardCreateContext` 在 v1 只提供 `Slot`。

插件应把无法识别的 `cardId`、无效设置和实例创建失败作为异常报告。此类异常属于启动配置错误，模拟器会在创建窗口前终止，而不是留下一个半初始化的槽位。

## 3. 命令生命周期

宿主接受 guest 命令时，会复制该槽完整的 1 KiB mailbox，并将私有副本作为 `Memory<byte>` 传给 `BeginCommand`。插件只能读写这份副本：它不获得物理内存、`IoBus`、中断控制器或任意 DMA 接口。

一个卡实例同一时间最多有一个命令。插件完成处理后必须对本次提供的 `IExpansionCardCommand` 调用一次且只能一次 `Complete()`；宿主随后把整份私有副本写回 guest mailbox、设置完成状态并触发共享 IRQ。同步完成是合法的，插件可在 `BeginCommand` 返回前调用 `Complete()`。

虚拟时间只通过 `AdvanceCycles(ulong cycles)` 推进。插件不得用后台 `Task`、线程、墙钟定时器或宿主帧率驱动命令；这样暂停模拟器时卡也会暂停，而 CPU 执行 `HALT` 时设备仍可随虚拟周期完成并唤醒 CPU。

`Reset()` 取消当前命令，且不得为被取消命令调用 `Complete()`。宿主会让复位前的完成句柄失效，所以迟到调用也不能完成新命令。`Dispose()` 同样必须取消在途工作并释放插件资源。

插件抛出的运行时异常会使当前命令以 `PluginFault` 完成并产生完成中断；该卡在复位前不再接受新命令。插件不应通过异常表达普通的卡协议错误，而应在自己的 mailbox 协议中返回错误结果。

## 4. 配置

模拟器默认读取 `AppContext.BaseDirectory/simulator.json`；`--config <path>` 可指定其他文件。程序集路径相对配置文件所在目录解析。

```json
{
  "version": 1,
  "slots": [
    {
      "slot": 0,
      "assembly": "plugins/Open16A.Loopback.dll",
      "cardId": "open16a.loopback",
      "settings": { "latencyCycles": 32 }
    }
  ]
}
```

默认配置缺失时八个槽位均为空；显式 `--config` 指定的文件缺失会使启动失败。重复槽位、`slot` 不在 `0-7`、DLL 不存在、API 版本不兼容、入口类型数量错误、未知 `cardId` 或无效设置也都会使启动失败。

## 5. 回环诊断卡

仓库中的 `OldSimulator.Expansion.Loopback` 是最小参考实现，其稳定 ID 为 `open16a.loopback`，卡协议版本为 `1`。

- `settings.latencyCycles` 默认为 `32`，必须是 JSON 非负整数；设为 `0` 时命令同步完成。
- 命令 `0000h` 不修改 1 KiB 快照，等待指定虚拟周期后原样返回。
- 其他命令把快照的前两个字节改为 `FFh FFh`，再按相同延迟完成。
- `Reset()` 和 `Dispose()` 直接取消在途命令。

该卡用于验证插件发现、配置、mailbox 快照、虚拟时钟、完成 IRQ 和复位行为，不定义通用扩展卡识别协议。

## 6. v1 边界

Guest ABI 不提供通用 `IDENTIFY` 或卡 ID 查询；操作系统和驱动必须从机器配置预先知道每个槽的卡类型及卡协议。插件也不能请求任意 guest 内存 DMA、注册额外 I/O 端口、直接抬起中断或创建热插拔设备。需要传输的数据必须放在本槽的 1 KiB mailbox 中，并通过单命令完成协议交换。

## 7. 内嵌 ASM 协处理器卡

`OldSimulator.Expansion.EmbeddedAsm` 提供 `open16a.embedded-asm` 卡。它执行构建期编译并嵌入 `Open16A.EmbeddedAsm.dll` 的原始 `.bin` 固件，不会在运行时解析或汇编 ASM 源码，也不接受 `firmwareBase64` 配置。固件从物理 `0300h` 固定装入，字节流不包含 origin 头，且不得跨入 `FC00h` mailbox 区。

卡拥有独立、平坦的 64 KiB 地址空间：`0000h-FFFFh` 的逻辑地址就是物理地址，`SG` 不参与映射。布局为 `0010h-0011h` 的唯一外部命令中断向量（向量 `0`）、固件入口 `0300h`、初始向下增长栈 `BFFFh`、以及末尾 `FC00h-FFFFh` 的 1 KiB mailbox。

外部槽命令到达时，卡先把其 mailbox 快照复制到内部 `FC00h`，把 16-bit 命令放进 `R0`，并抬起向量 `0`。固件应在启动时写入 `0010h`、执行 `EI`，通常进入 `HALT` 等待命令；中断处理程序读写 `R0` 和末尾 mailbox，执行 `IRET` 后回到 `HALT` 即完成本次外部命令，内部 mailbox 会完整写回主机卡 mailbox。仓库固件把命令字（big-endian）写入 mailbox `0000h-0001h`，把状态字 `0001h`（ACK）写入 `0002h-0003h` 后 `IRET`。

固件源位于 `OldSimulator.Expansion.EmbeddedAsm/firmware/main.asm`。构建会先以 `Open16A-ASM -c` 生成 `.o16o`，再以 `Open16A-LD --base 0300h` 生成 `firmware.bin` 并嵌入最终 DLL。可从仓库根目录运行 `powershell -ExecutionPolicy Bypass -File OldSimulator.Expansion.EmbeddedAsm/build.ps1`，或直接运行 `dotnet build OldSimulator.Expansion.EmbeddedAsm`；IDE 和 CI 构建走同一套 MSBuild Target。`embedded-asm.example.json` 的 `settings` 为空。固件若未启用中断、未返回 `HALT` 或触发 CPU fault，外部命令不会正常完成，后者会使扩展卡进入 `PluginFault`。

## 8. 磁盘镜像卡

`OldSimulator.Expansion.Disk` 提供 `open16a.disk` 卡，稳定 ID 为 `open16a.disk`，卡协议版本为 `1`。它把宿主上的一个裸磁盘镜像文件作为块设备暴露给 guest：512 字节扇区、32-bit LBA，一次命令传输一个扇区。卡不定义文件系统；文件系统属于 guest 侧软件协议层。

### 8.1 配置

```json
{
  "version": 1,
  "slots": [
    {
      "slot": 1,
      "assembly": "plugins/Open16A.Disk.dll",
      "cardId": "open16a.disk",
      "settings": {
        "imagePath": "D:\\disks\\system.img",
        "readOnly": false,
        "latencyCycles": 512
      }
    }
  ]
}
```

- `imagePath` 必填，必须是**绝对路径**；卡不解析相对路径。镜像文件必须已存在、长度非零且为 `512` 的倍数，扇区数必须能装入 32-bit LBA。不满足时实例创建失败，属于启动配置错误。
- `readOnly` 可选，默认 `false`。为 `true` 时以 `FileShare.Read` 打开镜像，写命令返回 `WriteProtected`；为 `false` 时以 `FileShare.None` 独占打开。
- `latencyCycles` 可选，默认 `512`，必须是非负整数。设为 `0` 时命令同步完成。

可在 Windows 用 `fsutil file createnew system.img 1048576` 生成 1 MiB 空白镜像。

### 8.2 命令与 mailbox 布局

mailbox 为 1 KiB。多字节字段全部 big-endian。头部占用 `000h-00Fh`，扇区数据从 `010h` 起：

| 偏移 | 字段 | 含义 |
|---|---:|---|---|
| `000h-001h` | 状态字 | 卡写入，见下表。 |
| `002h-005h` | LBA | READ/WRITE 请求的 32-bit 扇区号。 |
| `010h-20Fh` | 数据 | 512 字节扇区数据。 |

| 命令 | 名称 | 请求 | 响应 |
|---|---|---|---|
| `0000h` | `IDENTIFY` | 无 | 容量信息写入 `002h` 起（见下）。 |
| `0001h` | `READ` | `002h-005h` 为 LBA | 状态字 + `010h` 起 512 字节数据。 |
| `0002h` | `WRITE` | LBA + `010h` 起 512 字节数据 | 状态字。 |
| 其他 | — | — | 状态字 `UnknownCommand`，数据区清零。 |

`IDENTIFY` 响应自 `002h` 起：

| 偏移 | 字段 | 含义 |
|---|---:|---|---|
| `002h-005h` | magic | `4Fh 44h 53h 4Bh`（`"ODSK"`），用于确认槽位安装的确实是磁盘卡。 |
| `006h-007h` | 协议版本 | 当前为 `1`。 |
| `008h-009h` | 扇区大小 | `0200h`（512）。 |
| `00Ah-00Dh` | 扇区总数 | 32-bit，等于镜像长度除以扇区大小。 |
| `00Eh-00Fh` | flags | bit `0h` 为 `ReadOnly`。 |

状态字取值：`0000h` `Ok`、`0001h` `UnknownCommand`、`0002h` `LbaOutOfRange`、`0003h` `WriteProtected`、`0004h` `HostIoError`。普通协议错误（未知命令、越界、只读写入）通过状态字返回，不抛异常。`HostIoError` 表示宿主文件 I/O 失败；命令期其他任何插件异常仍会使卡进入 `PluginFault`。

### 8.3 执行模型

`BeginCommand` 会在私有 mailbox 副本上立即完成宿主文件 I/O（读取扇区或写入扇区），然后按 `latencyCycles` 倒计时，由 `AdvanceCycles` 推进到零后调用 `Complete()`。已落盘的写不受之后 `Reset()` 影响；`Reset()` 只取消未完成的命令，`Dispose()` 同时关闭镜像文件句柄。宿主 I/O 错误映射为 `HostIoError` 状态字，而不是插件异常。
