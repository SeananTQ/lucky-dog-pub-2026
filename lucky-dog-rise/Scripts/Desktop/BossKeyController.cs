using System;
using Godot;

namespace LuckyDogRise;

public partial class BossKeyController : Node2D
{
    [Signal] public delegate void ModeSwitchRequestedEventHandler();
    [Signal] public delegate void SystemPanelRequestedEventHandler();

    private TestSettingPanelController _settingsPanel = null!;

    private bool _isDragging, _potentialDrag, _isClickThrough = true;
    private Vector2I _mouseScreenStart, _windowPosStart;
    private const float DragThreshold = 5f;

    private static readonly Rect2 DogHitRect = new(60, 90, 180, 180);
    private static readonly Rect2 BtnHitRect = new(50, 265, 240, 40);

    public override void _Ready()
    {
        var modeBtn = GetNode<Button>("CanvasLayer/Panel/HBoxContainer/ModeSwitch");
        var sysBtn = GetNode<Button>("CanvasLayer/Panel/HBoxContainer/SystemButton");
        modeBtn.Pressed += () => EmitSignal(SignalName.ModeSwitchRequested);
        sysBtn.Pressed += () => EmitSignal(SignalName.SystemPanelRequested);

        _settingsPanel = GD.Load<PackedScene>("res://Scenes/TestSettingPanel.tscn").Instantiate<TestSettingPanelController>();
        _settingsPanel.Name = "SettingsPanel";
        CallDeferred(nameof(AddSettingsPanelToRoot));

        SetupTransparentWindow();
        EnableLayeredWindow();
        GetNode<CanvasLayer>("Bubble").Visible = false;
    }

    private void AddSettingsPanelToRoot()
    {
        GetTree().Root.AddChild(_settingsPanel);
        var btn = GetNode<Button>("CanvasLayer/Panel/HBoxContainer/SystemButton");
        btn.Pressed += () => ToggleSettingsPanel();
    }

    public override void _Process(double _)
    {
        var localPos = DisplayServer.MouseGetPosition() - DisplayServer.WindowGetPosition();
        bool over = DogHitRect.HasPoint(localPos) || BtnHitRect.HasPoint(localPos);
        if (_isClickThrough && over) SetClickThrough(false);
        else if (!_isClickThrough && !over && !_isDragging) SetClickThrough(true);
    }

    private void ToggleSettingsPanel()
    {
        if (_settingsPanel.IsOpen)
        {
            _settingsPanel.Close();
            Position = Vector2.Zero;
            GetNode<CanvasLayer>("CanvasLayer").Offset = Vector2.Zero;
            var sz = GetNode<Marker2D>("WindowSize").Position;
            DisplayServer.WindowSetSize(new Vector2I((int)sz.X, (int)sz.Y));
            return;
        }

        var baseSz = GetNode<Marker2D>("WindowSize").Position;
        int bw = (int)baseSz.X, bh = (int)baseSz.Y;
        int pw = (int)TestSettingPanelController.PanelWidth;
        int ph = (int)TestSettingPanelController.PanelHeight;
        var winPos = DisplayServer.WindowGetPosition();
        var scr = DisplayServer.ScreenGetSize();
        var canvas = GetNode<CanvasLayer>("CanvasLayer");

        int L = winPos.X, R = (int)(scr.X - (winPos.X + bw));
        int T = winPos.Y, Btm = (int)(scr.Y - (winPos.Y + bh));

        if (L >= pw)
        {
            DisplayServer.WindowSetPosition(new Vector2I(winPos.X - pw, winPos.Y));
            DisplayServer.WindowSetSize(new Vector2I(bw + pw, Math.Max(bh, ph)));
            Position = new Vector2(pw, 0);
            canvas.Offset = new Vector2(pw, 0);
            _settingsPanel.SetTargetPosition(new Vector2(0, (Math.Max(bh, ph) - ph) / 2f));
        }
        else if (R >= pw)
        {
            DisplayServer.WindowSetSize(new Vector2I(bw + pw, Math.Max(bh, ph)));
            _settingsPanel.SetTargetPosition(new Vector2(bw, (Math.Max(bh, ph) - ph) / 2f));
        }
        else if (T >= ph)
        {
            DisplayServer.WindowSetPosition(new Vector2I(winPos.X, winPos.Y - ph));
            DisplayServer.WindowSetSize(new Vector2I(Math.Max(bw, pw), bh + ph));
            Position = new Vector2(0, ph);
            canvas.Offset = new Vector2(0, ph);
            _settingsPanel.SetTargetPosition(new Vector2((Math.Max(bw, pw) - pw) / 2f, 0));
        }
        else
        {
            DisplayServer.WindowSetSize(new Vector2I(Math.Max(bw, pw), bh + ph));
            _settingsPanel.SetTargetPosition(new Vector2((Math.Max(bw, pw) - pw) / 2f, bh));
        }
        _settingsPanel.Open();
    }

    // ===== 窗口管理 =====

    private void SetupTransparentWindow()
    {
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Transparent, true);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, true);
        var sz = GetNode<Marker2D>("WindowSize").Position;
        DisplayServer.WindowSetSize(new Vector2I((int)sz.X, (int)sz.Y));
        SetWindowAboveTaskbar();
        RenderingServer.SetDefaultClearColor(new Color(0, 0, 0, 0));
    }

    private void SetWindowAboveTaskbar()
    {
        var scrRect = DisplayServer.ScreenGetUsableRect();
        int taskbarTop = (int)(scrRect.Position.Y + scrRect.Size.Y);
        int x = (int)(scrRect.Position.X + (scrRect.Size.X - DisplayServer.WindowGetSize().X) / 2);
        var anchor = GetNode<Marker2D>("TaskBar").Position;
        DisplayServer.WindowSetPosition(new Vector2I(x, taskbarTop - (int)anchor.Y));
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
                GetViewport().SetInputAsHandled();
            }
        }
    }
}
