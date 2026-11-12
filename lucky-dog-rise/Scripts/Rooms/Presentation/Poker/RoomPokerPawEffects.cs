#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LuckyDogRise.Rooms.Poker;

namespace LuckyDogRise.Rooms;

// Temporary copies, not reparented dog parts. Rendering crosses the poker SubViewport;
// hit testing, the authority clock, textures and original paw transforms are untouched.
public partial class RoomPokerPawEffects : Node2D
{
    [Export] private CanvasLayer _layer;
    [Export] private Godot.Collections.Array<Sprite2D> _sprites = new();
    public const double ApproachSeconds = 0.10, PushSeconds = 0.16, ReturnSeconds = 0.38;
    public const double ReturnTurnSeconds = 0.10;
    public const float MaxPushAngleDegrees = 35;
    private Func<int, bool, Node2D> _source;
    private Func<CanvasItem, Transform2D> _toWindow;
    private readonly List<Flight> _flights = new();
    private readonly Dictionary<Node2D, Color> _hidden = new();
    private sealed class Flight
    {
        public Sprite2D Copy, Source;
        public Node2D Root;
        public RoomPokerCardView Card;
        public Action<Vector2> Contact;
        public double Age, Delay;
        public bool Local, Contacted, Returning;
        public Transform2D Start;
        public float Angle;
        public Vector2 Direction;
    }
    public bool IsTouching => _flights.Any(f => f.Age < f.Delay + PushSeconds);
    internal int ActiveCount => _flights.Count;
    internal int TouchCount { get; private set; }
    internal Vector2 LastReturnPosition { get; private set; }
    public bool HasPendingFlip(RoomPokerCardView card) => _flights.Any(f => f.Card == card && !f.Contacted);

    public void Bind(Viewport viewport, Func<int, bool, Node2D> source, Func<CanvasItem, Transform2D> toWindow)
    {
        if (_layer.CustomViewport != viewport)
        {
            // Godot 4.6's live setter changes vp without rebinding child_order_changed.
            // Let EXIT/ENTER_TREE pair the original and new viewport registrations correctly.
            RemoveChild(_layer);
            _layer.CustomViewport = viewport;
            AddChild(_layer);
        }
        _source = source; _toWindow = toWindow;
    }

    public void Play(PokerFlip flip, RoomPokerCardView card, bool local, float angle, Action<Vector2> contact)
    {
        bool right = flip.Number == 2;
        var root = _source?.Invoke(flip.PlayerId, right);
        var source = IsInstanceValid(root) && root.IsVisibleInTree()
            ? root.GetChildren().OfType<Sprite2D>().FirstOrDefault(s => s.Visible && s.Texture != null) : null;
        var sprite = _sprites.FirstOrDefault(s => !_flights.Any(f => f.Copy == s));
        if (source == null || sprite == null || _toWindow == null) { contact(Vector2.Zero); return; }
        // A rapid local click must never wait for a prior remote touch of the same card.
        if (local)
            foreach (var old in _flights.Where(f => f.Card == card && !f.Contacted).ToArray()) Touch(old);
        double delay = local ? 0 : Math.Max(ApproachSeconds,
            _flights.Where(f => f.Card == card).Select(f => f.Delay + PushSeconds - f.Age).DefaultIfEmpty(0).Max());
        if (!_hidden.ContainsKey(root))
        {
            _hidden[root] = root.Modulate;
            root.Modulate = new Color(root.Modulate, 0);
        }
        sprite.Texture = source.Texture;
        sprite.FlipH = source.FlipH; sprite.FlipV = source.FlipV;
        sprite.Centered = source.Centered; sprite.Offset = source.Offset;
        sprite.Modulate = _hidden[root]; sprite.SelfModulate = source.SelfModulate;
        // Which paw moves and which way it pushes are independent. Each client lays out its
        // dogs differently, so derive the approach from this paw's actual screen position.
        var cardPose = _toWindow(card.GetParent<CanvasItem>()) * card.PawPose(angle);
        Vector2 screenPush = cardPose.Origin - _toWindow(source).Origin;
        // Prefer a shallow left/right push even when the dog sits above/below the card.
        // Clamp in screen space so the card's random tilt cannot turn it vertical again.
        float side = Mathf.Abs(screenPush.X) > 0.01f ? Mathf.Sign(screenPush.X) : (right ? -1 : 1);
        float horizontal = Mathf.Max(Mathf.Abs(screenPush.X),
            Mathf.Abs(screenPush.Y) / Mathf.Tan(Mathf.DegToRad(MaxPushAngleDegrees)));
        screenPush.X = side * Mathf.Max(horizontal, 0.001f);
        var inverse = cardPose.AffineInverse();
        Vector2 direction = (inverse.X * screenPush.X + inverse.Y * screenPush.Y).Normalized();
        var flight = new Flight { Copy = sprite, Source = source, Root = root, Card = card,
            Local = local, Contact = contact, Delay = delay, Angle = angle, Direction = direction };
        _flights.Add(flight);
        sprite.Show();
        if (local) Touch(flight);
        Pose(flight);
    }

