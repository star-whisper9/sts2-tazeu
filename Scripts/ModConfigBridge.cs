using MegaCrit.Sts2.Core.Logging;

namespace TazeU.Scripts;

/// <summary>
/// 通过反射零依赖接入 ModConfig-STS2。
/// 玩家未安装 ModConfig 时 Mod 照常运行，所有值走 TazeUConfig.json 默认。
/// </summary>
internal static class ModConfigBridge
{
    private const string ModId = "sts2.tazeu";

    private static bool _detected;
    private static bool _available;
    private static Type? _apiType;
    private static Type? _entryType;
    private static Type? _configType;

    // 快捷键值（Godot Key 码），由 ModConfig 管理
    internal static long ShowQRKey { get; private set; }
    internal static long TestShockKey { get; private set; }
    internal static long DisconnectKey { get; private set; }

    internal static bool IsAvailable
    {
        get
        {
            if (!_detected)
            {
                _detected = true;
                _apiType = Type.GetType("ModConfig.ModConfigApi, ModConfig");
                _entryType = Type.GetType("ModConfig.ConfigEntry, ModConfig");
                _configType = Type.GetType("ModConfig.ConfigType, ModConfig");
                _available = _apiType != null && _entryType != null && _configType != null;
            }
            return _available;
        }
    }

    /// <summary>
    /// 向 ModConfig 注册所有配置项。需在 SceneTree 延迟两帧后调用。
    /// </summary>
    internal static void Register(TazeUConfig config, DGLabServer server)
    {
        if (!IsAvailable) return;

        try
        {
            var originalAddress = config.BindAddress;
            var originalPort = config.Port;
            DeferredRegister(config, server);

            // SyncSavedValues 可能从 ModConfig 持久化中恢复了不同的端口
            // 此时服务端已在旧端口运行，需要重启
            if (config.Port != originalPort)
            {
                Log.Debug($"[TazeU] Port changed from ModConfig persistence ({originalPort} → {config.Port}), restarting server...");
                server.Restart();
            }

            if (config.BindAddress != originalAddress)
            {
                Log.Debug($"[TazeU] BindAddress changed from ModConfig persistence ({originalAddress} → {config.BindAddress}), restarting server...");
                server.Restart();
            }
        }
        catch (Exception e)
        {
            Log.Error($"[TazeU] ModConfig registration failed: {e}");
        }
    }

