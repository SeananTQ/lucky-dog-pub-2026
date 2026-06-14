using Godot;
using System.Collections.Generic;

namespace LuckyDogRise;

public partial class InfoPanelController : CanvasLayer
{
    [Signal] public delegate void SettingsRequestedEventHandler();

    [Export] private Label _chipsLabel = null!;
    [Export] private Label _rankNameLabel = null!;
    [Export] private Label _winResultLabel = null!;
    [Export] private GridContainer _payoutGrid = null!;
    [Export] private Button _settingsBtn = null!;
    [Export] private Button _blindBoxBtn = null!;
    [Export] private Label _blindBoxCostLabel = null!;

    // ===== 动画参数 =====
    private const float ChipsAnimDuration = 0.4f;
    private const float BlinkVisibleDuration = 0.8f;
    private const float BlinkHiddenDuration = 0.4f;

    private GameData _gameData = null!;
    private readonly List<Label> _payoutNames = new();
    private readonly List<Label> _payoutValues = new();
    private Label _lastHighlightedName = null!;
    private Label _lastHighlightedValue = null!;
    private bool _hasHighlight;
    private Color _defaultNameColor;
    private Color _defaultValueColor;

    private int _displayedChips;
    private Tween _chipsTween;
    private Tween _blinkTween;

    // Display payout = CardEvaluator.PayTable multiplier × GameData.BetAmount (50)
    // User-editable: change values here to match desired reward amounts
    private static readonly int[] PayoutTable =
    {
        12500, // RoyalFlush   (250 × 50)
        2500,  // StraightFlush (50 × 50)
        1250,  // FourOfAKind   (25 × 50)
        450,   // FullHouse     (9 × 50)
        300,   // Flush         (6 × 50)
        200,   // Straight      (4 × 50)
        150,   // ThreeOfAKind  (3 × 50)
        100,   // TwoPair       (2 × 50)
        50,    // JacksOrBetter (1 × 50)
    };

    private static readonly HandRank[] GridOrder =
    {
        HandRank.RoyalFlush,
        HandRank.StraightFlush,
        HandRank.FourOfAKind,
        HandRank.FullHouse,
        HandRank.Flush,
        HandRank.Straight,
        HandRank.ThreeOfAKind,
        HandRank.TwoPair,
        HandRank.JacksOrBetter,
    };

    public override void _Ready()
    {
        _settingsBtn.Pressed += () => EmitSignal(SignalName.SettingsRequested);

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

        for (int i = 0; i < PayoutTable.Length && i < _payoutValues.Count; i++)
            _payoutValues[i].Text = PayoutTable[i].ToString();

        if (_payoutNames.Count > 0)
            _defaultNameColor = _payoutNames[0].GetThemeColor("font_color");
        if (_payoutValues.Count > 0)
            _defaultValueColor = _payoutValues[0].GetThemeColor("font_color");

        _blindBoxBtn.Disabled = true;

        // 初始状态：清除 .tscn 占位文本
        _winResultLabel.Text = "";
        _winResultLabel.SelfModulate = new Color(1, 1, 1, 0);
        _rankNameLabel.Text = "Good Luck!";
    }

    public void Bind(GameData data)
    {
        _gameData = data;
        _displayedChips = data.Chips;
        _chipsLabel.Text = _displayedChips.ToString("N0");
        data.ChipsChanged += OnChipsChanged;
        data.HandResolved += OnHandResolved;
        data.NewHandStarted += OnNewHandStarted;
    }

    private void OnChipsChanged(int newChips)
    {
        int delta = newChips - _displayedChips;
        if (Mathf.Abs(delta) <= 1)
        {
            _displayedChips = newChips;
            _chipsLabel.Text = newChips.ToString("N0");
            return;
        }

        _chipsTween?.Kill();
        _chipsTween = CreateTween();
        _chipsTween.TweenMethod(
            Callable.From<int>(v => _chipsLabel.Text = v.ToString("N0")),
            _displayedChips,
            newChips,
            ChipsAnimDuration
        );
        _displayedChips = newChips;
    }

    private void OnNewHandStarted()
    {
        StopBlink();
        ClearHighlight();
        _winResultLabel.Text = "";
        _winResultLabel.SelfModulate = new Color(1, 1, 1, 0);
        _rankNameLabel.Text = "Good Luck!";
    }

    private void OnHandResolved(HandRank rank, int payout)
    {
        StopBlink();
        _winResultLabel.SelfModulate = Colors.White;

        if (payout > 0)
        {
            _rankNameLabel.Text = rank.ToString();
            _winResultLabel.Text = $"You win {payout}";
            HighlightPayoutRow(rank);
            StartBlink();
        }
        else
        {
            _rankNameLabel.Text = rank == HandRank.Nothing ? "Nothing" : rank.ToString();
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
        _chipsLabel.Text = chips.ToString("N0");
    }

    public void SetRank(string rankName)
    {
        _rankNameLabel.Text = rankName;
    }

    public void SetWinResult(string text)
    {
        _winResultLabel.Text = text;
    }

    public void SetBlindBoxCost(int cost)
    {
        _blindBoxCostLabel.Text = cost.ToString("N0");
    }

    public void HighlightPayoutRow(HandRank rank)
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
        var panel = GetNode<PanelContainer>("Panel");
        panel.Position = pos;
    }
}
