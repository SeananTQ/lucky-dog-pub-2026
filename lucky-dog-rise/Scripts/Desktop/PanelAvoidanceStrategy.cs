using System;
using Godot;

namespace LuckyDogRise;

public enum PanelHostMode
{
    BossKey,
    Play,
}

public readonly record struct HostLayoutContext(
    PanelHostMode Mode,
    Vector2 MainContentOrigin,
    Vector2 MainContentSize,
    Vector2 PanelSize,
    int PlayInfoPanelWidth,
    int PlayGameSize);

public readonly record struct HostLayoutSpec(
    Vector2I WindowSize,
    Vector2 MainContentOrigin);

public readonly record struct PanelPlacementContext(
    PanelHostMode Mode,
    Vector2I WindowPosition,
    Vector2I HostSize,
    Rect2I UsableScreen,
    Vector2 MainContentPosition,
    Vector2I MainContentSize,
    Vector2I PanelSize,
    float SidePanelBottomY,
    Rect2? InfoPanelRect,
    int TopActionHeight,
    int PlaySettingsGap);

public readonly record struct PanelPlacementResult(Vector2 PanelPosition);

public interface IPanelAvoidanceStrategy
{
    bool ReflowWhileDragging { get; }

    HostLayoutSpec CalculateHostLayout(HostLayoutContext context);

    PanelPlacementResult CalculatePanelPlacement(PanelPlacementContext context);
}

/// <summary>
/// The original real-time nine-slot panel avoidance behavior. This strategy intentionally
/// preserves its existing size formula, five-pixel tolerance, slot order, and fallbacks.
/// It contains no Godot node or native-window mutations; ModeManager remains the executor.
/// </summary>
public sealed class LegacyRealtimeGridStrategy : IPanelAvoidanceStrategy
{
    private const int LegacyScreenTolerance = 5;

    // 789 / 456 / 123. Preserve the currently accepted per-mode priorities verbatim.
    private static readonly int[] BossKeyPanelSlotPriority =
    [
        8, 9, 7, 6, 4, 2, 3, 1,
    ];

    private static readonly int[] PlayPanelSlotPriority =
    [
        6, 8, 9, 7, 4, 2, 3, 1,
    ];

    public bool ReflowWhileDragging => true;

    public HostLayoutSpec CalculateHostLayout(HostLayoutContext context)
    {
        if (context.Mode == PanelHostMode.Play)
        {
            int panelWidth = (int)context.PanelSize.X;
            int panelHeight = (int)context.PanelSize.Y;
            int contentWidth = context.PlayInfoPanelWidth + context.PlayGameSize;
            int contentHeight = context.PlayGameSize;
            return new HostLayoutSpec(
                new Vector2I(
                    contentWidth + panelWidth * 2,
                    Math.Max(contentHeight, panelHeight) + panelHeight * 2),
                context.PanelSize);
        }

        return new HostLayoutSpec(
            new Vector2I(
                Mathf.CeilToInt(
                    context.MainContentOrigin.X
                    + context.MainContentSize.X
                    + context.PanelSize.X),
                Mathf.CeilToInt(
                    context.MainContentOrigin.Y
                    + context.MainContentSize.Y
                    + context.PanelSize.Y)),
            context.MainContentOrigin);
    }

