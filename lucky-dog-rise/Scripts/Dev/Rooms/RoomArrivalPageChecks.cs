#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace LuckyDogRise.Rooms;

// Actual room page and production arrival state machine, driven by a fake native
// transport. These checks validate wiring separately from the real desktop art.
internal static class RoomArrivalPageChecks
{
    private const ulong Lobby = 109775243348105001;

    public static async Task Run(Node parent)
    {
        string locale = TranslationServer.GetLocale();
        try
        {
            await CheckHandoff(parent);
            await CheckRetry(parent);
            await CheckLeave(parent);
            GD.Print("[RoomArrivalPageChecks] PASS local entry, hidden code/invite, local filtered chat, observation, private handoff, retained layout, localized retry and Leave wiring (fake Steam).");
        }
        finally { L10n.SetLocale(locale, save: false); }
    }

    private static async Task CheckHandoff(Node parent)
    {
        using var test = new Fixture(parent);
        await test.Enter();
        var client = test.Page.CurrentClient;
        long layout = client.LayoutSession;
        int host = client.Arrival.HostMemberId;
        var bots = client.View.Members.Where(m => m.IsCompanion).ToDictionary(m => m.Id);
        Check(client.Arrival is { Phase: RoomArrivalPhase.Waiting, Presented: true }
            && client.View.OwnerId == host && test.Transport.CreateCalls == 0,
            "clicking the empty-directory entry presents a local companion host before any native create");
        Check(test.Arrival.Visible && test.Room.Visible && !test.LobbyPanel.Visible
            && !test.CodeRow.Visible && test.Invite.Disabled && !test.Retry.Visible,
            "local room shows arrival status but no native room code or enabled invitation");
        foreach (string language in new[] { "zh_CN", "en" })
        {
            L10n.SetLocale(language, save: false);
            await test.Frame();
            Check(test.Message.Text.Length > 0 && !test.Message.Text.Contains("Rooms_", StringComparison.Ordinal),
                "arrival status is localized immediately in " + language);
        }
        int searches = test.Transport.SearchCalls;
        test.Page.OnBrowse();
        await test.Frame();
        Check(test.Room.Visible && !test.LobbyPanel.Visible && test.Transport.SearchCalls == searches,
            "browse cannot start a competing directory request during local arrival");
        test.Transport.ChatFilter = (_, _) => "filtered local greeting";
        Check(client.SendChat("hello") == "" && client.Bubbles[client.Id].Text == "filtered local greeting"
            && test.Transport.Chats.Count == 0, "arrival chat is filtered and displayed locally without Steam broadcast");
        test.Now = 2.999;
        await test.Frame();
        Check(test.Transport.CreateCalls == 0, "the displayed room receives the full three-second observation time");
        test.Now = 3;
        await test.Frame();
        Check(test.Transport.PrivateCreateCalls == 1 && client.Arrival.Phase == RoomArrivalPhase.Creating,
            "the page clock starts one private create at the observation deadline");
        test.Transport.SucceedMembership(Lobby);
        await test.Frame();
        Check(client.Arrival.Phase == RoomArrivalPhase.Departing && !test.CodeRow.Visible
            && test.Transport.NativeAccess[Lobby] == RoomAccess.InviteOnly,
            "native preparation does not reveal the code or publish the room during departure");
        test.Now = 3.751;
        await test.Frame();
        Check(!client.IsLocalArrival && !test.Arrival.Visible && test.CodeRow.Visible && !test.Invite.Disabled
            && client.LayoutSession == layout && client.View.OwnerId == client.Id
            && test.Page.GetNode<Label>("Room/TitleRow/Count").Text == "3/6",
            "handoff exposes the real room controls and three-member count without resetting layout");
        Check(client.View.Members.Where(m => m.IsCompanion).All(m => m.Id != host && bots.TryGetValue(m.Id, out var old)
                && m.Presence == old.Presence && m.SkinId == old.SkinId && m.HeadwearId == old.HeadwearId),
            "visible remaining companions keep the same identities and outfits");
        var activity = client.View.Members.Where(m => m.IsCompanion).ToDictionary(m => m.Id, m => m.ActivitySequence);
        test.Now += RoomCompanionPlan.MaxActivityCycleSeconds + 1;
        await test.Frame();
        Check(client.View.Members.Where(m => m.IsCompanion).All(m => m.ActivitySequence > activity[m.Id]),
            "companions keep advancing their shared activity clock after the local-to-Steam handoff");
    }

