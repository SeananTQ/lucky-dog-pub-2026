using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using DataTables;

namespace LuckyDogRise;

public enum GameState
{
    WaitingForBet,
    Dealt,
    Holding,
    Drawing,
    Settled,
    GameOver
}

public partial class GameManager : Node2D
{
    private static readonly PackedScene ChipRewardScene = GD.Load<PackedScene>("res://Scenes/ChipReward.tscn");
    private static readonly PackedScene ItemCellScene = GD.Load<PackedScene>("res://Scenes/Prefabs/ItemCell.tscn");

    [Signal] public delegate void BlindBoxRewardClaimRequestedEventHandler();

    public GameState State { get; private set; } = GameState.WaitingForBet;
    public bool HasDogGivenHint => _dogHint.HasGivenHint;
    public Node2D PendingReward => _pendingReward;
    private int _pendingPayout;
    private Node2D _pendingReward;

    private GameData _gameData = null!;
    public GameData GameData
    {
        get => _gameData;
        set
        {
            _gameData = value;
            if (_hud != null && _chipStack != null)
            {
                _dogVisual.GameData = _gameData;
                RefreshUI();
                ApplyEquippedVisuals();
                _gameData.EquipmentChanged += ApplyEquippedVisuals;
                _hud.SetMessage("");
                _chipStack.ShowHint("Click to bet");
            }
        }
    }

    private DeckManager _deck = null!;
    private DogHintSystem _dogHint = null!;
    private bool[] _held = [true, true, true, true, true];

    private HUDController _hud = null!;
    private CardTableController _cardTable = null!;
    private DogVisual _dogVisual = null!;
    private ChipStackController _chipStack = null!;
    private HandAreaController _handArea = null!;
    private ItemAreaController _itemArea = null!;
    private Marker2D _rewardSpawnPoint = null!;
    private CanvasLayer _blindBoxRewardLayer = null!;
    private ColorRect _blindBoxRewardBackground = null!;
    private ColorRect _blindBoxRewardWhiteMask = null!;
    private Label _blindBoxRewardDebugLabel = null!;
    private ItemCellController _blindBoxRewardCell = null!;
    private Vector2 _blindBoxRewardCellBasePosition;
    private CanvasLayer _blindBoxRevealLayer = null!;
    private ColorRect _blindBoxRevealBackground = null!;
    private ColorRect _blindBoxRevealWhiteMask = null!;
    private TextureRect _blindBoxSprite = null!;
    private TextureRect _blindBoxShadow = null!;
    private Label _blindBoxRevealHint = null!;
    private Vector2 _blindBoxSpriteBasePosition;
    private Vector2 _blindBoxShadowBasePosition;
    private PendingBlindBoxReward _activeBlindBoxReward = null!;
    private Tween _blindBoxRevealTween = null!;
    private Tween _blindBoxRewardTween = null!;
    private bool _blindBoxRevealAnimating;
    public SystemPanelController SettingsPanel { get; set; } = null!;

    public override void _Ready()
    {
        _deck = new DeckManager();
        _dogHint = new DogHintSystem();

        _hud = GetNode<HUDController>("HUD");
        _cardTable = GetNode<CardTableController>("CardArea");
        _dogVisual = GetNode<DogVisual>("DogArea");
        _chipStack = GetNode<ChipStackController>("ChipStack");
        _handArea = GetNode<HandAreaController>("HandArea");
        _itemArea = GetNode<ItemAreaController>("ItemArea");
        _rewardSpawnPoint = GetNode<Marker2D>("RewardSpawnPoint");
        _rewardSpawnPoint.GetNode<Sprite2D>("PreviewSprite").Visible = false;
        BuildBlindBoxRewardOverlay();
        BuildBlindBoxRevealOverlay();

        // 信号连接
        _dogVisual.DogClicked += OnDogClicked;
        _handArea.HandKnocked += OnDrawPressed;
        _chipStack.BetPlaced += OnBetPlaced;
        _cardTable.CardClicked += OnCardClicked;

        if (_gameData != null)
        {
            RefreshUI();
            _hud.SetMessage("");
            _chipStack.ShowHint("Click to bet");
        }

        AudioManager.Instance.PlayBgmByName("MainTheme.ogg");
    }

