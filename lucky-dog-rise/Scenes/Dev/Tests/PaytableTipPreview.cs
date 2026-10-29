#if DEBUG
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace LuckyDogRise;

/// <summary>Paytable behavior checks, plus a layout-only pass for both new notes; no arm-note regression.</summary>
public partial class PaytableTipPreview : Node
{
    [Export] private GameManager _game = null!;
    [Export] private InfoPanelController _info = null!;
    [Export] private SubViewportContainer _play = null!;

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
        GD.Print("[PaytableTip] PASS: " + reason);
    }

    public override void _EnterTree()
    {
        const string sandbox = "LuckyDogRise/Tests/PaytableTip";
        ProjectSettings.SetSetting("application/config/use_custom_user_dir", true);
        ProjectSettings.SetSetting("application/config/custom_user_dir_name", sandbox);
        Require(ProjectSettings.GlobalizePath("user://").Replace('\\', '/').EndsWith(sandbox + "/"), "Isolated storage");
        Directory.CreateDirectory(ProjectSettings.GlobalizePath("user://"));
        SettingsManager.SaveSaveDataMode(SettingsManager.SaveDataMode.LocalSave);
        L10n.SetLocale("zh_CN", save: false);
    }

    public override async void _Ready()
    {
        try
        {
            GetWindow().ContentScaleSize = new Vector2I(846, 600);
            GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
            GetWindow().Size = new Vector2I(846, 600);
            _play.Position = new Vector2(246, 0);
            _play.GetNode<SubViewport>("SubViewport").RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
            var storage = AccountStorageContext.ForDevelopment("paytable-" + Guid.NewGuid().ToString("N"));
            var account = new AccountStateManager(storage);
            var data = new GameData();
            data.ConfigureStorage(storage);
            AddChild(data);
            _game.GameData = data;
            _game.AddChips(1000);
            _game.PersistentTips = new PersistentTipProgress(account.LoadPersistentTip, account.SavePersistentTip);
            _game.PaytableTip = _info.PaytableTip;
            _info.Bind(data);
            _info.PaytableRequested += _game.TogglePokerHandShowcase;
            _game.SetInteractionHintPokerModeActive(true);
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--new-tip-localization") >= 0)
            {
                await CheckNewTipLocalization();
                GD.Print("[PaytableTip] NEW TIP LOCALIZATION PASS");
                GetTree().Quit();
                return;
            }
            var note = _info.PaytableTip;
            var panel = note.GetNode<Control>("Panel");
            var payoutPanel = _info.GetNode<Control>("Panel/RootVBox/PayoutPanel");
            var paytableButton = payoutPanel.GetNode<Button>("PaytableBtn");
            await Wait(.25);
            Require(!note.Visible, "Entering poker does not show the note");

            await Settle(new[] { 8, 21, 3, 18, 51 }); // Pair: a scored hand must not activate it.
            Require(_game.State == GameState.Settled && !note.Visible, "Winning settlement does not activate the note");
            ((ChipRewardController)_game.PendingReward).EmitSignal(ChipRewardController.SignalName.Collected);
            await Wait(.6);
            await Settle(new[] { 0, 15, 30, 46, 8 }); // First zero payout may follow earlier wins.
            Require(note.Visible && account.LoadPersistentTip(PersistentTipProgress.PokerPaytable) == PersistentTipState.Active,
                "First zero-payout settlement activates and saves the note");

            string directory = ProjectSettings.GlobalizePath("res://../.local-build/persistent-tips");
            Directory.CreateDirectory(directory);
            foreach (string locale in new[] { "zh_CN", "en" })
            {
                L10n.SetLocale(locale, save: false);
                await Wait(.15);
                Require(payoutPanel.GetGlobalRect().Encloses(panel.GetGlobalRect()), locale + ": note fits within the paytable");
                foreach (var label in new[] { note.GetNode<Label>("Panel/Content/Message"), note.GetNode<Label>("Panel/Content/DismissLabel") })
                    Require(panel.GetGlobalRect().Encloses(label.GetGlobalRect()) && label.GetLineCount() == label.GetVisibleLineCount(),
                        locale + ": text fully visible");
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, "paytable-" + locale + ".png"));
            }
            _info.SetRenderScale(.75f);
            _info.SetPanelPosition(new Vector2(650, 0));
            await Wait(.1);
            Require(payoutPanel.GetGlobalRect().Encloses(panel.GetGlobalRect()), "Note follows panel scale and relocation");
            _info.SetPanelPosition(Vector2.Zero);
            _info.SetRenderScale(1);
            _game.SetInteractionHintPokerModeActive(false);
            Require(!note.Visible, "Desktop mode hides the note without completing it");
            _game.PersistentTips = new PersistentTipProgress(account.LoadPersistentTip, account.SavePersistentTip);
            _game.SetInteractionHintPokerModeActive(true);
            Require(note.Visible, "Unread note restores from disk");
            _game.RequestPokerWelcome(true);
            Require(!note.Visible, "Welcome temporarily hides the note");
            _game.TogglePokerHandShowcase();
            Require(account.LoadPersistentTip(PersistentTipProgress.PokerPaytable) == PersistentTipState.Active,
                "Blocked paytable action does not complete the note");
            _game.RequestPokerWelcome(false);
            Require(note.Visible, "Closing welcome restores the note");

            await Click(panel.GetGlobalRect().GetCenter());
            Require(!note.Visible && !_game.IsPokerHandShowcaseVisible
                && account.LoadPersistentTip(PersistentTipProgress.PokerPaytable) == PersistentTipState.Dismissed,
                "Note click acknowledges without clicking through to help");
            await Settle(new[] { 0, 15, 30, 46, 8 });
            Require(!note.Visible, "Later zero payout does not reopen a completed note");

            foreach (bool active in new[] { false, true })
            {
                var progress = new PersistentTipProgress();
                if (active) progress.Activate(PersistentTipProgress.PokerPaytable);
                _game.PersistentTips = progress;
                _game.SetInteractionHintPokerModeActive(true);
                // An exposed part of the paytable, above the sticky note.
                await Click(paytableButton.GetGlobalRect().Position + new Vector2(15, 15));
                Require(_game.IsPokerHandShowcaseVisible && progress.GetState(PersistentTipProgress.PokerPaytable) == PersistentTipState.Dismissed,
                    "Paytable click opens ranks and completes " + (active ? "active" : "unseen") + " note");
                Require(progress.GetState(PersistentTipProgress.PokerHelpBar) == PersistentTipState.Unseen
                    && progress.GetState(PersistentTipProgress.PokerDraw) == PersistentTipState.Unseen,
                    "Paytable action leaves other note records untouched");
                _game.TogglePokerHandShowcase();
                progress.Activate(PersistentTipProgress.PokerPaytable);
                Require(!note.Visible && progress.GetState(PersistentTipProgress.PokerPaytable) == PersistentTipState.Dismissed,
                    "Returning from ranks keeps paytable note completed");
            }
            GD.Print("[PaytableTip] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task Settle(int[] hand)
    {
        bool wasVisible = _info.PaytableTip.Visible;
        _game.GetNode<ChipStackController>("ChipStack").EmitSignal(ChipStackController.SignalName.BetRequested);
        await Wait(1.2);
        Require(_game.State == GameState.Dealt && _info.PaytableTip.Visible == wasVisible, "Bet and deal alone do not activate a note");
        // Control the fixture's cards, not the settlement outcome; execute the production evaluator and signal chain.
        var deck = (DeckManager)typeof(GameManager).GetField("_deck", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_game)!;
        Array.Copy(hand, deck.CurrentHand, 5);
        Array.Copy(hand, deck.FinalHand, 5);
        _game.GetNode<CardTableController>("CardArea").DealCards(hand);
        _game.GetNode<HandAreaController>("HandArea").EmitSignal(HandAreaController.SignalName.FirstKnockLanded);
        await Wait(.8);
    }

    private async Task Click(Vector2 point)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
        foreach (bool pressed in new[] { true, false })
        {
            GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
            await Wait(.05);
        }
    }

    private async Task CheckNewTipLocalization()
    {
        string directory = ProjectSettings.GlobalizePath("res://../.local-build/persistent-tips/localization");
        Directory.CreateDirectory(directory);
        var help = _game.GetNode<PersistentTipController>("PersistentTips/HelpBarTip");
        var paytable = _info.PaytableTip;
        var payoutPanel = _info.GetNode<Control>("Panel/RootVBox/PayoutPanel");
        var bar = _game.GetNode<PokerQuickBarController>("PokerQuickBar");
        var measurements = new List<object>();
        string[] locales = { "en", "zh_CN", "zh_TW", "ja", "es_ES", "es_419", "pt_BR", "pt_PT", "fr", "de",
            "da", "id", "nb", "sv", "nl", "vi", "ms", "ko", "ru", "uk" };
        string localeArgument = Array.Find(OS.GetCmdlineUserArgs(), argument => argument.StartsWith("--tip-locale="));
        string selectedLocale = localeArgument?["--tip-locale=".Length..];
        if (selectedLocale != null)
        {
            Require(Array.IndexOf(locales, selectedLocale) >= 0, "Selected locale is supported");
            locales = new[] { selectedLocale };
        }
        await Wait(.2);
        foreach (string locale in locales)
        {
            _game.SetInteractionHintPokerModeActive(false);
            L10n.SetLocale(locale, save: false);
            var progress = new PersistentTipProgress();
            progress.Activate(PersistentTipProgress.PokerHelpBar);
            progress.Activate(PersistentTipProgress.PokerPaytable);
            _game.PersistentTips = progress;
            _game.SetInteractionHintPokerModeActive(true);
            bar.RevealAfterWelcome();
            // Check the first rendered appearance as well as the stable layout.
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            CheckBounds();
            await Wait(.2);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            CheckBounds();
            var screenshot = GetViewport().GetTexture().GetImage();
            screenshot.SavePng(Path.Combine(directory, locale + ".png"));
            foreach (var (name, note) in new[] { ("help-bar", help), ("paytable", paytable) })
            {
                var panel = note.GetNode<Control>("Panel");
                var message = note.GetNode<Label>("Panel/Content/Message");
                var dismiss = note.GetNode<Label>("Panel/Content/DismissLabel");
                foreach (var label in new[] { message, dismiss })
                    Require(panel.GetGlobalRect().Encloses(label.GetGlobalRect())
                        && label.GetLineCount() == label.GetVisibleLineCount(), locale + " " + name + ": text fully visible");
                Rect2 rect = panel.GetGlobalRect();
                if (name == "help-bar")
                    rect = new Rect2(_play.Position + rect.Position * _play.Scale, rect.Size * _play.Scale);
                var crop = new Rect2I((Vector2I)rect.Position - new Vector2I(3, 3), (Vector2I)rect.Size + new Vector2I(6, 6));
                screenshot.GetRegion(crop).SavePng(Path.Combine(directory, locale + "-" + name + ".png"));
                measurements.Add(new { locale, note = name, width = rect.Size.X, height = rect.Size.Y,
                    lines = message.GetLineCount(), dismissLines = dismiss.GetLineCount(), text = message.Text });
            }

            void CheckBounds()
            {
                var helpRect = help.GetNode<Control>("Panel").GetGlobalRect();
                var paytableRect = paytable.GetNode<Control>("Panel").GetGlobalRect();
                Require(help.Visible && paytable.Visible && helpRect.Position.Y >= 104 && helpRect.End.Y < 500,
                    locale + ": help note stays below toolbar without stretching");
                Require(payoutPanel.GetGlobalRect().Encloses(paytableRect)
                    && paytableRect.Position.Y >= payoutPanel.GetGlobalRect().Position.Y + 50,
                    locale + ": paytable note fits and leaves an exposed click target");
            }
        }
        string reportName = selectedLocale == null ? "layout.json" : "layout-" + selectedLocale + ".json";
        File.WriteAllText(Path.Combine(directory, reportName), JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
    }
    private async Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
}
#endif
