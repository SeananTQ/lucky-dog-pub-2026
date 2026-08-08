using Godot;
using System.Collections.Generic;
using DataTables;

namespace LuckyDogRise;

public partial class ItemAreaController : Node2D, IInteractionHintTarget
{
    [Signal] public delegate void InteractionActivatedEventHandler();
    [Signal] public delegate void InteractionIgnoredEventHandler();
    [Signal] public delegate void HintContextChangedEventHandler();

    private const string ReferenceRefreshmentFileName = "Whisky.png";
    private const double StatusBalloonHideDelaySeconds = 0.15;
    private const double RefreshmentFadeOutDurationSeconds = 0.22;
    private static readonly Vector2 UseFeedbackOvershootScale = new(1.08f, 1.12f);
    private static readonly Color UseFeedbackBrightModulate = new(1.22f, 1.22f, 1.22f, 1f);

    [Export] private Node2D _refreshmentAnchor = null!;
    [Export] private Sprite2D _refreshmentSprite = null!;
    [Export] private Button _clickButton = null!;
    [Export] private Control _useBalloon = null!;
    [Export] private TextureRect _useCheckIcon = null!;
    [Export] private Control _statusBalloon = null!;
    [Export] private Label _statusHandsLabel = null!;

    private readonly Dictionary<string, Vector2> _localCache = new();
    private readonly Dictionary<string, float> _balloonVerticalOffsetCache = new();
    private readonly Dictionary<string, float> _heightCache = new();
    private GameData _gameData;
    private Tween _hintTween;
    private Tween _refreshmentFadeTween;
    private Tween _useFeedbackTween;
    private Tween _useBalloonTween;
    private Tween _useCheckTween;
    private Tween _useCheckHintTween;
    private Tween _statusBalloonTween;
    private Vector2 _referenceRefreshmentPosition;
    private Vector2 _refreshmentRestPosition;
    private Vector2 _useBalloonBasePosition;
    private Vector2 _statusBalloonBasePosition;
    private Vector2 _useBalloonBaseScale;
    private Vector2 _statusBalloonBaseScale;
    private bool _refreshmentHovered;
    private bool _statusBalloonHovered;
    private bool _useFromRefreshmentArmed;
    private int _statusBalloonHideToken;
    private int _refreshmentFadeToken;
    private int _displayedRefreshmentItemId;
    private TableRefreshmentStatus _lastRefreshmentStatus = TableRefreshmentStatus.Empty;
    private bool _cacheBuilt;

    public bool CanPlayInteractionHint =>
        _useBalloon.IsVisibleInTree()
        || (_gameData != null
            && _gameData.RefreshmentState.Status == TableRefreshmentStatus.ReadyToUse
            && _refreshmentSprite.Visible);

    public bool IsInteractionHintPlaying => (_hintTween?.IsRunning() ?? false)
        || (_useCheckHintTween?.IsRunning() ?? false);
    public bool IsUseBalloonOpen => _useBalloon.IsVisibleInTree();

    public bool CapturesPointerAt(Vector2 viewportPosition) =>
        ContainsViewportPoint(_useBalloon, viewportPosition)
        || ContainsViewportPoint(_statusBalloon, viewportPosition);

    public override void _Ready()
    {
        _referenceRefreshmentPosition = _refreshmentAnchor.Position + _refreshmentSprite.Position;
        _refreshmentRestPosition = _refreshmentAnchor.Position;
        _useBalloonBasePosition = _useBalloon.Position;
        _statusBalloonBasePosition = _statusBalloon.Position;
        _useBalloonBaseScale = _useBalloon.Scale;
        _statusBalloonBaseScale = _statusBalloon.Scale;
        BuildPositionCache();

        _useBalloon.Visible = false;
        _statusBalloon.Visible = false;
        _clickButton.Pressed += OnRefreshmentPressed;
        _clickButton.MouseEntered += OnRefreshmentMouseEntered;
        _clickButton.MouseExited += OnRefreshmentMouseExited;
        _useBalloon.GuiInput += OnUseBalloonGuiInput;
        _statusBalloon.GuiInput += OnStatusBalloonGuiInput;
        _statusBalloon.MouseEntered += OnStatusBalloonMouseEntered;
        _statusBalloon.MouseExited += OnStatusBalloonMouseExited;
        ClearRefreshment();
    }

    public void BindGameData(GameData gameData)
    {
        if (_gameData == gameData)
            return;

        if (_gameData != null)
            _gameData.RefreshmentStateChanged -= RefreshFromGameData;

        _gameData = gameData;
        if (_gameData != null)
            _gameData.RefreshmentStateChanged += RefreshFromGameData;

        RefreshFromGameData();
    }