    private static void DeferredRegister(TazeUConfig config, DGLabServer server)
    {
        var waveformOptions = server.GetAllWaveformNames();

        var entries = new[]
        {
            // ── 连接 ──
            MakeEntry("header_connection", "", ConfigTypeValue("Header"),
                labels: new() { { "en", "Connection" }, { "zhs", "连接" } }),

            MakeEntry("port", "Port", ConfigTypeValue("Slider"),
                defaultValue: (float)config.Port, min: 1024, max: 65535, step: 1,
                labels: new() { { "en", "Port" }, { "zhs", "端口" } },
                descriptions: new() { { "en", "WebSocket port (auto-restarts server)" }, { "zhs", "WebSocket 端口（修改后自动重启服务）" } },
                onChanged: v => { config.Port = (int)(float)v; server.Restart(); }),

            MakeEntry("bind_address", "Bind Address", ConfigTypeValue("TextInput"),
                defaultValue: config.BindAddress,
                labels: new() { { "en", "Bind Address" }, { "zhs", "绑定 IP 地址" } },
                descriptions: new() { { "en", "Override the IP address used for connection (Leave empty to auto-detect)" }, { "zhs", "覆盖用于连接的 IP 地址（多网卡连接失败时使用，留空自动检测）" } },
                onChanged: v => { config.BindAddress = (string)v; server.Restart(); }),

            MakeEntry("show_qr_key", "Show QR Code", ConfigTypeValue("KeyBind"),
                defaultValue: (long)0,
                labels: new() { { "en", "Show QR Code" }, { "zhs", "显示二维码" } },
                descriptions: new() { { "en", "Press to show/hide DG-LAB connection QR code" }, { "zhs", "按下显示/隐藏 DG-LAB 连接二维码" } },
                onChanged: v => ShowQRKey = Convert.ToInt64(v)),

            MakeEntry("max_connections", "Max Connections", ConfigTypeValue("Slider"),
                defaultValue: (float)config.MaxConnections, min: 1, max: 32, step: 1,
                labels: new() { { "en", "Max Connections" }, { "zhs", "最大连接数" } },
                descriptions: new() { { "en", "Maximum simultaneous APP connections" }, { "zhs", "允许同时连接的 APP 最大数量" } },
                onChanged: v => config.MaxConnections = Math.Clamp((int)(float)v, 1, 64)),

            MakeEntry("disconnect_key", "Disconnect All", ConfigTypeValue("KeyBind"),
                defaultValue: (long)0,
                labels: new() { { "en", "Disconnect All" }, { "zhs", "断开所有连接" } },
                descriptions: new() { { "en", "Press to disconnect all connected APPs" }, { "zhs", "按下断开所有已连接的 APP" } },
                onChanged: v => DisconnectKey = Convert.ToInt64(v)),

            // ── 电击设置 ──
            MakeEntry("header_shock", "", ConfigTypeValue("Header"),
                labels: new() { { "en", "Shock Settings" }, { "zhs", "电击设置" } }),

            MakeEntry("min_strength", "Min Strength", ConfigTypeValue("Slider"),
                defaultValue: (float)config.MinStrength, min: 0, max: 200, step: 1,
                labels: new() { { "en", "Min Strength" }, { "zhs", "最低强度" } },
                descriptions: new() { { "en", "Minimum output strength (0-200)" }, { "zhs", "最低输出强度（0-200）" } },
                onChanged: v => config.MinStrength = (int)(float)v),

            MakeEntry("damage_cap", "Damage Cap", ConfigTypeValue("Slider"),
                defaultValue: (float)config.DamageCap, min: 1, max: 100, step: 1,
                labels: new() { { "en", "Damage Cap" }, { "zhs", "伤害上限" } },
                descriptions: new() { { "en", "Damage at which max strength is reached" }, { "zhs", "达到最大强度对应的伤害值" } },
                onChanged: v => config.DamageCap = Math.Max(1, (int)(float)v)),

            MakeEntry("waveform", "Waveform", ConfigTypeValue("Dropdown"),
                defaultValue: config.Waveform,
                options: waveformOptions,
                labels: new() { { "en", "Waveform" }, { "zhs", "波形" } },
                onChanged: v => config.Waveform = (string)v),

            MakeEntry("use_channel_a", "Channel A", ConfigTypeValue("Toggle"),
                defaultValue: config.UseChannelA,
                labels: new() { { "en", "Channel A" }, { "zhs", "A 通道" } },
                onChanged: v => config.UseChannelA = (bool)v),

            MakeEntry("use_channel_b", "Channel B", ConfigTypeValue("Toggle"),
                defaultValue: config.UseChannelB,
                labels: new() { { "en", "Channel B" }, { "zhs", "B 通道" } },
                onChanged: v => config.UseChannelB = (bool)v),

            MakeEntry("only_own_orbs", "Only Own Orbs", ConfigTypeValue("Toggle"),
                defaultValue: config.OnlyOwnOrbs,
                labels: new() { { "en", "Only Own Orbs" }, { "zhs", "仅自己的电球" } },
                descriptions: new() { { "en", "Only trigger shocks from your own lightning orbs (for multiplayer)" }, { "zhs", "仅自己的闪电充能球触发电击（多人模式适用）" } },
                onChanged: v => config.OnlyOwnOrbs = (bool)v),

            // ── 连击递增 ──
            MakeEntry("header_combo", "", ConfigTypeValue("Header"),
                labels: new() { { "en", "Combo Escalation" }, { "zhs", "连击递增" } }),

            MakeEntry("combo_enabled", "Combo Enabled", ConfigTypeValue("Toggle"),
                defaultValue: config.ComboEnabled,
                labels: new() { { "en", "Enable Combo" }, { "zhs", "启用连击递增" } },
                descriptions: new() { { "en", "Consecutive shocks within the time window will escalate intensity" }, { "zhs", "在时间窗口内连续触发电击时强度逐步递增" } },
                onChanged: v => config.ComboEnabled = (bool)v),

            MakeEntry("combo_rate", "Combo Rate", ConfigTypeValue("Slider"),
                defaultValue: (float)(config.ComboRate * 100), min: 5, max: 100, step: 5,
                format: "F0",
                labels: new() { { "en", "Escalation Per Stack (%)" }, { "zhs", "每层递增比例 (%)" } },
                descriptions: new() { { "en", "Strength increase per combo stack (e.g. 15 = +15% per stack)" }, { "zhs", "每层连击增加的强度百分比（如 15 = 每层+15%）" } },
                onChanged: v => config.ComboRate = (float)v / 100f),

            MakeEntry("combo_window", "Combo Window", ConfigTypeValue("Slider"),
                defaultValue: config.ComboWindow, min: 1, max: 30, step: 1,
                format: "F0",
                labels: new() { { "en", "Combo Window (sec)" }, { "zhs", "连击窗口 (秒)" } },
                descriptions: new() { { "en", "Time window to maintain combo (resets if no shock within this period)" }, { "zhs", "维持连击的时间窗口（超时未触发则重置）" } },
                onChanged: v => config.ComboWindow = (float)v),

            MakeEntry("combo_max", "Combo Max Stacks", ConfigTypeValue("Slider"),
                defaultValue: (float)config.ComboMaxStacks, min: 1, max: 50, step: 1,
                labels: new() { { "en", "Max Stacks" }, { "zhs", "最大叠加层数" } },
                descriptions: new() { { "en", "Maximum combo stacks (caps the escalation)" }, { "zhs", "连击最大叠加次数（防止无限递增）" } },
                onChanged: v => config.ComboMaxStacks = Math.Max(1, (int)(float)v)),

            // ── 测试 ──
            MakeEntry("header_test", "", ConfigTypeValue("Header"),
                labels: new() { { "en", "Test" }, { "zhs", "测试" } }),

            MakeEntry("test_damage", "Test Damage", ConfigTypeValue("Slider"),
                defaultValue: (float)config.TestDamage, min: 1, max: 50, step: 1,
                labels: new() { { "en", "Test Damage" }, { "zhs", "测试伤害值" } },
                descriptions: new() { { "en", "Damage value for test shock" }, { "zhs", "测试电击的模拟伤害值" } },
                onChanged: v => config.TestDamage = Math.Max(1, (int)(float)v)),

            MakeEntry("test_shock_key", "Test Shock", ConfigTypeValue("KeyBind"),
                defaultValue: (long)0,
                labels: new() { { "en", "Test Shock" }, { "zhs", "测试电击" } },
                descriptions: new() { { "en", "Press to trigger a test shock" }, { "zhs", "按下触发一次测试电击" } },
                onChanged: v => TestShockKey = Convert.ToInt64(v)),
        };

        // 创建正确类型的 ConfigEntry[] 数组（object[] 无法匹配 Register 签名）
        var typedEntries = Array.CreateInstance(_entryType!, entries.Length);
        for (int i = 0; i < entries.Length; i++)
            typedEntries.SetValue(entries[i], i);

        var displayNames = new Dictionary<string, string>
        {
            { "en", "TazeU" },
            { "zhs", "TazeU 电击联动" }
        };

        // Register(modId, displayName, displayNames, entries)
        var register = _apiType!.GetMethod("Register",
            [typeof(string), typeof(string), typeof(Dictionary<string, string>), typedEntries.GetType()]);
        register?.Invoke(null, [ModId, "TazeU", displayNames, typedEntries]);

        // ModConfig.LoadValues 不触发 OnChanged，需手动同步已持久化的值
        SyncSavedValues(config);

        Log.Debug("[TazeU] ModConfig registered");
    }

