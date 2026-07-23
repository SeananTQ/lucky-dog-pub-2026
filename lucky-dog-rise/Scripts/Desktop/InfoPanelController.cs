using Godot;
using DataTables;
using System;
using System.Collections.Generic;

namespace LuckyDogRise;

public partial class InfoPanelController : CanvasLayer
{
    [Signal] public delegate void SettingsRequestedEventHandler();
    [Signal] public delegate void BlindBoxRequestedEventHandler();

    [Export] private PanelContainer _panel = null!;
    [Export] private Label _chipsKeyLabel = null!;
    [Export] private Label _chipsLabel = null!;
    [Export] private Label _rankNameLabel = null!;
    [Export] private Label _winResultLabel = null!;
    [Export] private GridContainer _payoutGrid = null!;
    [Export] private Button _settingsBtn = null!;
    [Export] private Button _blindBoxBtn = null!;
    [Export] private BalloonHintController _blindBoxHint = null!;

    // ===== 动画参数 =====
    private static readonly Vector2 PanelSize = new(246, 600);
    private const float ChipsAnimDuration = 0.4f;
    private const float BlinkVisibleDuration = 0.8f;
    private const float BlinkHiddenDuration = 0.4f;
    private const int DefaultPayoutNameFontSize = 13;
    private const int JapanesePayoutNameFontSize = 12;

    private GameData _gameData = null!;
    private readonly List<Label> _payoutNames = new();
    private readonly List<Label> _payoutValues = new();
    private Label _lastHighlightedName = null!;
    private Label _lastHighlightedValue = null!;
    private bool _hasHighlight;
    private Color _defaultNameColor;
    private Color _defaultValueColor;
    private Color _normalChipsTextColor;
    private LabelSettings _chipsLabelSettings;

    private int _displayedChips;
    private Tween _chipsTween;
    private Tween _insufficientBetFlashTween;
    private Tween _blinkTween;
    private Texture2D _blindBoxIcon = null!;
    private EHandRank _currentRank = EHandRank.Nothing;
    private int _currentPayout;
    private bool _hasResolvedHand;

    // 赔率表数据来自 Luban PayTable（JSON → C# 数据驱动）

    private static readonly EHandRank[] GridOrder =
    {
        EHandRank.RoyalFlush,
        EHandRank.StraightFlush,
        EHandRank.FourOfAKind,
        EHandRank.FullHouse,
        EHandRank.Flush,
        EHandRank.Straight,
        EHandRank.ThreeOfAKind,
        EHandRank.TwoPair,
        EHandRank.OnePair,
    };

    public override void _Ready()
    {
        LockPanelSize();
        // 复制专用设置，避免运行时闪烁污染场景资源；阴影参数会原样保留。
        _chipsLabelSettings = _chipsLabel.LabelSettings?.Duplicate() as LabelSettings;
        if (_chipsLabelSettings != null)
        {
            _chipsLabel.LabelSettings = _chipsLabelSettings;
            _normalChipsTextColor = _chipsLabelSettings.FontColor;
        }
        else
        {
            _normalChipsTextColor = _chipsLabel.GetThemeColor("font_color");
        }

        _settingsBtn.Pressed += () => EmitSignal(SignalName.SettingsRequested);
        _blindBoxBtn.Pressed += () => EmitSignal(SignalName.BlindBoxRequested);
        _blindBoxHint.Pressed += OnBlindBoxHintPressed;
        _blindBoxIcon = GD.Load<Texture2D>("res://Assets/UI/BlindBox/BlindBox_Common_Closed.png");

        foreach (var child in _payoutGrid.GetChildren())
        {
            if (child is Label label)
            {
                if (_payoutNames.Count <= _payoutValues.Count)
                    _payoutNames.Add(label);
                else
                    _payoutValues.Add(label);
            }
        }

        // 从 Luban PayTable 数据表填充赔率名称和数值
        var payList = LubanData.Tables.TbPayTable.DataList;
        for (int i = 0; i < payList.Count; i++)
        {
            int gridIdx = payList.Count - 1 - i; // JSON 是低→高，Grid 是高→低
            if (gridIdx < _payoutNames.Count)
                _payoutNames[gridIdx].Text = L10n.Tr(L10n.GetHandRankKey(payList[i].HandRank));
        }
        RefreshPayoutNameFontSize();

        if (_payoutNames.Count > 0)
            _defaultNameColor = _payoutNames[0].GetThemeColor("font_color");
        if (_payoutValues.Count > 0)
            _defaultValueColor = _payoutValues[0].GetThemeColor("font_color");

        _blindBoxBtn.Disabled = true;
        // 初始状态：清除 .tscn 占位文本
        _winResultLabel.Text = "";
        _winResultLabel.SelfModulate = new Color(1, 1, 1, 0);
        _rankNameLabel.Text = L10n.Tr(L10nKey.InfoPanel_GoodLuck);
        RefreshLocalizedText();
        L10n.Changed += RefreshLocalizedText;
    }

