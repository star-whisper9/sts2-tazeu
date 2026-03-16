#!/usr/bin/env python3
"""
Mock DG-LAB APP — 模拟 DG-LAB APP 的 WebSocket v2 协议行为。

用法:
    python3 mock_client.py <clientId> [port]

参数:
    clientId  服务端的 clientId（从游戏日志获取）
    port      WS 端口，默认 9999

游戏日志路径 (macOS):
    ~/Library/Application Support/SlayTheSpire2/logs/godot.log
    搜索: [TazeU] DG-LAB connect URL

依赖:
    pip install websockets
"""

import asyncio
import json
import sys

try:
    import websockets
except ImportError:
    print("需要安装 websockets: pip install websockets")
    sys.exit(1)


async def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return

    client_id = sys.argv[1]
    port = int(sys.argv[2]) if len(sys.argv) > 2 else 9999
    uri = f"ws://localhost:{port}/{client_id}"

    # 模拟 APP 端配置
    strength_limit_a = 100
    strength_limit_b = 100
    current_strength_a = 0
    current_strength_b = 0

    print(f"[Mock] Connecting to {uri} ...")
    print(f"[Mock] Strength limits: A={strength_limit_a}, B={strength_limit_b}")

    async with websockets.connect(uri) as ws:
        print("[Mock] WebSocket connected")

        # ── Step 1: 接收初始 bind（服务端分配 ID）
        initial = await ws.recv()
        print(f"[Mock] << {initial}")
        init_data = json.loads(initial)
        my_id = init_data["clientId"]
        print(f"[Mock] Assigned ID: {my_id}")

        # ── Step 2: 发送 bind 请求
        bind_req = json.dumps({
            "type": "bind",
            "clientId": client_id,
            "targetId": my_id,
        })
        print(f"[Mock] >> {bind_req}")
        await ws.send(bind_req)

        # ── Step 3: 接收 bind 确认
        confirm = await ws.recv()
        print(f"[Mock] << {confirm}")
        confirm_data = json.loads(confirm)
        if confirm_data.get("message") == "200":
            print("[Mock] ✓ Bind successful!")
        else:
            print(f"[Mock] ✗ Bind failed: {confirm_data.get('message')}")

        print()
        print("══════════════════════════════════════════")
        print("  Listening for commands (Ctrl+C to exit)")
        print("══════════════════════════════════════════")
        print()

        # ── 监听指令
        async for raw in ws:
            try:
                data = json.loads(raw)
                message = data.get("message", "")

                if message.startswith("strength-"):
                    print(f"  ⚡ STRENGTH  {message}")
                    # 解析 strength-{channel}+{mode}+{value}
                    parts = message[len("strength-"):].split("+")
                    if len(parts) >= 3:
                        ch = int(parts[0])
                        mode = int(parts[1])
                        val = int(parts[2])
                        if ch == 1:
                            if mode == 0: current_strength_a = max(0, current_strength_a - val)
                            elif mode == 1: current_strength_a = min(strength_limit_a, current_strength_a + val)
                            elif mode == 2: current_strength_a = min(strength_limit_a, val)
                        elif ch == 2:
                            if mode == 0: current_strength_b = max(0, current_strength_b - val)
                            elif mode == 1: current_strength_b = min(strength_limit_b, current_strength_b + val)
                            elif mode == 2: current_strength_b = min(strength_limit_b, val)
                        # 回传强度反馈（格式: strength-{currentA}+{currentB}+{limitA}+{limitB}）
                        fb = f"strength-{current_strength_a}+{current_strength_b}+{strength_limit_a}+{strength_limit_b}"
                        fb_msg = json.dumps({
                            "type": "msg",
                            "clientId": my_id,
                            "targetId": client_id,
                            "message": fb,
                        })
                        await ws.send(fb_msg)
                        print(f"  ⚡ FEEDBACK  {fb}")

                elif message.startswith("pulse-"):
                    colon = message.index(":")
                    channel = message[6:colon]
                    chunks = json.loads(message[colon + 1 :])
                    duration_ms = len(chunks) * 100
                    print(f"  〰 PULSE     channel={channel}  chunks={len(chunks)}  duration={duration_ms}ms")
                    for i, hex_str in enumerate(chunks):
                        if len(hex_str) >= 16:
                            freq = int(hex_str[:2], 16)
                            intens = int(hex_str[8:10], 16)
                            print(f"             [{i:2d}] freq={freq:3d}  intensity={intens:3d}")

                elif message.startswith("clear-"):
                    print(f"  ✕ CLEAR      {message}")

                else:
                    print(f"  ? UNKNOWN    {message}")

            except Exception:
                print(f"  << RAW: {raw}")


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\n[Mock] Disconnected.")
