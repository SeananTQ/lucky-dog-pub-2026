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

    [Export] private Control _revealBackground = null!;
    [Export] private Polygon2D? _revealTail;
    [Export] private Control _revealWhiteMask = null!;
    [Export] private Polygon2D? _revealWhiteMaskTail;
    [Export] private TextureRect _boxSprite = null!;
    [Export] private TextureRect _boxShadow = null!;
    [Export] private Label _hintLabel = null!;
    [Export] private Control _rewardRoot = null!;
    [Export] private Control _rewardBackground = null!;
    [Export] private Polygon2D? _rewardTail;
    [Export] private Control _rewardWhiteMask = null!;
    [Export] private Polygon2D? _rewardWhiteMaskTail;
    [Export] private Control _rewardVisualRoot = null!;
    [Export] private TextureRect _rewardCellShadow = null!;
    [Export] private ItemCellController _rewardCell = null!;
    [Export] private Label _debugLabel = null!;

    [Export] private float _rewardDropHeightRatio = 1.8f;
    [Export] private float _boxAppearHeightRatio = 1.08f;
    [Export] private float _boxJumpHeightRatio = 0.7f;
    [Export] private float _boxFinalOpenHeightRatio = 0.35f;
    [Export] private float _boxAppearAirborneShadowScale = 0.456f;
    [Export] private float _boxJumpAirborneShadowScale = 0.7f;
    [Export] private float _boxVisualScale = 1f;
    [Export] private float _rewardVisualScale = 1f;
    [Export] private int _hintTextFontSize = 32;
    [Export] private Vector2 _boxShadowRuntimeOffset = Vector2.Zero;
    [Export] private Vector2 _rewardShadowRuntimeOffset = Vector2.Zero;
    [Export] private bool _showDebugLabel = true;
    [Export] private float _rewardAutoClaimSeconds = 3f;

    private PendingBlindBoxReward _pending = null!;
    private Tween _tween = null!;
    private Tween _rotationTween = null!;
    private bool _animating;
    private Vector2 _initialBoxSpritePosition;
    private Vector2 _initialBoxShadowPosition;
    private Vector2 _initialHintPosition;
    private Vector2 _initialRewardVisualRootPosition;
    private Vector2 _initialRewardCellShadowPosition;
    private Vector2 _initialDebugLabelPosition;
    private Vector2 _boxSpriteRestPosition;
    private Vector2 _boxShadowRestPosition;
    private Vector2 _rewardVisualRootPosition;
    private Vector2 _rewardCellShadowPosition;
    private bool _rewardAutoClaimActive;
    private bool _rewardClaimRequested;
    private double _rewardAutoClaimRemaining;

    public override void _Ready()
    {
        Visible = false;
        _initialBoxSpritePosition = _boxSprite.Position;
        _initialBoxShadowPosition = _boxShadow.Position;
        _initialHintPosition = _hintLabel.Position;
        _initialRewardVisualRootPosition = _rewardVisualRoot.Position;
        _initialRewardCellShadowPosition = _rewardCellShadow.Position;
        _initialDebugLabelPosition = _debugLabel.Position;
        _revealBackground.GuiInput += OnRevealGuiInput;
        _rewardBackground.GuiInput += OnRewardGuiInput;
        _rewardCell.Pressed += RequestRewardClaim;
        ApplyHintTextFontSize();
    }

    public override void _Process(double delta)
    {
        if (!_rewardAutoClaimActive || _pending == null || !_pending.RewardShown)
            return;

        if (_animating)
            return;

        _rewardAutoClaimRemaining -= delta;
        UpdateRewardCountdownLabel();
        if (_rewardAutoClaimRemaining <= 0.0)
            RequestRewardClaim();
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
        SetRewardVisible(true);
        ApplyRewardLayout();
        _rewardCell.Setup(item, isEquipped: false, count: 1, isNew: false);
        _debugLabel.Text = pending.DebugText;
        SetBackgroundColor(_rewardBackground, _rewardTail, GetBlindBoxBackgroundColor(item.ItemRarity));
        PlayRewardDrop(animateDrop, item.ItemRarity);
        StartRewardAutoClaimCountdown();
    }

    public void HideOverlay()
    {
        KillTweens();
        StopRewardAutoClaimCountdown();
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
        SetRewardVisible(false);
        SetRevealVisible(true);
        SetMaskColor(_revealWhiteMask, _revealWhiteMaskTail, new Color(1f, 1f, 1f, 0f));
        _rewardWhiteMask.Visible = false;
        if (_rewardWhiteMaskTail != null)
            _rewardWhiteMaskTail.Visible = false;
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
        if (_revealTail != null)
            _revealTail.Visible = visible;
        _revealWhiteMask.Visible = visible;
        if (_revealWhiteMaskTail != null)
            _revealWhiteMaskTail.Visible = visible;
        _boxSprite.Visible = visible;
        _boxShadow.Visible = visible;
        _hintLabel.Visible = visible;
    }

    private void SetRewardVisible(bool visible)
    {
        _rewardRoot.Visible = visible;
        if (_rewardTail != null)
            _rewardTail.Visible = visible;
        _rewardWhiteMask.Visible = visible;
        if (_rewardWhiteMaskTail != null)
            _rewardWhiteMaskTail.Visible = visible;
        _rewardVisualRoot.Visible = visible;
        _rewardCellShadow.Visible = visible;
        _rewardCell.Visible = visible;
        _hintLabel.Visible = visible;
        _debugLabel.Visible = visible && _showDebugLabel;
    }

    private void ApplyRevealLayout()
    {
        _boxSpriteRestPosition = _initialBoxSpritePosition;
        _boxShadowRestPosition = _initialBoxShadowPosition + _boxShadowRuntimeOffset;
        _boxSprite.Position = _boxSpriteRestPosition;
        _boxShadow.Position = _boxShadowRestPosition;
        _hintLabel.Position = _initialHintPosition;
        _boxSprite.Scale = GetBoxScale(Vector2.One);
        _boxSprite.RotationDegrees = 0f;
        _boxShadow.Scale = GetBoxScale(Vector2.One);
    }

    private void ApplyRewardLayout()
    {
        _rewardVisualRootPosition = _initialRewardVisualRootPosition;
        _rewardCellShadowPosition = _initialRewardCellShadowPosition + _rewardShadowRuntimeOffset;
        _rewardVisualRoot.Position = _rewardVisualRootPosition;
        _rewardVisualRoot.Scale = GetRewardScale(Vector2.One);
        _rewardCell.Scale = Vector2.One;
        _rewardCellShadow.Visible = true;
        _rewardCellShadow.Position = _rewardCellShadowPosition;
        _rewardCellShadow.Scale = Vector2.One;
        _rewardCellShadow.Modulate = Colors.White;
        _hintLabel.Position = _initialHintPosition;
        _hintLabel.Modulate = Colors.White;
        _debugLabel.Position = _initialDebugLabelPosition;
        _debugLabel.Visible = _showDebugLabel;
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

    private void OnRewardGuiInput(InputEvent @event)
    {
        if (_pending == null || _animating)
            return;

        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            return;

        RequestRewardClaim();
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
        _hintLabel.Text = "Tap to power up...";
        _boxSprite.Scale = GetBoxScale(new Vector2(0.4f, 0.4f));
        _boxSprite.Position = _boxSpriteRestPosition + BoxUpOffset(_boxAppearHeightRatio);
        _boxShadow.Scale = GetBoxScale(new Vector2(0.4f * _boxAppearAirborneShadowScale, 0.4f * _boxAppearAirborneShadowScale));
        _hintLabel.Modulate = Colors.Transparent;

        _tween = CreateTween();

        // 出现
        _tween.SetParallel(true);
        _tween.TweenProperty(_boxSprite, "scale", GetBoxScale(Vector2.One), 0.18).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_boxShadow, "scale", GetBoxScale(new Vector2(_boxAppearAirborneShadowScale, _boxAppearAirborneShadowScale)), 0.18).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        _tween.SetParallel(false);

        // 落地弹跳
        _tween.SetParallel(true);
        _tween.TweenProperty(_boxSprite, "position", _boxSpriteRestPosition, 0.34).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_boxShadow, "scale", GetBoxScale(Vector2.One), 0.34).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        _tween.SetParallel(false);

        // 提示文字在落地完成后淡入，避免 SetDelay 把上一组并行动画的总时长拖长。
        _tween.TweenProperty(_hintLabel, "modulate", Colors.White, 0.15);
        _tween.TweenCallback(Callable.From(() => _animating = false));
    }

    private void PlayUpgrade(ERarity oldRarity, ERarity newRarity)
    {
        KillTweens();
        _animating = true;
        _hintLabel.Modulate = Colors.Transparent;

        var changed = oldRarity != newRarity;
        var squashScale = new Vector2(1.2f, 0.8f);
        _tween = CreateTween();

        // 蓄力压扁
        _tween.SetParallel(true);
        _tween.TweenProperty(_boxSprite, "scale", GetBoxScale(squashScale), 0.18).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        _tween.TweenProperty(_boxShadow, "scale", GetBoxScale(new Vector2(1.2f, 0.8f)), 0.18).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        _tween.TweenProperty(_boxSprite, "position", _boxSpriteRestPosition + GetBoxGroundCompensationOffset(squashScale.Y), 0.18).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        _tween.SetParallel(false);

        // 压扁后停一小下，让玩家看见“蓄力完成”
        _tween.TweenInterval(0.04);


        // 起跳 瘦高的上窜
        _tween.SetParallel(true);
        _tween.TweenProperty(_boxSprite, "scale", GetBoxScale(new Vector2(0.8f, 1.2f)), 0.18).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_boxSprite, "position", _boxSpriteRestPosition + BoxUpOffset(_boxJumpHeightRatio * 0.8f), 0.18).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_boxShadow, "scale", GetBoxScale(new Vector2(_boxJumpAirborneShadowScale, _boxJumpAirborneShadowScale)), 0.18);
        if (changed)
            TweenRevealBackgroundColor(GetBlindBoxBackgroundColor(newRarity), 0.18);
        _tween.SetParallel(false);

        // 换盲盒图标
        _tween.TweenCallback(Callable.From(() => ApplyBlindBoxVisual(newRarity, instant: false)));

        // 砰！换图标后引起的膨胀，Elastic之后回到正常尺寸
        _tween.SetParallel(true);
        _tween.TweenProperty(_boxSprite, "scale", GetBoxScale(Vector2.One), 0.18).SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_boxShadow, "scale", GetBoxScale(new Vector2(_boxJumpAirborneShadowScale, _boxJumpAirborneShadowScale)) , 0.18).SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_boxSprite, "position", _boxSpriteRestPosition + BoxUpOffset(_boxJumpHeightRatio) , 0.18).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _tween.SetParallel(false);

        // 下落
        _tween.SetParallel(true);
        _tween.TweenProperty(_boxSprite, "position", _boxSpriteRestPosition, 0.34).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_boxShadow, "scale", GetBoxScale(Vector2.One), 0.34).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        _tween.SetParallel(false);
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
        _hintLabel.Text = "Open it up!";
        _hintLabel.Modulate = Colors.White;
        _boxSprite.Position = _boxSpriteRestPosition;
        _boxSprite.RotationDegrees = 0f;
        _boxSprite.Scale = GetBoxScale(Vector2.One);
        _boxShadow.Scale = GetBoxScale(Vector2.One);

        _tween = CreateTween();
        _tween.SetLoops(0);
        _tween.TweenProperty(_boxSprite, "scale", GetBoxScale(new Vector2(1.08f, 0.92f)), 0.055);
        _tween.Parallel().TweenProperty(_boxShadow, "scale", GetBoxScale(new Vector2(1.06f, 0.9f)), 0.055);
        _tween.TweenProperty(_boxSprite, "scale", GetBoxScale(new Vector2(0.94f, 1.11f)), 0.045);
        _tween.Parallel().TweenProperty(_boxShadow, "scale", GetBoxScale(new Vector2(0.92f, 1.04f)), 0.045);
        _tween.TweenProperty(_boxSprite, "scale", GetBoxScale(new Vector2(1.13f, 0.96f)), 0.05);
        _tween.Parallel().TweenProperty(_boxShadow, "scale", GetBoxScale(new Vector2(1.1f, 0.92f)), 0.05);
        _tween.TweenProperty(_boxSprite, "scale", GetBoxScale(new Vector2(0.97f, 1.07f)), 0.04);
        _tween.Parallel().TweenProperty(_boxShadow, "scale", GetBoxScale(new Vector2(0.94f, 1.02f)), 0.04);
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
        _tween.TweenProperty(_boxSprite, "position", _boxSpriteRestPosition + BoxUpOffset(_boxFinalOpenHeightRatio), 0.22).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_boxSprite, "scale", GetBoxScale(new Vector2(1.18f, 1.18f)), 0.22).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_boxShadow, "scale", GetBoxScale(new Vector2(_boxJumpAirborneShadowScale, _boxJumpAirborneShadowScale)), 0.22);
        TweenMaskColor(_revealWhiteMask, _revealWhiteMaskTail, Colors.White, 0.22);
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
        SetMaskColor(_rewardWhiteMask, _rewardWhiteMaskTail, animate ? Colors.White : new Color(1f, 1f, 1f, 0f));
        _rewardVisualRoot.Position = animate ? _rewardVisualRootPosition + RewardUpOffset(_rewardDropHeightRatio) : _rewardVisualRootPosition;
        _rewardVisualRoot.Scale = animate ? GetRewardScale(new Vector2(0.85f, 0.85f)) : GetRewardScale(Vector2.One);
        _rewardCellShadow.Position = _rewardCellShadowPosition;
        _rewardCellShadow.Scale = Vector2.One;
        _rewardCellShadow.Modulate = animate ? new Color(1f, 1f, 1f, 0.45f) : Colors.White;

        if (!animate)
            return;

        _tween = CreateTween();
        _tween.SetParallel(true);
        TweenMaskColor(_rewardWhiteMask, _rewardWhiteMaskTail, new Color(1f, 1f, 1f, 0f), 0.16);
        _tween.TweenProperty(_rewardVisualRoot, "position", _rewardVisualRootPosition, 0.38).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_rewardVisualRoot, "scale", GetRewardScale(Vector2.One), 0.22).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_rewardCellShadow, "modulate", Colors.White, 0.24);
        _tween.SetParallel(false);
    }

    private void StartRewardAutoClaimCountdown()
    {
        _rewardClaimRequested = false;
        _rewardAutoClaimActive = _rewardAutoClaimSeconds > 0f;
        _rewardAutoClaimRemaining = _rewardAutoClaimSeconds;
        UpdateRewardCountdownLabel();
    }

    private void StopRewardAutoClaimCountdown()
    {
        _rewardAutoClaimActive = false;
    }

    private void UpdateRewardCountdownLabel()
    {
        var seconds = Mathf.Max(0, Mathf.CeilToInt(_rewardAutoClaimRemaining));
        _hintLabel.Text = $"Auto-claiming in {seconds}s";
    }

    private void RequestRewardClaim()
    {
        if (_rewardClaimRequested)
            return;

        _rewardClaimRequested = true;
        StopRewardAutoClaimCountdown();
        EmitSignal(SignalName.RewardClaimRequested);
    }

    private Vector2 BoxUpOffset(float heightRatio) => new(0f, -GetBoxDisplayHeight() * heightRatio);

    private Vector2 RewardUpOffset(float heightRatio) => new(0f, -GetRewardCellDisplayHeight() * heightRatio);

    private Vector2 GetBoxScale(Vector2 animationScale) => animationScale * _boxVisualScale;

    private Vector2 GetRewardScale(Vector2 animationScale) => animationScale * _rewardVisualScale;

    private void ApplyHintTextFontSize()
    {
        if (_hintTextFontSize <= 0)
            return;

        // 桌宠模式会整体缩放共用舞台，所以提示文字需要单独放大；复制设置以免改到共享资源。
        var settings = _hintLabel.LabelSettings == null
            ? new LabelSettings()
            : (LabelSettings)_hintLabel.LabelSettings.Duplicate();
        settings.FontSize = _hintTextFontSize;
        _hintLabel.LabelSettings = settings;
    }

    private Vector2 GetBoxGroundCompensationOffset(float scaleY)
    {
        var lowerHalfHeight = Mathf.Max(GetBoxDisplayHeight() - _boxSprite.PivotOffset.Y, 0f);
        return new Vector2(0f, lowerHalfHeight * (1f - scaleY));
    }

    private float GetBoxDisplayHeight()
    {
        var height = _boxSprite.Size.Y;
        if (height <= 0f && _boxSprite.Texture != null)
            height = _boxSprite.Texture.GetHeight();
        return Mathf.Max(height, 1f);
    }

    private float GetRewardCellDisplayHeight()
    {
        var height = _rewardCell.Size.Y;
        return Mathf.Max(height, 1f);
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
            SetRevealBackgroundColor(GetBlindBoxBackgroundColor(rarity));
    }

    private void SetRevealBackgroundColor(Color color)
    {
        SetBackgroundColor(_revealBackground, _revealTail, color);
    }

    private void TweenRevealBackgroundColor(Color targetColor, double duration)
    {
        TweenBackgroundColor(_revealBackground, _revealTail, targetColor, duration);
    }

    private void SetBackgroundColor(Control background, Polygon2D? tail, Color color)
    {
        switch (background)
        {
            case ColorRect colorRect:
                colorRect.Color = color;
                break;
            case PanelContainer panel:
                ApplyPanelBackgroundColor(panel, color);
                break;
        }

        if (tail != null)
            tail.Color = color;
    }

    private void TweenBackgroundColor(Control background, Polygon2D? tail, Color targetColor, double duration)
    {
        if (background is ColorRect colorRect)
        {
            _tween.Parallel().TweenProperty(colorRect, "color", targetColor, duration);
            if (tail != null)
                _tween.Parallel().TweenProperty(tail, "color", targetColor, duration);
            return;
        }

        if (background is PanelContainer panel)
        {
            var startColor = GetPanelBackgroundColor(panel);
            _tween.Parallel().TweenMethod(
                Callable.From<Color>(color => SetBackgroundColor(background, tail, color)),
                startColor,
                targetColor,
                duration);
        }
    }

    private void SetMaskColor(Control mask, Polygon2D? tailMask, Color color)
    {
        switch (mask)
        {
            case ColorRect colorRect:
                colorRect.Color = color;
                break;
            case PanelContainer panel:
                ApplyPanelBackgroundColor(panel, color);
                break;
        }

        if (tailMask != null)
            tailMask.Color = color;
    }

    private void TweenMaskColor(Control mask, Polygon2D? tailMask, Color targetColor, double duration)
    {
        if (mask is ColorRect colorRect)
        {
            _tween.TweenProperty(colorRect, "color", targetColor, duration);
            if (tailMask != null)
                _tween.Parallel().TweenProperty(tailMask, "color", targetColor, duration);
            return;
        }

        if (mask is PanelContainer panel)
        {
            var startColor = GetPanelBackgroundColor(panel);
            _tween.TweenMethod(
                Callable.From<Color>(color => SetMaskColor(mask, tailMask, color)),
                startColor,
                targetColor,
                duration);
        }
    }

    private static void ApplyPanelBackgroundColor(PanelContainer panel, Color color)
    {
        if (panel.GetThemeStylebox("panel") is not StyleBoxFlat style)
            return;

        var localStyle = (StyleBoxFlat)style.Duplicate();
        localStyle.BgColor = color;
        panel.AddThemeStyleboxOverride("panel", localStyle);
    }

    private static Color GetPanelBackgroundColor(PanelContainer panel)
    {
        return panel.GetThemeStylebox("panel") is StyleBoxFlat style
            ? style.BgColor
            : Colors.Black;
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
