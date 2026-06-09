using System;
using Godot;

namespace LuckyDogRise;

/// <summary>
/// 透明窗口测试场景控制器
/// 演示 [Export] 引用父节点和子节点，避免 GetNode 路径问题。
/// </summary>
public partial class TestDesktopController : Node2D
{
    [Export] private Node2D _parentNode = null!;
    [Export] private Sprite2D _childA = null!;
    [Export] private Sprite2D _childB = null!;

    private bool _isDragging;
    private bool _potentialDrag;
    private bool _isClickThrough = true;
    private Vector2I _mouseScreenStart;
    private Vector2I _windowPosStart;
    private Button _quitButton = null!;

    private const float DragThreshold = 5f;

    private static readonly Rect2 DogHitRect = new(100, 70, 120, 120);
    private static readonly Rect2 BtnHitRect = new(5, 5, 110, 50);

    public override void _Ready()
    {
        _quitButton = GetNode<Button>("CanvasLayer/QuitButton");
        _quitButton.Pressed += () => GetTree().Quit();

        // 演示 [Export] 引用：父节点移动，子节点跟随
        GD.Print($"[Export] _parentNode={_parentNode?.Name}, _childA={_childA?.Name}, _childB={_childB?.Name}");

        // 即使 Parent 被挪到别的容器下，引用仍然有效
        // 父节点位置改变，子节点自动跟随
        if (_parentNode != null)
        {
            _parentNode.Position = new Vector2(50, 50);
            GD.Print($"ChildA global: {_childA?.GlobalPosition}");
        }

        SetupTransparentWindow();
    }

    public override void _Process(double _)
    {
        var mouseScreen = DisplayServer.MouseGetPosition();
        var windowPos = DisplayServer.WindowGetPosition();
        var localPos = mouseScreen - windowPos;

        bool overInteractive = DogHitRect.HasPoint(localPos) || BtnHitRect.HasPoint(localPos);

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
                SetClickThrough(false);
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
