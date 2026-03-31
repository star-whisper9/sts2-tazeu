---
title: 协议详解
editLink: false
---

# {{ $frontmatter.title }}

TazeU 内嵌了一个 DG-LAB WebSocket v2 协议的服务端实现，采用**一对多**架构——Mod 作为 WS Server，多个 DG-LAB APP 作为 WS Client。

## 架构概览

```
┌─────────────────────────────────────┐
│  Slay the Spire 2 (Mod = Server)    │
│                                     │
│  TazeU Mod                          │
│  ├─ DGLabServer (TcpListener)       │
│  └─ ws://localIP:port/clientId      │
└──────────┬──────────────────────────┘
           │ WebSocket (一对多)
     ┌─────┴─────┐
     ▼           ▼
┌─────────┐ ┌─────────┐
│ DG-LAB  │ │ DG-LAB  │  ...N 个 APP
│ APP #1  │ │ APP #2  │
│ (BLE)   │ │ (BLE)   │
└────┬────┘ └────┬────┘
     ▼           ▼
┌─────────┐ ┌─────────┐
│Coyote 3 │ │Coyote 3 │  各自的硬件
└─────────┘ └─────────┘
```

每个 APP 通过蓝牙（BLE）桥接到各自的 Coyote 3.0 硬件。电击事件由 Server 广播给所有已绑定的客户端，但各客户端按**自身的通道强度上限**独立映射。

## 连接流程

每个客户端独立经历以下握手流程：

```
Server                                    APP (Client)
  │                                         │
  │  1. 生成 clientId（全局唯一 GUID）       │
  │  2. 启动 TcpListener                    │
  │                                         │
  │◄────────── 3. APP 扫码连接 ─────────────│
  │     ws://ip:port/{clientId}              │
  │                                         │
  │  4. TCP Accept → HTTP → WS 握手          │
  │                                         │
  │──── 5. Server 分配 targetId ───────────►│
  │     { type: "bind",                      │
  │       clientId, targetId }               │
  │                                         │
  │◄──── 6. APP 回复 bind 确认 ────────────│
  │     { type: "bind", message: "200" }     │
  │                                         │
  │──── 7. Strength 归零 ─────────────────►│
  │     触发 APP 回传通道上限                 │
  │                                         │
  │◄──── 8. Strength 反馈 ────────────────│
  │     strength-{currentA}+{limitA}+        │
  │              {currentB}+{limitB}         │
  │                                         │
  │  9. 通信就绪，加入广播列表                │
  ▼                                         ▼
```

> [!TIP]
> 扫码 URL 格式：`https://www.dungeon-lab.com/app-download.php#DGLAB-SOCKET#ws://{ip}:{port}/{clientId}`
>
> DG-LAB APP 解析 `#` 后的锚点部分获取 WS 地址。

## WebSocket 握手

`DGLabServer` **手动实现了 HTTP → WebSocket 升级握手**，没有使用 .NET 的 `HttpListener`。原因是 Godot 运行时的网络栈限制。

握手步骤：

1. `TcpListener.AcceptTcpClientAsync()` 接受 TCP 连接
2. 逐字节读取 HTTP 请求头（最大 8192 字节，防止恶意超长头）
3. 提取 `Sec-WebSocket-Key`
4. 计算 `Sec-WebSocket-Accept`（SHA-1 + Base64）
5. 返回 `101 Switching Protocols` 响应
6. 使用 `WebSocket.CreateFromStream()` 进入 WS 通信模式

## 消息格式

所有消息均为 JSON 文本帧。

### Server → Client

**bind（初始绑定）：**

```json
{
  "type": "bind",
  "clientId": "server-guid",
  "targetId": "assigned-target-id",
  "message": "200"
}
```

**msg（指令传递）：**

```json
{
  "type": "msg",
  "clientId": "server-guid",
  "targetId": "target-id",
  "message": "strength-1+1+2+2"
}
```

**heartbeat：**

```json
{
  "type": "heartbeat",
  "clientId": "server-guid",
  "targetId": "target-id",
  "message": "200"
}
```

**error：**

```json
{
  "type": "error",
  "clientId": "server-guid",
  "targetId": "target-id",
  "message": "403"
}
```

### Client → Server

**bind（绑定确认）：**

```json
{
  "type": "bind",
  "message": "200"
}
```

