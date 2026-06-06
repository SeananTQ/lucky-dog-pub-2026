using Godot;

namespace LuckyDogRise;

public partial class DragHandler : Node
{
    [Signal]
    public delegate void DragEndedEventHandler();

    private const float DragThreshold = 5f;
    private bool _isDragging;
    private bool _potentialDrag;
    private Vector2I _mouseScreenStart;
    private Vector2I _windowPosStart;

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                if (IsInGameView(mb.GlobalPosition))
                {
                    _mouseScreenStart = DisplayServer.MouseGetPosition();
                    _windowPosStart = DisplayServer.WindowGetPosition();
                    _potentialDrag = true;
                }
            }
            else
            {
                if (_isDragging)
                {
                    GetViewport().SetInputAsHandled();
                    EmitSignal(SignalName.DragEnded);
                }
                _isDragging = false;
                _potentialDrag = false;
            }
        }
        else if (@event is InputEventMouseMotion && _potentialDrag)
        {
            var screenPos = DisplayServer.MouseGetPosition();
            var delta = screenPos - _mouseScreenStart;
            if (Mathf.Abs(delta.X) > DragThreshold || Mathf.Abs(delta.Y) > DragThreshold)
                _isDragging = true;

            if (_isDragging)
            {
                DisplayServer.WindowSetPosition(_windowPosStart + delta);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private static bool IsInGameView(Vector2 localPos)
    {
        return localPos.X >= WindowManager.GameViewOffsetX
            && localPos.X <= WindowManager.GameViewOffsetX + WindowManager.GameViewWidth
            && localPos.Y >= WindowManager.GameViewOffsetY
            && localPos.Y <= WindowManager.GameViewOffsetY + WindowManager.GameViewHeight;
    }
}
