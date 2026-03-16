using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace TazeU.Scripts;

[ModInitializer("Init")]
public class Entry
{
    public static DGLabServer? Server { get; private set; }

    public static void Init()
    {
        var harmony = new Harmony("sts2.tazeu.scripts");
        harmony.PatchAll();
        ScriptManagerBridge.LookupScriptsInAssembly(typeof(Entry).Assembly);

        var config = TazeUConfig.Load();
        Server = new DGLabServer(config);
        Server.Start();

        Log.Info("[TazeU] Mod initialized, WS server starting...");
    }
}
