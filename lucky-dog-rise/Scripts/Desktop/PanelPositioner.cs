using Godot;

namespace LuckyDogRise;

public enum PanelSide { Left, Right, Top, Bottom }

public static class PanelPositioner
{
    /// <summary>
    /// 计算面板的屏幕坐标位置。
    /// 优先级：左 > 右 > 上 > 下。
    /// </summary>
    /// <param name="screenSize">屏幕可用区域尺寸</param>
    /// <param name="gameViewScreenRect">游戏面板在屏幕上的矩形</param>
    /// <param name="panelSize">面板尺寸</param>
    /// <param name="gap">面板与游戏面板的间距</param>
    /// <returns>面板左上角在屏幕坐标系中的位置</returns>
    public static (Vector2 position, PanelSide side) Calculate(
        Vector2I screenSize,
        Rect2 gameViewScreenRect,
        Vector2 panelSize,
        float gap = 8f)
    {
        // 左
        if (gameViewScreenRect.Position.X - panelSize.X - gap >= 0)
        {
            var pos = new Vector2(
                gameViewScreenRect.Position.X - panelSize.X - gap,
                gameViewScreenRect.Position.Y
            );
            return (pos, PanelSide.Left);
        }

        // 右
        if (gameViewScreenRect.Position.X + gameViewScreenRect.Size.X + panelSize.X + gap <= screenSize.X)
        {
            var pos = new Vector2(
                gameViewScreenRect.Position.X + gameViewScreenRect.Size.X + gap,
                gameViewScreenRect.Position.Y
            );
            return (pos, PanelSide.Right);
        }

        // 上
        if (gameViewScreenRect.Position.Y - panelSize.Y - gap >= 0)
        {
            var pos = new Vector2(
                gameViewScreenRect.Position.X,
                gameViewScreenRect.Position.Y - panelSize.Y - gap
            );
            return (pos, PanelSide.Top);
        }

        // 下
        if (gameViewScreenRect.Position.Y + gameViewScreenRect.Size.Y + panelSize.Y + gap <= screenSize.Y)
        {
            var pos = new Vector2(
                gameViewScreenRect.Position.X,
                gameViewScreenRect.Position.Y + gameViewScreenRect.Size.Y + gap
            );
            return (pos, PanelSide.Bottom);
        }

        // 兜底：强制放在左侧，夹在屏幕边缘和游戏面板之间
        var fallbackX = Mathf.Max(0, gameViewScreenRect.Position.X - panelSize.X - gap);
        return (new Vector2(fallbackX, gameViewScreenRect.Position.Y), PanelSide.Left);
    }
}
