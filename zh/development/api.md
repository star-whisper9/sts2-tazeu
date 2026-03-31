---
title: API 参考
editLink: false
---

# {{ $frontmatter.title }}

本页列出 TazeU Mod 各类的公开/内部 API。所有类型位于 `namespace TazeU.Scripts`。

## DGLabServer

WebSocket 服务端核心。构造时注入 `TazeUConfig`，在独立后台线程运行。

```csharp
public class DGLabServer(TazeUConfig config)
```

### 属性

| 属性             | 类型   | 说明                     |
| :--------------- | :----- | :----------------------- |
| `IsConnected`    | `bool` | 是否有任意已绑定的客户端 |
| `ConnectedCount` | `int`  | 已绑定客户端数量         |

### 公开方法

#### `Start()`

启动 WS 监听线程。创建 `CancellationTokenSource`，在后台线程 `TazeU-WS` 中运行 TCP 监听循环。

#### `Stop()`

停止服务端。取消 CTS，向所有已连接客户端发送正常关闭帧，停止 TcpListener。

#### `Restart()`

先 `Stop()` 再 `Start()`。端口或绑定地址变更后由 ModConfig 回调自动触发。

#### `GetConnectUrl() → string`

返回 DG-LAB APP 扫码连接 URL：

```
https://www.dungeon-lab.com/app-download.php#DGLAB-SOCKET#ws://{ip}:{port}/{clientId}
```

#### `TriggerShock(decimal damageValue)`

触发一次电击广播。可从任意线程安全调用（fire-and-forget）。

1. Combo 计算（若启用）
2. 波形选择
3. 遍历所有已绑定客户端，按各自通道上限独立映射强度
4. 异步执行电击指令

#### `DisconnectAll()`

断开所有已连接客户端。

#### `DisconnectClient(string targetId)`

断开指定 targetId 的客户端。

#### `BlockClient(string targetId)`

屏蔽指定客户端的 IP 地址（会话级，不持久化）。后续来自该 IP 的连接将被返回 `403` 错误。

#### `GetConnectedClients() → ClientInfo[]`

返回所有已连接客户端的信息 DTO。

```csharp
public record ClientInfo(string TargetId, string RemoteEndpoint, DateTime ConnectedAt, bool IsBound);
```

### 内部方法

#### `LoadCustomWaveforms()`

调用 `CustomWaveformLoader.LoadAll()` 加载/刷新自定义波形。在 `Entry.Init()` 中于 `Start()` 前调用。

#### `GetAllWaveformNames() → string[]`

返回所有可用波形名称数组：8 个内置预设 + 自定义波形 key + `"Random"`。供 ModConfig 下拉菜单使用。

## DGLabProtocol

协议辅助静态类，提供波形预设和指令格式化。

```csharp
public static class DGLabProtocol
```

### 波形常量

| 常量           | 类型         | 说明          |
| :------------- | :----------- | :------------ |
| `BreathWaveV3` | `string[]`   | 呼吸（12 组） |
| `TideV3`       | `string[]`   | 潮汐          |
| `BatterV3`     | `string[]`   | 连击          |
| `PinchV3`      | `string[]`   | 快速按捏      |
| `PinchRampV3`  | `string[]`   | 按捏渐强      |
| `HeartbeatV3`  | `string[]`   | 心跳节奏      |
| `SqueezeV3`    | `string[]`   | 压缩          |
| `RhythmV3`     | `string[]`   | 节奏步伐      |
| `AllWaveforms` | `string[][]` | 全部预设集合  |

### 静态方法

#### `GetWaveformByName(string name) → string[]?`

按名称查找波形预设，不区分大小写。未找到返回 `null`。

#### `StrengthCommand(int channelA, int channelB) → string`

生成强度控制指令：

```csharp
StrengthCommand(50, 30) → "strength-0+0+50+30"
```

#### `PulseCommand(string channel, string[] waveHexArray) → string`

生成脉冲波形指令：

```csharp
PulseCommand("A", ["0A0A0A0A64646464"]) → "pulse-A:[\"0A0A0A0A64646464\"]"
```

#### `ClearCommand(string channel) → string`

生成清除指令：

```csharp
ClearCommand("A") → "clear-A"
```

#### `ConstantWaveChunk(int frequency = 100, int intensity = 60) → string`

生成恒定频率/强度的单条波形 HEX。频率限幅 10-240，强度限幅 0-100。

## TazeUConfig

配置模型，JSONC 序列化。

```csharp
public class TazeUConfig
```

### 属性

