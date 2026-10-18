#if DEBUG
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LuckyDogRise;

public partial class PokerHandHelpPreview : Control
{
    [Export] private PokerHandShowcaseController _showcase = null!;
    private readonly List<string> _issues = new();
    private const string BasicsPath = "Content/Scroll/Pages/Basics";
    private const string RanksPath = "Content/Scroll/Pages/Rows";
    private static readonly string[] Locales =
        ["en", "zh_CN", "zh_TW", "ja", "es_ES", "es_419", "pt_BR", "pt_PT", "fr", "de", "da", "id", "nb", "sv", "nl", "vi", "ms", "ko", "ru", "uk"];

    public override async void _Ready()
    {
        // This preview never creates GameData, a platform service, or a player save.
        // --loot-validation-headless also skips the normal account mutex autoload.
        GetWindow().ContentScaleSize = new Vector2I(1200, 1200);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        GetWindow().Size = new Vector2I(600, 600);
        bool englishOnly = OS.GetCmdlineUserArgs().Contains("--poker-help-english");
        bool frenchReview = OS.GetCmdlineUserArgs().Contains("--poker-help-french");
        bool drawReview = OS.GetCmdlineUserArgs().Contains("--poker-help-draw");
        bool flushReview = OS.GetCmdlineUserArgs().Contains("--poker-help-flush");
        L10n.SetLocale(englishOnly ? "en" : "zh_CN", save: false);
        L10n.SetSafeMode(false);
        _showcase.ShowOverlay();
        var standardFont = GD.Load<Theme>("res://Themes/DefaultTheme.tres").GetFont("font", "Label");
        GD.Print("[PokerHelp] Standard label suit glyphs: " + string.Join(", ", "♥♦♣♠".Select(symbol => $"{symbol}={standardFont.HasChar(symbol)}")));
        if (!OS.GetCmdlineUserArgs().Contains("--poker-help-validate"))
        {
            _showcase.ShowcaseVisibilityChanged += visible => { if (!visible) GetTree().Quit(); };
            return;
        }

        try
        {
            bool clickedBehind = false;
            GetNode<Button>("BehindHelp").Pressed += () => clickedBehind = true;
            await Settle();
            await Click(_showcase.GetNode<Control>(BasicsPath + "/Pair/Comparison/Before/Cards/Card1"));
            Require(_showcase.IsOverlayVisible && !clickedBehind, "Read-only cards must neither dismiss help nor click the table behind it.");
            await Click(_showcase.GetNode<Button>("Header/RanksTab"));
            Require(_showcase.GetNode<Control>(RanksPath).Visible, "Mouse click must select the hand-rank tab.");
            await Click(_showcase.GetNode<Button>("Header/BasicsTab"));
            Require(_showcase.GetNode<Control>(BasicsPath).Visible, "Mouse click must return to the basics tab.");
            if (frenchReview || drawReview || OS.GetCmdlineUserArgs().Contains("--poker-help-layout")) await CheckFlipDemo();
            if (drawReview) await CheckPairDemo();
            if (flushReview) await CheckFlushDemo();
            string[] locales = drawReview ? ["en", "fr", "zh_CN", "zh_TW"] : frenchReview ? ["en", "fr"] : englishOnly ? ["en"] : Locales;
            float englishLegendHeight = 0;
            foreach (bool safe in new[] { false, true })
            {
                L10n.SetSafeMode(safe);
                foreach (string locale in locales)
                {
                    L10n.SetLocale(locale, save: false);
                    await Settle();
                    CheckLayout($"{locale}/safe={safe}/basics");
                    _showcase.GetNode<AnimationPlayer>(BasicsPath + "/Pair/Comparison/After/Demo/AnimationPlayer").Seek(5, update: true);
                    _showcase.GetNode<AnimationPlayer>(BasicsPath + "/Flush/Comparison/After/Demo/AnimationPlayer").Seek(4.3, update: true);
                    await Settle();
                    CheckLayout($"{locale}/safe={safe}/basics-result");
                    CheckMarkerAlignment();
                    CheckGuideLayout();
                    if (locale is "en" or "fr" or "zh_CN" or "zh_TW")
                        await Capture($"{locale}-{(safe ? "safe" : "standard")}-basics-result");
                    var legend = _showcase.GetNode<Control>(BasicsPath + "/Legend");
                    if (locale == "en") englishLegendHeight = legend.Size.Y;
                    if (locale == "fr" && englishLegendHeight > 0)
                        Require(Math.Abs(legend.Size.Y - englishLegendHeight) < 1, "French must not increase the legend height compared with English.");
                    if (locale is "en" or "fr" or "zh_CN" or "zh_TW") await CheckScrolling($"{locale}-{(safe ? "safe" : "standard")}-basics");
                    _showcase.GetNode<Button>("Header/RanksTab").EmitSignal(Button.SignalName.Pressed);
                    await Settle();
                    Require(_showcase.GetNode<Control>(RanksPath).Visible, "Rank tab must show the original hand guide.");
                    CheckLayout($"{locale}/safe={safe}/ranks");
                    if (locale is "en" or "fr" or "zh_CN" or "zh_TW") await CheckScrolling($"{locale}-{(safe ? "safe" : "standard")}-ranks");
                    _showcase.GetNode<Button>("Header/BasicsTab").EmitSignal(Button.SignalName.Pressed);
                }
            }

            await Click(_showcase.GetNode<Button>("Header/CloseButton"));
            Require(!_showcase.IsOverlayVisible, "Close button must hide the help.");
            _showcase.ShowOverlay();
            GetViewport().PushInput(new InputEventKey { Pressed = true, Keycode = Key.Escape }, inLocalCoords: true);
            await Settle();
            Require(!_showcase.IsOverlayVisible, "Escape must hide the help.");
            _showcase.ShowOverlay();
            await ClickAt(new Vector2(5, 5));
            Require(!_showcase.IsOverlayVisible, "Outside dismiss must hide the help.");
            foreach (var issue in _issues.Distinct()) GD.PrintErr("[PokerHelp] " + issue);
            GD.Print($"[PokerHelp] {locales.Length} locales x 2 naming modes x 2 pages; scroll wheel, drag, fixed header, close and Escape checked; issues={_issues.Distinct().Count()}");
            GetTree().Quit(_issues.Count == 0 ? 0 : 1);
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private async Task Settle()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task CheckFlipDemo()
    {
        L10n.SetLocale("fr", save: false);
        _showcase.HideOverlay();
        _showcase.ShowOverlay();
        await Settle();
        var demo = _showcase.GetNode<Control>(BasicsPath + "/Legend/Row/Flip");
        var player = demo.GetNode<AnimationPlayer>("AnimationPlayer");
        var front = demo.GetNode<TextureRect>("Card/Front");
        var back = demo.GetNode<TextureRect>("Card/Back");
        var card = demo.GetNode<Control>("Card");
        var pointer = demo.GetNode<Control>("Pointer");
        Require(player.IsPlaying() && front.Visible && !back.Visible, "Demo must start automatically with a face-up card.");
        Require(front.Texture.ResourcePath.EndsWith("Club4.png"), "Demo must use the four of clubs.");
        var startPointer = pointer.Position;
        bool sawBack = false, sawReturn = false, sawMovement = false, sawClick = false, sawFlip = false, sawLoop = false;
        double previousPosition = player.CurrentAnimationPosition;
        ulong start = Time.GetTicksMsec();
        ulong nextCapture = start;
        int frame = 0;
        while (Time.GetTicksMsec() - start < 7400)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(front.Visible != back.Visible, "Exactly one card face must be visible throughout the demo.");
            sawBack |= back.Visible;
            sawReturn |= sawBack && front.Visible;
            sawMovement |= pointer.Position.DistanceTo(startPointer) > 20;
            sawClick |= pointer.Scale.X < 0.9f;
            sawFlip |= card.Scale.X < 0.5f;
            double position = player.CurrentAnimationPosition;
            sawLoop |= position + 1 < previousPosition;
            previousPosition = position;
            if (Time.GetTicksMsec() >= nextCapture)
            {
                await Capture($"animation-{frame++:D3}");
                nextCapture = Time.GetTicksMsec() + 100;
            }
        }
        Require(sawMovement && sawClick && sawFlip && sawBack && sawReturn && sawLoop,
            "Demo must move the pointer, press, flip to the back, flip to the front, and loop without real input.");
        await Click(_showcase.GetNode<Button>("Header/RanksTab"));
        Require(!player.IsPlaying(), "Demo must stop on the hand-ranks page.");
        await Click(_showcase.GetNode<Button>("Header/BasicsTab"));
        Require(player.IsPlaying() && front.Visible && !back.Visible, "Returning to basics must restart face-up.");
        _showcase.HideOverlay();
        Require(!player.IsPlaying(), "Closing help must stop the demo.");
        _showcase.ShowOverlay();
        await Settle();
        Require(player.IsPlaying() && front.Visible && !back.Visible, "Reopening help must restart face-up.");
        GD.Print($"[PokerHelp] Automatic flip loop, pointer movement, press, tab/close/reopen lifecycle passed; {frame} rendered frames captured.");
    }

    private async Task CheckPairDemo()
    {
        L10n.SetLocale("zh_CN", save: false);
        _showcase.HideOverlay();
        _showcase.ShowOverlay();
        await Settle();
        Require(!_showcase.HasNode("Header/Title"), "The redundant help title must be removed.");
        var tabs = new[] { "BasicsTab", "RanksTab", "CloseButton" }
            .Select(name => _showcase.GetNode<Control>("Header/" + name)).ToArray();
        Require(tabs.All(tab => Math.Abs(tab.GetGlobalRect().GetCenter().Y - tabs[0].GetGlobalRect().GetCenter().Y) < 1),
            "Both tabs and close must be centered on the same row.");
        Require(Math.Abs(_showcase.GetNode<Control>("Header/Separator").GetGlobalRect().Position.Y - tabs[0].GetGlobalRect().End.Y) < 1,
            "The header separator must meet the bottom of the tabs without a gap.");
        var marker = _showcase.GetNode<TextureRect>(BasicsPath + "/Pair/Comparison/Before/Markers/Replace/Icon");
        Require(marker.Texture.ResourcePath.EndsWith("Any.png"), "The unmatched-card marker must use a question card.");
        var demo = _showcase.GetNode<Control>(BasicsPath + "/Pair/Comparison/After/Demo");
        var player = demo.GetNode<AnimationPlayer>("AnimationPlayer");
        var flipPlayer = _showcase.GetNode<AnimationPlayer>(BasicsPath + "/Legend/Row/Flip/AnimationPlayer");
        var initials = Enumerable.Range(3, 3).Select(i => demo.GetNode<TextureRect>($"Card{i}/InitialFace")).ToArray();
        var backs = Enumerable.Range(3, 3).Select(i => demo.GetNode<TextureRect>($"Card{i}/Back")).ToArray();
        var results = Enumerable.Range(3, 3).Select(i => demo.GetNode<TextureRect>($"Card{i}/ResultFace")).ToArray();
        var pointer = demo.GetNode<Control>("Pointer");
        var hand = demo.GetNode<Control>("Hand");
        var reward = demo.GetNode<Control>("Reward");
        Require(player.IsPlaying() && initials.All(card => card.Visible) && reward.Modulate.A == 0,
            "The automatic draw must begin with the original 4, 6, K and no reward.");
        Require(initials.Select(card => System.IO.Path.GetFileName(card.Texture.ResourcePath)).SequenceEqual(new[] { "Club4.png", "Diamond6.png", "Spade13.png" }),
            "The example must initially match the cards on the left.");
        bool sawOneBack = false, sawTwoBacks = false, sawThreeBacks = false, sawHandClick = false, sawResult = false, sawLoop = false;
        double previousPosition = player.CurrentAnimationPosition;
        ulong start = Time.GetTicksMsec(), nextCapture = start;
        int frame = 0;
        while (Time.GetTicksMsec() - start < 8400)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(demo.GetNode<Control>("Card1").Visible && demo.GetNode<Control>("Card2").Visible,
                "The kept pair must remain face-up throughout.");
            for (int i = 0; i < 3; i++)
                Require(new[] { initials[i].Visible, backs[i].Visible, results[i].Visible }.Count(value => value) == 1,
                    "Each replaceable card must show exactly one face.");
            int count = backs.Count(card => card.Visible);
            sawOneBack |= count == 1;
            sawTwoBacks |= count == 2;
            sawThreeBacks |= count == 3;
            if (hand.Scale.X < .95f)
            {
                Require(sawThreeBacks && count == 3 && hand.GetGlobalRect().HasPoint(pointer.GlobalPosition),
                    "The pointer must press the hand only after all three cards are selected.");
                sawHandClick = true;
            }
            if (reward.Modulate.A > .01f)
            {
                Require(sawHandClick && results.All(card => card.Visible), "Reward must appear only after the hand press and the replacement faces.");
                sawResult = true;
            }
            double position = player.CurrentAnimationPosition;
            sawLoop |= position + 1 < previousPosition;
            previousPosition = position;
            if (Time.GetTicksMsec() >= nextCapture)
            {
                await Capture($"draw-{frame++:D3}");
                nextCapture = Time.GetTicksMsec() + 100;
            }
        }
        Require(sawOneBack && sawTwoBacks && sawThreeBacks && sawHandClick && sawResult && sawLoop,
            "The automatic example must select three cards in order, press the hand, reveal the result and loop.");
        await Click(_showcase.GetNode<Button>("Header/RanksTab"));
        Require(!player.IsPlaying() && !flipPlayer.IsPlaying(), "Both demos must stop on the ranks tab.");
        await Click(_showcase.GetNode<Button>("Header/BasicsTab"));
        Require(player.IsPlaying() && initials.All(card => card.Visible) && reward.Modulate.A == 0,
            "Returning to basics must reset the example to its original hand.");
        _showcase.HideOverlay();
        Require(!player.IsPlaying() && !flipPlayer.IsPlaying(), "Closing help must stop both demos.");
        _showcase.ShowOverlay();
        await Settle();
        Require(player.IsPlaying() && initials.All(card => card.Visible), "Reopening help must restart the draw.");
        GD.Print($"[PokerHelp] Header, question-card marker, automatic draw sequence and both demo lifecycles passed; {frame} draw frames captured.");
    }

