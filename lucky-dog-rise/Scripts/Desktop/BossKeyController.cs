using System;
using Godot;

namespace LuckyDogRise;

/// <summary>
/// 伪装模式（老板来了）控制器。
/// 透明小窗口 + 狗 + 时钟/筹码 + 俩按钮 + 可选气泡。
/// </summary>
public partial class BossKeyController : Node2D
{
    [Signal]
    public delegate void ModeSwitchRequestedEventHandler();
    [Signal]
    public delegate void SystemPanelRequestedEventHandler();

    private bool _isDragging;
    private bool _potentialDrag;
    private bool _isClickThrough = true;
    private Vector2I _mouseScreenStart;
    private Vector2I _windowPosStart;
    private const float DragThreshold = 5f;

    private Label _mainText = null!;
    private Label _bubbleText = null!;
    private CanvasLayer _bubbleLayer = null!;
    private SettingsPanelController _settingsPanel = null!;
    private Node2D _baseWrapper = null!;
    private bool _showClock = true;
    private int _lastChips = -1;

    // 交互区
    private static readonly Rect2 DogHitRect = new(60, 90, 180, 180);
    private static readonly Rect2 ButtonHitRect = new(50, 265, 240, 40);

    public override void _Ready()
    {
        // 基础内容包装到独立节点，窗口扩展时只移 wrapper 不碰面板
        _baseWrapper = new Node2D();
        _baseWrapper.Name = "BaseWrapper";
        var toMove = new System.Collections.Generic.List<Node>();
        foreach (var child in GetChildren())
            if (child.Name != "WindowSize")
                toMove.Add(child);
        foreach (var child in toMove)
        {
            RemoveChild(child);
            _baseWrapper.AddChild(child);
        }
        AddChild(_baseWrapper);

        _mainText = GetNode<Label>("BaseWrapper/CanvasLayer/Panel/HBoxContainer/MainText");
        _bubbleText = GetNode<Label>("BaseWrapper/Bubble/BubbleBg/BubbleText");
        _bubbleLayer = GetNode<CanvasLayer>("BaseWrapper/Bubble");

        // 按钮
        var modeBtn = GetNode<Button>("BaseWrapper/CanvasLayer/Panel/HBoxContainer/ModeSwitch");
        modeBtn.Pressed += () => EmitSignal(SignalName.ModeSwitchRequested);
        var sysBtn = GetNode<Button>("BaseWrapper/CanvasLayer/Panel/HBoxContainer/SystemButton");
        sysBtn.Pressed += () => EmitSignal(SignalName.SystemPanelRequested);

        // 设置面板
        _settingsPanel = new SettingsPanelController();
        _settingsPanel.Name = "SettingsPanel";
        _settingsPanel.Layer = 10;
        AddChild(_settingsPanel);
        sysBtn.Pressed += () => ToggleSettingsPanel();

        // 窗口设置
        SetupTransparentWindow();
        EnableLayeredWindow();

        // 默认隐藏气泡
        _bubbleLayer.Visible = false;
    }

    public override void _Process(double _)
    {
        // 点击穿透：鼠标悬停交互区时关闭 WS_EX_TRANSPARENT
        var mouseScreen = DisplayServer.MouseGetPosition();
        var localPos = mouseScreen - DisplayServer.WindowGetPosition();
        bool overInteractive = DogHitRect.HasPoint(localPos) || ButtonHitRect.HasPoint(localPos);

        if (_isClickThrough && overInteractive)
            SetClickThrough(false);
        else if (!_isClickThrough && !overInteractive && !_isDragging)
            SetClickThrough(true);

        // 更新文字（每秒）
        int now = DateTime.Now.Hour * 100 + DateTime.Now.Minute;
        string text;
        if (_showClock)
        {
            text = $"{DateTime.Now.Hour:D2}:{DateTime.Now.Minute:D2}";
        }
        else
        {
            int chips = 0;
            if (GetParent() is GameManager gm)
                chips = gm.Chips;
            text = chips >= 10000 ? $"{chips / 1000f:F1}K" : chips.ToString();
        }
        _mainText.Text = text;
    }

    public void SetChips(int chips) { }

    public void ShowBubble(string msg)
    {
        _bubbleLayer.Visible = true;
        _bubbleText.Text = msg;
    }

    public void HideBubble()
    {
        _bubbleLayer.Visible = false;
    }

    // ===== 窗口管理 =====

