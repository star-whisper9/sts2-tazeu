using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;

namespace TazeU.Scripts;

/// <summary>
/// 内嵌 WebSocket 服务端，实现 DG-LAB WebSocket v2 协议。支持一对多连接。
/// 独立后台线程运行，不阻塞游戏主线程。
///
/// 架构说明（一对多模式，Mod = server，N 个 DG-LAB APP = clients）：
///   - Mod 充当 WS 服务端，多个 DG-LAB APP 作为 WS 客户端连接
///   - 每个 APP 通过蓝牙桥接到各自的 Coyote 3.0 硬件
///   - 电击事件广播给所有已绑定的客户端
///
/// 连接流程（每个客户端独立）：
///   1. 生成 clientId（服务端唯一），启动 WS 监听
///   2. APP 扫码连接 ws://ip:port/clientId
///   3. 服务端为该 APP 分配独立 targetId，发送初始 bind
///   4. APP 回复 bind 请求 → 服务端确认（message="200"）
///   5. 服务端发送 strength 归零触发该 APP 回传通道上限
///   6. 通信就绪，加入广播列表
/// </summary>
public class DGLabServer(TazeUConfig config)
{
    #region 嵌套类型

    /// <summary>
    /// 单个已连接的 APP 客户端。每个客户端维护自己的 WS 连接、通道上限和发送锁。
    /// </summary>
    internal class AppClient(string targetId, WebSocket socket, string remoteEndpoint)
    {
        public string TargetId { get; } = targetId;
        public WebSocket Socket { get; } = socket;
        public string RemoteEndpoint { get; } = remoteEndpoint;
        public DateTime ConnectedAt { get; } = DateTime.UtcNow;
        public volatile bool IsBound;
        public int StrengthLimitA = 200;
        public int StrengthLimitB = 200;
        public int CurrentStrengthA;
        public int CurrentStrengthB;
        public readonly SemaphoreSlim SendLock = new(1, 1);
    }

    /// <summary>供 UI 展示的客户端信息 DTO。</summary>
    public record ClientInfo(string TargetId, string RemoteEndpoint, DateTime ConnectedAt, bool IsBound);

    #endregion

    #region 字段

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, AppClient> _clients = new();
    private readonly HashSet<string> _blockedEndpoints = [];
    private readonly object _blockLock = new();

    private readonly string _clientId = Guid.NewGuid().ToString();
    private readonly TazeUConfig _config = config;
    private readonly Random _random = new();

    // Combo 连击状态（所有客户端共享）
    private int _comboCount;
    private DateTime _lastShockTime = DateTime.MinValue;

    // 自定义波形
    private Dictionary<string, CustomWaveform> _customWaveforms = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>是否有任意已绑定的客户端。</summary>
    public bool IsConnected => _clients.Values.Any(c => c.IsBound);

    /// <summary>已绑定客户端数量。</summary>
    public int ConnectedCount => _clients.Values.Count(c => c.IsBound);

    #endregion

    #region 服务端控制

    /// <summary>
    /// 获取 DG-LAB APP 扫码连接 URL。
    /// </summary>
    public string GetConnectUrl()
    {
        var localIp = GetIpAddress();
        return $"https://www.dungeon-lab.com/app-download.php#DGLAB-SOCKET#ws://{localIp}:{_config.Port}/{_clientId}";
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        new Thread(RunServer) { IsBackground = true, Name = "TazeU-WS" }.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        var snapshot = _clients.ToArray();
        _clients.Clear();
        foreach (var kvp in snapshot)
        {
            try { kvp.Value.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", CancellationToken.None).Wait(2000); } catch { }
        }
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    /// <summary>
    /// 重启服务端（端口变更后调用）。
    /// </summary>
    public void Restart()
    {
        Log.Debug($"[TazeU] Restarting WS server on port {_config.Port}...");
        Stop();
        Start();
    }

    /// <summary>
    /// 断开所有已连接的 APP 客户端（保持服务端监听）。
    /// </summary>
    public void DisconnectAll()
    {
        var snapshot = _clients.ToArray();
        _clients.Clear();
        foreach (var kvp in snapshot)
        {
            try
            {
                kvp.Value.IsBound = false;
                kvp.Value.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnect", CancellationToken.None).Wait(2000);
            }
            catch { }
        }
        Log.Debug("[TazeU] All clients disconnected");
    }

    /// <summary>
    /// 断开指定客户端。
    /// </summary>
    public void DisconnectClient(string targetId)
    {
        if (!_clients.TryRemove(targetId, out var client)) return;
        client.IsBound = false;
        try { client.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "kicked", CancellationToken.None).Wait(2000); } catch { }
        Log.Debug($"[TazeU] Client {targetId} ({client.RemoteEndpoint}) kicked");
    }

    /// <summary>
    /// 屏蔽指定客户端（断开 + 加入本次会话屏蔽列表）。
    /// </summary>
    public void BlockClient(string targetId)
    {
        if (_clients.TryGetValue(targetId, out var client))
        {
            lock (_blockLock) { _blockedEndpoints.Add(client.RemoteEndpoint); }
            DisconnectClient(targetId);
            Log.Debug($"[TazeU] Client {client.RemoteEndpoint} blocked for this session");
        }
    }

    /// <summary>
    /// 获取当前所有连接的客户端信息（供 UI 展示）。
    /// </summary>
    public IReadOnlyList<ClientInfo> GetConnectedClients()
    {
        return _clients.Values
            .Select(c => new ClientInfo(c.TargetId, c.RemoteEndpoint, c.ConnectedAt, c.IsBound))
            .OrderBy(c => c.ConnectedAt)
            .ToList();
    }

    /// <summary>断开所有连接（兼容旧快捷键调用）。</summary>
    public void Disconnect() => DisconnectAll();

    /// <summary>
    /// WS 监听主循环。接受连接 → 处理消息 → 连接断开。
    /// </summary>
    private async void RunServer()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, _config.Port);
            _listener.Start();