    public void Bind(GameData data)
    {
        _gameData = data;
        _displayedChips = data.Chips;
        SetChipsText(_displayedChips);
        data.ChipsChanged += OnChipsChanged;
        data.HandResolved += OnHandResolved;
        data.NewHandStarted += OnNewHandStarted;
        data.BlindBoxStateChanged += RefreshBlindBoxButton;
        RefreshPayoutValues();
        RefreshBlindBoxButton();
    }

    private void OnChipsChanged(int newChips)
    {
        int delta = newChips - _displayedChips;
        if (Mathf.Abs(delta) <= 1)
        {
            _displayedChips = newChips;
            SetChipsText(newChips);
            RefreshBlindBoxButton();
            return;
        }

        _chipsTween?.Kill();
        _chipsTween = CreateTween();
        _chipsTween.TweenMethod(
            Callable.From<int>(SetChipsText),
            _displayedChips,
            newChips,
            ChipsAnimDuration
        );
        _displayedChips = newChips;
        RefreshBlindBoxButton();
    }

    private void OnNewHandStarted()
    {
        StopBlink();
        ClearHighlight();
        _winResultLabel.Text = "";
        _winResultLabel.SelfModulate = new Color(1, 1, 1, 0);
        _rankNameLabel.Text = L10n.Tr(L10nKey.InfoPanel_GoodLuck);
        _currentRank = EHandRank.Nothing;
        _currentPayout = 0;
        _hasResolvedHand = false;
    }

    private void OnHandResolved(EHandRank rank, int payout)
    {
        StopBlink();
        _winResultLabel.SelfModulate = Colors.White;
        _currentRank = rank;
        _currentPayout = payout;
        _hasResolvedHand = true;

        if (payout > 0)
        {
            _rankNameLabel.Text = L10n.Tr(L10n.GetHandRankKey(rank));
            _winResultLabel.Text = L10n.Format(L10nKey.InfoPanel_YouWin, payout);
            HighlightPayoutRow(rank);
            StartBlink();
        }
        else
        {
            _rankNameLabel.Text = rank == EHandRank.Nothing
                ? L10n.Tr(L10nKey.InfoPanel_Nothing)
                : L10n.Tr(L10n.GetHandRankKey(rank));
            _winResultLabel.Text = "";
            ClearHighlight();
        }
    }

    private void StartBlink()
    {
        _blinkTween?.Kill();
        _blinkTween = CreateTween();
        _blinkTween.SetLoops(0); // infinite
        _blinkTween.TweenCallback(Callable.From(() => _winResultLabel.SelfModulate = Colors.White));
        _blinkTween.TweenInterval(BlinkVisibleDuration);
        _blinkTween.TweenCallback(Callable.From(() => _winResultLabel.SelfModulate = new Color(1, 1, 1, 0)));
        _blinkTween.TweenInterval(BlinkHiddenDuration);
    }

