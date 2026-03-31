using Godot;
using MegaCrit.Sts2.Core.Logging;
using Timer = Godot.Timer;

namespace TazeU.Scripts;

/// <summary>
/// 游戏内 Overlay 节点：处理快捷键输入，显示/隐藏 QR 码弹窗，触发测试电击。
/// 作为 Node 挂载到 SceneTree.Root，通过 _UnhandledKeyInput 捕获按键。
/// </summary>
internal partial class TazeUOverlay(DGLabServer server, TazeUConfig config) : Node
{
    private readonly DGLabServer _server = server;
    private readonly TazeUConfig _config = config;

    private CanvasLayer? _qrLayer;
    private bool _qrVisible;
    private Label? _statusLabel;
    private VBoxContainer? _clientListBox;
    private Timer? _refreshTimer;

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
            Log.Debug($"[TazeU] Test shock triggered (damage={_config.TestDamage})");
            GetViewport().SetInputAsHandled();
        }
        else if (keyCode != 0 && keyCode == ModConfigBridge.DisconnectKey)
        {
            _server.DisconnectAll();
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
            Log.Error($"[TazeU] QR generation failed: {ex.Message}");
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
        _statusLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _statusLabel.AddThemeFontSizeOverride("font_size", 16);
        UpdateStatusLabel();

        // ── 已连接客户端列表 ──
        var separator = new HSeparator();
        separator.AddThemeConstantOverride("separation", 4);

        var clientHeader = new Label
        {
            Text = "Connected Clients / 已连接客户端",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        clientHeader.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        clientHeader.AddThemeFontSizeOverride("font_size", 14);

        _clientListBox = new VBoxContainer();
        _clientListBox.AddThemeConstantOverride("separation", 6);
        RefreshClientList();

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
        vbox.AddChild(_statusLabel);
        vbox.AddChild(separator);
        vbox.AddChild(clientHeader);
        vbox.AddChild(_clientListBox);
        vbox.AddChild(hint);
        panel.AddChild(vbox);
        center.AddChild(panel);
        _qrLayer.AddChild(bg);
        _qrLayer.AddChild(center);

        // 1 秒刷新定时器
        _refreshTimer = new Timer { WaitTime = 1.0, Autostart = true };
        _refreshTimer.Timeout += OnRefreshTick;
        _qrLayer.AddChild(_refreshTimer);

        AddChild(_qrLayer);
        _qrVisible = true;
        Log.Debug("[TazeU] QR popup shown");
    }

    private void UpdateStatusLabel()
    {
        if (_statusLabel == null) return;
        int count = _server.ConnectedCount;
        if (count > 0)
        {
            _statusLabel.Text = $"✓ {count} Connected / 已连接 {count} 台";
            _statusLabel.RemoveThemeColorOverride("font_color");
            _statusLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.9f, 0.4f));
        }
        else
        {
            _statusLabel.Text = "Scan with DG-LAB APP / 用 DG-LAB APP 扫码";
            _statusLabel.RemoveThemeColorOverride("font_color");
            _statusLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        }
    }

    private void RefreshClientList()
    {
        if (_clientListBox == null) return;

        // 清空旧列表
        foreach (var child in _clientListBox.GetChildren())
            child.QueueFree();

        var clients = _server.GetConnectedClients();
        if (clients.Count == 0)
        {
            var empty = new Label
            {
                Text = "No connections / 暂无连接",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            empty.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
            empty.AddThemeFontSizeOverride("font_size", 13);
            _clientListBox.AddChild(empty);
            return;
        }

        foreach (var info in clients)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            var label = new Label
            {
                Text = info.RemoteEndpoint,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            label.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
            label.AddThemeFontSizeOverride("font_size", 14);

            var kickBtn = new Button { Text = "Kick" };
            kickBtn.AddThemeFontSizeOverride("font_size", 12);
            var targetId = info.TargetId;
            kickBtn.Pressed += () => _server.DisconnectClient(targetId);

            var blockBtn = new Button { Text = "Block" };
            blockBtn.AddThemeFontSizeOverride("font_size", 12);
            blockBtn.Pressed += () => _server.BlockClient(targetId);

            row.AddChild(label);
            row.AddChild(kickBtn);
            row.AddChild(blockBtn);
            _clientListBox.AddChild(row);
        }
    }

    private void OnRefreshTick()
    {
        UpdateStatusLabel();
        RefreshClientList();
    }

    private void HideQR()
    {
        if (_qrLayer == null) return;

        if (_refreshTimer != null)
        {
            _refreshTimer.Timeout -= OnRefreshTick;
            _refreshTimer = null;
        }
        _statusLabel = null;
        _clientListBox = null;

        RemoveChild(_qrLayer);
        _qrLayer.QueueFree();
        _qrLayer = null;
        _qrVisible = false;
    }
}
