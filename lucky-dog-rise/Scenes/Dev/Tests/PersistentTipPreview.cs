#if DEBUG
using Godot;
using System;
using System.IO;
using System.Threading.Tasks;

namespace LuckyDogRise;

public partial class PersistentTipPreview : Node
{
    [Export] private GameManager _game = null!;
    private static void Require(bool ok, string reason)
    {
        if (!ok) throw new InvalidOperationException(reason);
        GD.Print("[PersistentTip] PASS: " + reason);
    }
    public override void _EnterTree()
    {
        const string sandbox = "LuckyDogRise/Tests/PersistentTips";
        ProjectSettings.SetSetting("application/config/use_custom_user_dir", true);
        ProjectSettings.SetSetting("application/config/custom_user_dir_name", sandbox);
        Require(ProjectSettings.GlobalizePath("user://").Replace('\\', '/').EndsWith(sandbox + "/"), "Isolated test storage");
        Directory.CreateDirectory(ProjectSettings.GlobalizePath("user://"));
        SettingsManager.SaveSaveDataMode(SettingsManager.SaveDataMode.LocalSave);
        L10n.SetLocale("zh_CN", save: false);
    }

    public override async void _Ready()
    {
        try
        {
            GetWindow().ContentScaleSize = new Vector2I(1200, 1200);
            GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
            GetWindow().Size = new Vector2I(600, 600);
            var storage = AccountStorageContext.ForDevelopment("tips-" + Guid.NewGuid().ToString("N"));
            var account = new AccountStateManager(storage);
            var data = new GameData();
            data.ConfigureStorage(storage);
            AddChild(data);
            _game.GameData = data;
            _game.AddChips(1000);
            _game.PersistentTips = new PersistentTipProgress(account.LoadPersistentTip, account.SavePersistentTip);
            _game.SetInteractionHintPokerModeActive(true);
            string directory = ProjectSettings.GlobalizePath("res://../.local-build/persistent-tips");
            Directory.CreateDirectory(directory);
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--help-bar-completion-only") >= 0)
            {
                await Wait(.1);
                CheckHelpBarCompletion();
                GD.Print("[PersistentTip] HELP BAR COMPLETION PASS");
                GetTree().Quit();
                return;
            }
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--help-bar-tip-only") >= 0)
            {
                await Wait(.3);
                await CheckHelpBarTip(account, directory);
                CheckHelpBarCompletion();
                GD.Print("[PersistentTip] HELP BAR ONLY PASS");
                GetTree().Quit();
                return;
            }
            var tip = _game.GetNode<PersistentTipController>("PersistentTips/DrawTip");
            var panel = tip.GetNode<Control>("Panel");
            await Wait(.3);
            Require(!tip.Visible, "No note on entering poker");
            _game.GetNode<ChipStackController>("ChipStack").EmitSignal(ChipStackController.SignalName.BetRequested);
            await Wait(1.5);
            Require(_game.State == GameState.Dealt && !tip.Visible, "Dealing alone does not trigger the note");
            _game.GetNode<CardTableController>("CardArea").EmitSignal(CardTableController.SignalName.CardClicked, 0);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Require(panel.Size.Y <= 295 && panel.GetGlobalRect().Position.Y >= 735,
                "First rendered frame has no stretched background: " + panel.GetGlobalRect());
            await Wait(.4);
            Require(tip.Visible && account.LoadPersistentTip(PersistentTipProgress.PokerDraw) == PersistentTipState.Active,
                "First card flip activates and immediately saves the note");
            Require(panel.Size.Y <= 295 && panel.GetGlobalRect().Position.Y >= 735,
                "First appearance fits without a locale refresh: " + panel.GetGlobalRect());
            _game.PersistentTips = new PersistentTipProgress(account.LoadPersistentTip, account.SavePersistentTip);
            _game.SetInteractionHintPokerModeActive(false);
            Require(!tip.Visible, "Desktop mode hides without acknowledging");
            _game.SetInteractionHintPokerModeActive(true);
            Require(tip.Visible, "Recreated progress and returning to poker restores unread note");
            _game.RequestPokerWelcome(true);
            Require(!tip.Visible, "Welcome overlay suppresses the note");
            _game.RequestPokerWelcome(false);
            Require(tip.Visible, "Closing overlay restores the note");
            _game.TogglePokerHandShowcase();
            Require(!tip.Visible, "Help suppresses the note");
            _game.TogglePokerHandShowcase();
            Require(tip.Visible, "Closing help restores unread note");

            await CheckHelpBarTip(account, directory);
            CheckHelpBarCompletion();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, "first-show-zh_CN.png"));
            foreach (string locale in new[] { "en", "zh_CN", "zh_TW", "ja", "es_ES", "es_419", "pt_BR", "pt_PT", "fr", "de", "da", "id", "nb", "sv", "nl", "vi", "ms", "ko", "ru", "uk" })
            {
                L10n.SetLocale(locale, save: false);
                // Fresh prefab: no second locale event to accidentally repair startup sizing.
                var fresh = GD.Load<PackedScene>("res://Scenes/Prefabs/PersistentTip.tscn").Instantiate<PersistentTipController>();
                fresh.Position = new Vector2(24, 1030);
                _game.GetNode("PersistentTips").AddChild(fresh);
                await Wait(.1);
                var previewProgress = new PersistentTipProgress();
                previewProgress.Activate(PersistentTipProgress.PokerDraw);
                fresh.SetContext(previewProgress, true);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var freshPanel = fresh.GetNode<Control>("Panel");
                Require(freshPanel.Size.Y <= 295 && freshPanel.GetGlobalRect().Position.Y >= 735,
                    locale + ": fresh instance first rendered frame fits " + freshPanel.GetGlobalRect());
                fresh.QueueFree();
                await Wait(.12);
                var rect = panel.GetGlobalRect();
                Require(rect.Position.X >= 0 && rect.End.X <= 270 && rect.Position.Y >= 735 && rect.End.Y <= 1031,
                    locale + ": fits between dog, cards and arm " + rect);
                foreach (var label in new[] { tip.GetNode<Label>("Panel/Content/Message"), tip.GetNode<Label>("Panel/Content/DismissLabel") })
                    Require(rect.Encloses(label.GetGlobalRect()) && label.GetVisibleLineCount() == label.GetLineCount(), locale + ": text fully visible");
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, locale + ".png"));
            }
            L10n.SetLocale("zh_CN", save: false);
            _game.GetNode<HandAreaController>("HandArea").EmitSignal(HandAreaController.SignalName.FirstKnockLanded);
            await Wait(2);
            Require(tip.Visible, "Drawing and settling never dismiss the note");
            var before = _game.State;
            var point = panel.GetGlobalRect().GetCenter();
            Require(_game.IsPointOverPersistentTip(point), "Host can exclude the note from window dragging");
            GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
            foreach (bool pressed in new[] { true, false })
            {
                GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
                await Wait(.05);
            }
            Require(!tip.Visible && _game.State == before, "Actual note click dismisses without operating the poker table");
            var reloaded = new PersistentTipProgress(account.LoadPersistentTip, account.SavePersistentTip);
            reloaded.Activate(PersistentTipProgress.PokerDraw);
            Require(reloaded.GetState(PersistentTipProgress.PokerDraw) == PersistentTipState.Dismissed, "Dismissal persists and never reactivates");
            var other = new AccountStateManager(AccountStorageContext.ForDevelopment("other-" + Guid.NewGuid().ToString("N")));
            Require(other.LoadPersistentTip(PersistentTipProgress.PokerDraw) == PersistentTipState.Unseen, "Other accounts are independent");
            var session = new PersistentTipProgress();
            session.Activate(PersistentTipProgress.PokerDraw);
            session.Dismiss(PersistentTipProgress.PokerDraw);
            Require(account.LoadPersistentTip(PersistentTipProgress.PokerDraw) == PersistentTipState.Dismissed
                && other.LoadPersistentTip(PersistentTipProgress.PokerDraw) == PersistentTipState.Unseen, "Sandbox never writes account state");
            var failed = new PersistentTipProgress(save: (_, _) => throw new IOException("simulated"));
            try { failed.Activate(PersistentTipProgress.PokerDraw); } catch (IOException) { }
            Require(failed.GetState(PersistentTipProgress.PokerDraw) == PersistentTipState.Unseen, "Failed save remains retryable");
            GD.Print("[PersistentTip] ALL PASS");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private async Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private async Task CheckHelpBarTip(AccountStateManager account, string directory)
    {
        var bar = _game.GetNode<PokerQuickBarController>("PokerQuickBar");
        var note = _game.GetNode<PersistentTipController>("PersistentTips/HelpBarTip");
        var draw = _game.GetNode<PersistentTipController>("PersistentTips/DrawTip");
        bool drawWasVisible = draw.Visible;
        void Advance(double seconds, bool pressed = false) =>
            _game.UpdateQuickBarPointer(true, new Vector2(1100, 700), false, pressed, seconds);

        bar.RevealAfterWelcome();
        Advance(5.1);
        Require(!note.Visible, "Welcome reveal alone does not activate help bar note");
        _game.UpdateQuickBarPointer(true, new Vector2(300, 40), false, false, .01);
        Require(bar.IsExpanded && !note.Visible, "Hover reveal alone does not activate help bar note");
        _game.SetPokerQuickBarUsed(false);
        _game.RequestPokerWelcome(true);
        Advance(60);
        Require(!note.Visible && !bar.IsIdleReminderActive, "Welcome pauses help bar discovery");
        _game.RequestPokerWelcome(false);
        Advance(19.9);
        Require(!note.Visible, "No note before twenty seconds");
        Advance(.2, pressed: true);
        Require(!note.Visible, "Idle threshold waits for mouse release");
        Advance(0);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Require(bar.IsExpanded && note.Visible && draw.Visible == drawWasVisible, "Idle reveal activates help note independently");
        Require(account.LoadPersistentTip(PersistentTipProgress.PokerHelpBar) == PersistentTipState.Active,
            "Help bar activation is saved immediately");
        foreach (string locale in new[] { "zh_CN", "en" })
        {
            L10n.SetLocale(locale, save: false);
            await Wait(.25);
            var rect = note.GetNode<Control>("Panel").GetGlobalRect();
            Require(rect.Position.Y >= bar.GetNode<Control>("Clip/Bar").Size.Y && rect.Position.Y <= 108 && rect.End.Y < 500 && rect.End.X <= 404,
                locale + ": help note sits just below the bar without stretching");
            if (drawWasVisible)
                Require(!rect.Intersects(draw.GetNode<Control>("Panel").GetGlobalRect()), locale + ": notes do not overlap");
            Require(_game.IsPointOverPersistentTip(rect.GetCenter()), locale + ": help note is protected from host dragging");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, "help-bar-" + locale + ".png"));
        }
        _game.GetNode<InteractionHintController>("InteractionHints").NotifyInteractionHandled();
        Advance(.51);
        Require(!bar.IsExpanded && note.Visible, "Effective play retracts the bar but preserves unread help note");
        _game.PersistentTips = new PersistentTipProgress(account.LoadPersistentTip, account.SavePersistentTip);
        _game.SetInteractionHintPokerModeActive(false);
        Require(!note.Visible, "Help note hides in desktop mode");
        _game.SetInteractionHintPokerModeActive(true);
        Require(note.Visible, "Help note restores from disk on return");
        _game.TogglePokerHandShowcase();
        Require(!note.Visible, "Help overlay hides help note");
        _game.TogglePokerHandShowcase();
        Require(note.Visible, "Closing help restores help note");
        // Click the part overlapping the discovery hot zone, not just the unobstructed footer.
        var point = note.GetNode<Control>("Panel").GetGlobalRect().Position + new Vector2(20, 20);
        GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
        foreach (bool pressed in new[] { true, false })
        {
            GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
            await Wait(.05);
        }
        Require(!note.Visible && draw.Visible == drawWasVisible, "Click in discovery hot zone closes only the help note");
        Require(account.LoadPersistentTip(PersistentTipProgress.PokerHelpBar) == PersistentTipState.Dismissed,
            "Help note dismissal persists independently");
        Advance(20.1);
        Require(bar.IsIdleReminderActive && !note.Visible, "Later idle reminder does not reopen dismissed note");
        bar.NotifyEffectiveInteraction();
        bar.Dismiss();
        L10n.SetLocale("zh_CN", save: false);
    }

    private void CheckHelpBarCompletion()
    {
        var originalProgress = _game.PersistentTips;
        var bar = _game.GetNode<PokerQuickBarController>("PokerQuickBar");
        var note = _game.GetNode<PersistentTipController>("PersistentTips/HelpBarTip");
        int desktopRequests = 0;
        void OnDesktopRequested() => desktopRequests++;
        _game.DesktopModeRequested += OnDesktopRequested;

        AccountStateManager Prepare(bool active)
        {
            _game.SetInteractionHintPokerModeActive(false);
            var account = new AccountStateManager(AccountStorageContext.ForDevelopment("tip-completion-" + Guid.NewGuid().ToString("N")));
            if (active) account.SavePersistentTip(PersistentTipProgress.PokerHelpBar, PersistentTipState.Active);
            // Only seed the arm state to detect accidental completion; no arm/card animation regression.
            account.SavePersistentTip(PersistentTipProgress.PokerDraw, PersistentTipState.Active);
            _game.PersistentTips = new PersistentTipProgress(account.LoadPersistentTip, account.SavePersistentTip);
            _game.SetPokerQuickBarUsed(false);
            _game.SetInteractionHintPokerModeActive(true);
            return account;
        }

        void RequireCompleted(AccountStateManager account, string reason)
        {
            var reloaded = new PersistentTipProgress(account.LoadPersistentTip, account.SavePersistentTip);
            reloaded.Activate(PersistentTipProgress.PokerHelpBar);
            Require(!note.Visible && reloaded.GetState(PersistentTipProgress.PokerHelpBar) == PersistentTipState.Dismissed,
                reason + ": completed on disk and cannot reactivate");
            Require(account.LoadPersistentTip(PersistentTipProgress.PokerDraw) == PersistentTipState.Active,
                reason + ": arm note still requires manual acknowledgment");
        }

        try
        {
            foreach (bool active in new[] { false, true })
            {
                var account = Prepare(active);
                account.SavePokerQuickBarUsed();
                _game.SetPokerQuickBarUsed(account.LoadPokerQuickBarUsed());
                _game.UpdateQuickBarPointer(true, new Vector2(1100, 700), false, false, 60);
                Require(!bar.IsIdleReminderActive, "Existing toolbar use disables the idle reminder");
                RequireCompleted(account, active ? "Existing account with unread note" : "Existing account with unseen note");
                _game.SetPokerQuickBarUsed(false);
                RequireCompleted(account, "Discovery reset does not undo completed note");
            }

            foreach (string button in new[] { "Help", "Tutorial", "DesktopPet" })
            foreach (bool active in new[] { false, true })
            {
                var account = Prepare(active);
                int beforeDesktop = desktopRequests;
                _game.RequestPokerWelcome(true);
                bar.GetNode<Button>("Clip/Bar/Buttons/" + button).EmitSignal(Button.SignalName.Pressed);
                Require(!_game.IsPokerWelcomeVisible, button + ": toolbar action still acknowledges welcome");
                RequireCompleted(account, button + (active ? " with unread note" : " before note activation"));
                Require(button == "Help" ? _game.IsPokerHandShowcaseVisible
                    : button == "Tutorial" ? _game.IsTutorialOverlayVisible : desktopRequests == beforeDesktop + 1,
                    button + ": requested function still executes");
                _game.SetInteractionHintPokerModeActive(false);
                _game.SetInteractionHintPokerModeActive(true);
                RequireCompleted(account, button + " after returning to poker");
            }

            var untouched = Prepare(true);
            _game.UpdateQuickBarPointer(true, new Vector2(300, 40), false, false, .01);
            Require(untouched.LoadPersistentTip(PersistentTipProgress.PokerHelpBar) == PersistentTipState.Active,
                "Hover alone does not complete the note");
            _game.RequestPokerWelcome(true);
            _game.GetNode<Button>("PokerWelcomeDialog/InputShield/Center/Panel/Margin/Content/Confirm").EmitSignal(Button.SignalName.Pressed);
            Require(untouched.LoadPersistentTip(PersistentTipProgress.PokerHelpBar) == PersistentTipState.Active,
                "Welcome acknowledgment does not complete the note");
            _game.TogglePokerHandShowcase();
            _game.TogglePokerHandShowcase();
            Require(note.Visible && untouched.LoadPersistentTip(PersistentTipProgress.PokerHelpBar) == PersistentTipState.Active,
                "Paytable help does not complete the note");
        }
        finally
        {
            _game.DesktopModeRequested -= OnDesktopRequested;
            _game.RequestPokerWelcome(false);
            _game.SetInteractionHintPokerModeActive(false);
            _game.PersistentTips = originalProgress;
            _game.SetPokerQuickBarUsed(false);
            bar.Dismiss();
            _game.SetInteractionHintPokerModeActive(true);
        }
    }
}
#endif