    private void Touch(Flight flight)
    {
        if (flight.Contacted) return;
        flight.Contacted = true;
        TouchCount++;
        flight.Contact(flight.Direction);
    }

    public override void _Process(double delta)
    {
        // A long suspension catches up to the current snapshot instead of replaying old pushes.
        if (!double.IsFinite(delta) || delta > 0.75) { Cancel(); return; }
        foreach (var f in _flights.ToArray())
        {
            if (!IsInstanceValid(f.Root) || !IsInstanceValid(f.Source) || !IsInstanceValid(f.Card)
                || !f.Root.IsVisibleInTree() || !f.Card.IsVisibleInTree()) { Remove(f); continue; }
            f.Age += Math.Max(0, delta);
            if (f.Age >= f.Delay) Touch(f);
            Pose(f);
            if (f.Age >= f.Delay + PushSeconds + ReturnSeconds) Remove(f);
        }
    }

    private void Pose(Flight f)
    {
        // Use the undeformed card axes, not the in-flight projection of its face.
        var card = _toWindow(f.Card.GetParent<CanvasItem>()) * f.Card.PawPose(f.Angle);
        Vector2 axis = (card.X * f.Direction.X + card.Y * f.Direction.Y).Normalized();
        float width = card.X.Length() * f.Card.Size.X;
        var home = _toWindow(f.Source);
        float sourceWidth = f.Copy.Texture.GetWidth() * home.X.Length();
        float pawWidth = f.Local ? sourceWidth : width * 0.64f;
        float scale = f.Local ? 1 : pawWidth / sourceWidth;
        // Toes point along the sprite's local +Y, also for the mirrored right paw. Rotate
        // its existing basis instead of reconstructing a negative scale as a half turn.
        Vector2 forward = home.Y * (f.Copy.FlipV ? -1 : 1);
        float turn = forward.AngleTo(axis);
        float edge = Mathf.Min(f.Card.Size.X * 0.5f / Mathf.Max(Mathf.Abs(f.Direction.X), 0.0001f),
            f.Card.Size.Y * 0.5f / Mathf.Max(Mathf.Abs(f.Direction.Y), 0.0001f));
        Vector2 contact = card * (-f.Direction * edge * 0.86f);
        var touchPose = new Transform2D(home.X.Rotated(turn) * scale, home.Y.Rotated(turn) * scale, contact);
        if (f.Age < f.Delay)
        {
            float t = Mathf.Clamp((float)(f.Age / Math.Max(ApproachSeconds, f.Delay)), 0, 1);
            touchPose.Origin = contact - axis * pawWidth * (1 - t * t);
            f.Copy.Transform = touchPose;
        }
        else if (f.Age < f.Delay + PushSeconds)
        {
            float t = Mathf.Clamp((float)((f.Age - f.Delay) / PushSeconds), 0, 1);
            touchPose.Origin = contact + axis * width * 0.32f * (1 - (1 - t) * (1 - t));
            f.Copy.Transform = touchPose;
        }
        else
        {
            // No fixed target: dragging/scaling the dog while its paw returns is safe.
            double elapsed = f.Age - f.Delay - PushSeconds;
            float t = Mathf.Clamp((float)(elapsed / ReturnSeconds), 0, 1);
            if (!f.Returning)
            {
                f.Returning = true;
                touchPose.Origin = contact + axis * width * 0.32f;
                f.Start = touchPose;
            }
            float eased = t * t * (3 - 2 * t);
            var pose = f.Start.InterpolateWith(home, eased);
            float turnT = Mathf.Clamp((float)(elapsed / ReturnTurnSeconds), 0, 1);
            float angle = Mathf.LerpAngle(f.Start.Rotation, home.Rotation, 1 - Mathf.Pow(1 - turnT, 3));
            float correction = Mathf.AngleDifference(pose.Rotation, angle);
            pose.X = pose.X.Rotated(correction); pose.Y = pose.Y.Rotated(correction);
            f.Copy.Transform = pose;
            LastReturnPosition = f.Copy.Position;
        }
    }

    private void Remove(Flight flight)
    {
        flight.Copy.Hide();
        _flights.Remove(flight);
        if (_flights.Any(f => f.Root == flight.Root)) return;
        if (_hidden.Remove(flight.Root, out Color color) && IsInstanceValid(flight.Root)) flight.Root.Modulate = color;
    }
    public void Cancel()
    {
        foreach (var f in _flights.ToArray()) Remove(f);
    }
    public override void _ExitTree() => Cancel();
}
#endif
