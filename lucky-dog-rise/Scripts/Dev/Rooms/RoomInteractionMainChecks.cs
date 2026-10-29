#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

namespace LuckyDogRise.Rooms;

// Real main scene, registered overlays, and memory-only room/platform providers.
internal static class RoomInteractionMainChecks
{
    public static async Task Run(ModeManager main, InGameRoomPreview page)
    {
        var tree = main.GetTree();
        var panel = main.SettingsPanelObj;
        var client = page.CurrentClient;
        var remote = page.MockClientForSmoke(2);
        var proposal = panel.GetNode<RoomGameProposalOverlay>("RoomGameProposalOverlay");
        async Task Wait(double seconds = .12) => await main.ToSignal(tree.CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        async Task Step(double seconds = .1) { page.AdvancePreview(seconds); await Wait(); }
        static void Click(Button button) => button.EmitSignal(BaseButton.SignalName.Pressed);
        async Task Propose(string mode = "work")
        {
            Check(remote.ProposeGameChange(mode) == "ok", "remote proposal submitted");
            await Step();
        }
        async Task Cancel()
        {
            if (remote.View?.GameChange != null) remote.CancelGameChange(remote.View.GameChange.Id);
            await Step();
            panel.CloseImmediate();
        }
        panel.CloseImmediate();
        Check(!UiInteractionOccupancy.IsOccupied(main), "ordinary room page does not occupy interaction");
        CheckSourceLifetimes(main);
        remote.Create("interaction coordination");
        await Step(.5);
        Check(client.Join(remote.JoinedCode), "join request accepted");
        await Step(.5);
        Check(client.JoinedCode == remote.JoinedCode, "local player joins remote host");
        main.RoomModeForSmoke(true);
        await Wait(.8);
        var game = main.GetNode<GameManager>("PlayRoot/SubViewportContainer/SubViewport/Main");
        var welcome = game.GetNode<PokerWelcomeDialogController>("PokerWelcomeDialog");
        var confirm = welcome.GetNode<Button>("InputShield/Center/Panel/Margin/Content/Confirm");
        var bar = game.GetNode<PokerQuickBarController>("PokerQuickBar");
        Check(welcome.Visible, "natural first-entry welcome visible");
        await Propose();
        double remaining = client.GameChangeSecondsRemaining;
        await Step(10);
        Check(welcome.Visible && !proposal.Visible && !panel.IsOpen && panel.HasPendingRoomGameProposal,
            "welcome retains pending vote without opening the system panel");
        Check(client.GameChangeSecondsRemaining < remaining - 9, "waiting does not pause the room deadline");
        await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string folder = ProjectSettings.GlobalizePath("res://../.local-build/room-lab");
        Directory.CreateDirectory(folder);
        main.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder, "room-welcome-defers-vote.png"));
        Click(confirm);
        await Wait(.25);
        Check(!welcome.Visible && proposal.Visible, "acknowledgement releases pending vote");
        game.RequestPokerWelcome(true);
        await Wait();
        Check(!welcome.Visible && proposal.Visible, "already visible vote defers a new welcome request");
        await Cancel();
        await Wait();
        Check(welcome.Visible, "deferred welcome resumes without being acknowledged automatically");

        await Propose();
        Click(bar.GetNode<Button>("Clip/Bar/Buttons/Help"));
        await Wait(.2);
        Check(!welcome.Visible && game.IsPokerHandShowcaseVisible && !proposal.Visible,
            "welcome-to-help handoff keeps the vote waiting");
        game.TogglePokerHandShowcase();
        await Wait(.25);
        Check(proposal.Visible, "closing help releases the pending vote");
        await Cancel();

        game.RequestPokerWelcome(true);
        await Propose();
        await Step(61);
        Check(welcome.Visible && client.View.GameChange == null && !panel.HasPendingRoomGameProposal,
            "expired waiting vote clears while welcome stays visible");
        Click(confirm);
        await Wait();
        Check(!proposal.Visible && !panel.IsOpen, "expired vote never appears after acknowledgement");

        game.RequestPokerWelcome(true);
        await Propose();
        await Cancel();
        Click(confirm);
        await Wait();
        Check(!proposal.Visible && !panel.HasPendingRoomGameProposal, "host cancellation does not leave a delayed popup");

        game.RequestPokerWelcome(true);
        await Propose();
        await Step(1.2);
        Check(remote.ConfirmGameChange(remote.View.GameChange.Id) == "ok", "host and companions can finish while a guest is reading");
        await Step();
        Click(confirm);
        await Wait();
        Check(client.View.GameId == "work" && !proposal.Visible, "completed proposal follows normal room rules without a stale popup");
        await Propose("social");
        await Step(1.2);
        remote.ConfirmGameChange(remote.View.GameChange.Id);
        await Step();
        panel.CloseImmediate();

