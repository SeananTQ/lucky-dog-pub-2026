#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace LuckyDogRise.Rooms;

// Uses the actual main window, system panel and in-memory development provider.
internal static class RoomGameChangeMainChecks
{
    public static async Task Run(ModeManager main, InGameRoomPreview page)
    {
        var panel = main.SettingsPanelObj;
        var tree = main.GetTree();
        var client = page.PreviewClient;
        var remote = page.MockClientForSmoke(2);
        async Task Frame()
        {
            await main.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await main.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
        async Task Settle(double seconds = 0.1)
        { page.AdvancePreview(seconds); await Frame(); }
        static void Click(Button button) => button.EmitSignal(BaseButton.SignalName.Pressed);
        async Task Capture(string name)
        {
            if (DisplayServer.GetName() == "headless") return;
            await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string folder = ProjectSettings.GlobalizePath("res://../.local-build/room-lab");
            Directory.CreateDirectory(folder);
            main.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder, name + ".png"));
        }
        client.Create("玩法与房龄回归");
        await Settle(0.5);
        panel.ShowRoomPage();
        await Frame();
        Check(client.View.Members.Length == 4, "new host room has three companion voters");
        var chat = main.RoomDesktopForSmoke.LocalChat;
        chat.OpenChat();
        var input = chat.GetNode<LineEdit>("Composer/Content/Input");
        input.Text = "preserved room-mode draft";
        Check(chat.Editing, "social chat opens before a proposal");
        Check(client.ProposeGameChange("work") == "ok", "host proposes work mode");
        await Settle();
        var overlay = panel.GetNode<RoomGameProposalOverlay>("RoomGameProposalOverlay");
        var content = overlay.GetNode<VBoxContainer>("OverlayPanel/Margin/Content");
        var confirm = content.GetNode<Button>("Actions/Primary");
        var cancel = content.GetNode<Button>("Actions/Secondary");
        Check(panel.IsOpen && overlay.Visible && !chat.Editing && input.Text == "preserved room-mode draft",
            $"proposal opens the panel and retracts the composer without erasing its draft (open={panel.IsOpen}, modal={overlay.Visible}, editing={chat.Editing}, pending={panel.HasPendingRoomGameProposal}, blocking={panel.HasBlockingPanelModal}, draft={input.Text})");
        Check(confirm.Disabled && !confirm.HasFocus() && !cancel.HasFocus(),
            "initial one-of-four votes cannot switch and neither action is preselected");
        Check(overlay.GetGlobalRect().Position.IsEqualApprox(panel.PanelRect.Position)
            && overlay.GetGlobalRect().Size.IsEqualApprox(panel.PanelRect.Size), "proposal covers the panel at its actual location");
        chat.OpenChat();
        Check(!chat.Editing, "dog cannot steal focus or close a room modal");
        await Settle(1.1);
        Check(!confirm.Disabled && client.View.GameId == "social", "companion majority enables manual switch without committing");
        await Capture("room-game-host-majority");
        Click(cancel);
        await Settle();
        Check(!overlay.Visible && client.View.GameId == "social" && client.View.GameChange == null,
            "cancel preserves the old mode and removes the proposal");
        Check(client.ProposeGameChange("work") == "ok", "new proposal after cancel");
        await Settle(1.1);
        Click(confirm);
        await Settle();
        Check(client.View.GameId == "work" && !overlay.Visible && !client.ChatAllowed, "host confirms work mode");
        chat.OpenChat();
        Check(!chat.Editing && client.SendChat("blocked") == "Rooms_ChatDisabled", "work blocks composer and message command");
        client.Join("missing-during-work");
        await Settle(0.5);
        Check(main.RoomJoinNoticeForSmoke?.HasBubble == true,
            "work mode still displays local join failure system notices");
        main.RoomAdvanceJoinNoticeForSmoke(RoomRules.ChatLifetime + 1);
        Check(client.ProposeGameChange("social") == "ok", "work still allows control proposals");
        await Settle(1.1);
        Click(confirm);
        await Settle();
        chat.OpenChat();
        Check(chat.Editing && input.Text == "preserved room-mode draft" && client.Bubbles.Count == 0,
            "returning to social restores the draft but never old messages");
        chat.CloseChat();
        panel.ShowRoomPage();
        L10n.SetLocale("zh_CN", save: false);
        await Settle(3661);
        string age = page.GetNode<Label>("Notice").Text;
        Check(age.Contains("01时01分") && !age.Contains("天"), "room age omits zero days and pads hours/minutes");
        await Settle(86400);
        Check(page.GetNode<Label>("Notice").Text.Contains("1天01时01分"),
            "room age displays days without resetting: " + page.GetNode<Label>("Notice").Text);
        await Capture("room-game-age-cn");
        L10n.SetLocale("en", save: false);
        await Frame();
        Check(page.Size.X <= panel.PanelSize.X + 1, "English mode controls remain within panel width");
        await Capture("room-game-age-en");
        client.Leave();
        remote.Create("remote host proposal");
        await Settle(0.5);
        client.Join(remote.JoinedCode);
        await Settle(0.5);
        panel.CloseImmediate();
        main.RoomFullscreenHideForSmoke(true);
        Check(remote.ProposeGameChange("work") == "ok", "remote host proposes");
        await Settle();
        Check(!panel.IsOpen, "fullscreen hiding defers an unsolicited modal");
        main.RoomFullscreenHideForSmoke(false);
        await Frame();
        Check(panel.IsOpen && overlay.Visible && !confirm.Disabled && !cancel.Disabled,
            "remote human is prompted automatically when visible again");
        await Capture("room-game-guest-vote");
        Click(cancel); // A guest's secondary action is decline, not host cancellation.
        await Settle(1.1);
        Check(!overlay.Visible && remote.View.GameChange != null, "guest decline does not cancel the host proposal");
        Check(remote.ConfirmGameChange(remote.View.GameChange.Id) == "ok", "host plus bots can form the user-selected majority");
        await Settle();
        Check(client.View.GameId == "work" && client.JoinedCode == remote.JoinedCode,
            "declining guest remains and follows the confirmed mode");
        Check(remote.ProposeGameChange("social") == "ok", "remote can propose from work");
        await Settle();
        Check(overlay.Visible, "new proposal is shown even after declining the previous one");
        await Settle(61);
        Check(!overlay.Visible && client.View.GameChange == null && client.View.GameId == "work",
            "deadline closes the actual modal and leaves the old mode");
        client.Leave();
        remote.Leave();
        await Settle();
        GD.Print("[RoomGameChangeMainChecks] PASS actual modal, auto-open/defer, focus, draft, manual commit/cancel, work chat, local notices, room age, dissent and timeout (Mock).");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
#endif
