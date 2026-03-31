---
title: 项目架构
editLink: false
---

# {{ $frontmatter.title }}

## 源文件总览

所有 C# 源文件位于 `Scripts/` 目录，统一使用 `namespace TazeU.Scripts`。

| 文件                      | 类型                                         | 职责                                                |
| :------------------------ | :------------------------------------------- | :-------------------------------------------------- |
| `Entry.cs`                | `public class Entry`                         | Mod 入口点，`[ModInitializer]` 标记，负责全局初始化 |
| `TazeUConfig.cs`          | `public class TazeUConfig`                   | 配置 POCO，JSONC 序列化与校验                       |
| `DGLabServer.cs`          | `public class DGLabServer`                   | WebSocket 服务端，一对多连接管理与电击广播          |
| `DGLabProtocol.cs`        | `public static class DGLabProtocol`          | 波形预设常量、协议指令格式化                        |
| `LightningOrbPatch.cs`    | `static class × 2`                           | Harmony Postfix 补丁，Hook 闪电球 Passive/Evoke     |
| `TazeUOverlay.cs`         | `internal partial class TazeUOverlay : Node` | Godot 节点，QR 弹窗 + 客户端管理 + 快捷键处理       |
| `QRCodeHelper.cs`         | `internal static class QRCodeHelper`         | QRCoder → Godot ImageTexture                        |
| `ModConfigBridge.cs`      | `internal static class ModConfigBridge`      | 反射接入 ModConfig-STS2，零编译依赖                 |
| `CustomWaveformLoader.cs` | `internal static class CustomWaveformLoader` | 从 `waveforms/*.jsonc` 加载自定义波形               |

## Mod 初始化流程

`Entry.Init()` 是整个 Mod 的唯一入口，由 STS2 Modding Framework 通过 `[ModInitializer("Init")]` 在游戏启动时调用。

```
Entry.Init()
  │
  ├─ 1. 注册 AssemblyResolve
  │     确保运行时能从 mod 目录加载 QRCoder 等依赖 DLL
  │
  ├─ 2. Harmony.PatchAll()
  │     自动发现并应用 LightningOrbPassivePatch / LightningOrbEvokePatch
  │
  ├─ 3. ScriptManagerBridge.LookupScriptsInAssembly()
  │     注册 Godot 脚本（TazeUOverlay 等 partial class 节点）
  │
  ├─ 4. TazeUConfig.Load()
  │     加载 default_config.jsonc，不存在则写入默认配置
  │
  ├─ 5. new DGLabServer(config) → LoadCustomWaveforms() → Start()
  │     启动后台 WS 线程，开始监听
  │
  └─ 6. SceneTree 延迟回调（两帧）
        ├─ 帧 1: 挂载 TazeUOverlay 到 Root
        └─ 帧 2: ModConfigBridge.Register() 注册配置项
```

> [!NOTE]
> Overlay 和 ModConfig 需要延迟注册是因为 `Init()` 在 SceneTree 完全就绪之前调用，此时 Root 节点可能尚未可用。使用 `CreateTimer(0)` 保证在下一个空闲帧执行。

## 核心数据流

从闪电球触发到物理电击下发的完整链路：

```
闪电充能球 Passive/Evoke 触发
          │
          ▼
 LightningOrbPatch (Harmony Postfix)
          │ 提取 damage 值
          │ 可选：OnlyOwnOrbs 过滤（比对 NetId）
          ▼
 Entry.Server.TriggerShock(damage)
          │
          ├─ Combo 计算
          │   若 ComboEnabled && 距上次电击 ≤ ComboWindow
          │   → comboCount++ (上限 ComboMaxStacks)
          │   → effectiveDamage = damage × (1 + comboCount × ComboRate)
          │
          ├─ SelectWaveform()
          │   按配置选择波形预设 / 自定义波形 / Random
          │
          └─ 广播到所有已绑定客户端
              对每个 client:
              ├─ MapDamageToStrength(effectiveDamage, client.StrengthLimitA/B)
              │   Stevens 幂律逆映射 → 物理强度值
              └─ ExecuteShockForClientAsync(client, strengthA, strengthB, waveform)
                  ├─ StrengthCommand → 设置通道强度
                  └─ PulseCommand → 下发波形数据
```

## 强度映射算法

基于 **Stevens 幂律定律（Stevens's power law）** 的逆映射：

人体对电刺激的感知强度 $S$ 与物理刺激强度 $I$ 的关系为：

$$S \propto I^{3.5}$$

为使 **感知强度与游戏伤害成正比**，需要：

$$I \propto \text{damage}^{1/3.5}$$

具体映射公式为：

$$\text{strength} = \text{MinStrength} + (\text{maxStrength} - \text{MinStrength}) \times \left(\frac{\min(\text{damage}, \text{DamageCap})}{\text{DamageCap}}\right)^{1/3.5}$$

其中 `maxStrength` 是每个客户端在 APP 中设置的通道物理上限，由客户端在绑定时回传。

## 配置系统

### TazeUConfig

配置以 JSONC 格式存储在 Mod 目录下的 `default_config.jsonc`，使用 `System.Text.Json` 进行序列化：

- `PropertyNamingPolicy = CamelCase` — JSON 字段名使用小驼峰
- `ReadCommentHandling = Skip` — 支持 JSONC 注释
- `AllowTrailingCommas = true` — 允许尾逗号

`Load()` 在文件不存在时自动写入默认配置并返回新实例。`Validate()` 负责限幅校验。

### ModConfig 集成

`ModConfigBridge` 通过反射（`Type.GetType`）动态发现 ModConfig-STS2 的 API，实现**零编译依赖**——玩家未安装 ModConfig 时 Mod 照常运行。

注册流程：

1. `IsAvailable` getter 首次访问时尝试发现 `ModConfigApi`、`ConfigEntry`、`ConfigType` 三个类型
2. `Register()` 构建所有配置项（Slider/Toggle/Dropdown/KeyBind/TextInput/Header）
3. `SyncSavedValues()` 从 ModConfig 持久化存储中读回值，同步到 `TazeUConfig` 实例
4. 各项的 `OnChanged` 回调实时更新 `TazeUConfig` 属性

> [!NOTE]
> Port 和 BindAddress 修改后会自动触发 `server.Restart()` 重启 WebSocket 服务。

## UI 系统

`TazeUOverlay` 继承自 Godot `Node`，挂载到 `SceneTree.Root`。通过 `_UnhandledKeyInput` 捕获全局按键：

| 快捷键（由 ModConfig 配置） | 功能                           |
| :-------------------------- | :----------------------------- |
| ShowQRKey                   | 切换 QR 码弹窗显示 / 隐藏      |
| TestShockKey                | 以 `TestDamage` 值触发测试电击 |
| DisconnectKey               | 断开所有已连接客户端           |

QR 弹窗是一个 `CanvasLayer`（Layer=100），包含半透明背景遮罩、面板、QR 码图片、状态标签、已连接客户端列表（支持 Kick/Block）。客户端列表通过 `Timer` 定时刷新。
