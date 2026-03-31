---
title: Protocol Details
editLink: false
---

# {{ $frontmatter.title }}

TazeU embeds a server implementation of the DG-LAB WebSocket v2 protocol, adopting a **one-to-many** architecture — the Mod acts as the WS Server, and multiple DG-LAB APPs act as WS Clients.

## Architecture Overview

```
┌─────────────────────────────────────┐
│  Slay the Spire 2 (Mod = Server)    │
│                                     │
│  TazeU Mod                          │
│  ├─ DGLabServer (TcpListener)       │
│  └─ ws://localIP:port/clientId      │
└──────────┬──────────────────────────┘
           │ WebSocket (One-to-many)
     ┌─────┴─────┐
     ▼           ▼
┌─────────┐ ┌─────────┐
│ DG-LAB  │ │ DG-LAB  │  ...N APPs
│ APP #1  │ │ APP #2  │
│ (BLE)   │ │ (BLE)   │
└────┬────┘ └────┬────┘
     ▼           ▼
┌─────────┐ ┌─────────┐
│Coyote 3 │ │Coyote 3 │  Respective Hardware
└─────────┘ └─────────┘
```

Each APP bridges to its respective Coyote 3.0 hardware via Bluetooth (BLE). Shock events are broadcast by the Server to all bound clients, but each client maps them independently according to **its own channel strength limit**.

## Connection Flow

Each client independently undergoes the following handshake process:

```
Server                                    APP (Client)
  │                                         │
  │  1. Generate clientId (Global GUID)      │
  │  2. Start TcpListener                   │
  │                                         │
  │◄────────── 3. APP Scans to Connect ─────│
  │     ws://ip:port/{clientId}              │
  │                                         │
  │  4. TCP Accept → HTTP → WS Handshake     │
  │                                         │
  │──── 5. Server assigns targetId ────────►│
  │     { type: "bind",                      │
  │       clientId, targetId }               │
  │                                         │
  │◄──── 6. APP replies bind confirm ───────│
  │     { type: "bind", message: "200" }     │
  │                                         │
  │──── 7. Strength to zero ───────────────►│
  │     Triggers APP to return channel limit  │
  │                                         │
  │◄──── 8. Strength Feedback ──────────────│
  │     strength-{currentA}+{limitA}+        │
  │              {currentB}+{limitB}         │
  │                                         │
  │  9. Ready for comms, join broadcast list  │
  ▼                                         ▼
```

> [!TIP]
> Scan URL Format: `https://www.dungeon-lab.com/app-download.php#DGLAB-SOCKET#ws://{ip}:{port}/{clientId}`
>
> The DG-LAB APP parses the anchor part after `#` to obtain the WS address.

## WebSocket Handshake

`DGLabServer` **manually implements the HTTP → WebSocket upgrade handshake**, without using .NET's `HttpListener`. The reason is the network stack limitation of the Godot runtime.

Handshake steps:

1. `TcpListener.AcceptTcpClientAsync()` accepts the TCP connection.
2. Reads the HTTP request headers byte by byte (max 8192 bytes, to prevent malicious overly long headers).
3. Extracts `Sec-WebSocket-Key`.
4. Calculates `Sec-WebSocket-Accept` (SHA-1 + Base64).
5. Returns `101 Switching Protocols` response.
6. Uses `WebSocket.CreateFromStream()` to enter WS communication mode.

## Message Format

All messages are JSON text frames.

### Server → Client

**bind (Initial Binding):**

```json
{
  "type": "bind",
  "clientId": "server-guid",
  "targetId": "assigned-target-id",
  "message": "200"
}
```

**msg (Instruction Passing):**

```json
{
  "type": "msg",
  "clientId": "server-guid",
  "targetId": "target-id",
  "message": "strength-1+1+2+2"
}
```

**heartbeat:**

```json
{
  "type": "heartbeat",
  "clientId": "server-guid",
  "targetId": "target-id",
  "message": "200"
}
```

**error:**

```json
{
  "type": "error",
  "clientId": "server-guid",
  "targetId": "target-id",
  "message": "403"
}
```

### Client → Server

**bind (Binding Confirmation):**

```json
{
  "type": "bind",
  "message": "200"
}
```

**msg (Message Report):**

```json
{
  "type": "msg",
  "message": "strength-{currentA}+{limitA}+{currentB}+{limitB}"
}
```

## Instruction Format

All business instructions are passed through the `msg.message` field in a plain text string format.

### Strength Control

```
strength-{channelA}+{channelB}+{channelA}+{channelB}
```

- The first two values are **strength delta** (positive increases, negative decreases, `0` remains unchanged).
- The last two values are **direct setting values** (`0` means not setting).

`DGLabProtocol.StrengthCommand` implementation:

```csharp
// Example: Channel A set to 50, Channel B set to 30
StrengthCommand(50, 30) → "strength-0+0+50+30"
```

### Pulse Waveform

```
pulse-{channel}:["hex1","hex2",...]
```

- `channel` is `"A"` or `"B"`.
- The HEX array is V3 format Waveform data (detailed below).
- The APP's internal queue caches up to 500 items (50 seconds), with a maximum of 100 items per array (10 seconds).

### Clear Waveform

```
clear-{channel}
```

Clears the Waveform queue for the specified channel.

## V3 Waveform Format

Each Waveform data is **16 hexadecimal characters** (8 bytes), representing **100ms** of output, containing 4 subsets × 25ms sub-pulses:

```
[freq1][freq2][freq3][freq4][int1][int2][int3][int4]
```
