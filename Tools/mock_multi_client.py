#!/usr/bin/env python3
"""
Mock Multi-Client — 同时模拟 N 个 DG-LAB APP 连接到 TazeU 服务端。

用法:
    python3 mock_multi_client.py <clientId> [count] [port]

参数:
    clientId  服务端的 clientId（从游戏日志获取）
    count     同时连接的客户端数量，默认 3
    port      WS 端口，默认 9999

示例:
    python3 mock_multi_client.py abc-def-123             # 3 个客户端
    python3 mock_multi_client.py abc-def-123 5           # 5 个客户端
    python3 mock_multi_client.py abc-def-123 2 8888      # 2 个客户端，端口 8888

游戏日志路径 (macOS):
    ~/Library/Application Support/SlayTheSpire2/logs/godot.log
    搜索: [TazeU] DG-LAB connect URL

依赖:
    pip install websockets
"""

import asyncio
import json
import sys
import signal

try:
    import websockets
except ImportError:
    print("需要安装 websockets: pip install websockets")
    sys.exit(1)

# 每个 mock 客户端的配色（终端 ANSI）
COLORS = [
    "\033[96m",   # cyan
    "\033[93m",   # yellow
    "\033[95m",   # magenta
    "\033[92m",   # green
    "\033[91m",   # red
    "\033[94m",   # blue
    "\033[97m",   # white
    "\033[33m",   # dark yellow
]
RESET = "\033[0m"


async def run_client(index: int, client_id: str, port: int, stop_event: asyncio.Event):
    """单个 mock 客户端的完整生命周期。"""
    color = COLORS[index % len(COLORS)]
    tag = f"{color}[Client-{index}]{RESET}"

    strength_limit_a = 80 + index * 10  # 每个客户端不同上限，方便区分
    strength_limit_b = 80 + index * 10
    current_strength_a = 0
    current_strength_b = 0

    uri = f"ws://localhost:{port}/{client_id}"
    print(f"{tag} Connecting to {uri} (limits A={strength_limit_a} B={strength_limit_b})")

    try:
        async with websockets.connect(uri) as ws:
            print(f"{tag} WebSocket connected")

            # Step 1: 接收初始 bind
            initial = await ws.recv()
            init_data = json.loads(initial)
            my_id = init_data["clientId"]
            print(f"{tag} Assigned targetId: {my_id}")

            # Step 2: 发送 bind 请求
            bind_req = json.dumps({
                "type": "bind",
                "clientId": client_id,
                "targetId": my_id,
            })
            await ws.send(bind_req)

            # Step 3: 接收 bind 确认
            confirm = await ws.recv()
            confirm_data = json.loads(confirm)
            if confirm_data.get("message") == "200":
                print(f"{tag} ✓ Bind successful!")
            else:
                print(f"{tag} ✗ Bind failed: {confirm_data.get('message')}")
                return

            print(f"{tag} Listening for commands...")

            # 监听指令
            async for raw in ws:
                if stop_event.is_set():
                    break
                try:
                    data = json.loads(raw)
                    message = data.get("message", "")

                    if message.startswith("strength-"):
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
                            # 回传强度反馈
                            fb = f"strength-{current_strength_a}+{current_strength_b}+{strength_limit_a}+{strength_limit_b}"
                            fb_msg = json.dumps({
                                "type": "msg",
                                "clientId": my_id,
                                "targetId": client_id,
                                "message": fb,
                            })
                            await ws.send(fb_msg)
                            print(f"{tag} ⚡ A={current_strength_a}/{strength_limit_a} B={current_strength_b}/{strength_limit_b}")

                    elif message.startswith("pulse-"):
                        colon = message.index(":")
                        channel = message[6:colon]
                        chunks = json.loads(message[colon + 1:])
                        print(f"{tag} 〰 PULSE ch={channel} chunks={len(chunks)} ({len(chunks)*100}ms)")

                    elif message.startswith("clear-"):
                        print(f"{tag} ✕ CLEAR {message}")

                    else:
                        print(f"{tag} ? {message}")

                except Exception as e:
                    print(f"{tag} Error: {e}")

    except websockets.exceptions.ConnectionClosed as e:
        print(f"{tag} Connection closed: {e.reason}")
    except Exception as e:
        print(f"{tag} Error: {e}")

    print(f"{tag} Disconnected")


async def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return

    client_id = sys.argv[1]
    count = int(sys.argv[2]) if len(sys.argv) > 2 else 3
    port = int(sys.argv[3]) if len(sys.argv) > 3 else 9999

    print(f"{'='*50}")
    print(f"  Mock Multi-Client: {count} clients → port {port}")
    print(f"  clientId: {client_id}")
    print(f"{'='*50}")
    print()

    stop_event = asyncio.Event()

    # 错开连接时间（200ms间隔），避免同时握手
    tasks = []
    for i in range(count):
        async def launch(idx=i):
            await asyncio.sleep(idx * 0.2)
            await run_client(idx, client_id, port, stop_event)
        tasks.append(asyncio.create_task(launch()))

    # 等待所有客户端完成或被中断
    try:
        await asyncio.gather(*tasks)
    except asyncio.CancelledError:
        pass

    print("\nAll clients finished.")


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\n[Multi-Mock] Ctrl+C — shutting down.")
