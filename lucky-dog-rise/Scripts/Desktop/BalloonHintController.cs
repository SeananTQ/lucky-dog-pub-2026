#nullable enable

using System;
using Godot;

namespace LuckyDogRise;

public partial class BalloonHintController : PanelContainer
{
    public enum TailSide
    {
        Left,
        Right,
    }

    [Signal] public delegate void PressedEventHandler();

    [Export] private TextureRect _iconRect = null!;
    [Export] private Label _textLabel = null!;
    [Export] private Polygon2D _tail = null!;
    [Export] public TailSide TailPlacement { get; set; } = TailSide.Left;
    [Export] public float TailInset { get; set; } = 16f;

    private Tween? _flashTween;
    private Color _normalTextColor = Colors.White;
    private readonly Color _warningTextColor = new(1f, 0.18f, 0.18f);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        _iconRect.Visible = false;
        _textLabel.Text = "";
        _normalTextColor = _textLabel.GetThemeColor("font_color");
        UpdateTail();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            UpdateTail();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            EmitSignal(SignalName.Pressed);
    }

    public void ShowCountdown(TimeSpan remaining)
    {
        _iconRect.Visible = false;
        _textLabel.Visible = true;
        _textLabel.Text = $"{Math.Max(0, (int)remaining.TotalMinutes):00}:{remaining.Seconds:00}";
        ResetTextColor();
    }

    public void ShowCost(Texture2D? icon, int cost)
    {
        _iconRect.Texture = icon;
        _iconRect.Visible = icon != null;
        _textLabel.Visible = true;
        _textLabel.Text = cost.ToString("N0");
        ResetTextColor();
    }

    public void ShowIconOnly(Texture2D? icon)
    {
        _iconRect.Texture = icon;
        _iconRect.Visible = icon != null;
        _textLabel.Visible = false;
        ResetTextColor();
    }

    public void FlashTextRed()
    {
        _flashTween?.Kill();
        _flashTween = CreateTween();
        for (var i = 0; i < 2; i++)
        {
            _flashTween.TweenCallback(Callable.From(() =>
                _textLabel.AddThemeColorOverride("font_color", _warningTextColor)));
            _flashTween.TweenInterval(0.12);
            _flashTween.TweenCallback(Callable.From(ResetTextColor));
            _flashTween.TweenInterval(0.12);
        }
    }

    private void ResetTextColor()
    {
        _textLabel.AddThemeColorOverride("font_color", _normalTextColor);
    }

    private void UpdateTail()
    {
        if (_tail == null)
            return;

        var x = TailPlacement == TailSide.Left
            ? TailInset
            : Mathf.Max(TailInset, Size.X - TailInset - 18f);
        _tail.Position = new Vector2(x, Size.Y - 2f);
        _tail.Polygon = TailPlacement == TailSide.Left
            ? [new Vector2(0, 0), new Vector2(18, 0), new Vector2(0, 16)]
            : [new Vector2(0, 0), new Vector2(18, 0), new Vector2(18, 16)];
    }
}
