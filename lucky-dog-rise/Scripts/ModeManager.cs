using System;
using Godot;

namespace LuckyDogRise;

public partial class ModeManager : Node2D
{
    public enum Mode { BossKey, Play, Immersive }
    public Mode CurrentMode { get; private set; } = Mode.BossKey;

    private TestSettingPanelController _settingsPanel = null!;
    public TestSettingPanelController SettingsPanelObj => _settingsPanel;
    private Label _mainText = null!;
    private Vector2 _windowBaseSize;
    private Vector2 _panelSize;
    private Vector2 _contentOffset;

    private bool _isDragging, _potentialDrag, _isClickThrough = true;
    private Vector2I _mouseScreenStart, _windowPosStart;
    private const float DragThreshold = 5f;

    private bool _taskbarSnapped;
    private const int SnapThreshold = 15;
    private const int BreakawayThreshold = 30;

    private Rect2 _dogHitRect;
    private Rect2 _btnHitRect;

    public override void _Ready()
    {
        _mainText = GetNode<Label>("CanvasLayer/Panel/HBoxContainer/MainText");
        var modeBtn = GetNode<Button>("CanvasLayer/Panel/HBoxContainer/ModeSwitch");
        var sysBtn = GetNode<Button>("CanvasLayer/Panel/HBoxContainer/SystemButton");
        modeBtn.Pressed += SwitchToPlay;
        sysBtn.Pressed += ToggleSettingsPanel;

        // 先实例化面板以读取实际尺寸
        _settingsPanel = GD.Load<PackedScene>("res://Scenes/TestSettingPanel.tscn").Instantiate<TestSettingPanelController>();
        _settingsPanel.Name = "SettingsPanel";
        _settingsPanel.Layer = 100;
        AddChild(_settingsPanel);

        _panelSize = _settingsPanel.PanelSize;
        _contentOffset = _panelSize;

        _dogHitRect = new Rect2(60 + _contentOffset.X, 90 + _contentOffset.Y, 180, 180);
        _btnHitRect = new Rect2(50 + _contentOffset.X, 265 + _contentOffset.Y, 240, 40);

        _windowBaseSize = GetNode<Marker2D>("ContentA/WindowSize").Position;
        GetNode<Node2D>("ContentA").Position = _contentOffset;
        SetupFatWindow();
        EnableLayeredWindow();

        GetNode<CanvasLayer>("CanvasLayer").Offset = _contentOffset;
        GetNode<CanvasLayer>("Bubble").Offset = _contentOffset;
        GetNode<CanvasLayer>("Bubble").Visible = false;

        var tracker = new GlobalInputTracker();
        tracker.Name = "GlobalInputTracker";
        AddChild(tracker);
    }

    private double _displayTimer;
    private SettingsManager.DisplayMode _lastMode = (SettingsManager.DisplayMode)(-1);

    public override void _Process(double _)
    {
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

        if (CurrentMode == Mode.BossKey)
        {
            var localPos = DisplayServer.MouseGetPosition() - DisplayServer.WindowGetPosition();
            bool over = _settingsPanel.IsOpen || _dogHitRect.HasPoint(localPos) || _btnHitRect.HasPoint(localPos);
            if (_isClickThrough && over) SetClickThrough(false);
            else if (!_isClickThrough && !over && !_isDragging) SetClickThrough(true);
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut && _settingsPanel.IsOpen)
        {
            var mouse = DisplayServer.MouseGetPosition();
            var wp = DisplayServer.WindowGetPosition();
            var ws = DisplayServer.WindowGetSize();
            if (mouse.X < wp.X || mouse.X > wp.X + ws.X ||
                mouse.Y < wp.Y || mouse.Y > wp.Y + ws.Y)
                _settingsPanel.CloseImmediate();
        }
    }

    // ===== 模式切换 =====

    private CanvasItem _playContent = null!;

    private void SwitchToPlay()
    {
        if (CurrentMode == Mode.Play) return;
        if (_settingsPanel.IsOpen) _settingsPanel.CloseImmediate();

        SetClickThrough(false);
        HideBossKeyContent();

        if (_playContent == null)
        {
            _playContent = (CanvasItem)GD.Load<PackedScene>("res://Scenes/Main.tscn").Instantiate();
            _playContent.Name = "PlayContent";
            AddChild(_playContent);
            var gm = _playContent as GameManager;
            if (gm != null)
            {
                gm.Position = Vector2.Zero;
                _settingsPanel.RandomizeRequested += gm.OnRandomizeScene;
                _settingsPanel.RandomizeDogRequested += gm.OnRandomizeDog;
            }
        }

        _playContent.Visible = true;
        CurrentMode = Mode.Play;
    }

    private void SwitchToBossKey()
    {
        if (CurrentMode == Mode.BossKey) return;
        if (_settingsPanel.IsOpen) _settingsPanel.CloseImmediate();

        if (_playContent != null)
            _playContent.Visible = false;

        ShowBossKeyContent();
        SetClickThrough(true);
        CurrentMode = Mode.BossKey;
    }