    private async Task CheckFlushDemo()
    {
        L10n.SetLocale("zh_CN", save: false);
        _showcase.HideOverlay();
        _showcase.ShowOverlay();
        await Settle();
        var scroll = _showcase.GetNode<ScrollContainer>("Content/Scroll");
        var section = _showcase.GetNode<Control>(BasicsPath + "/Flush");
        scroll.ScrollVertical = (int)section.Position.Y;
        await Settle();
        var demo = section.GetNode<Control>("Comparison/After/Demo");
        var player = demo.GetNode<AnimationPlayer>("AnimationPlayer");
        var otherPlayers = new[] {
            _showcase.GetNode<AnimationPlayer>(BasicsPath + "/Legend/Row/Flip/AnimationPlayer"),
            _showcase.GetNode<AnimationPlayer>(BasicsPath + "/Pair/Comparison/After/Demo/AnimationPlayer")
        };
        var kept = new[] { 1, 3, 4, 5 }.Select(i => demo.GetNode<TextureRect>($"Card{i}")).ToArray();
        var initial = demo.GetNode<TextureRect>("Card2/InitialFace");
        var back = demo.GetNode<TextureRect>("Card2/Back");
        var result = demo.GetNode<TextureRect>("Card2/ResultFace");
        var card = demo.GetNode<Control>("Card2");
        var pointer = demo.GetNode<Control>("Pointer");
        var hand = demo.GetNode<Control>("Hand");
        var reward = demo.GetNode<Control>("Reward");
        Require(kept.Select(item => System.IO.Path.GetFileName(item.Texture.ResourcePath))
            .SequenceEqual(new[] { "Spade9.png", "Spade4.png", "Spade6.png", "Spade13.png" }), "Keep the four spades at positions 1, 3, 4 and 5.");
        var before = Enumerable.Range(1, 5).Select(i => section.GetNode<TextureRect>($"Comparison/Before/Cards/Card{i}")).ToArray();
        Require(before.Select(item => System.IO.Path.GetFileName(item.Texture.ResourcePath))
            .SequenceEqual(new[] { "Spade9.png", "Heart9.png", "Spade4.png", "Spade6.png", "Spade13.png" }),
            "The lower example must start with 9, 9, 4, 6, K and only the second card in hearts.");
        var pairBefore = Enumerable.Range(1, 5).Select(i => _showcase.GetNode<TextureRect>(BasicsPath + $"/Pair/Comparison/Before/Cards/Card{i}")).ToArray();
        Require(pairBefore.Select(item => System.IO.Path.GetFileName(item.Texture.ResourcePath))
            .SequenceEqual(new[] { "Spade9.png", "Heart9.png", "Club4.png", "Diamond6.png", "Spade13.png" }),
            "The upper example must retain its original mixed suits for the strategy comparison.");
        Require(initial.Texture.ResourcePath.EndsWith("Heart9.png") && result.Texture.ResourcePath.EndsWith("Spade12.png"),
            "Replace the second card, the 9 of hearts, with a fifth spade in the example.");
        Require(demo.GetNode<TextureRect>("Reward/Icon").Texture.ResourcePath.EndsWith("ChipStack/Flush.png"),
            "The reward must use the flush chip asset.");
        Require(player.IsPlaying() && initial.Visible && !back.Visible && !result.Visible && reward.Modulate.A == 0,
            "The flush demo must start automatically with the 9 of hearts and no reward.");
        bool sawCardClick = false, sawBack = false, sawHandClick = false, sawReward = false, sawLoop = false;
        double previousPosition = player.CurrentAnimationPosition;
        ulong start = Time.GetTicksMsec(), nextCapture = start;
        int frame = 0;
        while (Time.GetTicksMsec() - start < 7200)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(kept.All(item => item.Visible && item.Scale == Vector2.One && item.Modulate.A == 1),
                "All four kept spades must stay face-up and undimmed throughout the animation.");
            Require(new[] { initial.Visible, back.Visible, result.Visible }.Count(value => value) == 1,
                "The replacement card must show exactly one face.");
            sawCardClick |= pointer.Scale.X < .9f && card.GetGlobalRect().HasPoint(pointer.GlobalPosition);
            if (back.Visible)
            {
                Require(sawCardClick, "The second card must turn over only after the pointer presses it.");
                sawBack = true;
            }
            if (hand.Scale.X < .95f)
            {
                Require(sawBack && back.Visible && hand.GetGlobalRect().HasPoint(pointer.GlobalPosition),
                    "Press the hand after selecting the 9 of hearts, with the pointer over the hand.");
                sawHandClick = true;
            }
            if (result.Visible) Require(sawHandClick, "The new spade must appear only after pressing the hand.");
            if (reward.Modulate.A > .01f)
            {
                Require(result.Visible && card.Scale.X > .99f, "Show the flush reward after the fifth spade finishes turning face-up.");
                Require(demo.GetNode<Label>("Reward/Name").Text == L10n.Tr(L10n.GetHandRankKey(DataTables.EHandRank.Flush)),
                    "The reward must name the flush hand.");
                sawReward = true;
            }
            double position = player.CurrentAnimationPosition;
            if (position + 1 < previousPosition)
            {
                Require(initial.Visible && !back.Visible && !result.Visible && reward.Modulate.A == 0,
                    "The next loop must reset to the 9 of hearts and hide the previous reward.");
                sawLoop = true;
            }
            previousPosition = position;
            if (Time.GetTicksMsec() >= nextCapture)
            {
                await Capture($"flush-{frame++:D3}");
                nextCapture = Time.GetTicksMsec() + 100;
            }
        }
        Require(sawCardClick && sawBack && sawHandClick && sawReward && sawLoop,
            "The automatic flush example must complete card selection, hand press, draw, reward and reset without input.");
        CheckLayout("flush/animated-result");
        await Click(_showcase.GetNode<Button>("Header/RanksTab"));
        Require(!player.IsPlaying() && otherPlayers.All(item => !item.IsPlaying()), "All demos must stop on the ranks page.");
        await Click(_showcase.GetNode<Button>("Header/BasicsTab"));
        Require(player.IsPlaying() && initial.Visible && reward.Modulate.A == 0, "Returning must restart the flush from the 9 of hearts.");
        _showcase.HideOverlay();
        Require(!player.IsPlaying() && otherPlayers.All(item => !item.IsPlaying()), "Closing must stop every demo.");
        _showcase.ShowOverlay();
        await Settle();
        Require(player.IsPlaying() && initial.Visible && reward.Modulate.A == 0, "Reopening must restart the flush example.");
        GD.Print($"[PokerHelp] Matching ranks across examples, four retained spades, second-card selection, hand press, fifth spade, flush reward, loop and all demo lifecycles passed; {frame} flush frames captured.");
    }

    private void CheckGuideLayout()
    {
        var tab = _showcase.GetNode<Control>("Header/BasicsTab");
        var line = _showcase.GetNode<Control>("Header/Separator");
        Require(Math.Abs(line.GetGlobalRect().Position.Y - tab.GetGlobalRect().End.Y) < 1 && Math.Abs(line.Size.Y - 6) < 1,
            "Header tabs must meet the settings-style separator at the correct scaled thickness.");
        var legend = _showcase.GetNode<Control>(BasicsPath + "/Legend/Row");
        Require(legend.GetNode<Control>("Flip").GetRect().End.X <= legend.GetNode<Control>("FaceUp").Position.X
            && legend.GetNode<Control>("FaceUp").GetRect().End.X <= legend.GetNode<Control>("FaceDown").Position.X,
            "Legend order must be automatic flip, face-up explanation, face-down explanation.");
        foreach (string section in new[] { "Pair", "Flush" })
            Require(_showcase.GetNode<Label>(BasicsPath + $"/{section}/Comparison/Before/Caption").Text == L10n.Tr(L10nKey.PokerHelp_YourCards),
                "Both examples must use the localized before-draw caption.");
    }

    private void CheckMarkerAlignment()
    {
        var rank = _showcase.GetNode<Label>(BasicsPath + "/Pair/Comparison/Before/Markers/Keep/Rank");
        Require(rank.Text == "9", "The pair marker must show only the rank 9, not a card or suit.");
        var suit = _showcase.GetNode<TextureRect>(BasicsPath + "/Flush/Comparison/Before/Markers/Keep/Icon/Suit");
        Require(suit.Texture.ResourcePath == "res://Assets/UI/Hint/Spade.svg", "The flush marker must use the standalone spade SVG.");
        foreach (string section in new[] { "Pair", "Flush" })
        {
            var hand = _showcase.GetNode<Control>(BasicsPath + $"/{section}/Comparison/After/Demo/Hand");
            float center = hand.GetGlobalRect().GetCenter().Y;
            foreach (string marker in new[] { "Keep/Count", "Replace/Count", "Replace/Icon" })
            {
                var item = _showcase.GetNode<Control>(BasicsPath + $"/{section}/Comparison/Before/Markers/{marker}");
                Require(Math.Abs(item.GetGlobalRect().GetCenter().Y - center) < 1,
                    $"{section}/{marker} must be vertically centered with the hand.");
            }
            var keep = section == "Pair" ? (Control)rank : suit;
            Require(Math.Abs(keep.GetGlobalRect().GetCenter().Y - center) < 1,
                $"{section} keep symbol must align with the hand.");
        }
    }

    private async Task CheckScrolling(string name)
    {
        var scroll = _showcase.GetNode<ScrollContainer>("Content/Scroll");
        var bar = scroll.GetVScrollBar();
        var header = _showcase.GetNode<Control>("Header");
        var headerRect = header.GetGlobalRect();
        Require(scroll.ScrollVertical == 0, "Switching tabs must start at the top.");
        Require(bar.Visible && bar.MaxValue > bar.Page, "Large text must have a visible, usable vertical scrollbar.");
        Require(!scroll.GetHScrollBar().Visible, "Help must fit the width without horizontal scrolling.");
        await Capture(name + "-top");
        var position = scroll.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position }, true);
        GetViewport().PushInput(new InputEventMouseButton { Position = position, GlobalPosition = position,
            ButtonIndex = MouseButton.WheelDown, Pressed = true, Factor = 1 }, true);
        GetViewport().PushInput(new InputEventMouseButton { Position = position, GlobalPosition = position,
            ButtonIndex = MouseButton.WheelDown, Pressed = false, Factor = 1 }, true);
        await Settle();
        Require(scroll.ScrollVertical > 0, "Mouse wheel must scroll the actual help content.");

        scroll.ScrollVertical = (int)((bar.MaxValue - bar.Page) / 2);
        await Settle();
        await Capture(name + "-middle");
        scroll.ScrollVertical = 0;
        await Settle();
        var rect = bar.GetGlobalRect();
        var from = new Vector2(rect.GetCenter().X, rect.Position.Y + (float)(rect.Size.Y * bar.Page / bar.MaxValue / 2));
        var to = new Vector2(from.X, rect.End.Y - 2);
        GetViewport().PushInput(new InputEventMouseMotion { Position = from, GlobalPosition = from }, true);
        GetViewport().PushInput(new InputEventMouseButton { Position = from, GlobalPosition = from,
            ButtonIndex = MouseButton.Left, Pressed = true, ButtonMask = MouseButtonMask.Left }, true);
        await Settle();
        GetViewport().PushInput(new InputEventMouseMotion { Position = to, GlobalPosition = to,
            Relative = to - from, ButtonMask = MouseButtonMask.Left }, true);
        GetViewport().PushInput(new InputEventMouseButton { Position = to, GlobalPosition = to,
            ButtonIndex = MouseButton.Left, Pressed = false }, true);
        await Settle();
        Require(scroll.ScrollVertical >= bar.MaxValue - bar.Page - 2, "Dragging the scrollbar must reach the last content.");
        Require(header.GetGlobalRect() == headerRect, "Header must remain fixed while scrolling.");
        Require(_showcase.IsOverlayVisible, "Scrolling must not dismiss the help.");
        var last = _showcase.GetNode<Control>(name.EndsWith("basics") ? BasicsPath + "/Footer/Minimum" : RanksPath + "/OnePair");
        Require(last.GetGlobalRect().End.Y <= scroll.GetGlobalRect().End.Y + 1, "Last content must be fully reachable.");
        CheckLayout(name + "-bottom");
        await Capture(name + "-bottom");
        scroll.ScrollVertical = 0;
        await Settle();
    }

    private Task Click(Control control) => ClickAt(control.GetGlobalRect().GetCenter());

    private async Task ClickAt(Vector2 position)
    {
        GetViewport().PushInput(new InputEventMouseButton { Position = position, GlobalPosition = position,
            ButtonIndex = MouseButton.Left, Pressed = true }, inLocalCoords: true);
        await Settle();
        GetViewport().PushInput(new InputEventMouseButton { Position = position, GlobalPosition = position,
            ButtonIndex = MouseButton.Left, Pressed = false }, inLocalCoords: true);
        await Settle();
    }

    private void CheckLayout(string context)
    {
        var scroll = _showcase.GetNode<ScrollContainer>("Content/Scroll");
        var pages = _showcase.GetNode<Control>("Content/Scroll/Pages");
        var visible = _showcase.FindChildren("*", "Control", recursive: true, owned: false)
            .Cast<Control>().Where(control => control.IsVisibleInTree() && HasVisibleAlpha(control)
                && control is Label or TextureRect or Button && control.Name != "DismissButton").ToArray();
        foreach (var control in visible)
        {
            var rect = control.GetGlobalRect();
            var bounds = pages.IsAncestorOf(control) ? pages.GetGlobalRect() : new Rect2(0, 0, 1200, 1200);
            if (rect.Position.X < bounds.Position.X - 1 || rect.Position.Y < bounds.Position.Y - 1
                || rect.End.X > bounds.End.X + 1 || rect.End.Y > bounds.End.Y + 1)
                _issues.Add($"{context}: outside viewport {control.GetPath()} {rect}");
            if (control is Label label)
            {
                var font = label.LabelSettings?.Font ?? label.GetThemeFont("font");
                foreach (var rune in label.Text.EnumerateRunes().Where(rune => !Rune.IsWhiteSpace(rune)).Distinct())
                    if (!font.HasChar(rune.Value)) _issues.Add($"{context}: missing glyph U+{rune.Value:X4} at {label.GetPath()}");
                if (label.Text.StartsWith("PokerHelp_")) _issues.Add($"{context}: untranslated key {label.Text}");
                if (label.GetVisibleLineCount() < label.GetLineCount())
                    _issues.Add($"{context}: clipped text {label.GetPath()}");
            }
        }
        for (int i = 0; i < visible.Length; i++)
            for (int j = i + 1; j < visible.Length; j++)
            {
                var first = visible[i]; var second = visible[j];
                if (first is not Label && second is not Label) continue;
                var firstRect = first.GetGlobalRect();
                var secondRect = second.GetGlobalRect();
                if (pages.IsAncestorOf(first)) firstRect = firstRect.Intersection(scroll.GetGlobalRect());
                if (pages.IsAncestorOf(second)) secondRect = secondRect.Intersection(scroll.GetGlobalRect());
                var overlap = firstRect.Intersection(secondRect);
                if (overlap.Size.X > 1 && overlap.Size.Y > 1)
                    _issues.Add($"{context}: overlap {first.GetPath()} / {second.GetPath()} ({overlap.Size})");
            }
    }

    private static bool HasVisibleAlpha(CanvasItem item)
    {
        for (CanvasItem current = item; current != null; current = current.GetParent() as CanvasItem)
            if (current.Modulate.A < .01f) return false;
        return true;
    }

    private async Task Capture(string locale)
    {
        if (DisplayServer.GetName() == "headless") return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string directory = ProjectSettings.GlobalizePath(OS.GetCmdlineUserArgs().Contains("--poker-help-layout")
            ? "res://../.local-build/poker-help/screenshots-layout"
            : OS.GetCmdlineUserArgs().Contains("--poker-help-markers")
            ? "res://../.local-build/poker-help/screenshots-markers"
            : OS.GetCmdlineUserArgs().Contains("--poker-help-flush")
            ? "res://../.local-build/poker-help/screenshots-spade"
            : "res://../.local-build/poker-help/screenshots-draw");
        System.IO.Directory.CreateDirectory(directory);
        var error = GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(directory, locale + ".png"));
        Require(error == Error.Ok, $"Screenshot failed: {error}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
