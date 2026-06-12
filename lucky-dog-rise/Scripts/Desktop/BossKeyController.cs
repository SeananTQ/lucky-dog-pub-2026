using System;
using Godot;

namespace LuckyDogRise;

public partial class BossKeyController : Node2D
{
    [Signal] public delegate void ModeSwitchRequestedEventHandler();
    [Signal] public delegate void SystemPanelRequestedEventHandler();

    private TestSettingPanelController _settingsPanel = null!;
    private Label _mainText = null!;
    private Vector2 _windowBaseSize;

    private const int PanelW = 300;
    private const int PanelH = 420;
    private static readonly Vector2 ContentOffset = new(PanelW, PanelH);

    private bool _isDragging, _potentialDrag, _isClickThrough = true;
    private Vector2I _mouseScreenStart, _windowPosStart;
    private const float DragThreshold = 5f;

    // 击中区：旧坐标 + ContentOffset
    private static readonly Rect2 DogHitRect = new(360, 510, 180, 180);
    private static readonly Rect2 BtnHitRect = new(350, 685, 240, 40);

    public override void _Ready()
    {
        _mainText = GetNode<Label>("CanvasLayer/Panel/HBoxContainer/MainText");
        var modeBtn = GetNode<Button>("CanvasLayer/Panel/HBoxContainer/ModeSwitch");
        var sysBtn = GetNode<Button>("CanvasLayer/Panel/HBoxContainer/SystemButton");
        modeBtn.Pressed += () => EmitSignal(SignalName.ModeSwitchRequested);
        sysBtn.Pressed += ToggleSettingsPanel;

        _windowBaseSize = GetNode<Marker2D>("ContentA/WindowSize").Position;
        SetupFatWindow();
        EnableLayeredWindow();

        // tscn 已设 offset，这里确保运行时正确
        GetNode<CanvasLayer>("CanvasLayer").Offset = ContentOffset;
        GetNode<CanvasLayer>("Bubble").Offset = ContentOffset;
        GetNode<CanvasLayer>("Bubble").Visible = false;

        _settingsPanel = GD.Load<PackedScene>("res://Scenes/TestSettingPanel.tscn").Instantiate<TestSettingPanelController>();
        _settingsPanel.Name = "SettingsPanel";
        _settingsPanel.Layer = 100;
        AddChild(_settingsPanel);

        var tracker = new GlobalInputTracker();
        tracker.Name = "GlobalInputTracker";
        AddChild(tracker);
    }

    private double _displayTimer;
    private SettingsManager.DisplayMode _lastMode = (SettingsManager.DisplayMode)(-1);