| 属性             | 类型     | 默认值     | 说明                          |
| :--------------- | :------- | :--------- | :---------------------------- |
| `Port`           | `int`    | `9999`     | WS 监听端口                   |
| `BindAddress`    | `string` | `""`       | 自定义绑定 IP（留空自动检测） |
| `MinStrength`    | `int`    | `5`        | 最低输出强度（0-200）         |
| `DamageCap`      | `int`    | `25`       | 伤害映射上限                  |
| `Waveform`       | `string` | `"Breath"` | 波形预设名称                  |
| `UseChannelA`    | `bool`   | `true`     | 启用 A 通道                   |
| `UseChannelB`    | `bool`   | `true`     | 启用 B 通道                   |
| `ComboEnabled`   | `bool`   | `false`    | 连击递增开关                  |
| `ComboRate`      | `float`  | `0.15`     | 每层递增比例                  |
| `ComboWindow`    | `float`  | `3.0`      | 连击时间窗口（秒）            |
| `ComboMaxStacks` | `int`    | `8`        | 最大叠加层数                  |
| `OnlyOwnOrbs`    | `bool`   | `true`     | 仅自己的电球触发              |
| `MaxConnections` | `int`    | `8`        | 最大同时连接数                |
| `TestDamage`     | `int`    | `3`        | 测试电击伤害值                |

### 静态方法

#### `Load() → TazeUConfig`

从 `default_config.jsonc` 加载配置。文件不存在时写入默认配置并返回。加载异常则返回默认实例。

### 实例方法

#### `Save()`

将当前配置序列化写入磁盘。目录不存在时自动创建。

## ModConfigBridge

通过反射零依赖接入 ModConfig-STS2。

```csharp
internal static class ModConfigBridge
```

### 属性

| 属性            | 类型   | 说明                                 |
| :-------------- | :----- | :----------------------------------- |
| `IsAvailable`   | `bool` | ModConfig 是否可用（首次访问时检测） |
| `ShowQRKey`     | `long` | 显示 QR 码快捷键（Godot Key 码）     |
| `TestShockKey`  | `long` | 测试电击快捷键                       |
| `DisconnectKey` | `long` | 断开连接快捷键                       |

### 方法

#### `Register(TazeUConfig config, DGLabServer server)`

注册所有配置项到 ModConfig。需在 SceneTree 就绪后调用。注册后会自动 `SyncSavedValues()` 读回持久化值，若端口/地址变更则自动重启服务端。

#### `GetValue<T>(string key, T fallback) → T`

从 ModConfig 获取指定 key 的已保存值。ModConfig 不可用时返回 `fallback`。

## CustomWaveformLoader

```csharp
internal static class CustomWaveformLoader
```

### 方法

#### `LoadAll() → Dictionary<string, CustomWaveform>`

扫描 `waveforms/` 目录下所有 `.jsonc` 文件，解析并验证后返回字典。

- Key: `"{显示名称}({文件名})"`
- Value: `CustomWaveform(string DisplayName, string[] Data)`

目录不存在时自动创建并返回空字典。

## QRCodeHelper

```csharp
internal static class QRCodeHelper
```

### 方法

#### `GenerateQRTexture(string url, int pixelsPerModule = 8) → ImageTexture`

使用 QRCoder 生成指定 URL 的 QR 码 PNG 字节，再通过 `Image.LoadPngFromBuffer` 转换为 Godot `ImageTexture`。

## LightningOrbPatch

两个 Harmony Postfix 补丁类，Hook 闪电充能球的触发事件。

### LightningOrbPassivePatch

```csharp
[HarmonyPatch(typeof(LightningOrb), nameof(LightningOrb.Passive))]
public static class LightningOrbPassivePatch
```

**Postfix** — 在充能球被动触发后执行。提取 `__instance.PassiveVal` 作为伤害值，调用 `Entry.Server.TriggerShock(damage)`。

当 `Config.OnlyOwnOrbs == true` 时，比对 `LocalContext.NetId` 与 `__instance.Owner.NetId`，不匹配则跳过（多人模式过滤）。

### LightningOrbEvokePatch

```csharp
[HarmonyPatch(typeof(LightningOrb), nameof(LightningOrb.Evoke))]
public static class LightningOrbEvokePatch
```

逻辑与 `PassivePatch` 相同，提取 `__instance.EvokeVal`。

## TazeUOverlay

```csharp
internal partial class TazeUOverlay(DGLabServer server, TazeUConfig config) : Node
```

Godot 节点，挂载到 `SceneTree.Root`。

### 行为

- `_UnhandledKeyInput` — 监听三个快捷键（ShowQR / TestShock / Disconnect）
- `ToggleQRPopup()` — 切换 QR 码弹窗显示
- `ShowQR()` — 创建 CanvasLayer 弹窗，含 QR 码、状态标签、客户端列表
- `RefreshClientList()` — 定时刷新已连接客户端列表（带 Kick/Block 按钮）

## Entry

```csharp
[ModInitializer("Init")]
public class Entry
```

### 静态属性

| 属性     | 类型           | 说明           |
| :------- | :------------- | :------------- |
| `Server` | `DGLabServer?` | 全局服务端实例 |
| `Config` | `TazeUConfig?` | 全局配置实例   |

### 静态方法

#### `Init()`

Mod 唯一入口。完整流程见[项目架构 - Mod 初始化流程](/zh/development/architecture#mod-初始化流程)。
