using Godot;

namespace LuckyDogRise;

public partial class TutorialManager : Node
{
    public bool Enabled { get; set; } = true;

    private GameManager _game = null!;
    private Node2D _chipStack = null!;
    private Node2D _dogArea = null!;
    private Node2D _handArea = null!;

    public override void _Ready()
    {
        _game = GetParent().GetNode<GameManager>(".");
        _chipStack = GetParent().GetNode<Node2D>("ChipStack");
        _dogArea = GetParent().GetNode<Node2D>("DogArea");
        _handArea = GetParent().GetNode<Node2D>("HandArea");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Enabled) return;
        if (@event is not InputEventMouseButton mb || !mb.Pressed) return;

        switch (_game.State)
        {
            case GameState.WaitingForBet:
                Bounce(_chipStack);
                break;
            case GameState.Dealt:
            case GameState.Holding:
                Bounce(_game.HasDogGivenHint ? _handArea : _dogArea);
                break;
            case GameState.Settled:
                if (_game.PendingReward != null && IsInstanceValid(_game.PendingReward))
                    Bounce(_game.PendingReward);
                break;
        }
    }

    private void Bounce(Node2D node)
    {
        var tween = CreateTween();
        var origY = node.Position.Y;
        tween.TweenProperty(node, "position:y", origY - 12, 0.08)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Quad);
        tween.TweenProperty(node, "position:y", origY, 0.1)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Bounce);
    }
}