    public override void _Process(double _)
    {
        // 显示模式刷新
        var mode = SettingsManager.CurrentDisplayMode;
        if (mode != _lastMode)
        {
            _lastMode = mode;
            _mainText.Text = mode switch
            {
                SettingsManager.DisplayMode.Clock => DateTime.Now.ToString("HH:mm"),
                SettingsManager.DisplayMode.Hidden => "",
                _ => "0"
            };
        }
        if (mode == SettingsManager.DisplayMode.Clock)
            _mainText.Text = DateTime.Now.ToString("HH:mm");
        else if (mode == SettingsManager.DisplayMode.Chips)
            _mainText.Text = GlobalInputTracker.TotalChips.ToString();

        var localPos = DisplayServer.MouseGetPosition() - DisplayServer.WindowGetPosition();
        bool over = DogHitRect.HasPoint(localPos) || BtnHitRect.HasPoint(localPos) || _settingsPanel.IsOpen;
        if (_isClickThrough && over) SetClickThrough(false);
        else if (!_isClickThrough && !over && !_isDragging) SetClickThrough(true);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut && _settingsPanel.IsOpen)
        {
            var mouse = DisplayServer.MouseGetPosition();
            var wp = DisplayServer.WindowGetPosition();
            var ws = DisplayServer.WindowGetSize();
            // 鼠标还在窗口内 → 原生下拉菜单导致，不关闭
            if (mouse.X < wp.X || mouse.X > wp.X + ws.X ||
                mouse.Y < wp.Y || mouse.Y > wp.Y + ws.Y)
                _settingsPanel.CloseImmediate();
        }
    }

    // ===== 面板切换 =====

    private void ToggleSettingsPanel()
    {
        if (_settingsPanel.IsOpen)
        {
            _settingsPanel.Close();
            return;
        }
        PositionPanelInBestSlot();
        _settingsPanel.Open();
    }

    private void PositionPanelInBestSlot()
    {
        var winPos = DisplayServer.WindowGetPosition();
        var scrSize = DisplayServer.ScreenGetSize();
        int aw = (int)_windowBaseSize.X;
        int ah = (int)_windowBaseSize.Y;
        const int pad = 5;

        // 检查 B 在屏幕 (sx, sy) 处是否能完全放下
        bool Fits(int sx, int sy) =>
            sx >= -pad && sx + PanelW <= scrSize.X + pad &&
            sy >= -pad && sy + PanelH <= scrSize.Y + pad;

        float bY = ContentOffset.Y + ah - PanelH;          // B 底边与 A 底边对齐
        float centerX = ContentOffset.X + aw / 2f - PanelW / 2f; // 水平居中

        // 左侧（4 号位默认）
        int leftY = winPos.Y + (int)bY;
        if (Fits(winPos.X, leftY))
        {
            _settingsPanel.SetPanelPosition(new Vector2(0, bY));
            return;
        }
        // 右侧（6 号位）
        int rx = winPos.X + (int)ContentOffset.X + aw;
        if (Fits(rx, leftY))
        {
            _settingsPanel.SetPanelPosition(new Vector2(ContentOffset.X + aw, bY));
            return;
        }
        // 正下方（2 号位）
        int cx = winPos.X + (int)centerX;
        int btmY = winPos.Y + (int)ContentOffset.Y + ah;
        if (Fits(cx, btmY))
        {
            _settingsPanel.SetPanelPosition(new Vector2(centerX, ContentOffset.Y + ah));
            return;
        }
        // 右下（3 号位）：B 在 A 下方右对齐
        int rX = winPos.X + (int)ContentOffset.X + aw;
        if (Fits(rX, btmY))
        {
            _settingsPanel.SetPanelPosition(new Vector2(ContentOffset.X + aw, ContentOffset.Y + ah));
            return;
        }
        // 左下（1 号位）：B 在 A 下方左对齐
        if (Fits(winPos.X, btmY))
        {
            _settingsPanel.SetPanelPosition(new Vector2(0, ContentOffset.Y + ah));
            return;
        }
        // 正上方（8 号位）
        if (Fits(cx, winPos.Y))
        {
            _settingsPanel.SetPanelPosition(new Vector2(centerX, 0));
            return;
        }
        // 右上（9 号位）
        if (Fits(rX, winPos.Y))
        {
            _settingsPanel.SetPanelPosition(new Vector2(ContentOffset.X + aw, 0));
            return;
        }
        // 左上（7 号位）兜底
        _settingsPanel.SetPanelPosition(new Vector2(0, 0));
    }

    // ===== 窗口管理 =====

    private void SetupFatWindow()
    {
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Transparent, true);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, true);

        int winW = (int)_windowBaseSize.X + PanelW * 2;
        int winH = (int)_windowBaseSize.Y + PanelH * 2;
        DisplayServer.WindowSetSize(new Vector2I(winW, winH));
        SetWindowAboveTaskbar();
        RenderingServer.SetDefaultClearColor(new Color(0, 0, 0, 0));
    }

    private void SetWindowAboveTaskbar()
    {
        var scrRect = DisplayServer.ScreenGetUsableRect();
        int taskbarTop = (int)(scrRect.Position.Y + scrRect.Size.Y);
        int winW = DisplayServer.WindowGetSize().X;
        var anchor = GetNode<Marker2D>("ContentA/TaskBar").Position;
        int anchorY = (int)(ContentOffset.Y + anchor.Y);
        int x = (int)(scrRect.Position.X + (scrRect.Size.X - winW) / 2);
        int y = taskbarTop - anchorY;
        DisplayServer.WindowSetPosition(new Vector2I(x, y));
    }

    private void EnableLayeredWindow()
    {
        var hWnd = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        if (hWnd == IntPtr.Zero) return;
        var style = WindowNative.GetWindowLong(hWnd, WindowNative.GWL_EXSTYLE);
        WindowNative.SetWindowLong(hWnd, WindowNative.GWL_EXSTYLE, style | WindowNative.WS_EX_LAYERED | WindowNative.WS_EX_TRANSPARENT);
        WindowNative.SetWindowPos(hWnd, WindowNative.HWND_TOPMOST, 0, 0, 0, 0, WindowNative.SWP_NOMOVE | WindowNative.SWP_NOSIZE | WindowNative.SWP_SHOWWINDOW);
        _isClickThrough = true;
    }

    private void SetClickThrough(bool enabled)
    {
        _isClickThrough = enabled;
        var hWnd = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        if (hWnd == IntPtr.Zero) return;
        var style = WindowNative.GetWindowLong(hWnd, WindowNative.GWL_EXSTYLE);
        if (enabled)
            WindowNative.SetWindowLong(hWnd, WindowNative.GWL_EXSTYLE, style | WindowNative.WS_EX_TRANSPARENT);
        else
            WindowNative.SetWindowLong(hWnd, WindowNative.GWL_EXSTYLE, style & ~WindowNative.WS_EX_TRANSPARENT);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                // 不响应面板区域的拖拽（避免滚动条等交互被拦截）
                var localPos = DisplayServer.MouseGetPosition() - DisplayServer.WindowGetPosition();
                if (_settingsPanel.ContainsPoint(localPos)) return;

                _mouseScreenStart = DisplayServer.MouseGetPosition();
                _windowPosStart = DisplayServer.WindowGetPosition();
                _potentialDrag = true;
            }
            else
            {
                if (_isDragging) GetViewport().SetInputAsHandled();
                _isDragging = false; _potentialDrag = false;
            }
        }
        else if (@event is InputEventMouseMotion && _potentialDrag)
        {
            var d = DisplayServer.MouseGetPosition() - _mouseScreenStart;
            if (Mathf.Abs(d.X) > DragThreshold || Mathf.Abs(d.Y) > DragThreshold)
            { _isDragging = true; SetClickThrough(false); }
            if (_isDragging)
            {
                DisplayServer.WindowSetPosition(_windowPosStart + d);
                if (_settingsPanel.IsOpen)
                    PositionPanelInBestSlot();
                GetViewport().SetInputAsHandled();
            }
        }
    }
}