    private void ToggleSettingsPanel()
    {
        if (_settingsPanel.IsOpen)
        {
            _settingsPanel.Close();
            _baseWrapper.Position = Vector2.Zero;
            var baseSize = GetNode<Marker2D>("WindowSize").Position;
            DisplayServer.WindowSetSize(new Vector2I((int)baseSize.X, (int)baseSize.Y));
            return;
        }

        var size = GetNode<Marker2D>("WindowSize").Position;
        int bw = (int)size.X, bh = (int)size.Y;
        int pw = (int)SettingsPanelController.PanelWidth;
        int ph = (int)SettingsPanelController.PanelHeight;
        var winPos = DisplayServer.WindowGetPosition();
        var scrSize = DisplayServer.ScreenGetSize();

        int left = winPos.X;
        int right = (int)(scrSize.X - (winPos.X + bw));
        int top = winPos.Y;
        int bottom = (int)(scrSize.Y - (winPos.Y + bh));

        if (left >= pw)
        {
            int newW = bw + pw, newH = Math.Max(bh, ph);
            DisplayServer.WindowSetPosition(new Vector2I(winPos.X - pw, winPos.Y));
            DisplayServer.WindowSetSize(new Vector2I(newW, newH));
            _baseWrapper.Position = new Vector2(pw, 0);
            _settingsPanel.SetTargetPosition(new Vector2(0, (newH - ph) / 2f));
        }
        else if (right >= pw)
        {
            int newW = bw + pw, newH = Math.Max(bh, ph);
            DisplayServer.WindowSetSize(new Vector2I(newW, newH));
            _baseWrapper.Position = Vector2.Zero;
            _settingsPanel.SetTargetPosition(new Vector2(bw, (newH - ph) / 2f));
        }
        else if (top >= ph)
        {
            int newW = Math.Max(bw, pw), newH = bh + ph;
            DisplayServer.WindowSetPosition(new Vector2I(winPos.X, winPos.Y - ph));
            DisplayServer.WindowSetSize(new Vector2I(newW, newH));
            _baseWrapper.Position = new Vector2(0, ph);
            _settingsPanel.SetTargetPosition(new Vector2((newW - pw) / 2f, 0));
        }
        else
        {
            int newW = Math.Max(bw, pw), newH = bh + ph;
            DisplayServer.WindowSetSize(new Vector2I(newW, newH));
            _baseWrapper.Position = Vector2.Zero;
            _settingsPanel.SetTargetPosition(new Vector2((newW - pw) / 2f, bh));
        }

        _settingsPanel.Open();
    }

    private void SetupTransparentWindow()
    {
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Transparent, true);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, true);
        var size = GetNode<Marker2D>("WindowSize").Position;
        DisplayServer.WindowSetSize(new Vector2I((int)size.X, (int)size.Y));
        SetWindowAboveTaskbar();
        // 位置保持当前不变
        RenderingServer.SetDefaultClearColor(new Color(0, 0, 0, 0));
    }

    private void SetWindowAboveTaskbar()
    {
        var screenRect = DisplayServer.ScreenGetUsableRect();
        var taskbarTop = (int)(screenRect.Position.Y + screenRect.Size.Y);
        var anchor = GetNode<Marker2D>("TaskBar").Position;
        var x = (int)(screenRect.Position.X + (screenRect.Size.X - DisplayServer.WindowGetSize().X) / 2);
        var y = taskbarTop - (int)anchor.Y;
        DisplayServer.WindowSetPosition(new Vector2I(x, y));
    }

    private void EnableLayeredWindow()
    {
        var hWnd = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        if (hWnd == IntPtr.Zero) return;
        var style = WindowNative.GetWindowLong(hWnd, WindowNative.GWL_EXSTYLE);
        WindowNative.SetWindowLong(hWnd, WindowNative.GWL_EXSTYLE,
            style | WindowNative.WS_EX_LAYERED | WindowNative.WS_EX_TRANSPARENT);
        WindowNative.SetWindowPos(hWnd, WindowNative.HWND_TOPMOST, 0, 0, 0, 0,
            WindowNative.SWP_NOMOVE | WindowNative.SWP_NOSIZE | WindowNative.SWP_SHOWWINDOW);
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
                _mouseScreenStart = DisplayServer.MouseGetPosition();
                _windowPosStart = DisplayServer.WindowGetPosition();
                _potentialDrag = true;
            }
            else
            {
                if (_isDragging)
                    GetViewport().SetInputAsHandled();
                _isDragging = false;
                _potentialDrag = false;
            }
        }
        else if (@event is InputEventMouseMotion && _potentialDrag)
        {
            var delta = DisplayServer.MouseGetPosition() - _mouseScreenStart;
            if (Mathf.Abs(delta.X) > DragThreshold || Mathf.Abs(delta.Y) > DragThreshold)
            {
                _isDragging = true;
                SetClickThrough(false);
            }
            if (_isDragging)
            {
                DisplayServer.WindowSetPosition(_windowPosStart + delta);
                GetViewport().SetInputAsHandled();
            }
        }
    }
}