    private void StopBlink()
    {
        _blinkTween?.Kill();
        _blinkTween = null;
        _winResultLabel.SelfModulate = new Color(1, 1, 1, 0);
    }

    public void SetChips(int chips)
    {
        SetChipsText(chips);
    }

    /// <summary>
    /// 下注金额不足时以“当前余额/下注额”呈现；余额达标后恢复为普通余额。
    /// </summary>
    private void SetChipsText(int chips)
    {
        if (_gameData != null && chips < _gameData.BetAmount)
        {
            _chipsLabel.Text = $"{chips:N0}/{_gameData.BetAmount:N0}";
            return;
        }

        _chipsLabel.Text = chips.ToString("N0");
    }

    /// <summary>
    /// 玩家在余额不足时点击下注筹码的操作反馈。
    /// 闪烁节奏和盲盒金额不足提示保持一致。
    /// </summary>
    public void FlashInsufficientBet()
    {
        _insufficientBetFlashTween?.Kill();
        ResetChipsTextColor();
        var warningColor = new Color(0.90588236f, 0.54901963f, 0.627451f); // #E78CA0
        _insufficientBetFlashTween = CreateTween();
        for (var i = 0; i < 2; i++)
        {
            _insufficientBetFlashTween.TweenCallback(Callable.From(() => SetChipsTextColor(warningColor)));
            _insufficientBetFlashTween.TweenInterval(0.12);
            _insufficientBetFlashTween.TweenCallback(Callable.From(ResetChipsTextColor));
            _insufficientBetFlashTween.TweenInterval(0.12);
        }
    }

    private void SetChipsTextColor(Color color)
    {
        // 仅改变字体色；LabelSettings 副本中的阴影大小、颜色和偏移保持不变。
        if (_chipsLabelSettings != null)
        {
            _chipsLabelSettings.FontColor = color;
            return;
        }

        _chipsLabel.AddThemeColorOverride("font_color", color);
    }

    private void ResetChipsTextColor()
    {
        SetChipsTextColor(_normalChipsTextColor);
    }

    public void SetRank(string rankName)
    {
        _rankNameLabel.Text = rankName;
    }

    public void SetWinResult(string text)
    {
        _winResultLabel.Text = text;
    }

    public void HighlightPayoutRow(EHandRank rank)
    {
        ClearHighlight();

        int idx = System.Array.IndexOf(GridOrder, rank);
        if (idx < 0 || idx >= _payoutNames.Count || idx >= _payoutValues.Count)
            return;

        _lastHighlightedName = _payoutNames[idx];
        _lastHighlightedName.AddThemeColorOverride("font_color", new Color(1, 0.78f, 0.2f));
        _lastHighlightedValue = _payoutValues[idx];
        _lastHighlightedValue.AddThemeColorOverride("font_color", new Color(1, 0.78f, 0.2f));
        _hasHighlight = true;
    }

    public void ClearHighlight()
    {
        if (_hasHighlight)
        {
            _lastHighlightedName.RemoveThemeColorOverride("font_color");
            _lastHighlightedValue.RemoveThemeColorOverride("font_color");
            _hasHighlight = false;
        }
    }

    public void SetPanelPosition(Vector2 pos)
    {
        _panel.Position = pos;
        LockPanelSize();
    }

    private void LockPanelSize()
    {
        _panel.CustomMinimumSize = PanelSize;
        _panel.Size = PanelSize;
    }

    public void RefreshBlindBoxButton()
    {
        if (_gameData == null)
            return;

        var state = _gameData.GetBlindBoxHintState();
        _blindBoxBtn.Disabled = state.Status == BlindBoxHintStatus.Waiting;
        RefreshActionButtonText(_blindBoxBtn, L10nKey.InfoPanel_Open);
        var hideWaitingBubble = state.Status == BlindBoxHintStatus.Waiting
            && !SettingsManager.LoadAlwaysShowBlindBoxBubble();
        SetBlindBoxHintDisplayVisible(state.Status != BlindBoxHintStatus.PendingReward && !hideWaitingBubble);

        switch (state.Status)
        {
            case BlindBoxHintStatus.PendingReward:
                break;
            case BlindBoxHintStatus.Ready:
            case BlindBoxHintStatus.NotEnoughChips:
                _blindBoxHint.ShowCost(_blindBoxIcon, state.Cost, _gameData.Chips);
                break;
            default:
                _blindBoxHint.ShowCountdown(TimeSpan.FromSeconds(state.RemainingSeconds));
                break;
        }
    }

