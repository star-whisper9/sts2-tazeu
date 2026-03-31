using System.Text.Json;

namespace TazeU.Scripts;

/// <summary>
/// DG-LAB WebSocket v2 协议辅助：波形预设、指令格式化。
/// V3 波形格式：每 100ms 一条数据，8 字节（16 hex chars）= 4 字节频率 + 4 字节强度，
/// 对应 4 组 × 25ms 子脉冲。频率范围 10-240，强度范围 0-100。
/// </summary>
public static class DGLabProtocol
{
    #region 波形

    /// <summary>
    /// 呼吸
    /// </summary>
    public static readonly string[] BreathWaveV3 =
    [
        "0A0A0A0A00000000", // int=0
        "0A0A0A0A14141414", // int=20
        "0A0A0A0A28282828", // int=40
        "0A0A0A0A3C3C3C3C", // int=60
        "0A0A0A0A50505050", // int=80
        "0A0A0A0A64646464", // int=100
        "0A0A0A0A64646464", // int=100
        "0A0A0A0A64646464", // int=100
        "0A0A0A0A00000000", // silence
        "0A0A0A0A00000000",
        "0A0A0A0A00000000",
        "0A0A0A0A00000000"
    ];

    /// <summary>
    /// 潮汐
    /// </summary>
    public static readonly string[] TideV3 = [
      "0A0A0A0A00000000",
      "0E0E0E0E32323232",
      "1313131364646464",
      "181818184C4C4C4C",
      "1A1A1A1A00000000",
      "1E1E1E1E32323232",
      "2323232364646464",
      "282828284C4C4C4C",
      "2A2A2A2A00000000",
      "0A0A0A0A00000000"
    ];

    /// <summary>
    /// 连击
    /// </summary>
    public static readonly string[] BatterV3 = [
      "0A0A0A0A64646464",
      "0A0A0A0A00000000",
      "0A0A0A0A64646464",
      "0A0A0A0A32323232",
      "0A0A0A0A00000000",
      "0A0A0A0A00000000",
      "0A0A0A0A64646464",
      "0A0A0A0A00000000",
      "0A0A0A0A64646464",
      "0A0A0A0A32323232",
      "0A0A0A0A00000000",
      "0A0A0A0A00000000"
    ];

    /// <summary>
    /// 快速按捏
    /// </summary>
    public static readonly string[] PinchV3 = [
      "0A0A0A0A00000000",
      "0A0A0A0A64646464",
      "0A0A0A0A00000000",
      "0A0A0A0A64646464",
      "0A0A0A0A00000000",
      "0A0A0A0A64646464",
      "0A0A0A0A00000000",
      "0A0A0A0A64646464",
      "0A0A0A0A00000000",
      "0A0A0A0A64646464",
      "0A0A0A0A00000000",
      "0A0A0A0A00000000"
    ];

    /// <summary>
    /// 按捏渐强
    /// </summary>
    public static readonly string[] PinchRampV3 = [
      "0A0A0A0A00000000",
      "0A0A0A0A34343434",
      "0A0A0A0A00000000",
      "0A0A0A0A49494949",
      "0A0A0A0A00000000",
      "0A0A0A0A64646464",
      "0A0A0A0A00000000",
      "0A0A0A0A34343434",
      "0A0A0A0A00000000",
      "0A0A0A0A64646464",
      "0A0A0A0A00000000",
      "0A0A0A0A00000000"
    ];

    /// <summary>
    /// 心跳节奏
    /// </summary>
    public static readonly string[] HeartbeatV3 = [
      "7070707064646464",
      "7070707064646464",
      "0A0A0A0A00000000",
      "0A0A0A0A00000000",
      "0A0A0A0A4B4B4B4B",
      "0A0A0A0A64646464",
      "0A0A0A0A00000000",
      "0A0A0A0A00000000",
      "0A0A0A0A4B4B4B4B",
      "0A0A0A0A64646464",
      "0A0A0A0A00000000",
      "0A0A0A0A00000000"
    ];

    /// <summary>
    /// 压缩
    /// </summary>
    public static readonly string[] SqueezeV3 = [
      "4A4A4A4A64646464",
      "4040404064646464",
      "3636363664646464",
      "2D2D2D2D64646464",
      "2323232364646464",
      "1A1A1A1A64646464",
      "0A0A0A0A64646464",
      "0A0A0A0A64646464",
      "0A0A0A0A64646464",
      "0A0A0A0A64646464",
      "0A0A0A0A00000000",
      "0A0A0A0A00000000"
    ];

    /// <summary>
    /// 节奏步伐
    /// </summary>
    public static readonly string[] RhythmV3 = [
      "0A0A0A0A00000000",
      "0A0A0A0A28282828",
      "0A0A0A0A50505050",
      "0A0A0A0A00000000",
      "0A0A0A0A32323232",
      "0A0A0A0A64646464",
      "0A0A0A0A00000000",
      "0A0A0A0A42424242",
      "0A0A0A0A64646464",
      "0A0A0A0A00000000",
      "0A0A0A0A64646464",
      "0A0A0A0A00000000"
    ];

    /// <summary>所有预设波形集合（用于 Random 模式）。</summary>
    public static readonly string[][] AllWaveforms =
    [
        BreathWaveV3, TideV3, BatterV3, PinchV3, PinchRampV3,
        HeartbeatV3, SqueezeV3, RhythmV3
    ];

    /// <summary>按名称查找波形预设，不区分大小写。</summary>
    public static string[]? GetWaveformByName(string name) => name.ToLowerInvariant() switch
    {
        "breath" => BreathWaveV3,
        "tide" => TideV3,
        "batter" => BatterV3,
        "pinch" => PinchV3,
        "pinchramp" => PinchRampV3,
        "heartbeat" => HeartbeatV3,
        "squeeze" => SqueezeV3,
        "rhythm" => RhythmV3,
        _ => null
    };

    /// <summary>
    /// 生成恒定波形数据块（固定频率和强度）。
    /// </summary>
    public static string ConstantWaveChunk(int frequency = 100, int intensity = 60)
    {
        byte f = (byte)Math.Clamp(frequency, 10, 240);
        byte i = (byte)Math.Clamp(intensity, 0, 100);
        return $"{f:X2}{f:X2}{f:X2}{f:X2}{i:X2}{i:X2}{i:X2}{i:X2}";
    }

    #endregion


    #region 指令生成

    /// <summary>
    /// 强度控制指令。
    /// </summary>
    /// <param name="channel">1=A, 2=B</param>
    /// <param name="mode">0=减, 1=增, 2=设为</param>
    /// <param name="value">0-200</param>
    public static string StrengthCommand(int channel, int mode, int value)
    {
        value = Math.Clamp(value, 0, 200);
        return $"strength-{channel}+{mode}+{value}";
    }

    /// <summary>
    /// 脉冲波形指令。格式: pulse-通道:["hex1","hex2",...]
    /// APP 内部队列最大缓存 500 条（50 秒），单次数组最大 100 条（10 秒）。
    /// </summary>
    /// <param name="channel">"A" 或 "B"</param>
    /// <param name="waveHexArray">V3 格式 HEX 波形数据数组</param>
    public static string PulseCommand(string channel, string[] waveHexArray)
    {
        var jsonArray = JsonSerializer.Serialize(waveHexArray);
        return $"pulse-{channel}:{jsonArray}";
    }

    /// <summary>
    /// 清空通道波形队列。
    /// </summary>
    /// <param name="channel">1=A, 2=B</param>
    public static string ClearCommand(int channel)
        => $"clear-{channel}";
    
    #endregion
}
