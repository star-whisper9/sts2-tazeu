---
title: Project Architecture
editLink: false
---

# {{ $frontmatter.title }}

## Source File Overview

All C# source files are located in the `Scripts/` directory, uniformly using `namespace TazeU.Scripts`.

| File                      | Type                                         | Responsibility                                                                         |
| :------------------------ | :------------------------------------------- | :------------------------------------------------------------------------------------- |
| `Entry.cs`                | `public class Entry`                         | Mod entry point, marked with `[ModInitializer]`, responsible for global initialization |
| `TazeUConfig.cs`          | `public class TazeUConfig`                   | Config POCO, JSONC serialization and validation                                        |
| `DGLabServer.cs`          | `public class DGLabServer`                   | WebSocket server, one-to-many connection management and shock broadcasting             |
| `DGLabProtocol.cs`        | `public static class DGLabProtocol`          | Waveform preset constants, protocol instruction formatting                             |
| `LightningOrbPatch.cs`    | `static class × 2`                           | Harmony Postfix patches, hooking Lightning Orb Passive/Evoke                           |
| `TazeUOverlay.cs`         | `internal partial class TazeUOverlay : Node` | Godot Node, QR popup + client management + hotkey processing                           |
| `QRCodeHelper.cs`         | `internal static class QRCodeHelper`         | QRCoder → Godot ImageTexture                                                           |
| `ModConfigBridge.cs`      | `internal static class ModConfigBridge`      | Reflection integration with ModConfig-STS2, zero compile-time dependency               |
| `CustomWaveformLoader.cs` | `internal static class CustomWaveformLoader` | Loads custom Waveforms from `waveforms/*.jsonc`                                        |

## Mod Initialization Flow

`Entry.Init()` is the sole entry point of the entire Mod, called by the STS2 Modding Framework via `[ModInitializer("Init")]` when the game starts.

```
Entry.Init()
  │
  ├─ 1. Register AssemblyResolve
  │     Ensures dependencies like QRCoder DLL can be loaded from the mod directory at runtime
  │
  ├─ 2. Harmony.PatchAll()
  │     Automatically discovers and applies LightningOrbPassivePatch / LightningOrbEvokePatch
  │
  ├─ 3. ScriptManagerBridge.LookupScriptsInAssembly()
  │     Registers Godot scripts (partial class nodes like TazeUOverlay)
  │
  ├─ 4. TazeUConfig.Load()
  │     Loads default_config.jsonc, or writes default config if it doesn't exist
  │
  ├─ 5. new DGLabServer(config) → LoadCustomWaveforms() → Start()
  │     Starts background WS thread, begins listening
  │
  └─ 6. SceneTree Delayed Callbacks (Two frames)
        ├─ Frame 1: Mount TazeUOverlay to Root
        └─ Frame 2: ModConfigBridge.Register() registers config items
```

> [!NOTE]
> Overlay and ModConfig need to be registered with a delay because `Init()` is called before the SceneTree is fully ready, at which point the Root node might not be available yet. `CreateTimer(0)` is used to guarantee execution in the next idle frame.

## Core Data Flow

The complete process from Lightning Orb triggering to issuing the physical shock:

```
Lightning Orb Passive/Evoke Triggered
          │
          ▼
 LightningOrbPatch (Harmony Postfix)
          │ Extract damage value
          │ Optional: OnlyOwnOrbs filter (compare NetId)
          ▼
 Entry.Server.TriggerShock(damage)
          │
          ├─ Combo Calculation
          │   If ComboEnabled && time since last shock ≤ ComboWindow
          │   → comboCount++ (Cap at ComboMaxStacks)
          │   → effectiveDamage = damage × (1 + comboCount × ComboRate)
          │
          ├─ SelectWaveform()
          │   Select Waveform preset / custom Waveform / Random based on config
          │
          └─ Broadcast to all bound clients
              For each client:
              ├─ MapDamageToStrength(effectiveDamage, client.StrengthLimitA/B)
              │   Stevens' power law inverse mapping → physical strength value
              └─ ExecuteShockForClientAsync(client, strengthA, strengthB, waveform)
                  ├─ StrengthCommand → Set channel strength
                  └─ PulseCommand → Issue Waveform data
```

## Intensity Mapping Algorithm

Based on the inverse mapping of **Stevens' power law**:

The relationship between the human perceived intensity of electrical stimulation $S$ and the physical stimulus intensity $I$ is:

$$S \propto I^{3.5}$$

To make **perceived intensity proportional to game damage**, we need:

$$I \propto \text{damage}^{1/3.5}$$

The specific mapping formula is:

$$\text{strength} = \text{MinStrength} + (\text{maxStrength} - \text{MinStrength}) \times \left(\frac{\min(\text{damage}, \text{DamageCap})}{\text{DamageCap}}\right)^{1/3.5}$$

Where `maxStrength` is the physical channel limit set by each client in their APP, which is returned by the client upon binding.

## Configuration System

### TazeUConfig

Configurations are stored in JSONC format in `default_config.jsonc` under the Mod directory, serialized using `System.Text.Json`:

- `PropertyNamingPolicy = CamelCase` — JSON fields use camelCase
- `ReadCommentHandling = Skip` — Supports JSONC comments
- `AllowTrailingCommas = true` — Allows trailing commas

`Load()` automatically writes the default configuration and returns a new instance when the file does not exist. `Validate()` is responsible for clipping validation.

### ModConfig Integration

`ModConfigBridge` dynamically discovers the API of ModConfig-STS2 through reflection (`Type.GetType`), achieving **zero compile-time dependency** — the Mod continues to run normally even if the player hasn't installed ModConfig.

Registration flow:

1. The `IsAvailable` getter, when accessed for the first time, attempts to discover three types: `ModConfigApi`, `ConfigEntry`, and `ConfigType`.
2. `Register()` builds all configuration items (Slider/Toggle/Dropdown/KeyBind/TextInput/Header).
3. `SyncSavedValues()` reads values back from ModConfig's persistent storage, syncing to the `TazeUConfig` instance.
4. The `OnChanged` callback for each item updates `TazeUConfig` properties in real-time.

> [!NOTE]
> Modifying Port and BindAddress will automatically trigger `server.Restart()` to restart the WebSocket service.

## UI System

`TazeUOverlay` inherits from Godot's `Node`, mounted to `SceneTree.Root`. It captures global keys via `_UnhandledKeyInput`:

| Hotkey (Configured by ModConfig) | Function                                        |
| :------------------------------- | :---------------------------------------------- |
| ShowQRKey                        | Toggles QR Code popup display / hide            |
| TestShockKey                     | Triggers test shock with the `TestDamage` value |
| DisconnectKey                    | Disconnects all connected clients               |

The QR Popup is a `CanvasLayer` (Layer=100), containing a semi-transparent background mask, a panel, the QR Code image, a status label, and a list of connected clients (supporting Kick/Block). The client list refreshes automatically via a `Timer`.
