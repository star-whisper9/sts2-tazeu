using System;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Logging;

namespace TazeU.Scripts;

/// <summary>
/// 内嵌 WebSocket 服务端，实现 DG-LAB WebSocket v2 协议。
/// 独立后台线程运行，不阻塞游戏主线程。
///
/// 架构说明（双方模式，Mod = server + client 一体）：
///   - Mod 充当 WS 服务端，DG-LAB APP 作为 WS 客户端连接
///   - APP 通过蓝牙桥接到 Coyote 3.0 硬件
///
/// 连接流程：
///   1. 生成 clientId，启动 WS 监听
///   2. APP 扫码连接 ws://ip:port/clientId
///   3. 服务端分配 targetId，发送初始 bind（告知 APP 其 ID）
///   4. APP 回复 bind 请求 → 服务端确认（message="200"）
///   5. 服务端发送 strength 归零触发 APP 回传通道上限
///   6. 通信就绪
///
/// WS 链路消息类型（Mod ↔ APP）：
///   - bind: 握手阶段双向
///   - msg:  业务通信双向（强度/波形/清空/回传/反馈）
/// </summary>
public class DGLabServer
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private WebSocket? _appSocket;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private readonly string _clientId = Guid.NewGuid().ToString();
    private string? _targetId;
    private volatile bool _isBound;

    private readonly TazeUConfig _config;
    private readonly Random _random = new();

    public bool IsConnected => _appSocket?.State == WebSocketState.Open && _isBound;

    // APP 回传的通道上限与当前值
    private int _strengthLimitA = 200;
    private int _strengthLimitB = 200;
    private int _currentStrengthA;
    private int _currentStrengthB;

    public DGLabServer(TazeUConfig config)
    {
        _config = config;
    }

    // #region 服务端控制

    public void Start()
    {
        _cts = new CancellationTokenSource();
        new Thread(RunServer) { IsBackground = true, Name = "TazeU-WS" }.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _appSocket?.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", CancellationToken.None).Wait(2000); } catch { }
        try { _listener?.Stop(); } catch { }
        _sendLock.Dispose();
    }

    /// <summary>
    /// WS 监听主循环。接受连接 → 处理消息 → 连接断开。
    /// </summary>
    /// <remarks>
    /// 连接异常（如 APP 突然断开）会抛出异常，捕获后循环继续等待新连接。
    /// 关闭服务器时会触发取消，抛出 OperationCanceledException，捕获后退出循环。
    /// 其他异常仅记录日志，继续等待新连接。
    /// </remarks>
    private async void RunServer()
    {
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{_config.Port}/");
            _listener.Start();

            var localIp = DetectLocalIp();
            var connectUrl = $"https://www.dungeon-lab.com/app-download.php#DGLAB-SOCKET#ws://{localIp}:{_config.Port}/{_clientId}";
            Log.Info($"[TazeU] WS server started on port {_config.Port}");
            Log.Info($"[TazeU] DG-LAB connect URL: {connectUrl}");

            while (!_cts!.Token.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync();
                if (context.Request.IsWebSocketRequest)
                {
                    _ = HandleConnectionAsync(context);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
        }
        catch (Exception ex)
        {
            // 仅在非关闭场景下记录错误
            if (_cts is not { IsCancellationRequested: true })
                Log.Info($"[TazeU] WS server error: {ex.Message}");
        }
    }

    // #endregion

    // #region 连接处理

    private async Task HandleConnectionAsync(HttpListenerContext httpContext)
    {
        try
        {
            var wsContext = await httpContext.AcceptWebSocketAsync(null);
            var socket = wsContext.WebSocket;

            // 替换已有连接
            if (_appSocket?.State == WebSocketState.Open)
            {
                _isBound = false;
                try { await _appSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "replaced", CancellationToken.None); } catch { }
            }

            // 为 APP 分配 targetId
            _targetId = Guid.NewGuid().ToString();
            _appSocket = socket;
            _isBound = false;

            // Step 1: 发送初始 bind — 告知 APP 它的 ID
            await SendRawAsync(JsonSerializer.Serialize(new
            {
                type = "bind",
                clientId = _targetId,  // APP 自身的 ID
                targetId = "",
                message = "targetId"
            }));
            Log.Info($"[TazeU] APP connected, assigned targetId={_targetId}, awaiting bind...");

            // Step 2: 接收循环中处理 bind 请求及后续消息
            await ReceiveLoopAsync(socket);
        }
        catch (Exception ex)
        {
            Log.Info($"[TazeU] Connection error: {ex.Message}");
        }
    }

    private async Task ReceiveLoopAsync(WebSocket socket)
    {
        var buffer = new byte[4096];
        try
        {
            while (socket.State == WebSocketState.Open && !_cts!.Token.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Log.Info("[TazeU] APP disconnected");
                    _isBound = false;
                    break;
                }
                var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                Log.Info($"[TazeU] From APP: {msg}");
                await HandleAppMessageAsync(msg);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Info($"[TazeU] Receive error: {ex.Message}");
            _isBound = false;
        }
    }

    private async Task HandleAppMessageAsync(string rawMessage)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawMessage);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;

            switch (type)
            {
                case "bind":
                    // APP 发送 bind 请求 → 确认配对
                    await SendRawAsync(JsonSerializer.Serialize(new
                    {
                        type = "bind",
                        clientId = _clientId,
                        targetId = _targetId,
                        message = "200" // 成功码
                    }));
                    _isBound = true;
                    Log.Info("[TazeU] Bind confirmed");

                    // 绑定完成后，设置双通道强度为 0 以触发 APP 回传当前上限
                    await SendCommandAsync(DGLabProtocol.StrengthCommand(1, 2, 0));
                    await SendCommandAsync(DGLabProtocol.StrengthCommand(2, 2, 0));
                    Log.Info("[TazeU] Initial strength query sent");
                    break;

                case "msg":
                    // APP 回传的业务消息（强度反馈、按钮反馈等）
                    if (message != null)
                        HandleIncomingMessage(message);
                    break;

                default:
                    Log.Info($"[TazeU] Unknown message type: {type}");
                    Log.Debug($"[TazeU] Full message: {rawMessage}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Info($"[TazeU] Message parse error: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理 APP 通过 msg 类型发来的业务消息。
    /// </summary>
    private void HandleIncomingMessage(string message)
    {
        if (message.StartsWith("strength-"))
        {
            // 格式: strength-{currentA}+{currentB}+{limitA}+{limitB}
            var parts = message["strength-".Length..].Split('+');
            if (parts.Length >= 4
                && int.TryParse(parts[0], out var currentA)
                && int.TryParse(parts[1], out var currentB)
                && int.TryParse(parts[2], out var limitA)
                && int.TryParse(parts[3], out var limitB))
            {
                _currentStrengthA = currentA;
                _currentStrengthB = currentB;
                _strengthLimitA = limitA;
                _strengthLimitB = limitB;
                Log.Info($"[TazeU] Strength feedback: A={currentA}/{limitA}, B={currentB}/{limitB}");
            }
        }
        else if (message.StartsWith("feedback-"))
        {
            // APP 端用户按钮操作反馈（0~4=A通道, 5~9=B通道）
            Log.Info($"[TazeU] APP feedback: {message}");
        }
        else
        {
            Log.Info($"[TazeU] APP message: {message}");
        }
    }

    // #endregion

    // #region 发送指令

    private async Task SendRawAsync(string message)
    {
        if (_appSocket?.State != WebSocketState.Open) return;
        await _sendLock.WaitAsync();
        try
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await _appSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                _cts?.Token ?? CancellationToken.None);
        }
        catch (Exception ex) { Log.Info($"[TazeU] Send error: {ex.Message}"); }
        finally { _sendLock.Release(); }
    }

    private async Task SendCommandAsync(string command)
    {
        if (_targetId == null) return;
        var json = JsonSerializer.Serialize(new
        {
            type = "msg",
            clientId = _clientId,
            targetId = _targetId,
            message = command
        });
        await SendRawAsync(json);
    }

    // #endregion

    // #region 指令封装

    /// <summary>
    /// 触发一次电击。可从游戏线程安全调用（fire-and-forget）。
    /// 对数映射 damage → [MinStrength, channelLimit]，A/B 通道独立约束。
    /// </summary>
    public void TriggerShock(decimal damageValue)
    {
        if (!IsConnected) return;
        int damage = (int)Math.Max(damageValue, 0);
        int strengthA = _config.UseChannelA ? MapDamageToStrength(damage, _strengthLimitA) : 0;
        int strengthB = _config.UseChannelB ? MapDamageToStrength(damage, _strengthLimitB) : 0;
        Log.Info($"[TazeU] Shock triggered (damage={damage}, A={strengthA}/{_strengthLimitA}, B={strengthB}/{_strengthLimitB})");
        _ = Task.Run(() => ExecuteShockAsync(strengthA, strengthB));
    }

    /// <summary>
    /// Stevens 幂律逆映射：damage → [MinStrength, maxStrength]。
    /// 感知强度 S ∝ I^3.5，为使感知与伤害成正比，需 I ∝ damage^(1/3.5)。
    /// </summary>
    private int MapDamageToStrength(int damage, int maxStrength)
    {
        if (maxStrength <= _config.MinStrength) return maxStrength;
        double ratio = Math.Pow((double)damage / _config.DamageCap, 1.0 / 3.5);
        ratio = Math.Clamp(ratio, 0.0, 1.0);
        return _config.MinStrength + (int)Math.Round((maxStrength - _config.MinStrength) * ratio);
    }

    /// <summary>
    /// 根据配置选择波形预设，支持 Random 模式。
    /// </summary>
    private string[] SelectWaveform()
    {
        var name = _config.Waveform;
        if (string.Equals(name, "Random", StringComparison.OrdinalIgnoreCase))
        {
            var all = DGLabProtocol.AllWaveforms;
            return all[_random.Next(all.Length)];
        }
        return DGLabProtocol.GetWaveformByName(name) ?? DGLabProtocol.BreathWaveV3;
    }

    private async Task ExecuteShockAsync(int strengthA, int strengthB)
    {
        try
        {
            var waveform = SelectWaveform();

            // 1. 设定通道强度（各自独立）
            if (_config.UseChannelA)
                await SendCommandAsync(DGLabProtocol.StrengthCommand(1, 2, strengthA));
            if (_config.UseChannelB)
                await SendCommandAsync(DGLabProtocol.StrengthCommand(2, 2, strengthB));

            // 2. 批量发送波形（APP 内部队列自动按 100ms/条播放）
            if (_config.UseChannelA)
                await SendCommandAsync(DGLabProtocol.PulseCommand("A", waveform));
            if (_config.UseChannelB)
                await SendCommandAsync(DGLabProtocol.PulseCommand("B", waveform));
        }
        catch (Exception ex)
        {
            Log.Info($"[TazeU] Shock execution error: {ex.Message}");
        }
    }

    // #endregion

    /// <summary>
    /// 查找本机局域网 IP（UDP 路由表查询，不实际发送数据）。
    /// </summary>
    private static string DetectLocalIp()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("114.114.114.114", 65530);
            return ((IPEndPoint)socket.LocalEndPoint!).Address.ToString();
        }
        catch
        {
            return "127.0.0.1";
        }
    }
}