    /// <summary>
    /// 注册后从 ModConfig 读回已持久化的值，同步到 TazeUConfig 和快捷键字段。
    /// 解决二次启动时 LoadValues 不触发 OnChanged 回调的问题。
    /// </summary>
    private static void SyncSavedValues(TazeUConfig config)
    {
        config.Port = (int)GetValue("port", (float)config.Port);
        config.BindAddress = GetValue("bind_address", config.BindAddress);
        config.MinStrength = (int)GetValue("min_strength", (float)config.MinStrength);
        config.DamageCap = Math.Max(1, (int)GetValue("damage_cap", (float)config.DamageCap));
        config.Waveform = GetValue("waveform", config.Waveform);
        config.UseChannelA = GetValue("use_channel_a", config.UseChannelA);
        config.UseChannelB = GetValue("use_channel_b", config.UseChannelB);
        config.OnlyOwnOrbs = GetValue("only_own_orbs", config.OnlyOwnOrbs);
        config.MaxConnections = Math.Clamp((int)GetValue("max_connections", (float)config.MaxConnections), 1, 64);
        config.ComboEnabled = GetValue("combo_enabled", config.ComboEnabled);
        config.ComboRate = GetValue("combo_rate", config.ComboRate * 100f) / 100f;
        config.ComboWindow = GetValue("combo_window", config.ComboWindow);
        config.ComboMaxStacks = Math.Max(1, (int)GetValue("combo_max", (float)config.ComboMaxStacks));
        config.TestDamage = Math.Max(1, (int)GetValue("test_damage", (float)config.TestDamage));

        ShowQRKey = GetValue("show_qr_key", 0L);
        TestShockKey = GetValue("test_shock_key", 0L);
        DisconnectKey = GetValue("disconnect_key", 0L);

        Log.Debug($"[TazeU] Config synced from ModConfig (ShowQR={ShowQRKey}, TestShock={TestShockKey}, Disconnect={DisconnectKey})");
    }

