using System.Text.Json;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Logging;

namespace TazeU.Scripts;

/// <summary>
/// 从 waveforms/ 目录加载自定义波形 JSON 文件。
/// 文件格式：{ "name": "显示名称", "data": ["HEX1", "HEX2", ...] }
/// </summary>
internal static class CustomWaveformLoader
{
    private static readonly Regex HexPattern = new(@"^[0-9A-Fa-f]{16}$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static string WaveformDir =>
        Path.Combine(Path.GetDirectoryName(typeof(CustomWaveformLoader).Assembly.Location)!, "waveforms");

    /// <summary>
    /// 扫描 waveforms/ 目录，加载所有合法的自定义波形。
    /// 返回 (显示名称, 波形数据) 字典，key = 文件名(不含扩展名)。
    /// </summary>
    internal static Dictionary<string, CustomWaveform> LoadAll()
    {
        var result = new Dictionary<string, CustomWaveform>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(WaveformDir))
        {
            Log.Info($"[TazeU] Waveform directory not found, creating: {WaveformDir}");
            Directory.CreateDirectory(WaveformDir);
            return result;
        }

        foreach (var file in Directory.GetFiles(WaveformDir, "*.jsonc"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var wf = JsonSerializer.Deserialize<WaveformFile>(json, JsonOptions);

                if (wf?.Data == null || wf.Data.Length == 0)
                {
                    Log.Info($"[TazeU] Skipping empty waveform: {file}");
                    continue;
                }

                // 验证每条 HEX 数据格式
                var validData = new List<string>();
                foreach (var hex in wf.Data)
                {
                    if (HexPattern.IsMatch(hex))
                        validData.Add(hex.ToUpperInvariant());
                    else
                        Log.Info($"[TazeU] Invalid hex in {file}: {hex}");
                }

                if (validData.Count == 0)
                {
                    Log.Warn($"[TazeU] No valid data in waveform: {file}");
                    continue;
                }

                var fileName = Path.GetFileNameWithoutExtension(file);
                var displayName = string.IsNullOrWhiteSpace(wf.Name) ? fileName : wf.Name;
                var key = $"{displayName}({fileName})";
                result[key] = new CustomWaveform(displayName, validData.ToArray());
                Log.Info($"[TazeU] Loaded custom waveform: {key} ({validData.Count} frames) from {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                Log.Error($"[TazeU] Failed to load waveform {file}: {ex.Message}");
            }
        }

        Log.Info($"[TazeU] {result.Count} custom waveform(s) loaded");
        return result;
    }

    private class WaveformFile
    {
        public string? Name { get; set; }
        public string[]? Data { get; set; }
    }
}

internal record CustomWaveform(string DisplayName, string[] Data);
