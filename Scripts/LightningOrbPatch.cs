using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models.Orbs;

namespace TazeU.Scripts;

/// <summary>
/// Harmony Patch: Hook 闪电充能球的被动触发和激发触发。
/// Postfix 在方法返回（async 方法为状态机启动后）时同步触发电击指令。
/// </summary>
[HarmonyPatch(typeof(LightningOrb), nameof(LightningOrb.Passive))]
public static class LightningOrbPassivePatch
{
    [HarmonyPostfix]
    public static void Postfix(LightningOrb __instance)
    {
        var damage = __instance.PassiveVal;
        Log.Debug($"[TazeU] Lightning Passive fired, damage={damage}");
        Entry.Server?.TriggerShock(damage);
    }
}

[HarmonyPatch(typeof(LightningOrb), nameof(LightningOrb.Evoke))]
public static class LightningOrbEvokePatch
{
    [HarmonyPostfix]
    public static void Postfix(LightningOrb __instance)
    {
        var damage = __instance.EvokeVal;
        Log.Debug($"[TazeU] Lightning Evoke fired, damage={damage}");
        Entry.Server?.TriggerShock(damage);
    }
}
