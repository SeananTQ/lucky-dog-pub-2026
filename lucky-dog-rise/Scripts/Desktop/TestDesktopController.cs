using System;
using Godot;

namespace LuckyDogRise;

/// <summary>
/// 透明窗口测试场景控制器
/// </summary>
public partial class TestDesktopController : Node2D
{
    private bool _isDragging;
    private bool _potentialDrag;
    private Vector2I _mouseScreenStart;
    private Vector2I _windowPosStart;

    private const float DragThreshold = 5f;

    public override void _Ready()
    {
        // 设置透明窗口
        SetupTransparentWindow();

        // 退出按钮
        GetNode<Button>("CanvasLayer/QuitButton").Pressed += () => GetTree().Quit();
    }

    private void SetupTransparentWindow()
    {
        // 透明背景
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Transparent, true);
        // 无边框
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
        // 置顶
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, true);

        // 窗口尺寸（刚好容纳缩放后的狗 + 留白）
        DisplayServer.WindowSetSize(new Vector2I(400, 400));
        // 居中
        var screenSize = DisplayServer.ScreenGetSize();
        DisplayServer.WindowSetPosition((screenSize - new Vector2I(400, 400)) / 2);

        // 透明背景渲染
        RenderingServer.SetDefaultClearColor(new Color(0, 0, 0, 0));

        // Windows API: 层叠窗口
        EnableLayeredWindow();
    }

    private void EnableLayeredWindow()
    {
        var hWnd = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        if (hWnd == IntPtr.Zero) return;

        // WS_EX_LAYERED
        var style = WindowNative.GetWindowLong(hWnd, WindowNative.GWL_EXSTYLE);
        WindowNative.SetWindowLong(hWnd, WindowNative.GWL_EXSTYLE, style | WindowNative.WS_EX_LAYERED);

        // DWM 扩展
        var margins = new WindowNative.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        WindowNative.DwmExtendFrameIntoClientArea(hWnd, ref margins);

        // 置顶
        WindowNative.SetWindowPos(hWnd, WindowNative.HWND_TOPMOST, 0, 0, 0, 0,
            WindowNative.SWP_NOMOVE | WindowNative.SWP_NOSIZE | WindowNative.SWP_SHOWWINDOW);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                // 点击任意位置都允许拖拽
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
                _isDragging = true;

            if (_isDragging)
            {
                DisplayServer.WindowSetPosition(_windowPosStart + delta);
                GetViewport().SetInputAsHandled();
            }
        }
    }
}
