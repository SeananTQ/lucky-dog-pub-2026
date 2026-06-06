using System;
using Godot;

namespace LuckyDogRise;

/// <summary>
/// 透明窗口测试场景控制器
/// 使用 WS_EX_TRANSPARENT 动态开关实现"透明区域穿透、交互区捕获点击"。
/// </summary>
public partial class TestDesktopController : Node2D
{
    private bool _isDragging;
    private bool _potentialDrag;
    private bool _isClickThrough = true;
    private Vector2I _mouseScreenStart;
    private Vector2I _windowPosStart;
    private Button _quitButton = null!;

    private const float DragThreshold = 5f;

    // 狗头在 DogArea 内部坐标约 (588,516)，25% 缩放后约 (147,129)，交互区放大些
    private static readonly Rect2 DogHitRect = new(100, 70, 120, 120);

    public override void _Ready()
    {
        _quitButton = GetNode<Button>("CanvasLayer/QuitButton");
        _quitButton.Pressed += () => GetTree().Quit();

        SetupTransparentWindow();
    }

    public override void _Process(double _)
    {
        var mouseScreen = DisplayServer.MouseGetPosition();
        var windowPos = DisplayServer.WindowGetPosition();
        var localPos = mouseScreen - windowPos;

        bool overInteractive = _quitButton.GetGlobalRect().HasPoint(localPos)
                              || DogHitRect.HasPoint(localPos);

        if (_isClickThrough && overInteractive)
            SetClickThrough(false);
        else if (!_isClickThrough && !overInteractive && !_isDragging)
            SetClickThrough(true);
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
                SetClickThrough(false); // 拖拽时关闭穿透
            }

            if (_isDragging)
            {
                DisplayServer.WindowSetPosition(_windowPosStart + delta);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private void SetupTransparentWindow()
    {
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Transparent, true);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, true);

        DisplayServer.WindowSetSize(new Vector2I(400, 400));
        var screenSize = DisplayServer.ScreenGetSize();
        DisplayServer.WindowSetPosition((screenSize - new Vector2I(400, 400)) / 2);

        RenderingServer.SetDefaultClearColor(new Color(0, 0, 0, 0));

        EnableLayeredWindow();
    }

    private void EnableLayeredWindow()
    {
        var hWnd = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        if (hWnd == IntPtr.Zero) return;

        var style = WindowNative.GetWindowLong(hWnd, WindowNative.GWL_EXSTYLE);
        WindowNative.SetWindowLong(hWnd, WindowNative.GWL_EXSTYLE, style | WindowNative.WS_EX_LAYERED | WindowNative.WS_EX_TRANSPARENT);

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
}