        var first = NewConfirm(main);
        var second = NewConfirm(main);
        first.ShowConfirm("First", "Test", "OK", "Cancel");
        second.ShowConfirm("Second", "Test", "OK", "Cancel");
        await Propose();
        Check(!proposal.Visible, "new common confirmation dialogs automatically defer votes");
        first.Hide();
        await Wait();
        Check(!proposal.Visible && UiInteractionOccupancy.IsOccupied(main), "closing one source cannot release another");
        second.Free();
        first.Free();
        await Wait(.25);
        Check(proposal.Visible, "destroying the final source releases the pending vote");
        await Cancel();

        var tips = (PersistentTipProgress)typeof(GameManager).GetField("_sandboxTips", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
        tips.Activate(PersistentTipProgress.PokerDraw);
        var draw = game.GetNode<PersistentTipController>("PersistentTips/DrawTip");
        await Wait();
        Check(draw.Visible && !UiInteractionOccupancy.IsOccupied(main), "persistent note alone does not block interaction");
        var guide = game.GetNode<TutorialManager>("TutorialManager");
        guide.ShowOnce();
        await Wait();
        await Propose();
        Check(guide.Visible && !proposal.Visible && !draw.Visible, "manual controls defer vote and temporarily hide notes");
        await Wait(2.1);
        guide.GetNode<Control>("OverlayInput").EmitSignal(Control.SignalName.GuiInput,
            new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        await Wait(.25);
        Check(!guide.Visible && proposal.Visible && draw.Visible, "closing controls restores unfinished notes and the vote");
        await Cancel();

        // Prepare a view fixture, without issuing or claiming an inventory reward.
        var pending = new PendingBlindBoxReward { BlindBoxId = 1001, RewardShown = true,
            ItemId = LubanData.Tables.TbItem.DataList.First(i => i.ItemType == DataTables.EItemType.Headwear).Id };
        var pendingProperty = typeof(GameData).GetProperty(nameof(GameData.PendingBlindBoxReward));
        pendingProperty.SetValue(game.GameData, pending);
        await Propose();
        Check(!proposal.Visible, "pending reward blocks vote before the visual shell is opened");
        game.ShowPendingBlindBoxReward(pending);
        await Wait();
        Check(!draw.Visible && !proposal.Visible, "reward display hides notes and keeps the vote waiting");
        game.HidePendingBlindBoxReward();
        await Wait();
        Check(!proposal.Visible, "hiding the shell cannot release an unfinished reward transaction");
        pendingProperty.SetValue(game.GameData, null);
        await Wait(.25);
        Check(proposal.Visible && draw.Visible, "finishing the reward releases vote and restores notes");
        await Cancel();

        main.RoomModeForSmoke(false);
        await Wait(.4);
        Check(!draw.Visible && !UiInteractionOccupancy.IsOccupied(main), "mode switch releases hidden poker UI without completing the note");
        main.RoomModeForSmoke(true);
        await Wait(.4);
        Check(draw.Visible && !welcome.Visible, "return to poker restores note without replaying welcome");
        game.RequestPokerWelcome(true);
        await Propose();
        client.Leave();
        await Step();
        Click(confirm);
        await Wait();
        Check(!proposal.Visible && !panel.HasPendingRoomGameProposal, "leaving the room discards its deferred vote");
        remote.Leave();
        await Step();
        GD.Print("[RoomInteractionMainChecks] ALL PASS shared sources, lifecycle, welcome handoff, reverse order, deadline/cancel/commit/leave, notes and reward handoff (Mock).");
    }

    private static ConfirmOverlayController NewConfirm(Node parent)
    {
        var dialog = GD.Load<PackedScene>("res://Scenes/Prefabs/ConfirmOverlay.tscn").Instantiate<ConfirmOverlayController>();
        parent.AddChild(dialog);
        return dialog;
    }

    private static void CheckSourceLifetimes(Node parent)
    {
        var container = new Node2D();
        parent.AddChild(container);
        var layer = new CanvasLayer();
        container.AddChild(layer);
        using var registration = UiInteractionOccupancy.TrackVisibility(layer);
        Check(UiInteractionOccupancy.IsOccupied(parent), "custom overlay registration needs no room-specific condition");
        container.Hide();
        Check(!UiInteractionOccupancy.IsOccupied(parent), "hidden logical parent releases a CanvasLayer source");
        container.Show();
        Check(UiInteractionOccupancy.IsOccupied(parent), "showing the logical parent restores occupancy");
        registration.Dispose();
        registration.Dispose();
        Check(!UiInteractionOccupancy.IsOccupied(parent), "explicit release is idempotent");
        container.Free();
        var dialog = NewConfirm(parent);
        dialog.ShowConfirm("Lifecycle", "Test", "OK", "Cancel");
        parent.RemoveChild(dialog);
        Check(!UiInteractionOccupancy.IsOccupied(parent), "detaching a visible source unregisters it");
        parent.AddChild(dialog);
        Check(UiInteractionOccupancy.IsOccupied(parent), "re-entering the tree registers the source again");
        dialog.QueueFree();
        Check(!UiInteractionOccupancy.IsOccupied(parent), "queued deletion cannot leave a stale blocker");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        GD.Print("[RoomInteractionMainChecks] PASS " + message);
    }
}
#endif
