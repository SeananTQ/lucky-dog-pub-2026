#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace LuckyDogRise.Rooms;

// Actual desktop host, room page, dog placement and host-transfer notification.
// The provider is the existing isolated development sandbox, never real Steam.
internal static class RoomArrivalMainChecks
{
    public static async Task Run(ModeManager main, InGameRoomPreview page)
    {
        var tree = main.GetTree();
        var client = page.PreviewClient;
        var second = page.MockClientForSmoke(2);
        var third = page.MockClientForSmoke(3);
        var panel = main.SettingsPanelObj;
        RoomDesktopPreview desktop;
        bool processing = page.IsProcessing();
        page.SetProcess(false);
        async Task Frame()
        {
            page._Process(0);
            await main.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await main.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            page._Process(0);
        }
        async Task Step(double delta)
        { page.AdvancePreview(delta); await Frame(); }
        async Task Capture(string name)
        {
            if (DisplayServer.GetName() == "headless") return;
            await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string folder = ProjectSettings.GlobalizePath("res://../.local-build/room-lab");
            Directory.CreateDirectory(folder);
            main.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder, name + ".png"));
        }
        void EnterLocal()
        {
            var rows = page.GetNode<VBoxContainer>("Lobby/RoomList");
            Check(rows.GetChildCount() == 1, "empty directory exposes one local entry");
            rows.GetChild<InGameRoomDirectoryRow>(0).GetNode<Button>("Join").EmitSignal(BaseButton.SignalName.Pressed);
        }
        try
        {
            L10n.SetLocale("zh_CN", save: false);
            main.RoomSnapForSmoke(false);
            client.Leave(); second.Leave(); third.Leave();
            page.RefreshRooms();
            await Step(0.5);
            panel.ShowRoomPage();
            EnterLocal();
            await Frame();
            desktop = main.RoomDesktopForSmoke;
            Check(client.IsLocalArrival && client.Arrival.Presented && !client.IsBusy
                && client.View.Members.Length == 4 && desktop.RemoteDogs.Count == 3,
                "first complete frame has four dogs and a presentation-gated local waiting phase");
            int hostId = client.Arrival.HostMemberId;
            Check(hostId is >= -3 and <= -1 && client.View.OwnerId == hostId,
                "one of the three companions is the temporary host");
            var hostDog = desktop.RemoteDogs.Single(d => d.MemberId == hostId);
            Check(hostDog.GetNode<Label>("NameBar/Name").Text.StartsWith("♛ "),
                "the selected temporary host has a visible crown on its actual name bar");
            Check(!page.GetNode<Control>("Room/CodeRow").Visible
                && !page.GetNode<LineEdit>("Room/TitleRow/Name").Editable,
                "local arrival has no shareable code or host-management privileges");
            var original = client.View.Members.Where(m => m.IsCompanion).ToDictionary(m => m.Id);
            long layout = client.LayoutSession;
            foreach (var member in client.View.Members)
            {
                var position = desktop.GetPosition(member.Id) + new Vector2(member.Id * 11, -55 - Math.Abs(member.Id) * 9);
                desktop.MoveMember(member.Id, position, 0, false, 0, 0);
            }
            await Frame();
            var positions = client.View.Members.ToDictionary(m => m.Id, m => desktop.GetPosition(m.Id));
            await Capture("arrival-main-observe");
            await Step(RoomRules.ArrivalObserveSeconds - 0.1);
            Check(client.Arrival.Phase == RoomArrivalPhase.Waiting && !client.IsBusy,
                "observed cast remains local during the full observation interval");
            await Step(0.101);
            Check(client.Arrival.Phase == RoomArrivalPhase.Creating && client.IsBusy,
                "observation ends by preparing a room without prematurely assigning ownership");
            await Step(0.351); // Configured main-page sandbox request latency.
            Check(client.Arrival.Phase == RoomArrivalPhase.Departing && client.View.OwnerId == hostId,
                "native preparation starts the old host's departure while retaining temporary ownership");
            var departure = hostDog.GetNode<DesktopRiseIntroController>("Departure");
            var clip = departure.GetNode<Control>("RevealClip");
            var head = clip.GetNode<DogVisual>("HeadDog");
            float headStart = head.Position.Y;
            var clipStart = clip.GetGlobalTransformWithCanvas();
            Check(clip.ClipContents && !hostDog.Dog.Visible
                && !hostDog.GetNode<Control>("NameBar").Visible
                && head.GetNode<Sprite2D>("HeadRoot/Headwear").Texture == hostDog.Dog.GetNode<Sprite2D>("HeadRoot/Headwear").Texture
                && head.GetNode<Sprite2D>("HeadRoot/Head").Texture == hostDog.Dog.GetNode<Sprite2D>("HeadRoot/Head").Texture,
                "reverse rise uses the same skin and hat behind the startup mask, with the name bar retracted");
            await Step(RoomRules.ArrivalDepartureSeconds / 2);
            Check(Mathf.IsEqualApprox(hostDog.Modulate.A, 1f) && head.Position.Y > headStart
                && clip.GetGlobalTransformWithCanvas().IsEqualApprox(clipStart)
                && departure.GetNodeOrNull<DogVisual>("FrontRoot/ClawDog") != null,
                "the head retracts below its fixed edge while the opaque paws stay in front");
            await Capture("arrival-main-departure");
            float headMiddle = head.Position.Y;
            await Step(RoomRules.ArrivalDepartureSeconds * 0.4);
            Check(head.Position.Y > headMiddle
                && clip.GetNodeOrNull<DogVisual>("ClawDog")?.Position.Y > headStart
                && !head.GetNode<Sprite2D>("HeadRoot/Tonghe").Visible
                && !departure.GetNode<Sprite2D>("FrontRoot/FrontTongueDog/HeadRoot/Tonghe").Visible,
                "paws follow the head behind the mask and no tongue remains floating outside it");
            await Capture("arrival-main-departure-end");
            hostDog.Hide();
            Check(!head.IsVisibleInTree(), "departure inherits its avatar's visibility");
            hostDog.Show();
            await Step(RoomRules.ArrivalDepartureSeconds * 0.1 + 0.001);
            Check(!client.IsLocalArrival && client.View.OwnerId == client.Id && client.View.Members.Length == 3
                && client.LayoutSession == layout && desktop.RemoteDogs.Count == 2,
                "real membership replaces local arrival with two companions and the same layout session");
            Check(client.View.Members.All(m => m.Id != hostId)
                && client.View.Members.Where(m => m.IsCompanion).All(m =>
                    m.Name == original[m.Id].Name && m.SkinId == original[m.Id].SkinId
                    && m.HeadwearId == original[m.Id].HeadwearId && m.Presence == original[m.Id].Presence),
                "only the selected host departs; surviving identities and appearance stay fixed");
            Check(client.View.Members.All(m => desktop.GetPosition(m.Id).IsEqualApprox(positions[m.Id])),
                "all surviving dog placements, including the local dog, survive handoff");
            var notice = main.RoomJoinNoticeForSmoke;
            Check(notice?.HasBubble == true
                && notice.GetNode<Label>("Message/Text").Text.Contains(InGameRoomPreview.ChatText("Rooms_HostTransferredSelf")),
                "the actual local dog shows the shared host-transfer notification");
            Check(page.GetNode<Control>("Room/CodeRow").Visible, "the real room code appears after successful handoff");
            await Capture("arrival-main-host-transferred");
            string code = client.JoinedCode;
            second.Join(code);
            await Step(0.01);
            Check(client.View.Members.Length == 3 && client.View.Members.Count(m => m.IsCompanion) == 1,
                "second human replaces exactly one surviving companion");
            third.Join(code);
            await Step(0.01);
            Check(client.View.Members.Length == 3 && client.View.Members.All(m => !m.IsCompanion),
                "third human replaces the final companion without increasing the displayed count");
            await Capture("arrival-main-human-replacements");
            client.Leave(); second.Leave(); third.Leave();
            page.RefreshRooms();
            await Step(0.5);
            panel.ShowRoomPage();
            EnterLocal();
            await Frame();
            Check(client.IsLocalArrival, "a second empty-directory attempt starts independently");
            page.OnLeave();
            await Step(10);
            Check(client.View == null && !client.IsLocalArrival && !main.RoomDesktopWindowActive
                && client.Listings.Length == 0,
                "leaving before observation completes never publishes a room and restores the single dog");
            GD.Print("[RoomArrivalMainChecks] PASS actual main window: observation gate, random companion crown, masked reverse-rise departure, stable identity/layout, host-transfer bubble, one-for-one replacements and early exit (Mock).");
        }
        finally { page.SetProcess(processing); }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Room arrival main: " + message); }
}
#endif
