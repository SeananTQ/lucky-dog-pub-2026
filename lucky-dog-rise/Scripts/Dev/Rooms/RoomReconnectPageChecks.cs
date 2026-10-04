#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace LuckyDogRise.Rooms;

// Runs the production page, client and Steam adapter against callback-controlled
// transport. No Steam runtime, account settings or player save is opened here.
internal static class RoomReconnectPageChecks
{
    private const ulong OriginalLobby = 0x0186000090E54E3FUL;
    private const ulong InvitedLobby = OriginalLobby + 1;

    public static async Task Run(Node parent)
    {
        string locale = TranslationServer.GetLocale();
        try
        {
            await CheckReplacementWhileHidden(parent);
            await CheckPageDetachBeforeServiceTick(parent);
            await CheckOfflineCancellation(parent);
            await CheckDebugWrapperIsolation(parent);
            await CheckInvitationPriority(parent);
            foreach (var failure in new[] { RoomFailure.Full, RoomFailure.NotFound })
                await CheckTerminalFailure(parent, failure);
            GD.Print("[RoomReconnectPageChecks] PASS offline waiting/cancellation, debug-wrapper forwarding and Mock isolation, detach-before-service-tick recovery, translated status, hidden-page rejoin without directory search, fresh client/draft/chat, invitation priority and full/missing-room termination (production service; fake transport).");
        }
        finally { L10n.SetLocale(locale, save: false); }
    }

    private static async Task CheckReplacementWhileHidden(Node parent)
    {
        using var test = new Fixture(parent);
        await test.JoinInitially();
        var original = test.Page.CurrentClient;
        var choices = test.Page.GetNode<OptionButton>("Room/Game/Choices/Current");
        choices.GrabFocus();
        choices.ShowPopup();
        choices.Select(1);
        choices.EmitSignal(OptionButton.SignalName.ItemSelected, 1);
        choices.GetPopup().Hide();
        await test.Frame();
        Check(choices.Selected == 1 && original.View.GameId == "social", "precondition: an unapplied activity draft exists");
        Check(original.SendChat("before connection loss") == "" && original.Bubbles.Count == 1,
            "precondition: the old session has a chat bubble");

        test.Disconnect();
        await test.Frame();
        Check(test.Page.CurrentClient == null && original.View == null && test.Context.IsPending,
            "detaching an offline service clears old membership without cancelling recovery intent");
        foreach (string language in new[] { "zh_CN", "en" })
        {
            L10n.SetLocale(language, save: false);
            await test.Frame();
            Check(test.Waiting.Visible && test.WaitMessage.Text == L10n.Tr("Rooms_ConnectingService")
                && test.WaitMessage.Text != "Rooms_ConnectingService" && test.WaitCancel.Visible && !test.WaitCancel.Disabled
                && !test.Page.GetNode<Control>("Lobby").Visible && !test.Page.GetNode<Control>("Room").Visible,
                "offline recovery shows translated waiting status and an enabled cancellation button in " + language);
        }

        test.Page.Hide();
        test.ReplaceService();
        await test.Frame();
        var replacement = test.Page.CurrentClient;
        Check(replacement != null && !ReferenceEquals(original, replacement)
            && test.Transport.JoinTargets.SequenceEqual(new[] { OriginalLobby }) && test.Transport.SearchCalls == 0,
            "the hidden page directly rejoins its interrupted lobby using a new client and no directory request");
        Check(test.Context.IsAttempting && test.Message.Text == L10n.Tr("Rooms_ReconnectJoining") && test.Cancel.Visible,
            "an in-flight automatic join retains translated recovery feedback and cancellation");
        test.Transport.SucceedMembership(OriginalLobby);
        await test.Frame();
        Check(replacement.JoinedCode == SteamRoomProtocol.Encode(OriginalLobby)
            && test.Page.GetNode<Control>("Room").Visible && !test.Page.GetNode<Control>("Lobby").Visible,
            "successful recovery prepares the joined-room page even while its tab is hidden");
        Check(!test.Page.Visible && !test.Context.IsPending && !test.Cancel.Visible
            && test.Message.Text == L10n.Tr("Rooms_ReconnectRestored"),
            "completion does not force open the hidden page or leave recovery actions active");
        Check(replacement.Bubbles.Count == 0 && choices.Selected == 0
            && test.Page.GetNode<Button>("Room/Game/Choices/Apply").Disabled,
            "the new session does not revive an old chat bubble or unapplied activity draft");
        test.Page.Show();
        await test.Frame();
        Check(test.Page.GetNode<Control>("Room").IsVisibleInTree(),
            "opening the page after recovery shows the room immediately");
        await test.Frame();
        Check(test.Transport.MembershipCalls == 1 && test.Transport.SearchCalls == 0,
            "idle frames after success neither rejoin nor search again");
    }

