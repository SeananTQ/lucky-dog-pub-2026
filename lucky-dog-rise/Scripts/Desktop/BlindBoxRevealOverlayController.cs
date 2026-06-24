#nullable enable

using Godot;
using System.Linq;
using DataTables;

namespace LuckyDogRise;

public partial class BlindBoxRevealOverlayController : CanvasLayer
{
    [Signal] public delegate void RewardClaimRequestedEventHandler();
    [Signal] public delegate void RevealStepChangedEventHandler(int step);
    [Signal] public delegate void RewardShownEventHandler();

    [Export] private ColorRect _revealBackground = null!;
    [Export] private ColorRect _revealWhiteMask = null!;
    [Export] private TextureRect _boxSprite = null!;
    [Export] private TextureRect _boxShadow = null!;
    [Export] private Label _hintLabel = null!;
    [Export] private Control _rewardRoot = null!;
    [Export] private ColorRect _rewardBackground = null!;
    [Export] private ColorRect _rewardWhiteMask = null!;
    [Export] private TextureRect _rewardCellShadow = null!;
    [Export] private ItemCellController _rewardCell = null!;
    [Export] private Label _debugLabel = null!;

    [Export] private Vector2 _rewardDropOffset = new(0, -230);
    [Export] private Vector2 _boxAppearOffset = new(0, -90);
    [Export] private Vector2 _boxJumpOffset = new(0, -100);
    [Export] private Vector2 _boxFinalOpenOffset = new(0, -150);

    private PendingBlindBoxReward _pending = null!;
    private Tween _tween = null!;
    private Tween _rotationTween = null!;
    private bool _animating;
    private Vector2 _initialBoxSpritePosition;
    private Vector2 _initialBoxShadowPosition;
    private Vector2 _initialHintPosition;
    private Vector2 _initialRewardCellPosition;
    private Vector2 _initialRewardCellShadowPosition;
    private Vector2 _initialDebugLabelPosition;
    private Vector2 _boxSpriteRestPosition;
    private Vector2 _boxShadowRestPosition;
    private Vector2 _rewardCellPosition;
    private Vector2 _rewardCellShadowPosition;

    public override void _Ready()
    {
        Visible = false;
        _initialBoxSpritePosition = _boxSprite.Position;
        _initialBoxShadowPosition = _boxShadow.Position;
        _initialHintPosition = _hintLabel.Position;
        _initialRewardCellPosition = _rewardCell.Position;
        _initialRewardCellShadowPosition = _rewardCellShadow.Position;
        _initialDebugLabelPosition = _debugLabel.Position;
        _revealBackground.GuiInput += OnRevealGuiInput;
        _rewardCell.Pressed += () => EmitSignal(SignalName.RewardClaimRequested);
    }

    public void ShowReward(PendingBlindBoxReward pending, bool animateDrop)
    {
        _pending = pending;
        if (!pending.RewardShown)
        {
            ShowReveal(pending);
            return;
        }

        var item = LubanData.Tables.TbItem.GetOrDefault(pending.ItemId);
        if (item == null)
            return;

        Visible = true;
        _rewardRoot.Visible = true;
        SetRevealVisible(false);
        ApplyRewardLayout();
        _rewardCell.Setup(item, isEquipped: false, count: 1, isNew: false);
        _debugLabel.Text = pending.DebugText;
        _rewardBackground.Color = GetBlindBoxBackgroundColor(item.ItemRarity);
        PlayRewardDrop(animateDrop, item.ItemRarity);
    }

    public void HideOverlay()
    {
        KillTweens();
        Visible = false;
        _animating = false;
    }

