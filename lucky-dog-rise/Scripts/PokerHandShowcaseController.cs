using Godot;
using DataTables;

namespace LuckyDogRise;

public partial class PokerHandShowcaseController : CanvasLayer
{
    [Signal] public delegate void ShowcaseVisibilityChangedEventHandler(bool visible);

    [Export] private Button _dismissButton = null!;
    [Export] private Label _royalFlushLabel = null!;
    [Export] private Label _straightFlushLabel = null!;
    [Export] private Label _fourOfAKindLabel = null!;
    [Export] private Label _fullHouseLabel = null!;
    [Export] private Label _flushLabel = null!;
    [Export] private Label _straightLabel = null!;
    [Export] private Label _threeOfAKindLabel = null!;
    [Export] private Label _twoPairLabel = null!;
    [Export] private Label _onePairLabel = null!;

    public bool IsOverlayVisible => Visible;

    public override void _Ready()
    {
        _dismissButton.Pressed += HideOverlay;
        L10n.Changed += RefreshLocalizedText;
        RefreshLocalizedText();
    }

    public override void _ExitTree()
    {
        L10n.Changed -= RefreshLocalizedText;
    }

    public void ShowOverlay()
    {
        if (Visible)
            return;

        Visible = true;
        EmitSignal(SignalName.ShowcaseVisibilityChanged, true);
    }

    public void HideOverlay()
    {
        if (!Visible)
            return;

        Visible = false;
        EmitSignal(SignalName.ShowcaseVisibilityChanged, false);
    }

    private void RefreshLocalizedText()
    {
        _royalFlushLabel.Text = GetHandName(EHandRank.RoyalFlush);
        _straightFlushLabel.Text = GetHandName(EHandRank.StraightFlush);
        _fourOfAKindLabel.Text = GetHandName(EHandRank.FourOfAKind);
        _fullHouseLabel.Text = GetHandName(EHandRank.FullHouse);
        _flushLabel.Text = GetHandName(EHandRank.Flush);
        _straightLabel.Text = GetHandName(EHandRank.Straight);
        _threeOfAKindLabel.Text = GetHandName(EHandRank.ThreeOfAKind);
        _twoPairLabel.Text = GetHandName(EHandRank.TwoPair);
        _onePairLabel.Text = GetHandName(EHandRank.OnePair);
    }

    private static string GetHandName(EHandRank rank)
    {
        return L10n.Tr(L10n.GetHandRankKey(rank));
    }
}
