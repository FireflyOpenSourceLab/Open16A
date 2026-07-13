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

`OldSimulator.Expansion.EmbeddedAsm` 提供 `open16a.embedded-asm` 卡。它执行已经编译为原始 `.bin` 字节流的 Open16A 固件，不会在运行时解析或汇编 ASM 源码。设置中的 `firmwareBase64` 是必填项，内容从物理 `0300h` 固定装入；字节流不包含 origin 头，且不得跨入 `FC00h` mailbox 区。

卡拥有独立、平坦的 64 KiB 地址空间：`0000h-FFFFh` 的逻辑地址就是物理地址，`SG` 不参与映射。布局为 `0010h-0011h` 的唯一外部命令中断向量（向量 `0`）、固件入口 `0300h`、初始向下增长栈 `BFFFh`、以及末尾 `FC00h-FFFFh` 的 1 KiB mailbox。

外部槽命令到达时，卡先把其 mailbox 快照复制到内部 `FC00h`，把 16-bit 命令放进 `R0`，并抬起向量 `0`。固件应在启动时写入 `0010h`、执行 `EI`，通常进入 `HALT` 等待命令；中断处理程序读写 `R0` 和末尾 mailbox，执行 `IRET` 后回到 `HALT` 即完成本次外部命令，内部 mailbox 会完整写回主机卡 mailbox。

仓库中的 `examples/command-echo.asm` 是可编译示例：它把输入命令写入 mailbox 字节 `2-3`，并将字节 `0` 加一。`embedded-asm.example.json` 包含该程序的 Base64 编译结果；将其作为 `simulator.json` 或通过 `--config` 指定即可加载。固件若未启用中断、未返回 `HALT` 或触发 CPU fault，外部命令不会正常完成，后者会使扩展卡进入 `PluginFault`。