    private static async Task CheckOfflineCancellation(Node parent)
    {
        using var test = new Fixture(parent);
        await test.JoinInitially();
        test.Disconnect();
        await test.Frame();
        test.WaitCancel.EmitSignal(BaseButton.SignalName.Pressed);
        await test.Frame();
        Check(!test.Context.IsPending && !test.WaitCancel.Visible,
            "cancellation works while both the service and local client are absent");
        test.ReplaceService();
        await test.Frame();
        Check(test.Transport.MembershipCalls == 0 && test.Transport.SearchCalls == 1,
            "restored connectivity after explicit cancellation returns to ordinary browsing");
        test.Transport.CompleteSearch();
        test.Now += 30;
        await test.Frame();
        Check(test.Page.CurrentClient.JoinedCode.Length == 0 && test.Transport.MembershipCalls == 0,
            "later frames never resurrect a cancelled recovery target");
    }

    private static async Task CheckPageDetachBeforeServiceTick(Node parent)
    {
        using var test = new Fixture(parent);
        await test.JoinInitially();
        var original = test.Page.CurrentClient;
        var oldService = test.Service;
        // A transport can become unavailable after the platform's frame pump,
        // before the room page queries the provider. No loss callback or service
        // Tick is allowed to capture the target before the page disposes its client.
        test.DisconnectWithoutServiceTick();
        Check(!test.Context.IsPending, "transport loss alone has not delivered a callback or service tick");
        await test.Frame(tickService: false);
        Check(test.Page.CurrentClient == null && original.View == null,
            "the page detaches and releases its offline client before the next service tick");
        oldService.Tick();
        Check(test.Context.IsPending,
            "page disposal before service tick must not erase the interrupted room target");
        oldService.Dispose();
        test.ReplaceService();
        await test.Frame();
        Check(test.Transport.SearchCalls == 0
            && test.Transport.JoinTargets.SequenceEqual(new[] { OriginalLobby }),
            "the detach-before-tick ordering still rejoins the original lobby without a search");
        test.Transport.SucceedMembership(OriginalLobby);
        await test.Frame();
        Check(test.Page.CurrentClient.JoinedCode == SteamRoomProtocol.Encode(OriginalLobby)
            && !test.Context.IsPending && test.Context.StatusKey == "Rooms_ReconnectRestored",
            "recovery captured during page teardown can settle successfully");
    }

    private static async Task CheckInvitationPriority(Node parent)
    {
        using var test = new Fixture(parent);
        await test.JoinInitially();
        test.Disconnect();
        await test.Frame();
        test.ReplaceService();
        test.Transport.Seed(InvitedLobby);
        test.Transport.DeliverInvitation(InvitedLobby);
        await test.Frame();
        Check(!test.Context.IsPending && test.Transport.SearchCalls == 0
            && test.Transport.JoinTargets.SequenceEqual(new[] { InvitedLobby }),
            "a new accepted invitation takes priority over the interrupted room before the next page frame");
        test.Transport.SucceedMembership(InvitedLobby);
        await test.Frame();
        Check(test.Page.CurrentClient.JoinedCode == SteamRoomProtocol.Encode(InvitedLobby)
            && test.Transport.MembershipCalls == 1 && !test.Cancel.Visible,
            "the ordinary invitation path settles in the requested room without a later old-room join");
    }

