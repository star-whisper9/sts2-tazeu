using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Logging;

namespace TazeU.Scripts;

public class TazeUConfig
{
    /// <summary>WS 监听端口。</summary>
    public int Port { get; set; } = 9999;

    /// <summary>最低输出强度（Mod 侧配置，0-200）。</summary>
    public int MinStrength { get; set; } = 5;

    /// <summary>伤害映射上限，超出后输出满强度。</summary>
    public int DamageCap { get; set; } = 20;

    /// <summary>
    /// 波形预设名称。可选值：Breath, Tide, Batter, Pinch, PinchRamp, Heartbeat, Squeeze, Rhythm, Random。
    /// "Random" 每次电击随机选择一种预设。
    /// </summary>
    public string Waveform { get; set; } = "Breath";

    /// <summary>是否启用 A 通道。</summary>
    public bool UseChannelA { get; set; } = true;

    /// <summary>是否启用 B 通道。</summary>
    public bool UseChannelB { get; set; } = true;

    // ── 序列化选项 ──────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private static string ConfigPath =>
        Path.Combine(Path.GetDirectoryName(typeof(TazeUConfig).Assembly.Location)!, "tazeu_config.json");

    /// <summary>
    /// 从磁盘加载配置。文件不存在则写入默认配置并返回。
    /// </summary>
    public static TazeUConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<TazeUConfig>(json, JsonOptions) ?? new TazeUConfig();
                config.Validate();
                Log.Info($"[TazeU] Config loaded from {ConfigPath}");
                return config;
            }
        }
        catch (Exception ex)
        {
            Log.Info($"[TazeU] Config load error, using defaults: {ex.Message}");
        }

        var defaultConfig = new TazeUConfig();
        defaultConfig.Save();
        return defaultConfig;
    }

    /// <summary>
    /// 将当前配置写入磁盘。
    /// </summary>
    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(ConfigPath, json);
            Log.Info($"[TazeU] Config saved to {ConfigPath}");
        }
        catch (Exception ex)
        {
            Log.Info($"[TazeU] Config save error: {ex.Message}");
        }
    }

    private void Validate()
    {
        Port = Math.Clamp(Port, 1024, 65535);
        MinStrength = Math.Clamp(MinStrength, 0, 200);
        DamageCap = Math.Max(DamageCap, 1);
    }
}