    public void ShowPendingBlindBoxReward(PendingBlindBoxReward pending)
    {
        _activeBlindBoxReward = pending;
        if (!pending.RewardShown)
        {
            ShowBlindBoxReveal(pending);
            return;
        }

        var item = LubanData.Tables.TbItem.GetOrDefault(pending.ItemId);
        if (item == null)
            return;

        _blindBoxRevealLayer.Visible = false;
        _blindBoxRewardCell.Setup(item, isEquipped: false, count: 1, isNew: false);
        _blindBoxRewardDebugLabel.Text = pending.DebugText;
        LayoutBlindBoxRewardElements();
        _blindBoxRewardBackground.Color = GetBlindBoxBackgroundColor(item.ItemRarity);
        _blindBoxRewardLayer.Visible = true;
        PlayBlindBoxRewardDrop(animate: false);
    }

    public void HidePendingBlindBoxReward()
    {
        if (_blindBoxRewardLayer != null)
            _blindBoxRewardLayer.Visible = false;
        if (_blindBoxRevealLayer != null)
            _blindBoxRevealLayer.Visible = false;
    }

    private void BuildBlindBoxRevealOverlay()
    {
        _blindBoxRevealLayer = new CanvasLayer
        {
            Name = "BlindBoxRevealOverlay",
            Layer = 19,
            Visible = false,
        };
        AddChild(_blindBoxRevealLayer);

        _blindBoxRevealBackground = new ColorRect
        {
            Color = Colors.Black,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _blindBoxRevealBackground.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _blindBoxRevealBackground.GuiInput += OnBlindBoxRevealGuiInput;
        _blindBoxRevealLayer.AddChild(_blindBoxRevealBackground);

        _blindBoxRevealWhiteMask = new ColorRect
        {
            Color = new Color(1f, 1f, 1f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _blindBoxRevealWhiteMask.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _blindBoxRevealLayer.AddChild(_blindBoxRevealWhiteMask);

        _blindBoxShadow = new TextureRect
        {
            CustomMinimumSize = new Vector2(220, 80),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            PivotOffset = new Vector2(110, 40),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _blindBoxShadow.Size = new Vector2(220, 80);
        _blindBoxRevealLayer.AddChild(_blindBoxShadow);

        _blindBoxSprite = new TextureRect
        {
            CustomMinimumSize = new Vector2(220, 220),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            PivotOffset = new Vector2(110, 110),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _blindBoxSprite.Size = new Vector2(220, 220);
        _blindBoxRevealLayer.AddChild(_blindBoxSprite);

        _blindBoxRevealHint = new Label
        {
            Text = "点击继续",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _blindBoxRevealHint.AddThemeFontSizeOverride("font_size", 20);
        _blindBoxRevealHint.Size = new Vector2(280, 40);
        _blindBoxRevealLayer.AddChild(_blindBoxRevealHint);
        _blindBoxRevealLayer.MoveChild(_blindBoxRevealWhiteMask, _blindBoxRevealLayer.GetChildCount() - 1);
    }

    private void ShowBlindBoxReveal(PendingBlindBoxReward pending)
    {
        var path = LubanData.Tables.TbBlindBoxRevealPath.GetOrDefault(pending.RevealPathId);
        if (path == null)
        {
            _gameData.MarkPendingBlindBoxRewardShown();
            ShowPendingBlindBoxReward(pending);
            return;
        }

        _blindBoxRewardLayer.Visible = false;
        _blindBoxRevealLayer.Visible = true;
        _blindBoxRevealWhiteMask.Color = new Color(1f, 1f, 1f, 0f);
        LayoutBlindBoxRevealElements();
        ApplyBlindBoxVisual(GetRevealRarity(path, pending.RevealStep), instant: true);
        if (pending.RevealStep >= 4)
            PlayBlindBoxPreRewardShake();
        else
            PlayBlindBoxAppear();
    }

    private void LayoutBlindBoxRevealElements()
    {
        var viewportSize = GetViewportRect().Size;
        var center = viewportSize * 0.5f;

        _blindBoxSpriteBasePosition = center + new Vector2(-110, -110);
        _blindBoxShadowBasePosition = center + new Vector2(-110, 88);

        _blindBoxSprite.Position = _blindBoxSpriteBasePosition;
        _blindBoxShadow.Position = _blindBoxShadowBasePosition;
        _blindBoxRevealHint.Position = center + new Vector2(-140, 175);
    }

    private void OnBlindBoxRevealGuiInput(InputEvent @event)
    {
        if (_activeBlindBoxReward == null || _blindBoxRevealAnimating)
            return;

        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            return;

        if (_activeBlindBoxReward.RevealStep >= 4)
        {
            PlayBlindBoxFinalOpen();
            return;
        }

        AdvanceBlindBoxReveal();
    }

    private void AdvanceBlindBoxReveal()
    {
        var path = LubanData.Tables.TbBlindBoxRevealPath.GetOrDefault(_activeBlindBoxReward.RevealPathId);
        if (path == null)
        {
            ShowBlindBoxFinalReward();
            return;
        }

        var nextStep = _activeBlindBoxReward.RevealStep + 1;
        if (nextStep > 3)
        {
            _activeBlindBoxReward.RevealStep = 4;
            _gameData.SetPendingBlindBoxRevealStep(4);
            PlayBlindBoxPreRewardShake();
            return;
        }

        var oldRarity = GetRevealRarity(path, _activeBlindBoxReward.RevealStep);
        var newRarity = GetRevealRarity(path, nextStep);
        _activeBlindBoxReward.RevealStep = nextStep;
        _gameData.SetPendingBlindBoxRevealStep(nextStep);
        PlayBlindBoxUpgrade(oldRarity, newRarity);
    }

    private void ShowBlindBoxFinalReward()
    {
        _gameData.MarkPendingBlindBoxRewardShown();
        _activeBlindBoxReward.RewardShown = true;
        _blindBoxRevealLayer.Visible = false;
        ShowPendingBlindBoxReward(_activeBlindBoxReward);
    }

    private void PlayBlindBoxPreRewardShake()
    {
        _blindBoxRevealTween?.Kill();
        _blindBoxRevealAnimating = false;
        _blindBoxRevealHint.Text = "点击开奖";
        _blindBoxRevealHint.Modulate = Colors.White;

        _blindBoxRevealTween = CreateTween();
        _blindBoxRevealTween.SetLoops(0);
        _blindBoxSprite.Position = _blindBoxSpriteBasePosition;
        _blindBoxRevealTween.TweenProperty(_blindBoxSprite, "scale", new Vector2(1.08f, 0.92f), 0.055);
        _blindBoxRevealTween.TweenProperty(_blindBoxSprite, "scale", new Vector2(0.94f, 1.11f), 0.045);
        _blindBoxRevealTween.TweenProperty(_blindBoxSprite, "scale", new Vector2(1.13f, 0.96f), 0.05);
        _blindBoxRevealTween.TweenProperty(_blindBoxSprite, "scale", new Vector2(0.97f, 1.07f), 0.04);
        _blindBoxRevealTween.TweenProperty(_blindBoxSprite, "scale", Vector2.One, 0.05);
    }

    private void PlayBlindBoxFinalOpen()
    {
        _blindBoxRevealTween?.Kill();
        _blindBoxRevealAnimating = true;
        _blindBoxRevealHint.Modulate = Colors.Transparent;

        _blindBoxRevealTween = CreateTween();
        _blindBoxRevealTween.SetParallel(true);
        _blindBoxRevealTween.TweenProperty(_blindBoxSprite, "position", _blindBoxSpriteBasePosition + new Vector2(0, -150), 0.22)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        _blindBoxRevealTween.TweenProperty(_blindBoxSprite, "scale", new Vector2(1.18f, 1.18f), 0.22)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        _blindBoxRevealTween.TweenProperty(_blindBoxShadow, "scale", new Vector2(0.45f, 0.45f), 0.22);
        _blindBoxRevealTween.TweenProperty(_blindBoxRevealWhiteMask, "color", Colors.White, 0.22);
        _blindBoxRevealTween.SetParallel(false);
        _blindBoxRevealTween.TweenCallback(Callable.From(() =>
        {
            _gameData.MarkPendingBlindBoxRewardShown();
            _activeBlindBoxReward.RewardShown = true;
            _blindBoxRevealLayer.Visible = false;
            ShowPendingBlindBoxReward(_activeBlindBoxReward);
            PlayBlindBoxRewardDrop(animate: true);
            _blindBoxRevealAnimating = false;
        }));
    }

    private void PlayBlindBoxAppear()
    {
        _blindBoxRevealTween?.Kill();
        _blindBoxRevealAnimating = true;
        _blindBoxRevealHint.Text = "点击继续";
        _blindBoxSprite.Scale = new Vector2(0.6f, 0.6f);
        _blindBoxSprite.Position = _blindBoxSpriteBasePosition + new Vector2(0, -90);
        _blindBoxShadow.Scale = new Vector2(0.55f, 0.55f);
        _blindBoxRevealHint.Modulate = Colors.Transparent;

        _blindBoxRevealTween = CreateTween();
        _blindBoxRevealTween.SetParallel(true);
        _blindBoxRevealTween.TweenProperty(_blindBoxSprite, "scale", new Vector2(1.1f, 1.1f), 0.18)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        _blindBoxRevealTween.TweenProperty(_blindBoxSprite, "position", _blindBoxSpriteBasePosition, 0.34)
            .SetTrans(Tween.TransitionType.Bounce)
            .SetEase(Tween.EaseType.Out);
        _blindBoxRevealTween.TweenProperty(_blindBoxShadow, "scale", Vector2.One, 0.34)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
        _blindBoxRevealTween.TweenProperty(_blindBoxRevealHint, "modulate", Colors.White, 0.15)
            .SetDelay(0.28);
        _blindBoxRevealTween.SetParallel(false);
        _blindBoxRevealTween.TweenProperty(_blindBoxSprite, "scale", Vector2.One, 0.12)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
        _blindBoxRevealTween.TweenCallback(Callable.From(() => _blindBoxRevealAnimating = false));
    }

    private void PlayBlindBoxUpgrade(ERarity oldRarity, ERarity newRarity)
    {
        _blindBoxRevealTween?.Kill();
        _blindBoxRevealAnimating = true;
        _blindBoxRevealHint.Modulate = Colors.Transparent;

        var changed = oldRarity != newRarity;
        var targetColor = GetBlindBoxBackgroundColor(newRarity);
        _blindBoxRevealTween = CreateTween();
        _blindBoxRevealTween.TweenProperty(_blindBoxSprite, "scale", new Vector2(0.78f, 0.72f), 0.08)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.In);
        _blindBoxRevealTween.TweenProperty(_blindBoxSprite, "position", _blindBoxSpriteBasePosition + new Vector2(0, -100), 0.18)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        _blindBoxRevealTween.Parallel().TweenProperty(_blindBoxSprite, "scale", new Vector2(1.1f, 1.1f), 0.18)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        _blindBoxRevealTween.Parallel().TweenProperty(_blindBoxShadow, "scale", new Vector2(0.5f, 0.5f), 0.18);
        if (changed)
            _blindBoxRevealTween.Parallel().TweenProperty(_blindBoxRevealBackground, "color", targetColor, 0.18);
        _blindBoxRevealTween.TweenCallback(Callable.From(() => ApplyBlindBoxVisual(newRarity, instant: false)));
        _blindBoxRevealTween.TweenProperty(_blindBoxSprite, "scale", new Vector2(0.62f, 0.62f), 0.12)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.In);
        _blindBoxRevealTween.TweenProperty(_blindBoxSprite, "position", _blindBoxSpriteBasePosition, 0.22)
            .SetTrans(Tween.TransitionType.Bounce)
            .SetEase(Tween.EaseType.Out);
        _blindBoxRevealTween.Parallel().TweenProperty(_blindBoxSprite, "scale", Vector2.One, 0.22)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        _blindBoxRevealTween.Parallel().TweenProperty(_blindBoxShadow, "scale", Vector2.One, 0.22);
        _blindBoxRevealTween.TweenProperty(_blindBoxRevealHint, "modulate", Colors.White, 0.12);
        _blindBoxRevealTween.TweenCallback(Callable.From(() =>
        {
            if (_activeBlindBoxReward.RevealStep >= 3)
            {
                _activeBlindBoxReward.RevealStep = 4;
                _gameData.SetPendingBlindBoxRevealStep(4);
                PlayBlindBoxPreRewardShake();
                return;
            }

            _blindBoxRevealAnimating = false;
        }));

        if (changed)
        {
            var visual = GetBlindBoxVisual(newRarity);
            if (visual != null && !string.IsNullOrWhiteSpace(visual.UpgradeSfxNamePath))
                GD.Print($"[BlindBox] Upgrade SFX: {visual.UpgradeSfxNamePath}");
        }
    }

    private void ApplyBlindBoxVisual(ERarity rarity, bool instant)
    {
        var visual = GetBlindBoxVisual(rarity);
        if (visual == null)
            return;

        _blindBoxSprite.Texture = LoadBlindBoxTexture(visual.BlindBoxSpritePath);
        _blindBoxShadow.Texture = LoadBlindBoxTexture(visual.BlindBoxShadowPath);
        if (instant)
            _blindBoxRevealBackground.Color = GetBlindBoxBackgroundColor(rarity);
    }

    private static ERarity GetRevealRarity(BlindBoxRevealPath path, int step)
    {
        return step switch
        {
            <= 0 => path.StartRarity,
            1 => path.MiddleRarity1,
            2 => path.MiddleRarity2,
            _ => path.FinalRarity,
        };
    }

    private static BlindBoxVisual GetBlindBoxVisual(ERarity rarity)
    {
        return LubanData.Tables.TbBlindBoxVisual.DataList.FirstOrDefault(visual => visual.ItemRarity == rarity);
    }

    private static Texture2D LoadBlindBoxTexture(string lubanPath)
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

    private void BuildBlindBoxRewardOverlay()
    {
        _blindBoxRewardLayer = new CanvasLayer
        {
            Name = "BlindBoxRewardOverlay",
            Layer = 20,
            Visible = false,
        };
        AddChild(_blindBoxRewardLayer);

        _blindBoxRewardBackground = new ColorRect
        {
            Color = Colors.Black,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _blindBoxRewardBackground.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _blindBoxRewardLayer.AddChild(_blindBoxRewardBackground);

        _blindBoxRewardWhiteMask = new ColorRect
        {
            Color = new Color(1f, 1f, 1f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _blindBoxRewardWhiteMask.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _blindBoxRewardLayer.AddChild(_blindBoxRewardWhiteMask);

        _blindBoxRewardCell = ItemCellScene.Instantiate<ItemCellController>();
        _blindBoxRewardCell.CustomMinimumSize = new Vector2(256, 256);
        _blindBoxRewardCell.Size = new Vector2(256, 256);
        _blindBoxRewardCell.Pressed += () => EmitSignal(SignalName.BlindBoxRewardClaimRequested);
        _blindBoxRewardLayer.AddChild(_blindBoxRewardCell);

        _blindBoxRewardDebugLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(260, 160),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _blindBoxRewardDebugLabel.AddThemeFontSizeOverride("font_size", 12);
        _blindBoxRewardLayer.AddChild(_blindBoxRewardDebugLabel);
        _blindBoxRewardLayer.MoveChild(_blindBoxRewardWhiteMask, _blindBoxRewardLayer.GetChildCount() - 1);
    }

    private void LayoutBlindBoxRewardElements()
    {
        var viewportSize = GetViewportRect().Size;
        var center = viewportSize * 0.5f;
        _blindBoxRewardCellBasePosition = center + new Vector2(-128, -128);
        _blindBoxRewardCell.Position = _blindBoxRewardCellBasePosition;
        _blindBoxRewardDebugLabel.Position = new Vector2(Mathf.Max(16, viewportSize.X - 280), 16);
        _blindBoxRewardDebugLabel.Size = new Vector2(260, 160);
    }

    private void PlayBlindBoxRewardDrop(bool animate)
    {
        _blindBoxRewardTween?.Kill();

        if (!animate)
        {
            _blindBoxRewardWhiteMask.Color = new Color(1f, 1f, 1f, 0f);
            _blindBoxRewardCell.Position = _blindBoxRewardCellBasePosition;
            _blindBoxRewardCell.Scale = Vector2.One;
            return;
        }

        _blindBoxRewardWhiteMask.Color = Colors.White;
        _blindBoxRewardCell.Position = _blindBoxRewardCellBasePosition + new Vector2(0, -230);
        _blindBoxRewardCell.Scale = new Vector2(0.85f, 0.85f);

        _blindBoxRewardTween = CreateTween();
        _blindBoxRewardTween.SetParallel(true);
        _blindBoxRewardTween.TweenProperty(_blindBoxRewardWhiteMask, "color", new Color(1f, 1f, 1f, 0f), 0.16);
        _blindBoxRewardTween.TweenProperty(_blindBoxRewardCell, "position", _blindBoxRewardCellBasePosition, 0.38)
            .SetTrans(Tween.TransitionType.Bounce)
            .SetEase(Tween.EaseType.Out);
        _blindBoxRewardTween.TweenProperty(_blindBoxRewardCell, "scale", Vector2.One, 0.22)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
    }

    // === 信号处理 ===

    private void OnBetPlaced()
    {
        if (State != GameState.WaitingForBet) return;
        if (!_gameData.CanAffordBet) { TriggerGameOver(); return; }
        if (SettingsPanel != null && SettingsPanel.TryGetFixedSeed(out int fixedSeed))
            _deck.SetFixedSeed(fixedSeed);
        DealNewHand();
    }

    private void OnDrawPressed()
    {
        if (State == GameState.Dealt || State == GameState.Holding)
            CallDeferred(nameof(DoDraw));
    }

    private void OnCardClicked(int index)
    {
        if (State != GameState.Dealt && State != GameState.Holding) return;

        _held[index] = !_held[index];
        _cardTable.SetHeld(index, _held[index]);
        State = GameState.Holding;
        _hud.SetMessage(_held[index] ? $"Card {index + 1} HELD" : $"Card {index + 1} discarded");

        if (_dogHint.HasGivenHint && !_dogHint.IsLocked)
        {
            _dogHint.IsLocked = true;
            _dogVisual.ApplyReaction(EDogReactionTrigger.HintExhausted);
            _hud.SetMessage("");
        }
        else if (!_dogHint.HasGivenHint)
        {
            _dogVisual.ApplyReaction(EDogReactionTrigger.WaitingForHint);
        }
    }

    private void OnDogClicked()
    {
        if (State != GameState.Dealt && State != GameState.Holding) return;

        if (_dogHint.HasGivenHint)
        {
            _dogVisual.ApplyReaction(EDogReactionTrigger.RefuseHint);
            _hud.SetMessage("");
            return;
        }

        var previewHand = _deck.PreviewFinalHand(_held);
        var previewRank = _dogHint.EvaluateHoldRank(_deck.CurrentHand, _held, previewHand);
        _dogVisual.ApplyReaction(GetSawReaction(previewRank));
        _dogHint.HasGivenHint = true;
        _hud.SetMessage("");
    }

    private void OnChipCollected()
    {
        _pendingReward = null;
        _gameData.ModifyChips(_pendingPayout);
        _pendingPayout = 0;
        RefreshUI();
        State = GameState.WaitingForBet;
        OnBetPlaced();
    }

    // === 游戏逻辑 ===

    public void AddChips(int amount)
    {
        _gameData.ModifyChips(amount);
        RefreshUI();
    }

    private void DealNewHand()
    {
        _gameData.ModifyChips(-_gameData.BetAmount);
        _gameData.EmitNewHandStarted();
        _deck.Deal();
        _held = [true, true, true, true, true];
        _dogHint.ResetForNewHand();
        _chipStack.HideHint();
        _cardTable.DealCards(_deck.CurrentHand);
        State = GameState.Dealt;
        _dogVisual.ApplyReaction(EDogReactionTrigger.Dealt);
        _handArea.Enabled = false;  // 发牌动画期间禁止敲桌
        GetTree().CreateTimer(1.1f).Timeout += () => _handArea.Enabled = true;
        _hud.SetMessage("");
        RefreshUI();
    }

    private void DoDraw()
    {
        State = GameState.Drawing;
        _handArea.Enabled = false;
        RefreshUI();
        _cardTable.BrightenAll();
        var finalHand = _deck.DrawReplacements(_held);
        _cardTable.ReplaceCards(finalHand, _held);
        var rank = CardEvaluator.Evaluate(finalHand);
        int payout = CardEvaluator.GetPayout(finalHand, _gameData.BetAmount);
        _gameData.EmitHandResolved(rank, payout);
        _dogVisual.ApplyReaction(GetSawReaction(rank));
        if (payout > 0)
        {
            _pendingPayout = payout;
            _hud.SetMessage("");
            SpawnChipReward(payout);
            State = GameState.Settled;
        }
        else
        {
            _hud.SetMessage("");
            State = GameState.WaitingForBet;
            _chipStack.ShowHint("Click to bet");
            if (!_gameData.CanAffordBet) { TriggerGameOver(); return; }
        }
        if (_gameData.Progression.CheckRankUp())
            _hud.ShowOverlay($"Rank Up: {_gameData.Progression.CurrentRank}!");
        RefreshUI();
    }

    private void TriggerGameOver()
    {
        State = GameState.GameOver;
        _hud.ShowOverlay(DogProverbs.GetRandom());
        _hud.SetMessage("");
        _chipStack.HideHint();
        RefreshUI();
    }

    private void SpawnChipReward(int amount)
    {
        var reward = ChipRewardScene.Instantiate<ChipRewardController>();
        _rewardSpawnPoint.AddChild(reward);
        reward.Position = Vector2.Zero;
        reward.Setup(amount);
        reward.Collected += OnChipCollected;
        _pendingReward = reward;
    }

    private void RefreshUI()
    {
        SettingsPanel?.UpdateSeed(_deck.LastSeed);
    }

    private static EDogReactionTrigger GetSawReaction(EHandRank rank)
    {
        if (rank == EHandRank.Nothing)
            return EDogReactionTrigger.SawNothing;

        var payTable = LubanData.Tables.TbPayTable.DataList.FirstOrDefault(row => row.HandRank == rank);
        if (payTable == null)
            return EDogReactionTrigger.SawNothing;

        return (EDogReactionTrigger)(3000 + (int)payTable.HandRank);
    }

    private void ApplyEquippedVisuals()
    {
        _dogVisual.RefreshEquippedVisuals();
        ApplyItemTexture(EItemType.Background, (tex, _) => GetNode<TextureRect>("Background").Texture = tex);
        ApplyItemTexture(EItemType.Table, (tex, _) => GetNode<TextureRect>("Table").Texture = tex);
        ApplyItemTexture(EItemType.Arm, (tex, name) => _handArea.SetArm(tex, name));
        ApplyItemTexture(EItemType.Clothes, (tex, name) => _handArea.SetClothes(tex, name), () => _handArea.SetClothes(null, ""));
        ApplyItemTexture(EItemType.Accessory, (tex, name) => _handArea.SetAccessory(tex, name), () => _handArea.SetAccessory(null, ""));
        ApplyItemTexture(EItemType.Refreshment, (tex, name) => _itemArea.SetTreat(tex, name), _itemArea.ClearTreat);
        _dogVisual.RefreshEquippedHeadwear();
        _dogVisual.RefreshEquippedEyewear();
    }

    private void ApplyItemTexture(EItemType type, System.Action<Texture2D, string> apply, Action clear = null)
    {
        var item = _gameData.Inventory.GetEquipped(type);
        if (item == null || item.AssetPathList.Count == 0)
        {
            clear?.Invoke();
            return;
        }
        var tex = GD.Load<Texture2D>(PlayerInventory.ToResPath(item.AssetPathList[0]));
        if (tex == null)
        {
            clear?.Invoke();
            return;
        }
        var fileName = item.AssetPathList[0].Split('\\').Last();
        apply(tex, fileName);
    }

    public void OnPlayDogReaction(int trigger)
    {
        _dogVisual.ApplyReaction((EDogReactionTrigger)trigger);
        _hud.SetMessage("");
    }
}