    private static async Task CheckDebugWrapperIsolation(Node parent)
    {
        foreach (bool enterMock in new[] { false, true })
        {
            using var test = new Fixture(parent, useDebugWrapper: true);
            await test.JoinInitially();
            test.Disconnect();
            await test.Frame();
            var wrapperProvider = (IPlatformRoomServiceProvider)test.DebugWrapper;
            Check(wrapperProvider.RoomService == null && test.Page.CurrentClient == null
                && ReferenceEquals(wrapperProvider.RoomReconnect, test.Context)
                && test.Context.IsPending && test.Waiting.Visible && test.WaitCancel.Visible && !test.WaitCancel.Disabled
                && test.WaitMessage.Text == L10n.Tr("Rooms_ConnectingService"),
                "the integrated-debug wrapper exposes offline recovery intent after losing its room service and client");

            if (enterMock)
            {
                Check(test.DebugWrapper.TrySelectScenario(DebugSteamScenario.NormalSuccess, out _),
                    "an idle debug provider can enter its inventory Mock environment");
                await test.Frame();
                Check(!test.Context.IsPending && test.Context.StatusKey.Length == 0
                    && wrapperProvider.RoomReconnect == null && wrapperProvider.RoomService == null
                    && !test.Recovery.Visible,
                    "entering Mock clears the offline real-room recovery intent and hides its controls");
                Check(test.DebugWrapper.TryUseRealSteam(out _), "the test wrapper can return to its real-provider path");
            }
            else
            {
                test.WaitCancel.EmitSignal(BaseButton.SignalName.Pressed);
                await test.Frame();
                Check(!test.Context.IsPending && !test.WaitCancel.Visible,
                    "the integrated-debug environment can cancel recovery while its inner service is absent");
            }

            test.ReplaceService();
            await test.Frame();
            Check(test.Transport.SearchCalls == 1 && test.Transport.MembershipCalls == 0,
                "cancelled or Mock-cleared recovery never restarts when the wrapped real provider returns");
            test.Transport.CompleteSearch();
            await test.Frame();
        }
    }

