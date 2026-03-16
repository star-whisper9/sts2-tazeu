using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace TazeU.Scripts;

// 必须要加的属性，用于注册 Mod。字符串和初始化函数命名一致。
[ModInitializer("Init")]
public class Entry
{
    // 初始化函数
    public static void Init()
    {
        var harmony = new Harmony("sts2.tazeu.scripts");
        harmony.PatchAll();
        // 使得 tscn 可以加载自定义脚本
        ScriptManagerBridge.LookupScriptsInAssembly(typeof(Entry).Assembly);
        Log.Debug("TazeU initialized!");
    }
}
