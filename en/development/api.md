---
title: API Reference
editLink: false
---

# {{ $frontmatter.title }}

This page lists the public/internal APIs of the TazeU Mod. All types are located in `namespace TazeU.Scripts`.

## DGLabServer

WebSocket server core. Injects `TazeUConfig` upon construction, runs in an independent background thread.

```csharp
public class DGLabServer(TazeUConfig config)
```

### Properties

| Property         | Type   | Description                         |
| :--------------- | :----- | :---------------------------------- |
| `IsConnected`    | `bool` | Whether there are any bound clients |
| `ConnectedCount` | `int`  | Number of bound clients             |

### Public Methods

#### `Start()`

Starts the WS listening thread. Creates a `CancellationTokenSource` and runs the TCP listen loop in the background thread `TazeU-WS`.

#### `Stop()`

Stops the server. Cancels the CTS, sends graceful close frames to all connected clients, and stops the TcpListener.

#### `Restart()`

Calls `Stop()` then `Start()`. Automatically triggered by ModConfig callbacks after port or bind address changes.

#### `GetConnectUrl() → string`

Returns the DG-LAB APP scan-to-connect URL:

```
https://www.dungeon-lab.com/app-download.php#DGLAB-SOCKET#ws://{ip}:{port}/{clientId}
```

#### `TriggerShock(decimal damageValue)`

Triggers a shock broadcast. Safe to call from any thread (fire-and-forget).

1. Combo calculation (if enabled)
2. Waveform selection
3. Iterates over all bound clients, mapping intensity independently according to their channel caps
4. Executing shock instruction asynchronously

#### `DisconnectAll()`

Disconnects all connected clients.

#### `DisconnectClient(string targetId)`

Disconnects the client with the specified `targetId`.

#### `BlockClient(string targetId)`

Blocks the specified client's IP address (session-level, not persisted). Subsequent connections from this IP will return a `403` error.

#### `GetConnectedClients() → ClientInfo[]`

Returns information DTOs for all connected clients.

```csharp
public record ClientInfo(string TargetId, string RemoteEndpoint, DateTime ConnectedAt, bool IsBound);
```

### Internal Methods

#### `LoadCustomWaveforms()`

Calls `CustomWaveformLoader.LoadAll()` to load/refresh custom Waveforms. Called in `Entry.Init()` prior to `Start()`.

#### `GetAllWaveformNames() → string[]`

Returns an array of all available Waveform names: 8 built-in presets + custom Waveform keys + `"Random"`. Used by the ModConfig dropdown menu.

## DGLabProtocol

Protocol helper static class, providing Waveform presets and instruction formatting.

```csharp
public static class DGLabProtocol
```

### Waveform Constants

| Constant       | Type         | Description            |
| :------------- | :----------- | :--------------------- |
| `BreathWaveV3` | `string[]`   | Breath (12 sets)       |
| `TideV3`       | `string[]`   | Tide                   |
| `BatterV3`     | `string[]`   | Batter                 |
| `PinchV3`      | `string[]`   | Pinch                  |
| `PinchRampV3`  | `string[]`   | PinchRamp              |
| `HeartbeatV3`  | `string[]`   | Heartbeat              |
| `SqueezeV3`    | `string[]`   | Squeeze                |
| `RhythmV3`     | `string[]`   | Rhythm                 |
| `AllWaveforms` | `string[][]` | All presets collection |

### Static Methods

#### `GetWaveformByName(string name) → string[]?`

Looks up Waveform presets by name, case-insensitive. Returns `null` if not found.

#### `StrengthCommand(int channelA, int channelB) → string`

Generates strength control instructions:

```csharp
StrengthCommand(50, 30) → "strength-0+0+50+30"
```

#### `PulseCommand(string channel, string[] waveHexArray) → string`

Generates pulse Waveform instructions:

```csharp
PulseCommand("A", ["0A0A0A0A64646464"]) → "pulse-A:[\"0A0A0A0A64646464\"]"
```

#### `ClearCommand(string channel) → string`

Generates clear instructions:

```csharp
ClearCommand("A") → "clear-A"
```

#### `ConstantWaveChunk(int frequency = 100, int intensity = 60) → string`

Generates a single Waveform HEX with constant frequency/intensity. Frequency limiting 10-240, intensity limiting 0-100.

## TazeUConfig

Configuration model, JSONC serialization.

```csharp
public class TazeUConfig
```

### Properties

| Property         | Type     | Default Value | Description                                  |
| :--------------- | :------- | :------------ | :------------------------------------------- |
| `Port`           | `int`    | `9999`        | WS listen port                               |
| `BindAddress`    | `string` | `""`          | Custom bind IP (leave blank for auto-detect) |
| `MinStrength`    | `int`    | `5`           | Minimum output intensity (0-200)             |
| `DamageCap`      | `int`    | `25`          | Damage mapping cap                           |
| `Waveform`       | `string` | `"Breath"`    | Waveform preset name                         |
| `UseChannelA`    | `bool`   | `true`        | Enable Channel A                             |
| `UseChannelB`    | `bool`   | `true`        | Enable Channel B                             |
| `ComboEnabled`   | `bool`   | `false`       | Combo Increment toggle                       |
| `ComboRate`      | `float`  | `0.15`        | Increment ratio per stack                    |
| `ComboWindow`    | `float`  | `3.0`         | Combo time window (seconds)                  |
| `ComboMaxStacks` | `int`    | `8`           | Maximum stack layers                         |
| `OnlyOwnOrbs`    | `bool`   | `true`        | Only own Lightning Orbs trigger              |
| `MaxConnections` | `int`    | `8`           | Maximum concurrent connections               |
| `TestDamage`     | `int`    | `3`           | Test shock damage value                      |

### Static Methods

#### `Load() → TazeUConfig`

Loads configs from `default_config.jsonc`. Writes the default config and returns it if the file does not exist. Returns a default instance if a load exception occurs.

### Instance Methods

#### `Save()`

Serializes and writes the current config to disk. Automatically creates the directory if it doesn't exist.

## ModConfigBridge

Connects to ModConfig-STS2 via reflection with zero dependency.

```csharp
internal static class ModConfigBridge
```

### Properties

| Property        | Type   | Description                                                 |
| :-------------- | :----- | :---------------------------------------------------------- |
| `IsAvailable`   | `bool` | Whether ModConfig is available (detected upon first access) |
| `ShowQRKey`     | `long` | Show QR hotkey (Godot Key code)                             |
| `TestShockKey`  | `long` | Test shock hotkey                                           |
| `DisconnectKey` | `long` | Disconnect connection hotkey                                |

### Methods

#### `Register(TazeUConfig config, DGLabServer server)`

Registers all configs items to ModConfig menu.
