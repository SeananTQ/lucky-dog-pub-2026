using Godot;
using System.Collections.Generic;
using System.Linq;

namespace LuckyDogRise;

public partial class InfoPanelController : CanvasLayer
{
    [Signal] public delegate void SettingsRequestedEventHandler();

    [Export] private Label _chipsLabel = null!;
    [Export] private Label _rankLabel = null!;
    [Export] private GridContainer _payoutGrid = null!;
    [Export] private Button _settingsBtn = null!;
    [Export] private Button _blindBoxBtn = null!;
    [Export] private Label _blindBoxCostLabel = null!;

    private readonly List<Label> _payoutNames = new();
    private readonly List<Label> _payoutValues = new();
    private Label _lastHighlightedName = null!;
    private bool _hasHighlight;
    private Color _defaultNameColor;

    // HandRank → grid row index (same order as CardEvaluator.HandRank enum)
    private static readonly HandRank[] GridOrder =
    {
        HandRank.RoyalFlush,     // 0
        HandRank.StraightFlush,  // 1
        HandRank.FourOfAKind,    // 2
        HandRank.FullHouse,      // 3
        HandRank.Flush,          // 4
        HandRank.Straight,       // 5
        HandRank.ThreeOfAKind,   // 6
        HandRank.TwoPair,        // 7
        HandRank.JacksOrBetter,  // 8
    };

    public override void _Ready()
    {
        _settingsBtn.Pressed += () => EmitSignal(SignalName.SettingsRequested);

        // Collect payout grid labels by index (even = name, odd = value)
        var children = _payoutGrid.GetChildren().Cast<Label>().ToList();
        for (int i = 0; i < children.Count; i++)
        {
            if (i % 2 == 0)
                _payoutNames.Add(children[i]);
            else
                _payoutValues.Add(children[i]);
        }

        if (_payoutNames.Count > 0)
            _defaultNameColor = _payoutNames[0].Theme.GetColor("font_color", "Label");

        // Blind box disabled for now
        _blindBoxBtn.Disabled = true;
    }

    public void SetChips(int chips)
    {
        _chipsLabel.Text = chips.ToString("N0");
    }

    public void SetRank(string rankName)
    {
        _rankLabel.Text = rankName;
    }

    public void SetBlindBoxCost(int cost)
    {
        _blindBoxCostLabel.Text = $"Cost: {cost:N0}";
    }

    public void HighlightPayoutRow(HandRank rank)
    {
        if (_hasHighlight)
        {
            _lastHighlightedName.RemoveThemeColorOverride("font_color");
            _hasHighlight = false;
        }

        int idx = System.Array.IndexOf(GridOrder, rank);
        if (idx < 0 || idx >= _payoutNames.Count)
            return;

        _lastHighlightedName = _payoutNames[idx];
        _lastHighlightedName.AddThemeColorOverride("font_color", new Color(1, 0.78f, 0.2f)); // gold
        _hasHighlight = true;
    }

    public void ClearHighlight()
    {
        if (_hasHighlight)
        {
            _lastHighlightedName.RemoveThemeColorOverride("font_color");
            _hasHighlight = false;
        }
    }

    public void SetPanelPosition(Vector2 pos)
    {
        var panel = GetNode<PanelContainer>("Panel");
        panel.Position = pos;
    }
}