    public PanelPlacementResult CalculatePanelPlacement(PanelPlacementContext context)
    {
        int panelWidth = context.PanelSize.X;
        int panelHeight = context.PanelSize.Y;
        float mainX = context.MainContentPosition.X;
        float mainY = context.MainContentPosition.Y;
        int mainWidth = context.MainContentSize.X;
        int mainHeight = context.MainContentSize.Y;
        float sideY = context.SidePanelBottomY - panelHeight;
        float centerX = mainX + mainWidth / 2f - panelWidth / 2f;

        bool Fits(int screenX, int screenY)
        {
            if (screenX < context.UsableScreen.Position.X - LegacyScreenTolerance
                || screenX + panelWidth > context.UsableScreen.End.X + LegacyScreenTolerance
                || screenY < context.UsableScreen.Position.Y - LegacyScreenTolerance
                || screenY + panelHeight > context.UsableScreen.End.Y + LegacyScreenTolerance)
            {
                return false;
            }

            if (context.InfoPanelRect is { } infoPanelRect)
            {
                var settingsRect = new Rect2(screenX, screenY, panelWidth, panelHeight);
                var infoScreenRect = new Rect2(
                    context.WindowPosition.X + infoPanelRect.Position.X,
                    context.WindowPosition.Y + infoPanelRect.Position.Y,
                    infoPanelRect.Size.X,
                    infoPanelRect.Size.Y);
                if (settingsRect.Intersects(infoScreenRect))
                    return false;
            }

            return true;
        }

        var priority = context.Mode == PanelHostMode.Play
            ? PlayPanelSlotPriority
            : BossKeyPanelSlotPriority;

        foreach (var slot in priority)
        {
            var position = GetPanelSlotPosition(
                slot,
                mainX,
                mainY,
                mainWidth,
                mainHeight,
                panelWidth,
                panelHeight,
                sideY,
                centerX);
            if (context.Mode == PanelHostMode.Play && (slot == 6 || slot == 9 || slot == 3))
                position.X += context.PlaySettingsGap;
            if (Fits(
                    context.WindowPosition.X + position.X,
                    context.WindowPosition.Y + position.Y))
            {
                return new PanelPlacementResult(new Vector2(position.X, position.Y));
            }
        }

        bool TopActionsFit(int screenX, int screenY)
        {
            return screenX >= context.UsableScreen.Position.X - LegacyScreenTolerance
                && screenX + panelWidth <= context.UsableScreen.End.X + LegacyScreenTolerance
                && screenY >= context.UsableScreen.Position.Y - LegacyScreenTolerance
                && screenY + context.TopActionHeight
                    <= context.UsableScreen.End.Y + LegacyScreenTolerance;
        }

        var centerFallback = GetPanelSlotPosition(
            5,
            mainX,
            mainY,
            mainWidth,
            mainHeight,
            panelWidth,
            panelHeight,
            sideY,
            centerX);
        if (TopActionsFit(
                context.WindowPosition.X + centerFallback.X,
                context.WindowPosition.Y + centerFallback.Y))
        {
            return new PanelPlacementResult(new Vector2(centerFallback.X, centerFallback.Y));
        }

        int[] partialBottomSlots = [2, 1, 3];
        foreach (var slot in partialBottomSlots)
        {
            var position = GetPanelSlotPosition(
                slot,
                mainX,
                mainY,
                mainWidth,
                mainHeight,
                panelWidth,
                panelHeight,
                sideY,
                centerX);
            if (context.Mode == PanelHostMode.Play && slot == 3)
                position.X += context.PlaySettingsGap;
            if (TopActionsFit(
                    context.WindowPosition.X + position.X,
                    context.WindowPosition.Y + position.Y))
            {
                return new PanelPlacementResult(new Vector2(position.X, position.Y));
            }
        }

        var fallback = GetPanelSlotPosition(
            2,
            mainX,
            mainY,
            mainWidth,
            mainHeight,
            panelWidth,
            panelHeight,
            sideY,
            centerX);
        int screenMinX = context.UsableScreen.Position.X
            + LegacyScreenTolerance
            - context.WindowPosition.X;
        int screenMaxX = context.UsableScreen.End.X
            - LegacyScreenTolerance
            - panelWidth
            - context.WindowPosition.X;
        int screenMinY = context.UsableScreen.Position.Y
            + LegacyScreenTolerance
            - context.WindowPosition.Y;
        int screenMaxY = context.UsableScreen.End.Y
            - LegacyScreenTolerance
            - context.TopActionHeight
            - context.WindowPosition.Y;
        int hostMaxX = Math.Max(0, context.HostSize.X - panelWidth);
        int hostMaxY = Math.Max(0, context.HostSize.Y - panelHeight);

        int allowedMinX = Math.Max(0, screenMinX);
        int allowedMaxX = Math.Min(hostMaxX, screenMaxX);
        fallback.X = allowedMaxX >= allowedMinX
            ? Mathf.Clamp(fallback.X, allowedMinX, allowedMaxX)
            : Mathf.Clamp(fallback.X, 0, hostMaxX);

        int allowedMinY = Math.Max(0, screenMinY);
        int allowedMaxY = Math.Min(hostMaxY, screenMaxY);
        fallback.Y = allowedMaxY >= allowedMinY
            ? Mathf.Clamp(fallback.Y, allowedMinY, allowedMaxY)
            : Mathf.Clamp(fallback.Y, 0, hostMaxY);

        return new PanelPlacementResult(new Vector2(fallback.X, fallback.Y));
    }

    private static Vector2I GetPanelSlotPosition(
        int slot,
        float mainX,
        float mainY,
        int mainWidth,
        int mainHeight,
        int panelWidth,
        int panelHeight,
        float sideY,
        float centerX)
    {
        return slot switch
        {
            9 => new Vector2I((int)mainX + mainWidth, 0),
            8 => new Vector2I((int)centerX, 0),
            7 => Vector2I.Zero,
            6 => new Vector2I((int)mainX + mainWidth, (int)sideY),
            4 => new Vector2I(0, (int)sideY),
            3 => new Vector2I((int)mainX + mainWidth, (int)mainY + mainHeight),
            2 => new Vector2I((int)centerX, (int)mainY + mainHeight),
            1 => new Vector2I(0, (int)mainY + mainHeight),
            _ => new Vector2I(
                (int)(mainX + mainWidth / 2f - panelWidth / 2f),
                (int)(mainY + mainHeight / 2f - panelHeight / 2f)),
        };
    }
}
