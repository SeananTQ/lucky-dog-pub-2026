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

            string directory = ProjectSettings.GlobalizePath("res://../.local-build/persistent-tips");
            Directory.CreateDirectory(directory);
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
}
#endif
