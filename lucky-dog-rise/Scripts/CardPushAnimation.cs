using System;
using System.Linq;
using Godot;

namespace LuckyDogRise;

/// <summary>
/// Shared visual-only push, directional flip and straighten. The caller owns the card
/// value/held state and supplies the face swap; this class knows no room or poker rules.
/// Works with a centered Sprite2D body as well as a top-left-origin Control.
/// </summary>
public sealed class CardPushAnimation
{
    public const double FlipHalfSeconds = 0.08;
    public CanvasItem Visual { get; }
    public Vector2 Size { get; }
    public Vector2 Center { get; }
    public Vector2 BaseScale { get; }
    public float HomeRotation { get; }
    public bool IsFlipping => _flip?.IsRunning() == true;
    public bool IsMoving => _motion?.IsRunning() == true;
    public bool IsAvailable => GodotObject.IsInstanceValid(Visual) && Visual.IsInsideTree() && Visual.IsVisibleInTree();
    public Vector2 FlipDirection { get; private set; }
    public float FlipProgress { get; private set; }
    internal long ResetVersion { get; private set; }
    private readonly CanvasItem _orderingRoot;
    private readonly ShaderMaterial[] _materials;
    private readonly Vector2 _home;
    private readonly int _homeZ;
    private Tween _flip, _motion;

    public CardPushAnimation(CanvasItem visual, Vector2 size, Vector2 center,
        CanvasItem orderingRoot = null, params ShaderMaterial[] materials)
    {
        if (visual is not Control && visual is not Node2D)
            throw new ArgumentException("Card visual must be a Control or Node2D.", nameof(visual));
        Visual = visual; Size = size; Center = center;
        _orderingRoot = orderingRoot ?? visual;
        _materials = materials.Where(m => m != null).Distinct().ToArray();
        _home = Position;
        HomeRotation = visual is Control control ? control.Rotation : ((Node2D)visual).Rotation;
        BaseScale = visual is Control c ? c.Scale : ((Node2D)visual).Scale;
        _homeZ = _orderingRoot.ZIndex;
    }

    private Vector2 Position => Visual is Control c ? c.Position : ((Node2D)Visual).Position;
    public float ChooseTiltAngle() => HomeRotation + Mathf.DegToRad((float)GD.RandRange(-14, 14));

    /// <summary>Undeformed, centered card pose in the visual parent's coordinate space.</summary>
    public Transform2D PawPose(float angle)
    {
        Vector2 pivot = Visual is Control c ? c.PivotOffset : Vector2.Zero;
        float skew = Visual is Node2D n ? n.Skew : 0;
        var pose = new Transform2D(angle, BaseScale, skew, Position + pivot);
        pose.Origin = pose * (Center - pivot);
        return pose;
    }

    /// <returns>False when no directional material/direction exists; caller can use its normal flip.</returns>
    public bool Flip(Vector2 direction, Action swapFace)
    {
        StopFlip();
        if (!IsAvailable || _materials.Length == 0 || direction.LengthSquared() <= 0.001f) return false;
        FlipDirection = direction.Normalized();
        foreach (var material in _materials)
        {
            material.SetShaderParameter("card_size", Size);
            material.SetShaderParameter("card_center", Center);
            material.SetShaderParameter("flip_direction", FlipDirection);
        }
        _flip = Visual.CreateTween();
        _flip.TweenMethod(Callable.From<float>(SetFlipProgress), 0f, 0.5f, FlipHalfSeconds)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        _flip.TweenCallback(Callable.From(swapFace));
        _flip.TweenMethod(Callable.From<float>(SetFlipProgress), 0.5f, 1f, FlipHalfSeconds)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        _flip.TweenCallback(Callable.From(() => SetFlipProgress(0)));
        return true;
    }

    private void SetFlipProgress(float progress)
    {
        FlipProgress = progress;
        foreach (var material in _materials) material.SetShaderParameter("flip_progress", progress);
    }

    public void StopFlip()
    {
        _flip?.Kill(); _flip = null;
        FlipDirection = Vector2.Zero;
        SetFlipProgress(0);
    }

    public void Tilt(int order, float? angle = null)
    {
        StopMotion();
        _orderingRoot.ZIndex = order;
        // Control picking follows sibling order rather than Z; move the whole card, not
        // just a face sprite, so an overlapping card is both drawn and picked on top.
        _orderingRoot.GetParent().MoveChild(_orderingRoot, -1);
        _motion = Visual.CreateTween().SetParallel(true);
        _motion.TweenProperty(Visual, "position", _home + new Vector2((float)GD.RandRange(-10, 10), (float)GD.RandRange(-10, 10)), 0.16)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _motion.TweenProperty(Visual, "rotation", angle ?? ChooseTiltAngle(), 0.16)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }

    public void Straighten(int slot)
    {
        StopMotion();
        _motion = Visual.CreateTween();
        if (slot > 0) _motion.TweenInterval(slot * 0.05);
        _motion.TweenProperty(Visual, "rotation", HomeRotation + Mathf.DegToRad(2.4f * (0.62f + slot * 0.095f)), 0.14)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _motion.Parallel().TweenProperty(Visual, "position", _home + Vector2.Up * (8 + slot), 0.14)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _motion.TweenProperty(Visual, "rotation", HomeRotation, 0.19)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _motion.Parallel().TweenProperty(Visual, "position", _home, 0.19)
            .SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        _motion.TweenCallback(Callable.From(() => _orderingRoot.ZIndex = _homeZ));
    }

    public void StopMotion() { _motion?.Kill(); _motion = null; }
    public void Cancel()
    {
        ResetVersion++;
        StopFlip(); StopMotion();
    }
    public void Reset()
    {
        Cancel();
        Visual.Set("position", _home); Visual.Set("rotation", HomeRotation); Visual.Set("scale", BaseScale);
        _orderingRoot.ZIndex = _homeZ;
    }
}