    private static async Task CheckTerminalFailure(Node parent, RoomFailure failure)
    {
        using var test = new Fixture(parent);
        await test.JoinInitially();
        test.Disconnect();
        await test.Frame();
        test.ReplaceService();
        await test.Frame();
        Check(test.Transport.MembershipCalls == 1, "precondition: one recovery request is in flight");
        test.Transport.FailMembership(failure);
        await test.Frame();
        foreach (string language in new[] { "zh_CN", "en" })
        {
            L10n.SetLocale(language, save: false);
            await test.Frame();
            string reason = L10n.Tr(InGameRoomPreview.FailureKey(failure));
            Check(test.Context.LastFailure == failure && !test.Context.IsPending
                && !test.Cancel.Visible && test.Recovery.Visible
                && test.Message.Text == L10n.Tr("Rooms_ReconnectStopped") + "\n" + reason
                && !test.Message.Text.Contains("Rooms_", StringComparison.Ordinal),
                "terminal recovery reports a localized stop message and concrete failure reason in " + language);
        }
        test.Now += RoomReconnectContext.LifetimeSeconds + 1;
        await test.Frame();
        Check(test.Transport.MembershipCalls == 1 && test.Transport.SearchCalls == 0
            && test.Transport.CreateCalls == 0 && test.Page.CurrentClient.JoinedCode.Length == 0,
            "a full or missing room stops retries without creating a replacement room or silently searching");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Room reconnect page: " + message); }

    private sealed class Fixture : IDisposable
    {
        private readonly Node _parent;
        private readonly Provider _provider;
        private readonly List<SteamRoomService> _services = new();
        public double Now = 100;
        public readonly RoomReconnectContext Context;
        public readonly InGameRoomPreview Page;
        public readonly DebugSteamMockPlatformService DebugWrapper;
        public SteamRoomChecks.FakeTransport Transport { get; private set; }
        public SteamRoomService Service { get; private set; }
        public Control Recovery => Page.GetNode<Control>("Recovery");
        public Label Message => Page.GetNode<Label>("Recovery/Message");
        public Button Cancel => Page.GetNode<Button>("Recovery/Cancel");
        public Control Waiting => Page.GetNode<Control>("ConnectionWait");
        public Label WaitMessage => Page.GetNode<Label>("ConnectionWait/Content/Message");
        public Button WaitCancel => Page.GetNode<Button>("ConnectionWait/Content/Cancel");

        public Fixture(Node parent, bool useDebugWrapper = false)
        {
            _parent = parent;
            Context = new RoomReconnectContext(() => Now);
            _provider = new Provider(Context);
            ReplaceService();
            DebugWrapper = useDebugWrapper ? new DebugSteamMockPlatformService(_provider) : null;
            Page = GD.Load<PackedScene>("res://Scenes/Rooms/InGameRoomPage.tscn").Instantiate<InGameRoomPreview>();
            Page.Theme = GD.Load<Theme>("res://Themes/DefaultTheme.tres");
            Page.Size = new Vector2(396, 700);
            Page.Configure((IGamePlatformService)DebugWrapper ?? _provider, null);
            parent.AddChild(Page);
        }

        public async Task Frame(bool tickService = true)
        {
            if (tickService) Service?.Tick();
            await _parent.ToSignal(_parent.GetTree(), SceneTree.SignalName.ProcessFrame);
            await _parent.ToSignal(_parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        public async Task JoinInitially()
        {
            await Frame();
            Check(Transport.SearchCalls == 1, "fresh page performs its ordinary initial search");
            Transport.CompleteSearch();
            Check(Page.CurrentClient.Join(SteamRoomProtocol.Encode(OriginalLobby)), "initial join is accepted");
            Transport.SucceedMembership(OriginalLobby);
            await Frame();
            Check(Page.CurrentClient.View?.GameId == "social", "initial membership is a confirmed social room");
        }

        public void Disconnect()
        {
            Transport.IsAvailable = false;
            Service.Tick();
            Check(Context.IsPending, "network loss arms the production reconnect context");
            _provider.RoomService = null;
            Service.Dispose();
            Service = null;
        }

        public void DisconnectWithoutServiceTick()
        {
            Transport.IsAvailable = false;
            // Leave the owned service in place: the production provider observes
            // availability and captures the interruption before returning null.
        }

        public void ReplaceService()
        {
            Transport = new SteamRoomChecks.FakeTransport { Now = Now };
            Transport.Seed(OriginalLobby);
            Service = new SteamRoomService(Transport, 10, _ => true, _ => true, _ => true,
                now: () => Now, reconnect: Context, appId: 2583700);
            _services.Add(Service);
            _provider.RoomService = Service;
        }

        public void Dispose()
        {
            if (GodotObject.IsInstanceValid(Page)) Page.Free();
            DebugWrapper?.Dispose();
            foreach (var service in _services) service.Dispose();
        }
    }

    private sealed class Provider(RoomReconnectContext context) : IGamePlatformService, IPlatformRoomServiceProvider
    {
        private IRoomService _service;
        public IRoomService RoomService
        {
            get => _service is SteamRoomService steam ? steam.CheckAvailability() ? steam : null : _service;
            set => _service = value;
        }
        public RoomReconnectContext RoomReconnect => context;
        public string ProviderName => "Steam";
        public string StatusMessage => "";
        public bool IsAvailable => RoomService != null;
        public uint AppId => 2583700;
        public string PersonaName => "Reconnect page test";
        public string AccountProvider => "steam";
        public string AccountId => "room-reconnect-ui-test";
        public event Action UserStatsReady { add { } remove { } }
        public void RunCallbacks() { }
        public bool OpenFriendsOverlay() => false;
        public PlatformAchievementReadResult ReadAchievementStates(IEnumerable<string> names)
            => new(false, "Test provider", Array.Empty<PlatformAchievementState>());
        public void Dispose() { }
    }
}
#endif