            var connectUrl = GetConnectUrl();
            Log.Debug($"[TazeU] WS server started on port {_config.Port} (TcpListener bypasses http.sys)");
            Log.Info($"[TazeU] DG-LAB connect URL: {connectUrl}");

            while (!_cts!.Token.IsCancellationRequested)
            {
                var tcpClient = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = HandleConnectionAsync(tcpClient);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (_cts is not { IsCancellationRequested: true })
                Log.Error($"[TazeU] WS server error: {ex.Message}");
        }
    }

    #endregion

    #region 连接处理

    private async Task HandleConnectionAsync(TcpClient tcpClient)
    {
        var remoteEp = "";
        try
        {
            remoteEp = (tcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";

            // 检查屏蔽列表
            lock (_blockLock)
            {
                if (_blockedEndpoints.Contains(remoteEp))
                {
                    Log.Debug($"[TazeU] Blocked client {remoteEp} rejected");
                    tcpClient.Close();
                    return;
                }
            }

            // 检查最大连接数
            if (_clients.Count >= _config.MaxConnections)
            {
                Log.Debug($"[TazeU] Max connections ({_config.MaxConnections}) reached, rejecting {remoteEp}");
                tcpClient.Close();
                return;
            }

            var stream = tcpClient.GetStream();
            
            // 1. 读取 HTTP 握手头（逐字节读取，防止读丢即将到来的 WebSocket 数据帧）
            var headerBytes = new List<byte>();
            var buf = new byte[1];
            while (await stream.ReadAsync(buf, _cts?.Token ?? CancellationToken.None) > 0)
            {
                headerBytes.Add(buf[0]);
                if (headerBytes.Count >= 4 &&
                    headerBytes[^4] == '\r' && headerBytes[^3] == '\n' &&
                    headerBytes[^2] == '\r' && headerBytes[^1] == '\n')
                {
                    break;
                }
                if (headerBytes.Count > 8192) throw new Exception("HTTP header too long");
            }

            var requestString = Encoding.UTF8.GetString(headerBytes.ToArray());
            string? secKey = null;
            foreach (var line in requestString.Split("\r\n"))
            {
                if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                {
                    secKey = line.Substring("Sec-WebSocket-Key:".Length).Trim();
                    break;
                }
            }

            if (secKey == null)
            {
                tcpClient.Close();
                return;
            }

            // 2. 发送 101 Switching Protocols 以完成 WebSocket 握手
            var acceptKey = Convert.ToBase64String(
                System.Security.Cryptography.SHA1.HashData(
                    Encoding.UTF8.GetBytes(secKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")
                )
            );

            var response = "HTTP/1.1 101 Switching Protocols\r\n" +
                           "Upgrade: websocket\r\n" +
                           "Connection: Upgrade\r\n" +
                           $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";

            var responseBytes = Encoding.UTF8.GetBytes(response);
            await stream.WriteAsync(responseBytes, _cts?.Token ?? CancellationToken.None);

            // 3. 升级为 WebSocket
            var socket = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.FromSeconds(120));

            // 创建客户端实例并加入字典
            var targetId = Guid.NewGuid().ToString();
            var appClient = new AppClient(targetId, socket, remoteEp);
            _clients[targetId] = appClient;

            // Step 1: 发送初始 bind — 告知 APP 它的 ID
            await SendRawToClientAsync(appClient, JsonSerializer.Serialize(new
            {
                type = "bind",
                clientId = targetId,  // APP 自身的 ID
                targetId = "",
                message = "targetId"
            }));
            Log.Debug($"[TazeU] APP connected from {remoteEp}, assigned targetId={targetId}, awaiting bind...");

            // Step 2: 接收循环中处理 bind 请求及后续消息
            await ReceiveLoopAsync(appClient);
        }
        catch (Exception ex)
        {
            Log.Error($"[TazeU] Connection error ({remoteEp}): {ex.Message}");
            tcpClient.Close();
        }
    }

    private async Task ReceiveLoopAsync(AppClient client)
    {
        var buffer = new byte[4096];
        try
        {
            while (client.Socket.State == WebSocketState.Open && !_cts!.Token.IsCancellationRequested)
            {
                var result = await client.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Log.Debug($"[TazeU] APP {client.RemoteEndpoint} disconnected");
                    client.IsBound = false;
                    _clients.TryRemove(client.TargetId, out _);
                    try 
                    {
                        await client.Socket.CloseAsync(result.CloseStatus ?? WebSocketCloseStatus.NormalClosure, result.CloseStatusDescription ?? "Closed by client", CancellationToken.None);
                    }
                    catch { }
                    break;
                }
                var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                Log.Info($"[TazeU] From APP {client.RemoteEndpoint}: {msg}");
                await HandleAppMessageAsync(client, msg);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error($"[TazeU] Receive error ({client.RemoteEndpoint}): {ex.Message}");
            client.IsBound = false;
            _clients.TryRemove(client.TargetId, out _);
        }
    }

    private async Task HandleAppMessageAsync(AppClient client, string rawMessage)
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
                    await SendRawToClientAsync(client, JsonSerializer.Serialize(new
                    {
                        type = "bind",
                        clientId = _clientId,
                        targetId = client.TargetId,
                        message = "200" // 成功码
                    }));
                    client.IsBound = true;
                    Log.Debug($"[TazeU] Bind confirmed for {client.RemoteEndpoint}");

                    // 绑定完成后，设置双通道强度为 0 以触发 APP 回传当前上限
                    await SendCommandToClientAsync(client, DGLabProtocol.StrengthCommand(1, 2, 0));
                    await SendCommandToClientAsync(client, DGLabProtocol.StrengthCommand(2, 2, 0));
                    Log.Debug($"[TazeU] Initial strength query sent to {client.RemoteEndpoint}");
                    break;

                case "msg":
                    // APP 回传的业务消息（强度反馈、按钮反馈等）
                    if (message != null)
                        HandleIncomingMessage(client, message);
                    break;

                default:
                    Log.Debug($"[TazeU] Unknown message type from {client.RemoteEndpoint}: {type}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[TazeU] Message parse error ({client.RemoteEndpoint}): {ex.Message}");
        }
    }

    /// <summary>
    /// 处理 APP 通过 msg 类型发来的业务消息。
    /// </summary>
    private void HandleIncomingMessage(AppClient client, string message)
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
                client.CurrentStrengthA = currentA;
                client.CurrentStrengthB = currentB;
                client.StrengthLimitA = limitA;
                client.StrengthLimitB = limitB;
                Log.Debug($"[TazeU] Strength feedback from {client.RemoteEndpoint}: A={currentA}/{limitA}, B={currentB}/{limitB}");
            }
        }
        else if (message.StartsWith("feedback-"))
        {
            Log.Debug($"[TazeU] APP feedback from {client.RemoteEndpoint}: {message}");
        }
        else
        {
            Log.Debug($"[TazeU] APP message from {client.RemoteEndpoint}: {message}");
        }
    }

    #endregion

    #region 发送指令

    private async Task SendRawToClientAsync(AppClient client, string message)
    {
        if (client.Socket.State != WebSocketState.Open) return;
        await client.SendLock.WaitAsync();
        try
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await client.Socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                _cts?.Token ?? CancellationToken.None);
        }
        catch (Exception ex) { Log.Error($"[TazeU] Send error to {client.RemoteEndpoint}: {ex.Message}"); }
        finally { client.SendLock.Release(); }
    }

    private async Task SendCommandToClientAsync(AppClient client, string command)
    {
        if (client.TargetId == null) return;
        var json = JsonSerializer.Serialize(new
        {
            type = "msg",
            clientId = _clientId,
            targetId = client.TargetId,
            message = command
        });
        await SendRawToClientAsync(client, json);
    }

    /// <summary>
    /// 触发一次电击，广播到所有已绑定客户端。可从游戏线程安全调用（fire-and-forget）。
    /// 共享 Combo 计数器和波形选择，各客户端按自身强度上限独立映射。
    /// </summary>
    public void TriggerShock(decimal damageValue)
    {
        if (!IsConnected) return;
        int damage = (int)Math.Max(damageValue, 0);

        // Combo 连击递增（全局共享）
        double effectiveDamage = damage;
        if (_config.ComboEnabled)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastShockTime).TotalSeconds <= _config.ComboWindow)
                _comboCount = Math.Min(_comboCount + 1, _config.ComboMaxStacks);
            else
                _comboCount = 0;
            _lastShockTime = now;
            effectiveDamage = damage * (1.0 + _comboCount * _config.ComboRate);
        }

        // 共享波形选择
        var waveform = SelectWaveform();

        // 广播到所有已绑定客户端
        foreach (var client in _clients.Values)
        {
            if (!client.IsBound || client.Socket.State != WebSocketState.Open) continue;

            int strengthA = _config.UseChannelA ? MapDamageToStrength(effectiveDamage, client.StrengthLimitA) : 0;
            int strengthB = _config.UseChannelB ? MapDamageToStrength(effectiveDamage, client.StrengthLimitB) : 0;
            Log.Debug($"[TazeU] Shock → {client.RemoteEndpoint} (damage={damage}, combo={_comboCount}, effective={effectiveDamage:F1}, A={strengthA}/{client.StrengthLimitA}, B={strengthB}/{client.StrengthLimitB})");
            var c = client; var wa = strengthA; var wb = strengthB; var wf = waveform;
            _ = Task.Run(() => ExecuteShockForClientAsync(c, wa, wb, wf));
        }
    }

    #endregion

    #region 内部方法

    /// <summary>
    /// Stevens 幂律逆映射：damage → [MinStrength, maxStrength]。
    /// 感知强度 S ∝ I^3.5，为使感知与伤害成正比，需 I ∝ damage^(1/3.5)。
    /// </summary>
    private int MapDamageToStrength(double damage, int maxStrength)
    {
        if (maxStrength <= _config.MinStrength) return maxStrength;
        double ratio = Math.Pow(damage / _config.DamageCap, 1.0 / 3.5);
        ratio = Math.Clamp(ratio, 0.0, 1.0);
        return _config.MinStrength + (int)Math.Round((maxStrength - _config.MinStrength) * ratio);
    }

    /// <summary>
    /// 根据配置选择波形预设，支持 Random 模式和自定义波形。
    /// </summary>
    private string[] SelectWaveform()
    {
        var name = _config.Waveform;
        if (string.Equals(name, "Random", StringComparison.OrdinalIgnoreCase))
        {
            var all = new List<string[]>(DGLabProtocol.AllWaveforms);
            foreach (var cw in _customWaveforms.Values)
                all.Add(cw.Data);
            return all[_random.Next(all.Count)];
        }

        var builtin = DGLabProtocol.GetWaveformByName(name);
        if (builtin != null) return builtin;

        if (_customWaveforms.TryGetValue(name, out var custom))
            return custom.Data;

        return DGLabProtocol.BreathWaveV3;
    }

    /// <summary>
    /// 加载自定义波形文件。启动时及需要刷新时调用。
    /// </summary>
    internal void LoadCustomWaveforms()
    {
        _customWaveforms = CustomWaveformLoader.LoadAll();
    }

    /// <summary>
    /// 获取所有可用波形名称（内置 + 自定义 + Random）。
    /// </summary>
    internal string[] GetAllWaveformNames()
    {
        var names = new List<string>
        {
            "Breath", "Tide", "Batter", "Pinch", "PinchRamp",
            "Heartbeat", "Squeeze", "Rhythm"
        };
        foreach (var kvp in _customWaveforms)
            names.Add(kvp.Key);
        names.Add("Random");
        return names.ToArray();
    }

    /// <summary>
    /// 执行对单个客户端的电击指令。独立 Task 运行，捕获异常防止影响其他客户端。
    /// </summary>
    private async Task ExecuteShockForClientAsync(AppClient client, int strengthA, int strengthB, string[] waveform)
    {
        try
        {
            if (_config.UseChannelA)
                await SendCommandToClientAsync(client, DGLabProtocol.StrengthCommand(1, 2, strengthA));
            if (_config.UseChannelB)
                await SendCommandToClientAsync(client, DGLabProtocol.StrengthCommand(2, 2, strengthB));

            if (_config.UseChannelA)
                await SendCommandToClientAsync(client, DGLabProtocol.PulseCommand("A", waveform));
            if (_config.UseChannelB)
                await SendCommandToClientAsync(client, DGLabProtocol.PulseCommand("B", waveform));
        }
        catch (Exception ex)
        {
            Log.Error($"[TazeU] Shock execution error for {client.RemoteEndpoint}: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取当前应该使用的 IP 地址（优先使用用户配置的 BindAddress，否则自动检测）。
    /// </summary>
    private string GetIpAddress()
    {
        if (!string.IsNullOrWhiteSpace(_config.BindAddress) && IPAddress.TryParse(_config.BindAddress, out _))
        {
            return _config.BindAddress.Trim();
        }
        return DetectLocalIp();
    }

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

    #endregion
}