    private void HideBossKeyContent()
    {
        GetNode<Node2D>("ContentA").Visible = false;
        GetNode<CanvasLayer>("CanvasLayer").Visible = false;
        GetNode<CanvasLayer>("Bubble").Visible = false;
    }

    private void ShowBossKeyContent()
    {
        GetNode<Node2D>("ContentA").Visible = true;
        GetNode<CanvasLayer>("CanvasLayer").Visible = true;
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
        int pw = (int)_panelSize.X;
        int ph = (int)_panelSize.Y;
        const int pad = 5;

        bool Fits(int sx, int sy) =>
            sx >= -pad && sx + pw <= scrSize.X + pad &&
            sy >= -pad && sy + ph <= scrSize.Y + pad;

        float bY = _contentOffset.Y + ah - ph;            // B 底边与 A 底边对齐
        float centerX = _contentOffset.X + aw / 2f - pw / 2f; // 水平居中

        // 左侧（4 号位默认）
        int leftY = winPos.Y + (int)bY;
        if (Fits(winPos.X, leftY))
        {
            _settingsPanel.SetPanelPosition(new Vector2(0, bY));
            return;
        }
        // 右侧（6 号位）
        int rx = winPos.X + (int)_contentOffset.X + aw;
        if (Fits(rx, leftY))
        {
            _settingsPanel.SetPanelPosition(new Vector2(_contentOffset.X + aw, bY));
            return;
        }
        // 正下方（2 号位）
        int cx = winPos.X + (int)centerX;
        int btmY = winPos.Y + (int)_contentOffset.Y + ah;
        if (Fits(cx, btmY))
        {
            _settingsPanel.SetPanelPosition(new Vector2(centerX, _contentOffset.Y + ah));
            return;
        }
        // 右下（3 号位）
        int rX = winPos.X + (int)_contentOffset.X + aw;
        if (Fits(rX, btmY))
        {
            _settingsPanel.SetPanelPosition(new Vector2(_contentOffset.X + aw, _contentOffset.Y + ah));
            return;
        }
        // 左下（1 号位）
        if (Fits(winPos.X, btmY))
        {
            _settingsPanel.SetPanelPosition(new Vector2(0, _contentOffset.Y + ah));
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
            _settingsPanel.SetPanelPosition(new Vector2(_contentOffset.X + aw, 0));
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

        int winW = (int)_windowBaseSize.X + (int)_panelSize.X * 2;
        int winH = (int)_windowBaseSize.Y + (int)_panelSize.Y * 2;
        DisplayServer.WindowSetSize(new Vector2I(winW, winH));
        SetWindowAboveTaskbar();
        RenderingServer.SetDefaultClearColor(new Color(0, 0, 0, 0));
    }

    private void ApplyTaskbarSnap(ref Vector2I newPos)
    {
        var scrRect = DisplayServer.ScreenGetUsableRect();
        int taskbarTop = scrRect.Position.Y + scrRect.Size.Y;
        var anchor = GetNode<Marker2D>("ContentA/TaskBar").Position;
        int anchorY = (int)(_contentOffset.Y + anchor.Y);
        int snappedY = taskbarTop - anchorY;

        int dist = Math.Abs(newPos.Y - snappedY);

        if (_taskbarSnapped)
        {
            if (newPos.Y < snappedY - BreakawayThreshold)
                _taskbarSnapped = false;
            else
                newPos.Y = snappedY;
        }
        else if (dist < SnapThreshold)
        {
            _taskbarSnapped = true;
            newPos.Y = snappedY;
        }
    }

    private void SetWindowAboveTaskbar()
    {
        var scrRect = DisplayServer.ScreenGetUsableRect();
        int taskbarTop = (int)(scrRect.Position.Y + scrRect.Size.Y);
        int winW = DisplayServer.WindowGetSize().X;
        var anchor = GetNode<Marker2D>("ContentA/TaskBar").Position;
        int anchorY = (int)(_contentOffset.Y + anchor.Y);
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
                _taskbarSnapped = false;
            }
        }
        else if (@event is InputEventMouseMotion && _potentialDrag)
        {
            var d = DisplayServer.MouseGetPosition() - _mouseScreenStart;
            if (Mathf.Abs(d.X) > DragThreshold || Mathf.Abs(d.Y) > DragThreshold)
            { _isDragging = true; SetClickThrough(false); }
            if (_isDragging)
            {
                var newPos = _windowPosStart + d;
                ApplyTaskbarSnap(ref newPos);
                DisplayServer.WindowSetPosition(newPos);
                if (_settingsPanel.IsOpen)
                    PositionPanelInBestSlot();
                GetViewport().SetInputAsHandled();
            }
        }
    }
}