    private void ShowReveal(PendingBlindBoxReward pending)
    {
        var path = LubanData.Tables.TbBlindBoxRevealPath.GetOrDefault(pending.RevealPathId);
        if (path == null)
        {
            EmitSignal(SignalName.RewardShown);
            pending.RewardShown = true;
            ShowReward(pending, animateDrop: false);
            return;
        }

        Visible = true;
        _rewardRoot.Visible = false;
        SetRevealVisible(true);
        _revealWhiteMask.Color = new Color(1f, 1f, 1f, 0f);
        ApplyRevealLayout();
        ApplyBlindBoxVisual(GetRevealRarity(path, pending.RevealStep), instant: true);
        if (pending.RevealStep >= 4)
            PlayPreRewardShake();
        else
            PlayAppear();
    }

    private void SetRevealVisible(bool visible)
    {
        _revealBackground.Visible = visible;
        _revealWhiteMask.Visible = visible;
        _boxSprite.Visible = visible;
        _boxShadow.Visible = visible;
        _hintLabel.Visible = visible;
    }

    private void ApplyRevealLayout()
    {
        _boxSpriteRestPosition = _initialBoxSpritePosition;
        _boxShadowRestPosition = _initialBoxShadowPosition;
        _boxSprite.Position = _boxSpriteRestPosition;
        _boxShadow.Position = _boxShadowRestPosition;
        _hintLabel.Position = _initialHintPosition;
        _boxSprite.Scale = Vector2.One;
        _boxSprite.RotationDegrees = 0f;
        _boxShadow.Scale = Vector2.One;
    }

    private void ApplyRewardLayout()
    {
        _rewardCellPosition = _initialRewardCellPosition;
        _rewardCellShadowPosition = _initialRewardCellShadowPosition;
        _rewardCell.Position = _rewardCellPosition;
        _rewardCell.Scale = Vector2.One;
        _rewardCellShadow.Visible = true;
        _rewardCellShadow.Position = _rewardCellShadowPosition;
        _rewardCellShadow.Scale = Vector2.One;
        _rewardCellShadow.Modulate = Colors.White;
        _debugLabel.Position = _initialDebugLabelPosition;
    }

    private void OnRevealGuiInput(InputEvent @event)
    {
        if (_pending == null || _animating)
            return;

        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            return;

        if (_pending.RevealStep >= 4)
        {
            PlayFinalOpen();
            return;
        }

        AdvanceReveal();
    }

    private void AdvanceReveal()
    {
        var path = LubanData.Tables.TbBlindBoxRevealPath.GetOrDefault(_pending.RevealPathId);
        if (path == null)
        {
            EmitSignal(SignalName.RewardShown);
            _pending.RewardShown = true;
            ShowReward(_pending, animateDrop: true);
            return;
        }

        var nextStep = _pending.RevealStep + 1;
        var oldRarity = GetRevealRarity(path, _pending.RevealStep);
        var newRarity = GetRevealRarity(path, nextStep);
        _pending.RevealStep = nextStep;
        EmitSignal(SignalName.RevealStepChanged, nextStep);
        PlayUpgrade(oldRarity, newRarity);
    }

