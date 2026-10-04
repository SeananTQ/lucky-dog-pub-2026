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
            await CheckUnavailableAndRestore(parent);
            await CheckOfflineLeave(parent);
            await CheckLeave(parent);
            GD.Print("[RoomArrivalPageChecks] PASS local entry, silent retry/backoff, complete connection-page replacement, localized guidance, original-cast recovery, public-before-departure handoff, retained layout and offline Leave wiring (fake Steam).");
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
        Check(!test.Waiting.Visible && test.Age.Visible && test.Room.Visible && !test.LobbyPanel.Visible
            && !test.CodeRow.Visible && test.Invite.Disabled && test.Page.GetNodeOrNull<Control>("Arrival/Retry") == null,
            "local room shows its age without preparation text, native room code or enabled invitation");
        foreach (string language in new[] { "zh_CN", "en" })
        {
            L10n.SetLocale(language, save: false);
            await test.Frame();
            Check(test.Age.Text == string.Format(L10n.Tr("Rooms_Age"),
                string.Format(L10n.Tr("Rooms_AgeHoursMinutesSeconds"), 0, 0, 0)),
                "local room uses the same localized age heading in " + language);
        }
        int searches = test.Transport.SearchCalls;
        test.Page.OnBrowse();
        await test.Frame();
        Check(test.Room.Visible && !test.LobbyPanel.Visible && test.Transport.SearchCalls == searches,
            "browse cannot start a competing directory request during local arrival");
        test.Transport.ChatFilter = (_, _) => "filtered local greeting";
        Check(client.SendChat("hello") == "" && client.Bubbles[client.Id].Text == "filtered local greeting"
            && test.Transport.Chats.Count == 0, "arrival chat is filtered and displayed locally without Steam broadcast");
        test.Now = RoomRules.ArrivalObserveSeconds - 0.001;
        await test.Frame();
        Check(test.Transport.CreateCalls == 0, "the displayed room receives the full observation time");
        test.Now = RoomRules.ArrivalObserveSeconds;
        await test.Frame();
        Check(test.Transport.PrivateCreateCalls == 1 && client.Arrival.Phase == RoomArrivalPhase.Creating,
            "the page clock starts one private create at the observation deadline");
        Check(!test.Waiting.Visible && test.Age.Visible, "native preparation preserves the ordinary room-age heading");
        test.Transport.SucceedMembership(Lobby);
        await test.Frame();
        Check(client.Arrival.Phase == RoomArrivalPhase.Departing && !test.CodeRow.Visible
            && test.Transport.NativeAccess[Lobby] == RoomAccess.Public && client.Arrival.Committed,
            "native preparation is public before departure but does not reveal the code before the visual handoff");
        Check(!test.Waiting.Visible && test.Age.Visible, "departure never announces itself in the room heading");
        test.Now += RoomRules.ArrivalDepartureSeconds + 0.001;
        await test.Frame();
        Check(!client.IsLocalArrival && !test.Waiting.Visible && test.CodeRow.Visible && !test.Invite.Disabled
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
        int failures = 0;
        client.JoinFailed += (_, _) => failures++;
        test.Now = RoomRules.ArrivalObserveSeconds;
        await test.Frame();
        test.Transport.FailMembership(RoomFailure.Unavailable);
        await test.Frame();
        Check(client.Arrival.Phase == RoomArrivalPhase.Failed && !test.Waiting.Visible
            && !test.Leave.Disabled && test.Room.Visible && !test.CodeRow.Visible && failures == 0,
            "transient failure retains the ordinary room without an error notice or manual retry");
        Check(test.Age.Visible && !test.Page.GetNode<Label>("Status").Visible,
            "silent retry keeps only the ordinary room-age heading");
        int searches = test.Transport.SearchCalls;
        test.Page.OnBrowse();
        test.Now += 4.999;
        await test.Frame();
        Check(test.Transport.CreateCalls == 1 && test.Transport.SearchCalls == searches && test.Room.Visible,
            "retry backoff prevents premature native requests or unrelated browsing");
        test.Now += 0.001;
        await test.Frame();
        Check(test.Transport.PrivateCreateCalls == 2 && client.Arrival.Phase == RoomArrivalPhase.Creating
            && ReferenceEquals(client.Arrival.Plan, plan) && client.LayoutSession == layout && !test.Waiting.Visible,
            "the first retry automatically starts once and retains the original local room presentation");
        test.Transport.FailMembership(RoomFailure.Unavailable);
        test.Now += 5;
        await test.Frame();
        Check(client.Arrival.WaitingForConnection && test.Waiting.Visible
            && !test.Room.Visible && !test.LobbyPanel.Visible && !test.Age.Visible
            && !test.Page.GetNode<Control>("Status").Visible && !test.Page.GetNode<Control>("Recovery").Visible
            && test.Waiting.GetNode<Control>("Content/LoadingIndicator").Visible
            && test.Cancel.Visible && !test.Cancel.Disabled && failures == 0,
            "prolonged failure replaces every room control with Steam waiting and a usable Leave action");
        foreach (string language in new[] { "zh_CN", "zh_TW", "en" })
        {
            L10n.SetLocale(language, save: false);
            test.Page._Process(10.001);
            await test.Frame();
            Check(test.Message.Text == L10n.Tr("Rooms_ConnectingService") + "\n\n" + L10n.Tr("Rooms_ConnectionAdvice")
                && !test.Message.Text.Contains("Rooms_", StringComparison.Ordinal)
                && test.Cancel.Text == L10n.Tr("Rooms_Leave"),
                "persistent connection waiting uses localized room guidance and Leave in " + language);
        }
        test.Now += 10;
        await test.Frame();
        Check(test.Transport.PrivateCreateCalls == 3 && test.Waiting.Visible,
            "background retries continue beneath the connection page without needing a button");
        test.Transport.SucceedMembership(Lobby);
        await test.Frame();
        Check(!test.Waiting.Visible && test.Room.Visible && client.Arrival.Committed,
            "successful native preparation restores the same room before beginning the handoff animation");
        test.Now += RoomRules.ArrivalDepartureSeconds + 0.001;
        await test.Frame();
        Check(!client.IsLocalArrival && client.View.Members.Length == 3 && client.LayoutSession == layout,
            "successful UI retry completes the original arrival presentation");
    }

    private static async Task CheckUnavailableAndRestore(Node parent)
    {
        using var test = new Fixture(parent);
        await test.Enter();
        var client = test.Page.CurrentClient;
        var plan = client.Arrival.Plan;
        int host = client.Arrival.HostMemberId;
        long layout = client.LayoutSession;
        test.Now = RoomRules.ArrivalObserveSeconds;
        await test.Frame();
        var oldTransport = test.Transport;
        oldTransport.IsAvailable = false;
        await test.Frame();
        Check(test.Waiting.Visible && !test.Room.Visible && !test.LobbyPanel.Visible
            && ReferenceEquals(test.Page.CurrentClient, client) && client.Arrival.WaitingForConnection,
            "provider loss immediately replaces the room page while retaining its local cast and client");
        test.Page.Hide();
        test.Now = 1000;
        test.ReplaceService();
        await test.Frame();
        Check(ReferenceEquals(test.Page.CurrentClient, client) && ReferenceEquals(client.Arrival.Plan, plan)
            && client.Arrival.HostMemberId == host && client.LayoutSession == layout
            && test.Transport.PrivateCreateCalls == 1 && test.Transport.SearchCalls == 0,
            "a restored provider continues the same arrival even when its tab is hidden");
        oldTransport.SucceedMembership(Lobby);
        Check(oldTransport.Left.Contains(Lobby) && client.IsBusy,
            "an obsolete callback cleans only its old private lobby without disturbing restored preparation");
        test.Transport.SucceedMembership(Lobby + 1);
        test.Now += RoomRules.ArrivalDepartureSeconds + 0.001;
        await test.Frame();
        Check(!client.IsLocalArrival && !test.Waiting.Visible && !test.Page.Visible
            && client.JoinedCode == SteamRoomProtocol.Encode(Lobby + 1) && client.LayoutSession == layout,
            "hidden automatic recovery completes the original handoff without opening the page");
    }

    private static async Task CheckOfflineLeave(Node parent)
    {
        using var test = new Fixture(parent);
        await test.Enter();
        test.Transport.IsAvailable = false;
        await test.Frame();
        Check(test.Waiting.Visible && test.Cancel.Visible, "offline local arrival still exposes Leave");
        Click(test.Cancel);
        await test.Frame();
        Check(test.Page.CurrentClient?.IsLocalArrival != true && !test.Cancel.Visible,
            "Leave clears the local arrival even while its Steam provider is unavailable");
        test.Now = 1000;
        test.ReplaceService();
        await test.Frame();
        Check(test.Transport.CreateCalls == 0 && test.Transport.SearchCalls == 1,
            "connectivity restoration after Leave browses normally without recreating the abandoned room");
    }

    private static async Task CheckLeave(Node parent)
    {
        foreach (bool prepared in new[] { false, true })
        {
            using var test = new Fixture(parent);
            await test.Enter();
            if (prepared)
            {
                test.Now = RoomRules.ArrivalObserveSeconds;
                await test.Frame();
                test.Transport.SucceedMembership(Lobby);
                await test.Frame();
            }
            Click(test.Leave);
            await test.Frame();
            Check(!test.Page.CurrentClient.IsLocalArrival && test.Page.CurrentClient.View == null
                && test.LobbyPanel.Visible && !test.Waiting.Visible && !test.Service.ReconnectContext.IsPending,
                "the existing room Leave button cancels local arrival and returns to browsing");
            test.Now += RoomRules.ArrivalObserveSeconds + 1;
            await test.Frame();
            Check(prepared ? test.Transport.Left.Contains(Lobby) && test.Transport.CreateCalls == 1
                    : test.Transport.CreateCalls == 0,
                "leaving cleans an already committed lobby or prevents an uncreated arrival from starting later");
        }
    }

    private static void Click(Button button) => button.EmitSignal(BaseButton.SignalName.Pressed);
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Room arrival page: " + message); }

    private sealed class Fixture : IDisposable
    {
        private readonly Node _parent;
        private readonly Provider _provider = new();
        private readonly List<SteamRoomService> _services = new();
        public double Now;
        public SteamRoomChecks.FakeTransport Transport { get; private set; }
        public SteamRoomService Service { get; private set; }
        public readonly InGameRoomPreview Page;
        public Control Waiting => Page.GetNode<Control>("ConnectionWait");
        public Label Message => Page.GetNode<Label>("ConnectionWait/Content/Message");
        public Label Age => Page.GetNode<Label>("Notice");
        public Button Cancel => Page.GetNode<Button>("ConnectionWait/Content/Cancel");
        public Button Leave => Page.GetNode<Button>("Room/Leave");
        public Button Invite => Page.GetNode<Button>("Room/Invite");
        public Control CodeRow => Page.GetNode<Control>("Room/CodeRow");
        public Control Room => Page.GetNode<Control>("Room");
        public Control LobbyPanel => Page.GetNode<Control>("Lobby");
        public Fixture(Node parent)
        {
            _parent = parent;
            ReplaceService();
            Page = GD.Load<PackedScene>("res://Scenes/Rooms/InGameRoomPage.tscn").Instantiate<InGameRoomPreview>();
            Page.Theme = GD.Load<Theme>("res://Themes/DefaultTheme.tres");
            Page.Size = new Vector2(396, 700);
            Page.Configure(_provider, null);
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
        public void ReplaceService()
        {
            Transport = new SteamRoomChecks.FakeTransport { Now = Now };
            Service = new SteamRoomService(Transport, 10, _ => true, _ => true, _ => true, () => Now);
            _services.Add(Service);
            _provider.Service = Service;
        }
        public void Dispose()
        {
            Page.Free();
            foreach (var service in _services) service.Dispose();
        }
    }

    private sealed class Provider : IGamePlatformService, IPlatformRoomServiceProvider
    {
        public SteamRoomService Service;
        public IRoomService RoomService => Service.CheckAvailability() ? Service : null;
        public RoomReconnectContext RoomReconnect => Service.ReconnectContext;
        public string ProviderName => "Steam";
        public string StatusMessage => "";
        public bool IsAvailable => Service.IsAvailable;
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
