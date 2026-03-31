---
title: Quick Start
editLink: false
---

# {{ $frontmatter.title }}

## Environmental Requirements

| Dependency          | Version |
| :------------------ | :------ |
| Godot (with .NET)   | 4.5.1   |
| .NET SDK            | 9.0     |
| C# Language Version | 12.0    |

> [!TIP]
> You do not actually need to install the Godot editor — the project references Godot's C# bindings via the `Godot.NET.Sdk` NuGet package, and compiles with `dotnet build`.

## Cloning and Dependency Setup

```bash
git clone https://github.com/star-whisper9/sts2-tazeu.git
cd sts2-tazeu
```

The project depends on two assemblies from the main game and one NuGet package:

| Reference       | Source        | Description                |
| :-------------- | :------------ | :------------------------- |
| `sts2.dll`      | Game data dir | Slay the Spire 2 Core API  |
| `0Harmony.dll`  | Game data dir | Harmony patching framework |
| `QRCoder` 1.6.0 | NuGet         | QR code generation library |

`TazeU.csproj` will auto-detect the game installation path (macOS arm64 / x86_64). If the path doesn't match, please modify the `<Sts2Dir>` property to point to the correct game root directory:

```xml
<Sts2Dir>/path/to/Slay the Spire 2/SlayTheSpire2.app/Contents/Resources</Sts2Dir>
```

## Build

```bash
dotnet build
```

The `PostBuild` target will automatically copy the artifacts to `mods/TazeU/`:

- `TazeU.dll` — Mod main assembly
- `QRCoder.dll` — Runtime dependency
- `TazeU.json` — Mod metadata
- `default_config.jsonc` — Default configuration (copied only if it doesn't exist in the target directory, it won't overwrite existing user configs)

## Project Directory Structure

```
sts2-tazeu/
├── Scripts/                    # C# source files (all core logic)
│   ├── Entry.cs                # Mod entry point
│   ├── TazeUConfig.cs          # Config models (Serialization / Deserialization)
│   ├── DGLabServer.cs          # WebSocket Server core
│   ├── DGLabProtocol.cs        # Protocol helpers and Waveform presets
│   ├── LightningOrbPatch.cs    # Harmony patch (Hooking Lightning Orb)
│   ├── TazeUOverlay.cs         # In-game UI Overlay
│   ├── QRCodeHelper.cs         # QR code generation tool
│   ├── ModConfigBridge.cs      # ModConfig reflection integration
│   └── CustomWaveformLoader.cs # Custom Waveform file loading
├── Tools/                      # Python auxiliary scripts
│   ├── mock_client.py          # Single client mock (Development & debugging)
│   ├── mock_multi_client.py    # Multi-client mock (One-to-many testing)
│   └── sim_strength.py         # Intensity mapping simulation / tuning tool
├── TazeU.csproj                # Project file
├── TazeU.json                  # Mod metadata
└── default_config.jsonc        # Default configuration
```

## Debugging and Testing

Since the Mod runs within the game process, directly attaching a debugger is relatively difficult. It's recommended to use the mock clients under the `Tools/` directory for joint debugging:

```bash
# Single client mock
python Tools/mock_client.py

# Multi-client mock (testing one-to-many broadcast)
python Tools/mock_multi_client.py
```

The mock clients will simulate the DG-LAB APP's connection handshake and message interaction, verifying WebSocket communication and shock logic without needing to connect to real hardware.

`sim_strength.py` can be used to locally simulate the Stevens' power law mapping and Combo increment calculations:

```bash
python Tools/sim_strength.py
```