    private void PlayAppear()
    {
        KillTweens();
        _animating = true;
        _hintLabel.Text = "点击继续";
        _boxSprite.Scale = new Vector2(0.6f, 0.6f);
        _boxSprite.Position = _boxSpriteRestPosition + _boxAppearOffset;
        _boxShadow.Scale = new Vector2(0.55f, 0.55f);
        _hintLabel.Modulate = Colors.Transparent;

        _tween = CreateTween();
        _tween.SetParallel(true);
        _tween.TweenProperty(_boxSprite, "scale", new Vector2(1.1f, 1.1f), 0.18).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_boxSprite, "position", _boxSpriteRestPosition, 0.34).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_boxShadow, "scale", Vector2.One, 0.34).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_hintLabel, "modulate", Colors.White, 0.15).SetDelay(0.28);
        _tween.SetParallel(false);
        _tween.TweenProperty(_boxSprite, "scale", Vector2.One, 0.12).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        _tween.TweenCallback(Callable.From(() => _animating = false));
    }

    private void PlayUpgrade(ERarity oldRarity, ERarity newRarity)
    {
        KillTweens();
        _animating = true;
        _hintLabel.Modulate = Colors.Transparent;

        var changed = oldRarity != newRarity;
        _tween = CreateTween();
        _tween.TweenProperty(_boxSprite, "scale", new Vector2(0.78f, 0.72f), 0.08).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        _tween.TweenProperty(_boxSprite, "position", _boxSpriteRestPosition + _boxJumpOffset, 0.18).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _tween.Parallel().TweenProperty(_boxSprite, "scale", new Vector2(1.1f, 1.1f), 0.18).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _tween.Parallel().TweenProperty(_boxShadow, "scale", new Vector2(0.5f, 0.5f), 0.18);
        if (changed)
            _tween.Parallel().TweenProperty(_revealBackground, "color", GetBlindBoxBackgroundColor(newRarity), 0.18);
        _tween.TweenCallback(Callable.From(() => ApplyBlindBoxVisual(newRarity, instant: false)));
        _tween.TweenProperty(_boxSprite, "scale", new Vector2(0.62f, 0.62f), 0.12).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        _tween.TweenProperty(_boxSprite, "position", _boxSpriteRestPosition, 0.22).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        _tween.Parallel().TweenProperty(_boxSprite, "scale", Vector2.One, 0.22).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _tween.Parallel().TweenProperty(_boxShadow, "scale", Vector2.One, 0.22);
        _tween.TweenProperty(_hintLabel, "modulate", Colors.White, 0.12);
        _tween.TweenCallback(Callable.From(() =>
        {
            if (_pending.RevealStep >= 3)
            {
                _pending.RevealStep = 4;
                EmitSignal(SignalName.RevealStepChanged, 4);
                PlayPreRewardShake();
                return;
            }
            _animating = false;
        }));
    }

    private void PlayPreRewardShake()
    {
        KillTweens();
        _animating = false;
        _hintLabel.Text = "点击开奖";
        _hintLabel.Modulate = Colors.White;
        _boxSprite.Position = _boxSpriteRestPosition;
        _boxSprite.RotationDegrees = 0f;
        _boxShadow.Scale = Vector2.One;

        _tween = CreateTween();
        _tween.SetLoops(0);
        _tween.TweenProperty(_boxSprite, "scale", new Vector2(1.08f, 0.92f), 0.055);
        _tween.Parallel().TweenProperty(_boxShadow, "scale", new Vector2(1.06f, 0.9f), 0.055);
        _tween.TweenProperty(_boxSprite, "scale", new Vector2(0.94f, 1.11f), 0.045);
        _tween.Parallel().TweenProperty(_boxShadow, "scale", new Vector2(0.92f, 1.04f), 0.045);
        _tween.TweenProperty(_boxSprite, "scale", new Vector2(1.13f, 0.96f), 0.05);
        _tween.Parallel().TweenProperty(_boxShadow, "scale", new Vector2(1.1f, 0.92f), 0.05);
        _tween.TweenProperty(_boxSprite, "scale", new Vector2(0.97f, 1.07f), 0.04);
        _tween.Parallel().TweenProperty(_boxShadow, "scale", new Vector2(0.94f, 1.02f), 0.04);
        //_tween.TweenProperty(_boxSprite, "scale", Vector2.One, 0.05);

        _rotationTween = CreateTween();
        _rotationTween.SetLoops(0);
        TweenShakeRotationStep(0f, -3f, 0.055);
        TweenShakeRotationStep(0f, 3f, 0.045);
        TweenShakeRotationStep(0f, -5f, 0.05);
        TweenShakeRotationStep(0f, 5f, 0.04);
        //TweenShakeRotationStep(0f, 0f, 0.05);
    }

    private void PlayFinalOpen()
    {
        KillTweens();
        _animating = true;
        _hintLabel.Modulate = Colors.Transparent;

        _tween = CreateTween();
        _tween.SetParallel(true);
        _tween.TweenProperty(_boxSprite, "position", _boxSpriteRestPosition + _boxFinalOpenOffset, 0.22).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_boxSprite, "scale", new Vector2(1.18f, 1.18f), 0.22).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_boxShadow, "scale", new Vector2(0.45f, 0.45f), 0.22);
        _tween.TweenProperty(_revealWhiteMask, "color", Colors.White, 0.22);
        _tween.SetParallel(false);
        _tween.TweenCallback(Callable.From(() =>
        {
            EmitSignal(SignalName.RewardShown);
            _pending.RewardShown = true;
            ShowReward(_pending, animateDrop: true);
            _animating = false;
        }));
    }

    private void PlayRewardDrop(bool animate, ERarity rarity)
    {
        KillTweens();
        _rewardWhiteMask.Color = animate ? Colors.White : new Color(1f, 1f, 1f, 0f);
        _rewardCell.Position = animate ? _rewardCellPosition + _rewardDropOffset : _rewardCellPosition;
        _rewardCell.Scale = animate ? new Vector2(0.85f, 0.85f) : Vector2.One;
        _rewardCellShadow.Position = _rewardCellShadowPosition;
        _rewardCellShadow.Scale = animate ? new Vector2(0.35f, 0.35f) : Vector2.One;
        _rewardCellShadow.Modulate = animate ? new Color(1f, 1f, 1f, 0.45f) : Colors.White;

        if (!animate)
            return;

        _tween = CreateTween();
        _tween.SetParallel(true);
        _tween.TweenProperty(_rewardWhiteMask, "color", new Color(1f, 1f, 1f, 0f), 0.16);
        _tween.TweenProperty(_rewardCell, "position", _rewardCellPosition, 0.38).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_rewardCell, "scale", Vector2.One, 0.22).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_rewardCellShadow, "scale", Vector2.One, 0.38).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_rewardCellShadow, "modulate", Colors.White, 0.24);
        _tween.SetParallel(false);
    }

    private void TweenShakeRotationStep(float restDegrees, float peakDegrees, double duration)
    {
        var halfDuration = duration * 0.5;
        _rotationTween.TweenProperty(_boxSprite, "rotation_degrees", peakDegrees, halfDuration);
        _rotationTween.TweenProperty(_boxSprite, "rotation_degrees", restDegrees, halfDuration);
    }

    private void KillTweens()
    {
        _tween?.Kill();
        _rotationTween?.Kill();
        _rotationTween = null!;
    }

    private void ApplyBlindBoxVisual(ERarity rarity, bool instant)
    {
        var visual = GetBlindBoxVisual(rarity);
        if (visual == null)
            return;
        _boxSprite.Texture = LoadBlindBoxTexture(visual.BlindBoxSpritePath);
        _boxShadow.Texture = LoadBlindBoxTexture(visual.BlindBoxShadowPath);
        if (instant)
            _revealBackground.Color = GetBlindBoxBackgroundColor(rarity);
    }

    private static ERarity GetRevealRarity(BlindBoxRevealPath path, int step) => step switch
    {
        <= 0 => path.StartRarity,
        1 => path.MiddleRarity1,
        2 => path.MiddleRarity2,
        _ => path.FinalRarity,
    };

    private static BlindBoxVisual? GetBlindBoxVisual(ERarity rarity)
    {
        return LubanData.Tables.TbBlindBoxVisual.DataList.FirstOrDefault(visual => visual.ItemRarity == rarity);
    }

    private static Texture2D? LoadBlindBoxTexture(string lubanPath)
    {
        if (string.IsNullOrWhiteSpace(lubanPath))
            return null;
        var path = PlayerInventory.ToResPath(lubanPath);
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    private static Color GetBlindBoxBackgroundColor(ERarity rarity)
    {
        var visual = GetBlindBoxVisual(rarity);
        if (visual == null || string.IsNullOrWhiteSpace(visual.BlindBoxBackgroundColor))
            return Colors.Black;
        var hex = visual.BlindBoxBackgroundColor.Trim().TrimStart('#');
        if (hex.Length == 6)
            hex += "ff";
        return Color.FromHtml(hex);
    }
}