    public override void _ExitTree()
    {
        if (_gameData == null)
            return;

        _gameData.RefreshmentStateChanged -= RefreshFromGameData;
    }

    public override void _Input(InputEvent @event)
    {
        if (!_useBalloon.IsVisibleInTree())
            return;

        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouseButton)
            return;

        if (ContainsViewportPoint(_useBalloon, mouseButton.Position)
            || ContainsViewportPoint(_clickButton, mouseButton.Position))
            return;

        HideUseBalloon();
    }

    public void SetRefreshment(Texture2D texture, string fileName, int configuredBalloonOffsetY)
    {
        StopRefreshmentFade();
        ResetHintAnimation();
        ResetUseFeedbackTransform();
        _refreshmentSprite.Texture = texture;
        _refreshmentSprite.Visible = true;
        var centerPosition = _localCache.GetValueOrDefault(fileName, _referenceRefreshmentPosition);
        var height = _heightCache.GetValueOrDefault(fileName, texture.GetHeight());
        _refreshmentAnchor.Position = centerPosition + new Vector2(0f, height * 0.5f);
        _refreshmentSprite.Position = new Vector2(0f, -height * 0.5f);
        _refreshmentRestPosition = _refreshmentAnchor.Position;
        var balloonOffsetY = _balloonVerticalOffsetCache.GetValueOrDefault(fileName, 0f)
            + configuredBalloonOffsetY;
        _useBalloon.Position = _useBalloonBasePosition + new Vector2(0f, balloonOffsetY);
        _statusBalloon.Position = _statusBalloonBasePosition + new Vector2(0f, balloonOffsetY);
        _clickButton.Visible = true;
        _clickButton.Disabled = false;
        _clickButton.MouseFilter = Control.MouseFilterEnum.Stop;
    }

    public void ClearRefreshment()
    {
        StopRefreshmentFade();
        ResetHintAnimation();
        ResetUseFeedbackTransform();
        _displayedRefreshmentItemId = 0;
        _refreshmentHovered = false;
        _statusBalloonHovered = false;
        _statusBalloonHideToken++;
        HideUseBalloon();
        HideStatusBalloon(animate: false);
        _refreshmentSprite.Visible = false;
        _clickButton.Visible = false;
        _clickButton.Disabled = true;
        _clickButton.MouseFilter = Control.MouseFilterEnum.Ignore;
    }

    public void PlayInteractionHint(InteractionHintTriggerKind triggerKind)
    {
        if (!CanPlayInteractionHint)
            return;

        if (_useBalloon.IsVisibleInTree())
        {
            PlayUseCheckInteractionHint(triggerKind);
            return;
        }

        PlayRefreshmentInteractionHint(playAudio: true);
    }

    private void PlayRefreshmentInteractionHint(bool playAudio)
    {
        ResetHintAnimation();
        if (playAudio)
            PlayInteractionHintSfx();
        _hintTween = CreateTween();
        _hintTween.TweenProperty(_refreshmentAnchor, "position", _refreshmentRestPosition + new Vector2(0f, -12f), 0.12)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        _hintTween.Parallel().TweenProperty(_refreshmentAnchor, "rotation", -0.08f, 0.12)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        _hintTween.Chain().TweenProperty(_refreshmentAnchor, "rotation", 0.075f, 0.10)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.InOut);
        _hintTween.TweenProperty(_refreshmentAnchor, "rotation", -0.045f, 0.08)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.InOut);
        _hintTween.Chain().TweenProperty(_refreshmentAnchor, "position", _refreshmentRestPosition, 0.16)
            .SetTrans(Tween.TransitionType.Bounce)
            .SetEase(Tween.EaseType.Out);
        _hintTween.Parallel().TweenProperty(_refreshmentAnchor, "rotation", 0f, 0.16)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        _hintTween.Chain().TweenCallback(Callable.From(ResetHintAnimation));
    }

    private void OnRefreshmentPressed()
    {
        if (_gameData == null || !_refreshmentSprite.Visible)
            return;

        if (_useBalloon.IsVisibleInTree())
        {
            if (_useFromRefreshmentArmed)
                ConfirmUseRefreshment();
            else
                EmitSignal(SignalName.InteractionIgnored);
            return;
        }

        if (_gameData.RefreshmentState.Status == TableRefreshmentStatus.BuffActive)
            return;

        if (_gameData.RefreshmentState.Status == TableRefreshmentStatus.ReadyToUse)
        {
            EmitSignal(SignalName.InteractionActivated);
            ShowUseBalloon();
        }
    }

    private void OnRefreshmentMouseEntered()
    {
        _refreshmentHovered = true;
        _statusBalloonHideToken++;
        if (_gameData?.RefreshmentState.Status == TableRefreshmentStatus.BuffActive)
            ShowStatusBalloon(animate: true);
    }

    private void OnRefreshmentMouseExited()
    {
        _refreshmentHovered = false;
        if (_gameData?.RefreshmentState.Status == TableRefreshmentStatus.BuffActive)
            ScheduleStatusBalloonHide();
    }

    private void OnUseBalloonGuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mouseButton)
            return;

        _useBalloon.AcceptEvent();
        if (mouseButton.Pressed || mouseButton.ButtonIndex != MouseButton.Left)
            return;

        ConfirmUseRefreshment();
    }

    private void OnStatusBalloonGuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mouseButton)
            return;

        _statusBalloon.AcceptEvent();
        if (mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.Left)
            EmitSignal(SignalName.InteractionActivated);
    }

    private void OnStatusBalloonMouseEntered()
    {
        _statusBalloonHovered = true;
        _statusBalloonHideToken++;
    }

    private void OnStatusBalloonMouseExited()
    {
        _statusBalloonHovered = false;
        ScheduleStatusBalloonHide();
    }

    private void ScheduleStatusBalloonHide()
    {
        var token = ++_statusBalloonHideToken;
        GetTree().CreateTimer(StatusBalloonHideDelaySeconds).Timeout += () =>
        {
            if (!IsInsideTree()
                || token != _statusBalloonHideToken
                || _refreshmentHovered
                || _statusBalloonHovered)
                return;

            HideStatusBalloon(animate: true);
        };
    }

    private void RefreshFromGameData()
    {
        if (!IsNodeReady() || _gameData == null)
            return;

        var state = _gameData.RefreshmentState;
        var shouldFadeOut = _lastRefreshmentStatus == TableRefreshmentStatus.BuffActive
            && state.Status == TableRefreshmentStatus.Empty
            && _refreshmentSprite.Visible
            && _displayedRefreshmentItemId > 0;
        _lastRefreshmentStatus = state.Status;
        if (shouldFadeOut)
        {
            PlayRefreshmentFadeOut();
            return;
        }

        var itemId = state.Status == TableRefreshmentStatus.BuffActive
            ? state.BuffSourceItemId
            : state.CurrentItemId;
        var item = itemId > 0 ? LubanData.Tables.TbItem.GetOrDefault(itemId) : null;
        var config = itemId > 0
            ? LubanData.Tables.TbRefreshmentConfig.GetOrDefault(itemId)
            : null;
        if (item == null
            || config == null
            || item.ItemType != EItemType.Refreshment
            || item.AssetPathList.Count == 0)
        {
            ClearRefreshment();
            return;
        }

        var texture = GD.Load<Texture2D>(PlayerInventory.ToResPath(item.AssetPathList[0]));
        if (texture == null)
        {
            ClearRefreshment();
            return;
        }

        var fileName = item.AssetPathList[0].Replace('\\', '/').Split('/')[^1];
        _displayedRefreshmentItemId = itemId;
        SetRefreshment(texture, fileName, config.BalloonOffsetY);
        RefreshStatusBalloonText();
        if (state.Status != TableRefreshmentStatus.BuffActive)
            HideStatusBalloon(animate: false);
    }

    private void ShowUseBalloon()
    {
        var wasVisible = _useBalloon.IsVisibleInTree();
        HideStatusBalloon(animate: false);
        StopUseBalloonAnimations();
        _useFromRefreshmentArmed = false;
        _useBalloon.Visible = true;
        _useBalloon.Modulate = Colors.White with { A = 0f };
        _useBalloon.Scale = _useBalloonBaseScale * 0.96f;
        _useBalloonTween = CreateTween();
        _useBalloonTween.SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _useBalloonTween.TweenProperty(_useBalloon, "scale", _useBalloonBaseScale, 0.14);
        _useBalloonTween.Parallel().TweenProperty(_useBalloon, "modulate:a", 1f, 0.10);
        _useBalloonTween.TweenCallback(Callable.From(StartUseCheckAnimation));
        if (!wasVisible)
            EmitSignal(SignalName.HintContextChanged);
    }

    private void HideUseBalloon()
    {
        var wasVisible = _useBalloon.IsVisibleInTree();
        StopUseBalloonAnimations();
        _useFromRefreshmentArmed = false;
        _useBalloon.Visible = false;
        _useBalloon.Modulate = Colors.White;
        _useBalloon.Scale = _useBalloonBaseScale;
        if (wasVisible)
            EmitSignal(SignalName.HintContextChanged);
    }

    private void StartUseCheckAnimation()
    {
        if (!_useBalloon.Visible || (_useCheckHintTween?.IsRunning() ?? false))
            return;

        StopUseCheckAnimation();
        _useCheckIcon.PivotOffset = _useCheckIcon.Size * 0.5f;
        _useCheckTween = CreateTween();
        _useCheckTween.SetLoops(0);
        _useCheckTween.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.InOut);
        _useCheckTween.TweenInterval(0.30);
        _useCheckTween.TweenProperty(_useCheckIcon, "rotation", -0.16f, 0.08);
        _useCheckTween.TweenProperty(_useCheckIcon, "rotation", 0.16f, 0.11);
        _useCheckTween.TweenProperty(_useCheckIcon, "rotation", -0.11f, 0.09);
        _useCheckTween.TweenProperty(_useCheckIcon, "rotation", 0.08f, 0.08);
        _useCheckTween.TweenProperty(_useCheckIcon, "rotation", 0f, 0.08);
        _useCheckTween.TweenInterval(0.90);
    }

    private void StopUseCheckAnimation()
    {
        _useCheckTween?.Kill();
        _useCheckTween = null;
        ResetUseCheckTransform();
    }

    private void PlayUseCheckInteractionHint(InteractionHintTriggerKind triggerKind)
    {
        if (!_useBalloon.IsVisibleInTree())
            return;

        if (triggerKind == InteractionHintTriggerKind.ProactiveIdle)
            _useFromRefreshmentArmed = true;
        PlayRefreshmentInteractionHint(playAudio: false);
        StopUseCheckAnimation();
        _useCheckHintTween?.Kill();
        _useCheckIcon.PivotOffset = new Vector2(
            _useCheckIcon.Size.X * 0.5f,
            _useCheckIcon.Size.Y * 0.4f);
        PlayInteractionHintSfx();

        _useCheckHintTween = CreateTween();
        _useCheckHintTween.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.InOut);
        _useCheckHintTween.TweenProperty(_useCheckIcon, "rotation", -0.24f, 0.12);
        _useCheckHintTween.TweenProperty(_useCheckIcon, "rotation", 0.24f, 0.16);
        _useCheckHintTween.TweenProperty(_useCheckIcon, "rotation", -0.16f, 0.13);
        _useCheckHintTween.TweenProperty(_useCheckIcon, "rotation", 0.10f, 0.11);
        _useCheckHintTween.TweenProperty(_useCheckIcon, "rotation", 0f, 0.12);
        _useCheckHintTween.TweenCallback(Callable.From(FinishUseCheckInteractionHint));
    }

    private void ConfirmUseRefreshment()
    {
        if (!_useBalloon.IsVisibleInTree())
            return;

        EmitSignal(SignalName.InteractionActivated);
        var used = _gameData?.TryUseTableRefreshment() ?? false;
        HideUseBalloon();
        if (used)
            PlayUseFeedback();
    }

    private void PlayRefreshmentFadeOut()
    {
        StopRefreshmentFade();
        var fadeToken = _refreshmentFadeToken;
        var fadingItemId = _displayedRefreshmentItemId;

        _hintTween?.Kill();
        ResetUseFeedbackTransform();
        _clickButton.Disabled = true;
        _clickButton.MouseFilter = Control.MouseFilterEnum.Ignore;
        HideUseBalloon();
        HideStatusBalloon(animate: false);

        _refreshmentFadeTween = CreateTween();
        _refreshmentFadeTween.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _refreshmentFadeTween.TweenProperty(
            _refreshmentAnchor,
            "modulate",
            UseFeedbackBrightModulate,
            0.06);
        _refreshmentFadeTween.TweenInterval(0.05);
        _refreshmentFadeTween.TweenProperty(
            _refreshmentAnchor,
            "modulate",
            Colors.White,
            0.08);
        _refreshmentFadeTween.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        _refreshmentFadeTween.TweenProperty(
            _refreshmentAnchor,
            "modulate:a",
            0f,
            RefreshmentFadeOutDurationSeconds);
        _refreshmentFadeTween.TweenCallback(
            Callable.From(() => FinishRefreshmentFadeOut(fadeToken, fadingItemId)));
    }

    private void FinishRefreshmentFadeOut(int fadeToken, int fadingItemId)
    {
        if (fadeToken != _refreshmentFadeToken
            || _gameData == null
            || _gameData.RefreshmentState.Status != TableRefreshmentStatus.Empty
            || _displayedRefreshmentItemId != fadingItemId)
            return;

        ClearRefreshment();
    }

    private void StopRefreshmentFade()
    {
        _refreshmentFadeToken++;
        _refreshmentFadeTween?.Kill();
        _refreshmentFadeTween = null;
    }

    private void PlayUseFeedback()
    {
        _useFeedbackTween?.Kill();
        _hintTween?.Kill();
        _refreshmentAnchor.Position = _refreshmentRestPosition;
        _refreshmentAnchor.Scale = Vector2.One;
        _refreshmentAnchor.Modulate = Colors.White;

        _useFeedbackTween = CreateTween();
        _useFeedbackTween.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _useFeedbackTween.TweenProperty(_refreshmentAnchor, "scale", UseFeedbackOvershootScale, 0.12);
        _useFeedbackTween.Parallel().TweenProperty(_refreshmentAnchor, "modulate", UseFeedbackBrightModulate, 0.10);
        _useFeedbackTween.SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _useFeedbackTween.TweenProperty(_refreshmentAnchor, "scale", new Vector2(1.05f, 0.95f), 0.13);
        _useFeedbackTween.Parallel().TweenProperty(_refreshmentAnchor, "modulate", Colors.White, 0.13);
        _useFeedbackTween.TweenProperty(_refreshmentAnchor, "scale", new Vector2(0.97f, 1.04f), 0.10);
        _useFeedbackTween.TweenProperty(_refreshmentAnchor, "scale", new Vector2(1.02f, 0.98f), 0.09);
        _useFeedbackTween.TweenProperty(_refreshmentAnchor, "scale", Vector2.One, 0.11);
        _useFeedbackTween.TweenCallback(Callable.From(ResetUseFeedbackTransform));
    }

    private void ResetUseFeedbackTransform()
    {
        if (_refreshmentAnchor == null)
            return;

        _useFeedbackTween?.Kill();
        _useFeedbackTween = null;
        _refreshmentAnchor.Position = _refreshmentRestPosition;
        _refreshmentAnchor.Scale = Vector2.One;
        _refreshmentAnchor.Modulate = Colors.White;
    }

    private void FinishUseCheckInteractionHint()
    {
        _useCheckHintTween = null;
        ResetUseCheckTransform();
        StartUseCheckAnimation();
    }

    private void StopUseCheckInteractionHint()
    {
        _useCheckHintTween?.Kill();
        _useCheckHintTween = null;
        ResetUseCheckTransform();
    }

    private void ResetUseCheckTransform()
    {
        if (_useCheckIcon == null)
            return;

        _useCheckIcon.Rotation = 0f;
        _useCheckIcon.PivotOffset = _useCheckIcon.Size * 0.5f;
    }

    private void StopUseBalloonAnimations()
    {
        _useBalloonTween?.Kill();
        _useBalloonTween = null;
        StopUseCheckInteractionHint();
        StopUseCheckAnimation();
    }

    private void ShowStatusBalloon(bool animate)
    {
        _statusBalloonHideToken++;
        RefreshStatusBalloonText();
        _statusBalloonTween?.Kill();
        _statusBalloon.Visible = true;
        if (!animate)
        {
            _statusBalloon.Modulate = Colors.White;
            _statusBalloon.Scale = _statusBalloonBaseScale;
            return;
        }

        _statusBalloon.Modulate = Colors.White with { A = 0f };
        _statusBalloon.Scale = _statusBalloonBaseScale * 0.96f;
        _statusBalloonTween = CreateTween();
        _statusBalloonTween.SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _statusBalloonTween.TweenProperty(_statusBalloon, "scale", _statusBalloonBaseScale, 0.14);
        _statusBalloonTween.Parallel().TweenProperty(_statusBalloon, "modulate:a", 1f, 0.10);
    }

    private void HideStatusBalloon(bool animate)
    {
        _statusBalloonTween?.Kill();
        if (!animate || !_statusBalloon.Visible)
        {
            _statusBalloon.Visible = false;
            return;
        }

        _statusBalloonTween = CreateTween();
        _statusBalloonTween.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _statusBalloonTween.TweenProperty(_statusBalloon, "scale", _statusBalloonBaseScale * 0.98f, 0.09);
        _statusBalloonTween.Parallel().TweenProperty(_statusBalloon, "modulate:a", 0f, 0.09);
        _statusBalloonTween.TweenCallback(Callable.From(() => _statusBalloon.Visible = false));
    }

    private void RefreshStatusBalloonText()
    {
        if (_gameData == null)
            return;

        var totalHands = Mathf.Max(1, _gameData.RefreshmentState.BuffTotalHands);
        _statusHandsLabel.Text = $"{Mathf.Max(0, _gameData.LuckyDealRemainingHands)}/{totalHands}";
    }

    private void PlayInteractionHintSfx()
    {
        if (_gameData == null)
            return;

        var state = _gameData.RefreshmentState;
        var itemId = state.Status == TableRefreshmentStatus.BuffActive
            ? state.BuffSourceItemId
            : state.CurrentItemId;
        var cue = LubanData.Tables.TbRefreshmentConfig.GetOrDefault(itemId)?.InteractionHintSfxCue;
        if (!string.IsNullOrWhiteSpace(cue))
            AudioManager.Instance.PlaySfx(cue);
    }

    private void ResetHintAnimation()
    {
        _hintTween?.Kill();
        if (_refreshmentAnchor == null)
            return;

        _refreshmentAnchor.Position = _refreshmentRestPosition;
        _refreshmentAnchor.Rotation = 0f;
    }

    /// <summary>
    /// 以 ItemArea.tscn 中预览 Whisky 的手调位置作为基础偏移。
    /// Sprite2D.Position 控制的是图片中心点，所以每种 Refreshment 再根据
    /// PSD 中图片中心点相对 Whisky 图片中心点的差值进行偏移。
    /// </summary>
    private void BuildPositionCache()
    {
        if (_cacheBuilt) return;
        _cacheBuilt = true;

        using var file = FileAccess.Open("res://Assets/v1/layer_index.json", FileAccess.ModeFlags.Read);
        if (file == null) return;

        var json = new Json();
        if (json.Parse(file.GetAsText()) != Error.Ok) return;

        var layers = json.Data.AsGodotDictionary()["layers"].AsGodotArray();
        Vector2 referenceCenter = Vector2.Zero;
        float referenceTop = 0f;
        bool foundReference = false;

        foreach (var layer in layers)
        {
            var d = layer.AsGodotDictionary();
            var filePath = d["file"].AsString().Replace('\\', '/');
            if (!filePath.StartsWith("Treat/")) continue;

            var fileName = filePath.Split('/')[^1];
            if (fileName != ReferenceRefreshmentFileName) continue;

            referenceCenter = ReadCenter(d);
            referenceTop = ReadTop(d);
            foundReference = true;
            break;
        }

        if (!foundReference) return;

        foreach (var layer in layers)
        {
            var d = layer.AsGodotDictionary();
            var filePath = d["file"].AsString().Replace('\\', '/');
            if (!filePath.StartsWith("Treat/")) continue;

            var fileOnly = filePath.Split('/')[^1];

            var centerDelta = ReadCenter(d) - referenceCenter;

            _localCache[fileOnly] = _referenceRefreshmentPosition + centerDelta;
            _balloonVerticalOffsetCache[fileOnly] = ReadTop(d) - referenceTop;
            _heightCache[fileOnly] = ReadDim(d, "h", "height");
        }
    }

    private static bool ContainsViewportPoint(Control control, Vector2 viewportPosition)
    {
        if (!control.IsVisibleInTree())
            return false;

        var localPosition = control.GetGlobalTransformWithCanvas().AffineInverse() * viewportPosition;
        return new Rect2(Vector2.Zero, control.Size).HasPoint(localPosition);
    }

    private static Vector2 ReadCenter(Godot.Collections.Dictionary d)
    {
        var x = (float)d["x"].AsDouble();
        var y = (float)d["y"].AsDouble();
        var w = ReadDim(d, "w", "width");
        var h = ReadDim(d, "h", "height");
        return new Vector2(x + w / 2f, y + h / 2f);
    }

    private static float ReadTop(Godot.Collections.Dictionary d)
    {
        return d.ContainsKey("doc_y")
            ? (float)d["doc_y"].AsDouble()
            : (float)d["y"].AsDouble();
    }

    private static float ReadDim(Godot.Collections.Dictionary d, string shortKey, string longKey)
    {
        return d.ContainsKey(shortKey)
            ? (float)d[shortKey].AsDouble()
            : (float)d[longKey].AsDouble();
    }
}
