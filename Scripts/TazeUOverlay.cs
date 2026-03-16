using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace TazeU.Scripts;

/// <summary>
/// 游戏内 Overlay 节点：处理快捷键输入，显示/隐藏 QR 码弹窗，触发测试电击。
/// 作为 Node 挂载到 SceneTree.Root，通过 _UnhandledKeyInput 捕获按键。
/// </summary>
internal partial class TazeUOverlay : Node
{
    private readonly DGLabServer _server;
    private readonly TazeUConfig _config;

    private CanvasLayer? _qrLayer;
    private bool _qrVisible;

    public TazeUOverlay(DGLabServer server, TazeUConfig config)
    {
        _server = server;
        _config = config;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } keyEvent) return;

        var keyCode = (long)keyEvent.Keycode;

        if (keyCode != 0 && keyCode == ModConfigBridge.ShowQRKey)
        {
            ToggleQRPopup();
            GetViewport().SetInputAsHandled();
        }
        else if (keyCode != 0 && keyCode == ModConfigBridge.TestShockKey)
        {
            _server.TriggerShock(_config.TestDamage);
            Log.Info($"[TazeU] Test shock triggered (damage={_config.TestDamage})");
            GetViewport().SetInputAsHandled();
        }
        else if (keyCode != 0 && keyCode == ModConfigBridge.DisconnectKey)
        {
            _server.Disconnect();
            GetViewport().SetInputAsHandled();
        }
    }

    private void ToggleQRPopup()
    {
        if (_qrVisible)
        {
            HideQR();
        }
        else
        {
            ShowQR();
        }
    }

    private void ShowQR()
    {
        if (_qrLayer != null) return;

        var url = _server.GetConnectUrl();
        ImageTexture texture;
        try
        {
            texture = QRCodeHelper.GenerateQRTexture(url);
        }
        catch (System.Exception ex)
        {
            Log.Info($"[TazeU] QR generation failed: {ex.Message}");
            return;
        }

        _qrLayer = new CanvasLayer { Layer = 100 };

        // 半透明背景遮罩
        var bg = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.6f),
            AnchorsPreset = (int)Control.LayoutPreset.FullRect,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        bg.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                HideQR();
        };

        // 居中容器
        var center = new CenterContainer
        {
            AnchorsPreset = (int)Control.LayoutPreset.FullRect,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        // 内容面板
        var panel = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.12f, 0.15f, 0.95f),
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12,
            ContentMarginLeft = 24,
            ContentMarginRight = 24,
            ContentMarginTop = 20,
            ContentMarginBottom = 20,
        };
        panel.AddThemeStyleboxOverride("panel", panelStyle);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);

        // 标题
        var title = new Label
        {
            Text = "DG-LAB Connection / DG-LAB 连接",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        title.AddThemeFontSizeOverride("font_size", 22);

        // QR 码
        var qrRect = new TextureRect
        {
            Texture = texture,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(280, 280),
            ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional,
        };

        // 状态文本
        var statusText = _server.IsConnected ? "✓ Connected / 已连接" : "Scan with DG-LAB APP / 用 DG-LAB APP 扫码";
        var status = new Label
        {
            Text = statusText,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        status.AddThemeColorOverride("font_color",
            _server.IsConnected ? new Color(0.4f, 0.9f, 0.4f) : new Color(0.7f, 0.7f, 0.7f));
        status.AddThemeFontSizeOverride("font_size", 16);

        // 提示
        var hint = new Label
        {
            Text = "Press key again to close\n再次按键关闭",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        hint.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
        hint.AddThemeFontSizeOverride("font_size", 13);

        vbox.AddChild(title);
        vbox.AddChild(qrRect);
        vbox.AddChild(status);
        vbox.AddChild(hint);
        panel.AddChild(vbox);
        center.AddChild(panel);
        _qrLayer.AddChild(bg);
        _qrLayer.AddChild(center);

        AddChild(_qrLayer);
        _qrVisible = true;
        Log.Info("[TazeU] QR popup shown");
    }

    private void HideQR()
    {
        if (_qrLayer == null) return;

        RemoveChild(_qrLayer);
        _qrLayer.QueueFree();
        _qrLayer = null;
        _qrVisible = false;
    }
}
