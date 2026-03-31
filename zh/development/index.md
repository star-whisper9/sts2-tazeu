---
title: 快速入门
editLink: false
---

# {{ $frontmatter.title }}

## 环境要求

| 依赖项            | 版本  |
| :---------------- | :---- |
| Godot (with .NET) | 4.5.1 |
| .NET SDK          | 9.0   |
| C# 语言版本       | 12.0  |

> [!TIP]
> 你无需实际安装 Godot 编辑器——项目通过 `Godot.NET.Sdk` NuGet 引用 Godot 的 C# 绑定，`dotnet build` 即可编译。

## 克隆与依赖设置

```bash
git clone https://github.com/star-whisper9/sts2-tazeu.git
cd sts2-tazeu
```

项目依赖两个来自游戏本体的程序集和一个 NuGet 包：

| 引用            | 来源           | 说明                |
| :-------------- | :------------- | :------------------ |
| `sts2.dll`      | 游戏 data 目录 | 杀戮尖塔 2 核心 API |
| `0Harmony.dll`  | 游戏 data 目录 | Harmony 补丁框架    |
| `QRCoder` 1.6.0 | NuGet          | QR 码生成库         |

`TazeU.csproj` 会自动探测游戏安装路径（macOS arm64 / x86_64）。若路径不匹配，请修改 `<Sts2Dir>` 属性指向正确的游戏根目录：

```xml
<Sts2Dir>/path/to/Slay the Spire 2/SlayTheSpire2.app/Contents/Resources</Sts2Dir>
```

## 构建

```bash
dotnet build
```

`PostBuild` 目标会自动将产物拷贝到 `mods/TazeU/`：

- `TazeU.dll` — Mod 主程序集
- `QRCoder.dll` — 运行时依赖
- `TazeU.json` — Mod 元数据
- `default_config.jsonc` — 默认配置（仅在目标目录不存在时拷贝，不会覆盖用户已有配置）

## 项目目录结构

```
sts2-tazeu/
├── Scripts/                    # C# 源文件（全部核心逻辑）
│   ├── Entry.cs                # Mod 入口点
│   ├── TazeUConfig.cs          # 配置模型（序列化 / 反序列化）
│   ├── DGLabServer.cs          # WebSocket 服务端核心
│   ├── DGLabProtocol.cs        # 协议辅助与波形预设
│   ├── LightningOrbPatch.cs    # Harmony 补丁（Hook 闪电球）
│   ├── TazeUOverlay.cs         # 游戏内 UI Overlay
│   ├── QRCodeHelper.cs         # QR 码生成工具
│   ├── ModConfigBridge.cs      # ModConfig 反射集成
│   └── CustomWaveformLoader.cs # 自定义波形文件加载
├── Tools/                      # Python 辅助脚本
│   ├── mock_client.py          # 单客户端 mock（开发调试）
│   ├── mock_multi_client.py    # 多客户端 mock（一对多测试）
│   └── sim_strength.py         # 强度映射模拟 / 调参工具
├── TazeU.csproj                # 项目文件
├── TazeU.json                  # Mod 元数据
└── default_config.jsonc        # 默认配置
```

## 调试与测试

由于 Mod 运行在游戏进程内，直接 attach debugger 比较困难。推荐使用 `Tools/` 目录下的 mock 客户端进行联调：

```bash
# 单客户端 mock
python Tools/mock_client.py

# 多客户端 mock（测试一对多广播）
python Tools/mock_multi_client.py
```

mock 客户端会模拟 DG-LAB APP 的连接握手与消息交互，可以在不连接真实硬件的情况下验证 WebSocket 通信和电击逻辑。

`sim_strength.py` 可用于离线模拟 Stevens 幂律映射和 Combo 递增计算：

```bash
python Tools/sim_strength.py
```
