using System;
using System.IO;
using System.Reflection;
using Godot;
using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace TazeU.Scripts;

[ModInitializer("Init")]
public class Entry
{
    public static DGLabServer? Server { get; private set; }
    public static TazeUConfig? Config { get; private set; }

    public static void Init()
    {
        // 注册程序集解析 — 确保运行时能从 mod 目录加载 QRCoder 等依赖 DLL
        var modDir = Path.GetDirectoryName(typeof(Entry).Assembly.Location)!;
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var name = new AssemblyName(args.Name).Name;
            var path = Path.Combine(modDir, name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };

        var harmony = new Harmony("sts2.tazeu.scripts");
        harmony.PatchAll();
        ScriptManagerBridge.LookupScriptsInAssembly(typeof(Entry).Assembly);

        Config = TazeUConfig.Load();
        Server = new DGLabServer(Config);
        Server.LoadCustomWaveforms();
        Server.Start();

        // 延迟注册 ModConfig + 挂载 Overlay（等待 SceneTree 就绪）
        // CreateTimer(0) = 下一帧触发一次后自动释放，避免 ProcessFrame 持久订阅
        var tree = (SceneTree)Engine.GetMainLoop();
        tree.CreateTimer(0).Timeout += () =>
        {
            // 挂载 Overlay 节点（快捷键 + QR 弹窗）
            var overlay = new TazeUOverlay(Server, Config);
            tree.Root.AddChild(overlay);

            // ModConfig 需要再延迟一帧注册
            tree.CreateTimer(0).Timeout += () => ModConfigBridge.Register(Config, Server);
        };

        Log.Info("[TazeU] Mod initialized, WS server starting...");
    }
}
