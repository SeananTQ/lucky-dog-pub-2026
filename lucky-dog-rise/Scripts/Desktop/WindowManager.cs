using System;
using Godot;

namespace LuckyDogRise;

public partial class WindowManager : Node
{
    public const int GameViewWidth = 1200;
    public const int GameViewHeight = 1200;
    public const int HostWidth = 1900;   // 1200 + 350 左 + 350 右
    public const int HostHeight = 1550;  // 1200 + 175 上 + 175 下
    public const int GameViewOffsetX = 350;
    public const int GameViewOffsetY = 175;

    /// <summary>游戏面板在屏幕坐标系中的矩形区域</summary>
    public Rect2 GameViewScreenRect
    {
        get
        {
            var hostPos = DisplayServer.WindowGetPosition();
            return new Rect2(
                hostPos.X + GameViewOffsetX,
                hostPos.Y + GameViewOffsetY,
                GameViewWidth,
                GameViewHeight
            );
        }
    }

    public override void _Ready()
    {
        SetupTransparentWindow();
        SetupLayeredWindow();
    }

    private void SetupTransparentWindow()
    {
        // 透明背景
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Transparent, true);
        // 无边框
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
        // 置顶
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, true);

        // 宿主窗口尺寸（游戏面板 + 面板预留空间）
        DisplayServer.WindowSetSize(new Vector2I(HostWidth, HostHeight));
        // 居中定位
        var screenSize = DisplayServer.ScreenGetSize();
        var centerPos = (screenSize - new Vector2I(HostWidth, HostHeight)) / 2;
        DisplayServer.WindowSetPosition(centerPos);

        // 渲染透明背景
        RenderingServer.SetDefaultClearColor(new Color(0, 0, 0, 0));
    }

    private void SetupLayeredWindow()
    {
        var hWnd = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        if (hWnd == IntPtr.Zero) return;

        // 追加 WS_EX_LAYERED 样式
        var style = WindowNative.GetWindowLong(hWnd, WindowNative.GWL_EXSTYLE);
        WindowNative.SetWindowLong(hWnd, WindowNative.GWL_EXSTYLE, style | WindowNative.WS_EX_LAYERED);

        // DWM 扩展整个客户区
        var margins = new WindowNative.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        WindowNative.DwmExtendFrameIntoClientArea(hWnd, ref margins);

        // 重新置顶
        WindowNative.SetWindowPos(hWnd, WindowNative.HWND_TOPMOST, 0, 0, 0, 0,
            WindowNative.SWP_NOMOVE | WindowNative.SWP_NOSIZE | WindowNative.SWP_SHOWWINDOW);
    }

    public void SetAlwaysOnTop(bool onTop)
    {
        var hWnd = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        if (hWnd == IntPtr.Zero) return;

        WindowNative.SetWindowPos(hWnd,
            onTop ? WindowNative.HWND_TOPMOST : (IntPtr)1,
            0, 0, 0, 0,
            WindowNative.SWP_NOMOVE | WindowNative.SWP_NOSIZE | WindowNative.SWP_SHOWWINDOW);
    }
}