**msg（消息上报）：**

```json
{
  "type": "msg",
  "message": "strength-{currentA}+{limitA}+{currentB}+{limitB}"
}
```

## 指令格式

所有业务指令通过 `msg.message` 字段传递，格式为纯文本字符串。

### 强度控制

```
strength-{channelA}+{channelB}+{channelA}+{channelB}
```

- 前两个值为**强度变化量**（正数增加，负数减少，`0` 保持不变）
- 后两个值为**直接设定值**（`0` 表示不设定）

`DGLabProtocol.StrengthCommand` 的实现：

```csharp
// 示例：A 通道设为 50，B 通道设为 30
StrengthCommand(50, 30) → "strength-0+0+50+30"
```

### 脉冲波形

```
pulse-{channel}:["hex1","hex2",...]
```

- `channel` 为 `"A"` 或 `"B"`
- HEX 数组为 V3 格式波形数据（详见下文）
- APP 内部队列最大缓存 500 条（50 秒），单次数组最大 100 条（10 秒）

### 清除波形

```
clear-{channel}
```

清空指定通道的波形队列。

## V3 波形格式

每条波形数据为 **16 个十六进制字符**（8 字节），代表 **100ms** 的输出，内含 4 组 × 25ms 子脉冲：

```
[freq1][freq2][freq3][freq4][int1][int2][int3][int4]
  2B     2B     2B     2B    2B    2B    2B    2B
```

| 字段          | 范围     | 说明                       |
| :------------ | :------- | :------------------------- |
| `freq` (频率) | 10 - 240 | 每组子脉冲的频率，以 Hz 计 |
| `int` (强度)  | 0 - 100  | 每组子脉冲的相对强度百分比 |

**示例解读：**

```
"0A0A0A0A64646464"
 │         │
 │         └─ int: 0x64=100, 0x64=100, 0x64=100, 0x64=100 → 各 100%
 └─ freq: 0x0A=10, 0x0A=10, 0x0A=10, 0x0A=10 → 各 10Hz
```

`DGLabProtocol.ConstantWaveChunk` 可生成恒定频率/强度的波形块：

```csharp
ConstantWaveChunk(frequency: 100, intensity: 60)
// → "6464646464643C3C3C3C"  (freq=100, int=60)
```

## 内置波形预设

| 波形名称 | 常量名         | 描述                               |
| :------- | :------------- | :--------------------------------- |
| 呼吸     | `BreathWaveV3` | 经典呼吸波，12 组循环              |
| 潮汐     | `TideV3`       | 连绵起伏（为短促电击精简）         |
| 连击     | `BatterV3`     | 强打击感（为短促电击精简）         |
| 快速按捏 | `PinchV3`      | 高频次开关（为短促电击精简）       |
| 按捏渐强 | `PinchRampV3`  | 从弱到强的按捏渐变                 |
| 心跳节奏 | `HeartbeatV3`  | 模拟心跳节律                       |
| 压缩     | `SqueezeV3`    | 频率递减的压缩感（为短促电击精简） |
| 节奏步伐 | `RhythmV3`     | 阶梯式节奏（为短促电击精简）       |

`GetWaveformByName(name)` 按名称查找，不区分大小写。`AllWaveforms` 数组包含全部预设，供 Random 模式使用。

## 自定义波形文件

自定义波形通过 `CustomWaveformLoader` 从 Mod 目录下的 `waveforms/` 加载。

### 文件格式

```jsonc
{
  // 在设置下拉菜单中显示的名称（可选，缺省则使用文件名）
  "name": "我的波形",
  // V3 格式 HEX 数组
  "data": [
    "0A0A0A0A00000000",
    "0A0A0A0A32323232",
    "0A0A0A0A64646464",
    "0A0A0A0A32323232",
    "0A0A0A0A00000000",
  ],
}
```

### 加载规则

- 文件扩展名必须为 `.jsonc`
- 支持 JSONC 注释和尾随逗号
- 每条 HEX 数据必须严格匹配 `^[0-9A-Fa-f]{16}$`（16 位十六进制）
- 不合法的 HEX 条目会被跳过并记录日志
- 波形在下拉菜单中的 key 格式为 `{显示名称}({文件名})`
- `waveforms/` 目录不存在时会自动创建
