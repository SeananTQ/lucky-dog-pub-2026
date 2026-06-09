using Godot;

namespace LuckyDogRise;

/// <summary>
/// 窗口拖拽到屏幕边缘时自动吸附对齐。
/// 在拖拽结束时触发检查，只吸附四个边，不支持角吸附。
/// </summary>
public partial class TaskbarSnap : Node
{
    private const int SnapThreshold = 10;
    private DragHandler _dragHandler = null!;

    public override void _Ready()
    {
        _dragHandler = GetNode<DragHandler>("../DragHandler");
        _dragHandler.DragEnded += OnDragEnded;
    }

    private void OnDragEnded()
    {
        var windowPos = DisplayServer.WindowGetPosition();
        var windowSize = DisplayServer.WindowGetSize();
        var screenRect = DisplayServer.ScreenGetUsableRect();

        int x = windowPos.X;
        int y = windowPos.Y;

        // 左
        if (Mathf.Abs(windowPos.X - screenRect.Position.X) < SnapThreshold)
            x = (int)screenRect.Position.X;
        // 右
        else if (Mathf.Abs((windowPos.X + windowSize.X) - (screenRect.Position.X + screenRect.Size.X)) < SnapThreshold)
            x = (int)(screenRect.Position.X + screenRect.Size.X - windowSize.X);
        // 上
        if (Mathf.Abs(windowPos.Y - screenRect.Position.Y) < SnapThreshold)
            y = (int)screenRect.Position.Y;
        // 下
        else if (Mathf.Abs((windowPos.Y + windowSize.Y) - (screenRect.Position.Y + screenRect.Size.Y)) < SnapThreshold)
            y = (int)(screenRect.Position.Y + screenRect.Size.Y - windowSize.Y);

        if (x != windowPos.X || y != windowPos.Y)
            DisplayServer.WindowSetPosition(new Vector2I(x, y));
    }
}
