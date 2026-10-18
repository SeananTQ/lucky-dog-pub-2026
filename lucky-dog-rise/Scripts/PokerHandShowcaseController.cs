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
    [Export] private Control _basicsPage = null!;
    [Export] private Control _ranksPage = null!;
    [Export] private ScrollContainer _contentScroll = null!;
    [Export] private Button _basicsTab = null!;
    [Export] private Button _ranksTab = null!;
    [Export] private Button _closeButton = null!;
    [Export] private Label _operationTitleLabel = null!;
    [Export] private Label _scrollHintLabel = null!;
    [Export] private Label _faceUpLabel = null!;
    [Export] private Label _faceDownLabel = null!;
    [Export] private AnimationPlayer _flipDemo = null!;
    [Export] private AnimationPlayer _pairDemo = null!;
    [Export] private AnimationPlayer _flushDemo = null!;
    [Export] private Label _pairGoalLabel = null!;
    [Export] private Label _flushGoalLabel = null!;
    [Export] private Label _pairBeforeLabel = null!;
    [Export] private Label _flushBeforeLabel = null!;
    [Export] private Label _pairAfterLabel = null!;
    [Export] private Label _flushAfterLabel = null!;
    [Export] private Label _pairResultLabel = null!;
    [Export] private Label _flushResultLabel = null!;
    [Export] private Label _pairNoteLabel = null!;
    [Export] private Label _flushNoteLabel = null!;
    [Export] private Label _warningLabel = null!;
    [Export] private Label _minimumLabel = null!;
    [Export] private Label _ranksIntroLabel = null!;
    [Export] private Label _ranksNoteLabel = null!;

    public bool IsOverlayVisible => Visible;

    public override void _Ready()
    {
        _dismissButton.Pressed += HideOverlay;
        _closeButton.Pressed += HideOverlay;
        _basicsTab.Pressed += () => SelectPage(showRanks: false);
        _ranksTab.Pressed += () => SelectPage(showRanks: true);
        SelectPage(showRanks: false);
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
        RefreshDemos();
        EmitSignal(SignalName.ShowcaseVisibilityChanged, true);
    }

    public void HideOverlay()
    {
        if (!Visible)
            return;

        Visible = false;
        _flipDemo.Stop();
        _pairDemo.Stop();
        _flushDemo.Stop();
        EmitSignal(SignalName.ShowcaseVisibilityChanged, false);
    }

    public bool TryScrollAt(Vector2 viewportPosition, double wheelSteps)
    {
        if (!Visible)
            return false;

        var localPosition = _contentScroll.GetGlobalTransformWithCanvas().AffineInverse() * viewportPosition;
        if (!new Rect2(Vector2.Zero, _contentScroll.Size).HasPoint(localPosition))
            return false;

        // Match Godot's ScrollContainer: each wheel notch scrolls one eighth of a page.
        // Return true at either end too, so the wheel cannot fall through to the table.
        var scrollbar = _contentScroll.GetVScrollBar();
        scrollbar.Value += scrollbar.Page / 8.0 * wheelSteps;
        return true;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (Visible && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            HideOverlay();
            GetViewport().SetInputAsHandled();
        }
    }

    private void SelectPage(bool showRanks)
    {
        _basicsPage.Visible = !showRanks;
        _ranksPage.Visible = showRanks;
        _basicsTab.SetPressedNoSignal(!showRanks);
        _ranksTab.SetPressedNoSignal(showRanks);
        _basicsTab.ThemeTypeVariation = showRanks ? "PokerHelpTab" : "PokerHelpTabSelected";
        _ranksTab.ThemeTypeVariation = showRanks ? "PokerHelpTabSelected" : "PokerHelpTab";
        _contentScroll.ScrollVertical = 0;
        RefreshDemos();
    }

    private void RefreshDemos()
    {
        _flipDemo.Stop();
        _pairDemo.Stop();
        _flushDemo.Stop();
        if (!Visible || !_basicsPage.Visible)
            return;

        _flipDemo.Play("flip_demo");
        _flipDemo.Advance(0);
        _pairDemo.Play("draw_demo");
        _pairDemo.Advance(0);
        _flushDemo.Play("flush_demo");
        _flushDemo.Advance(0);
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
        _operationTitleLabel.Text = L10n.Tr(L10nKey.PokerHelp_OperationTitle);
        _scrollHintLabel.Text = L10n.Tr(L10nKey.PokerHelp_ScrollHint);
        _basicsTab.Text = L10n.Tr(L10nKey.PokerHelp_BuildTab);
        _ranksTab.Text = L10n.Tr(L10nKey.PokerHelp_RanksTab);
        _closeButton.TooltipText = L10n.Tr(L10nKey.PokerHelp_Close);
        _faceUpLabel.Text = L10n.Tr(L10nKey.PokerHelp_FaceUpKeep);
        _faceDownLabel.Text = L10n.Tr(L10nKey.PokerHelp_FaceDownReplace);
        _pairGoalLabel.Text = L10n.Format(L10nKey.PokerHelp_PairGoal,
            GetHandName(EHandRank.OnePair), GetHandName(EHandRank.ThreeOfAKind));
        _flushGoalLabel.Text = L10n.Format(L10nKey.PokerHelp_FlushGoal, GetHandName(EHandRank.Flush));
        _pairBeforeLabel.Text = _flushBeforeLabel.Text = L10n.Tr(L10nKey.PokerHelp_YourCards);
        _pairAfterLabel.Text = _flushAfterLabel.Text = L10n.Tr(L10nKey.PokerHelp_PossibleResult);
        _pairResultLabel.Text = GetHandName(EHandRank.ThreeOfAKind);
        _flushResultLabel.Text = GetHandName(EHandRank.Flush);
        _pairNoteLabel.Text = L10n.Tr(L10nKey.PokerHelp_PairNote);
        _flushNoteLabel.Text = L10n.Tr(L10nKey.PokerHelp_FlushNote);
        _warningLabel.Text = L10n.Tr(L10nKey.PokerHelp_ResultWarning);
        _minimumLabel.Text = L10n.Format(L10nKey.PokerHelp_MinimumHand, GetHandName(EHandRank.OnePair));
        _ranksIntroLabel.Text = L10n.Tr(L10nKey.PokerHelp_RanksIntro);
        _ranksNoteLabel.Text = L10n.Tr(L10nKey.PokerHelp_RanksNote);
    }

    private static string GetHandName(EHandRank rank)
    {
        return L10n.Tr(L10n.GetHandRankKey(rank));
    }
}