    private void OnBlindBoxHintPressed()
    {
        if (_gameData == null)
            return;

        var state = _gameData.GetBlindBoxHintState();
        switch (state.Status)
        {
            case BlindBoxHintStatus.PendingReward:
                EmitSignal(SignalName.BlindBoxRequested);
                break;
            case BlindBoxHintStatus.Ready:
                EmitSignal(SignalName.BlindBoxRequested);
                break;
            case BlindBoxHintStatus.NotEnoughChips:
                _blindBoxHint.FlashTextRed();
                break;
        }
    }

    private void SetBlindBoxHintDisplayVisible(bool visible)
    {
        _blindBoxHint.SetDisplayVisible(visible);
    }

    private void RefreshLocalizedText()
    {
        _chipsKeyLabel.Text = L10n.Tr(L10nKey.InfoPanel_Chips);
        RefreshActionButtonText(_blindBoxBtn, L10nKey.InfoPanel_Open);
        RefreshActionButtonText(_settingsBtn, L10nKey.InfoPanel_Menu);

        var payList = LubanData.Tables.TbPayTable.DataList;
        for (int i = 0; i < payList.Count; i++)
        {
            int gridIdx = payList.Count - 1 - i;
            if (gridIdx < _payoutNames.Count)
                _payoutNames[gridIdx].Text = L10n.Tr(L10n.GetHandRankKey(payList[i].HandRank));
        }
        RefreshPayoutNameFontSize();

        if (!_hasResolvedHand)
        {
            _rankNameLabel.Text = L10n.Tr(L10nKey.InfoPanel_GoodLuck);
            return;
        }

        _rankNameLabel.Text = _currentRank == EHandRank.Nothing
            ? L10n.Tr(L10nKey.InfoPanel_Nothing)
            : L10n.Tr(L10n.GetHandRankKey(_currentRank));
        _winResultLabel.Text = _currentPayout > 0
            ? L10n.Format(L10nKey.InfoPanel_YouWin, _currentPayout)
            : "";
    }

    /// <summary>赔率表填写倍率，面板展示本局下注额对应的实际奖励。</summary>
    private void RefreshPayoutValues()
    {
        if (_gameData == null)
            return;

        var payList = LubanData.Tables.TbPayTable.DataList;
        for (int i = 0; i < payList.Count; i++)
        {
            int gridIdx = payList.Count - 1 - i;
            if (gridIdx < _payoutValues.Count)
                _payoutValues[gridIdx].Text = (payList[i].PayoutMultiplier * _gameData.BetAmount).ToString();
        }
    }

    private static void RefreshActionButtonText(Button button, string key)
    {
        var showText = L10n.CurrentLocale == L10n.SimplifiedChineseLocale;
        button.Text = showText ? L10n.Tr(key) : string.Empty;
        button.IconAlignment = showText ? HorizontalAlignment.Left : HorizontalAlignment.Center;
    }

    private void RefreshPayoutNameFontSize()
    {
        var fontSize = GetPayoutNameFontSizeForLocale(L10n.CurrentLocale);

        foreach (var label in _payoutNames)
            label.AddThemeFontSizeOverride("font_size", fontSize);
    }

    private static int GetPayoutNameFontSizeForLocale(string locale)
    {
        return locale switch
        {
            L10n.JapaneseLocale => JapanesePayoutNameFontSize,
            _ => DefaultPayoutNameFontSize,
        };
    }

}