    private static async Task CheckRetry(Node parent)
    {
        using var test = new Fixture(parent);
        await test.Enter();
        var client = test.Page.CurrentClient;
        var plan = client.Arrival.Plan;
        long layout = client.LayoutSession;
        test.Now = 3;
        await test.Frame();
        test.Transport.FailMembership(RoomFailure.Unavailable);
        await test.Frame();
        Check(client.Arrival.Phase == RoomArrivalPhase.Failed && test.Arrival.Visible
            && test.Retry.Visible && !test.Retry.Disabled && !test.Leave.Disabled
            && test.Room.Visible && !test.CodeRow.Visible,
            "failed creation retains the local room with usable retry and leave controls");
        int searches = test.Transport.SearchCalls;
        test.Page.OnBrowse();
        test.Now = 40;
        await test.Frame();
        Check(test.Transport.CreateCalls == 1 && test.Transport.SearchCalls == searches && test.Room.Visible,
            "a failed local room neither auto-retries nor browses into an unrelated request");
        Click(test.Retry);
        Click(test.Retry);
        await test.Frame();
        Check(test.Transport.PrivateCreateCalls == 2 && client.Arrival.Phase == RoomArrivalPhase.Creating
            && ReferenceEquals(client.Arrival.Plan, plan) && client.LayoutSession == layout && !test.Retry.Visible,
            "retry signal starts one request and retains the existing local room presentation");
        test.Transport.SucceedMembership(Lobby);
        test.Now = 40.751;
        await test.Frame();
        Check(!client.IsLocalArrival && client.View.Members.Length == 3 && client.LayoutSession == layout,
            "successful UI retry completes the original arrival presentation");
    }

    private static async Task CheckLeave(Node parent)
    {
        foreach (bool prepared in new[] { false, true })
        {
            using var test = new Fixture(parent);
            await test.Enter();
            if (prepared)
            {
                test.Now = 3;
                await test.Frame();
                test.Transport.SucceedMembership(Lobby);
                await test.Frame();
            }
            Click(test.Leave);
            await test.Frame();
            Check(!test.Page.CurrentClient.IsLocalArrival && test.Page.CurrentClient.View == null
                && test.LobbyPanel.Visible && !test.Arrival.Visible && !test.Service.ReconnectContext.IsPending,
                "the existing room Leave button cancels local arrival and returns to browsing");
            test.Now = 30;
            await test.Frame();
            Check(!test.Transport.AccessWrites.Any(w => w.Access == RoomAccess.Public)
                && (prepared ? test.Transport.Left.Contains(Lobby) : test.Transport.CreateCalls == 0),
                "leaving never publishes a prepared lobby or later restarts an uncreated one");
        }
    }

    private static void Click(Button button) => button.EmitSignal(BaseButton.SignalName.Pressed);
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Room arrival page: " + message); }

    private sealed class Fixture : IDisposable
    {
        private readonly Node _parent;
        public double Now;
        public readonly SteamRoomChecks.FakeTransport Transport = new();
        public readonly SteamRoomService Service;
        public readonly InGameRoomPreview Page;
        public Control Arrival => Page.GetNode<Control>("Arrival");
        public Label Message => Page.GetNode<Label>("Arrival/Message");
        public Button Retry => Page.GetNode<Button>("Arrival/Retry");
        public Button Leave => Page.GetNode<Button>("Room/Leave");
        public Button Invite => Page.GetNode<Button>("Room/Invite");
        public Control CodeRow => Page.GetNode<Control>("Room/CodeRow");
        public Control Room => Page.GetNode<Control>("Room");
        public Control LobbyPanel => Page.GetNode<Control>("Lobby");
        public Fixture(Node parent)
        {
            _parent = parent;
            Service = new SteamRoomService(Transport, 10, _ => true, _ => true, _ => true, () => Now);
            Page = GD.Load<PackedScene>("res://Scenes/Rooms/InGameRoomPage.tscn").Instantiate<InGameRoomPreview>();
            Page.Theme = GD.Load<Theme>("res://Themes/DefaultTheme.tres");
            Page.Size = new Vector2(396, 700);
            Page.Configure(new Provider(Service), null);
            parent.AddChild(Page);
        }
        public async Task Enter()
        {
            await Frame();
            Transport.CompleteSearch();
            await Frame();
            var rows = Page.GetNode<VBoxContainer>("Lobby/RoomList");
            Check(rows.GetChildCount() == 1, "fixture has one local empty-directory entry");
            Click(rows.GetChild(0).GetNode<Button>("Join"));
            await Frame();
            await Frame(); // Also covers the two-frame headless presentation gate.
        }
        public async Task Frame()
        {
            Transport.Now = Now;
            Service.Tick();
            await _parent.ToSignal(_parent.GetTree(), SceneTree.SignalName.ProcessFrame);
            await _parent.ToSignal(_parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        public void Dispose() { Page.Free(); Service.Dispose(); }
    }

    private sealed class Provider(SteamRoomService service) : IGamePlatformService, IPlatformRoomServiceProvider
    {
        public IRoomService RoomService => service.CheckAvailability() ? service : null;
        public RoomReconnectContext RoomReconnect => service.ReconnectContext;
        public string ProviderName => "Steam";
        public string StatusMessage => "";
        public bool IsAvailable => service.IsAvailable;
        public uint AppId => 2583700;
        public string PersonaName => "Arrival page test";
        public string AccountProvider => "steam";
        public string AccountId => "room-arrival-page-test";
        public event Action UserStatsReady { add { } remove { } }
        public void RunCallbacks() { }
        public bool OpenFriendsOverlay() => false;
        public PlatformAchievementReadResult ReadAchievementStates(IEnumerable<string> names)
            => new(false, "Test provider", Array.Empty<PlatformAchievementState>());
        public void Dispose() { }
    }
}
#endif