    // ── 反射工具 ──────────────────────────────────────────

    private static object ConfigTypeValue(string name) =>
        Enum.Parse(_configType!, name);

    private static object MakeEntry(string key, string label, object type,
        object? defaultValue = null, float min = 0, float max = 100, float step = 1,
        string format = "F0", string[]? options = null,
        Action<object>? onChanged = null,
        Dictionary<string, string>? labels = null,
        Dictionary<string, string>? descriptions = null)
    {
        var entry = Activator.CreateInstance(_entryType!)!;
        SetProp(entry, "Key", key);
        SetProp(entry, "Label", label);
        SetProp(entry, "Type", type);
        if (defaultValue != null) SetProp(entry, "DefaultValue", defaultValue);
        SetProp(entry, "Min", min);
        SetProp(entry, "Max", max);
        SetProp(entry, "Step", step);
        SetProp(entry, "Format", format);
        if (options != null) SetProp(entry, "Options", options);
        if (onChanged != null) SetProp(entry, "OnChanged", onChanged);
        if (labels != null) SetProp(entry, "Labels", labels);
        if (descriptions != null) SetProp(entry, "Descriptions", descriptions);
        return entry;
    }

    private static void SetProp(object obj, string name, object value) =>
        obj.GetType().GetProperty(name)?.SetValue(obj, value);

    internal static T GetValue<T>(string key, T fallback)
    {
        if (!IsAvailable) return fallback;
        try
        {
            var method = _apiType!.GetMethod("GetValue")!.MakeGenericMethod(typeof(T));
            return (T)method.Invoke(null, [ModId, key])!;
        }
        catch { return fallback; }
    }
}
