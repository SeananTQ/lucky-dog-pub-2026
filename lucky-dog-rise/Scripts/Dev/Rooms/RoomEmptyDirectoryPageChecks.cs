#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace LuckyDogRise.Rooms;

// Exercise the actual page against controllable asynchronous search/create
// results. The virtual row must stay presentation-only until explicitly joined.
internal static class RoomEmptyDirectoryPageChecks
{
    public static async Task Run(Node parent)
    {
        var service = new Service();
        var provider = new Provider { RoomService = service };
        var page = GD.Load<PackedScene>("res://Scenes/Rooms/InGameRoomPage.tscn")
            .Instantiate<InGameRoomPreview>();
        page.Configure(provider, null);
        parent.AddChild(page);
        var rows = page.GetNode<VBoxContainer>("Lobby/RoomList");
        async Task Frame()
        {
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        static void Click(Button button) => button.EmitSignal(BaseButton.SignalName.Pressed);
        Button Entry() => rows.GetChild(0).GetNode<Button>("Join");
        try
        {
            await Frame();
            var client = page.CurrentClient;
            var display = page.GetNode<OptionButton>("Lobby/Display/Mode");
            void SelectCompact() => display.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
            Check(display.Disabled, "pending search locks the display setting");
            SelectCompact();
            Check(page.SelectedDisplayMode == RoomDisplayMode.FullScreenFree, "queued selection during request is ignored");
            var failures = new List<(RoomFailure Reason, bool TimedOut)>();
            client.JoinFailed += (reason, timedOut) => failures.Add((reason, timedOut));
            Check(client.IsBusy && rows.GetChildCount() == 0 && service.CreateRequests == 0,
                "initial empty array during a pending search does not fabricate an entry");
            service.Complete(RoomFailure.Unavailable);
            await Frame();
            Check(rows.GetChildCount() == 0 && !page.GetNode<Label>("Lobby/Empty").Visible,
                "search error is not presented as an empty successful directory");

            Check(!display.Disabled, "failed request outside a room unlocks display setting");
            SelectCompact();
            Check(page.SelectedDisplayMode == RoomDisplayMode.HalfScreenCompact, "unjoined player can select compact layout");
            page.RefreshRooms();
            service.Complete(RoomFailure.None);
            await Frame();
            Check(rows.GetChildCount() == 1 && rows.GetChild(0).GetNode<Label>("Count").Text == "3/6"
                && rows.GetChild(0).GetNode<Label>("Name").Text == L10n.Tr("Rooms_DefaultName")
                && client.Listings.Length == 0 && client.JoinedCode.Length == 0 && service.CreateRequests == 0,
                "successful empty directory displays one local 3/6 row without creating or inventing a lobby ID");
            var staleEntry = Entry();
            page.RefreshRooms();
            Click(staleEntry);
            service.Complete(RoomFailure.None);
            // Even before deferred rendering removes the row, it belongs to the
            // previous result array and must not create on a queued old signal.
            Click(staleEntry);
            Check(service.CreateRequests == 0, "refresh invalidates old entry signals before rendering catches up");
            await Frame();
            var entry = Entry();
            page.Hide();
            Click(entry);
            Check(service.CreateRequests == 0, "hidden room page cannot create from a queued row signal");
            page.Show();
            Click(entry);
            Click(entry);
            Check(service.CreateRequests == 0 && client.Arrival?.Phase == RoomArrivalPhase.Waiting,
                "joining the local row starts local presentation without a premature native create");
            await Frame();
            Check(page.GetNode<Control>("Room").Visible && !page.GetNode<Control>("Lobby").Visible,
                "local arrival leaves the directory and displays its temporary room");
            Check(display.Disabled, "local companion presentation locks the display setting");
            client.Leave();
            Check(client.Create("Existing real room"), "ordinary room fixture can still create immediately");
            service.Complete(RoomFailure.None);
            await Frame();
            Check(client.View.OwnerId == client.Id && client.View.Members.Length == 1
                && !client.View.Members[0].IsCompanion && service.JoinRequests == 0 && !client.IsLocalArrival,
                "ordinary creation starts owner-only and does not enter companion arrival");
            string firstCode = client.JoinedCode;

            page.OnBrowse();
            service.Complete(RoomFailure.None);
            await Frame();
            Check(client.JoinedCode == firstCode && rows.GetChildCount() == 1,
                "browsing an empty directory while already in a room also offers the entry");
            Check(display.Disabled && page.GetNode<Label>("Lobby/Display/Locked").Visible,
                "browsing other rooms while joined keeps mode locked with an explanation");
            var stableRow = rows.GetChild(0);
            service.Publish(client);
            await Frame();
            service.Publish(client);
            await Frame();
            Check(ReferenceEquals(stableRow, rows.GetChild(0)),
                "background room snapshots do not rebuild the empty-directory row or lose a pending click");
            Click(Entry());
            await Frame();
            Check(client.IsLocalArrival && client.JoinedCode != firstCode,
                "choosing another local room leaves the previous real membership");
            client.MarkArrivalPresented();
            service.Now += RoomRules.ArrivalObserveSeconds;
            await Frame();
            Check(service.Pending?.Operation == RoomOperation.Create && service.CreateRequests == 2,
                "arrival submits its single creation only after the observation interval");
            service.Complete(RoomFailure.Unavailable);
            await Frame();
            Check(client.IsLocalArrival && client.View.Members.Length == 4
                && client.Arrival.Phase == RoomArrivalPhase.Failed && client.RequestState == RoomRequestState.Failed
                && failures.Count == 0 && !page.GetNode<Control>("ConnectionWait").Visible,
                "transient arrival failure silently retains the local presentation without a join-failure notice");

            client.Leave();
            page.RefreshRooms();
            client.Cancel();
            await Frame();
            Check(rows.GetChildCount() == 0, "cancelled search does not offer a virtual row");
            page.RefreshRooms();
            service.Now += RoomRules.RequestTimeout + 1;
            await Frame();
            Check(client.RequestState == RoomRequestState.TimedOut && rows.GetChildCount() == 0,
                "timed-out search does not offer a virtual row");

            page.RefreshRooms();
            service.Directory = new[] { new RoomListing("REAL1", "Existing room", "social", 2, 6) };
            service.Complete(RoomFailure.None);
            await Frame();
            Check(rows.GetChildCount() == 1 && rows.GetChild(0).GetNode<Label>("Count").Text == "2/6"
                && rows.GetChild(0).GetNode<Label>("Name").Text == "Existing room",
                "nonempty real results never receive an extra virtual row");
            page.RefreshRooms();
            service.Complete(RoomFailure.Unavailable);
            await Frame();
            Check(rows.GetChildCount() == 1 && rows.GetChild(0).GetNode<Label>("Name").Text == "Existing room",
                "failed refresh preserves real cached results without appending an invented room");

            service.Directory = Array.Empty<RoomListing>();
            page.RefreshRooms();
            service.Complete(RoomFailure.None);
            await Frame();
            staleEntry = Entry();
            int creations = service.CreateRequests;
            client.BeginSession("EXTERNAL_ROOM_CHANGE");
            Click(staleEntry);
            Check(service.CreateRequests == creations, "an old room-session entry cannot create after membership changes");
            service.Publish(client);
            await Frame();
            page.OnBrowse();
            service.Complete(RoomFailure.None);
            await Frame();
            staleEntry = Entry();
            provider.RoomService = null;
            page.Configure(provider, null);
            Click(staleEntry);
            Check(service.CreateRequests == creations, "service replacement invalidates its old entry immediately");
            await CheckRandomCompanionRoom(parent);
            GD.Print("[RoomEmptyDirectoryPageChecks] PASS successful-empty-search gating, local 3/6 row, observation-gated creation, stale/hidden callbacks, stable row and retained failed local presentation (fake service).");
        }
        finally { page.Free(); }
    }

    private static async Task CheckRandomCompanionRoom(Node parent)
    {
        var service = new Service();
        var page = GD.Load<PackedScene>("res://Scenes/Rooms/InGameRoomPage.tscn").Instantiate<InGameRoomPreview>();
        var provider = new Provider { RoomService = service };
        page.Configure(provider, null);
        parent.AddChild(page);
        async Task Frame()
        {
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        var join = page.GetNode<Button>("Lobby/JoinAction/Join");
        void RandomJoin() => join.EmitSignal(BaseButton.SignalName.Pressed);
        try
        {
            await Frame();
            service.Complete(RoomFailure.None);
            await Frame();
            var client = page.CurrentClient;
            int notices = 0;
            client.JoinFailed += (_, _) => notices++;
            RandomJoin();
            RandomJoin();
            service.Complete(RoomFailure.None);
            await Frame();
            Check(client.IsLocalArrival && client.View.Members.Length == 4 && client.View.OwnerId < 0
                && service.CreateRequests == 0 && notices == 0,
                "random successful empty search enters companion presentation once without errors or immediate creation");
            client.MarkArrivalPresented();
            service.Now += RoomRules.ArrivalObserveSeconds;
            await Frame();
            Check(service.Pending?.Arrival == client.Arrival && service.CreateRequests == 1,
                "random fallback uses the same observed companion cast for delayed real creation");
            client.Leave();
            await Frame();

            client.Create("Original room");
            service.Complete(RoomFailure.None);
            await Frame();
            string original = client.JoinedCode;
            page.OnBrowse(); service.Complete(RoomFailure.None); await Frame();
            int creations = service.CreateRequests;
            RandomJoin(); service.Complete(RoomFailure.Unavailable); await Frame();
            Check(!client.IsLocalArrival && client.JoinedCode == original && service.CreateRequests == creations,
                "failed random query retains current membership without companion fallback");
            RandomJoin();
            long timedOut = client.ActiveRequestId;
            service.Now += RoomRules.RequestTimeout + 1;
            await Frame();
            Check(client.RequestState == RoomRequestState.TimedOut && !client.IsLocalArrival
                && client.JoinedCode == original
                && !client.TryComplete(timedOut, () => new RoomResult(RoomFailure.None, Array.Empty<RoomListing>())),
                "timed-out search and its late empty response cannot enter a companion room");
            RandomJoin();
            long cancelled = client.ActiveRequestId;
            client.Cancel();
            Check(!client.TryComplete(cancelled, () => new RoomResult(RoomFailure.None, Array.Empty<RoomListing>()))
                && !client.IsLocalArrival && client.JoinedCode == original,
                "cancelled search discards its companion fallback");
            await Frame();

            service.Directory = [new RoomListing("REAL", "Real room", "social", 2, 6)];
            RandomJoin(); service.Complete(RoomFailure.None);
            Check(service.Pending?.Operation == RoomOperation.Join && service.JoinRequests == 1 && !client.IsLocalArrival,
                "random join prioritizes an available real room");
            service.Complete(RoomFailure.Full); await Frame();
            Check(!client.IsLocalArrival && client.JoinedCode == original && service.CreateRequests == creations,
                "a selected real room becoming full reports join failure rather than secretly creating another room");

            service.Directory = [new RoomListing(original, "Current room", "social", 1, 6),
                new RoomListing("FULL", "Full room", "social", 6, 6)];
            RandomJoin(); service.Complete(RoomFailure.None); await Frame();
            Check(client.IsLocalArrival && client.JoinedCode != original && service.CreateRequests == creations,
                "only current/full rooms counts as no other target and begins the shared companion-room switch");
            client.Leave(); await Frame();
            RandomJoin();
            long oldRequest = client.ActiveRequestId;
            provider.RoomService = null; page.Configure(provider, null);
            Check(!client.TryComplete(oldRequest, () => new RoomResult(RoomFailure.None, Array.Empty<RoomListing>()))
                && service.CreateRequests == creations,
                "replacing the page client invalidates late random fallback callbacks");
            GD.Print("[RoomRandomCompanionChecks] PASS shared arrival, single-flight, ordinary creation, real-room priority, failed/timed-out/cancelled searches and stale client.");
        }
        finally { page.Free(); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static async Task CheckAutoRefresh(Node parent)
    {
        var service = new Service();
        var provider = new Provider { RoomService = service };
        var page = GD.Load<PackedScene>("res://Scenes/Rooms/InGameRoomPage.tscn").Instantiate<InGameRoomPreview>();
        // Reuse the real settings scroll container without starting the unrelated
        // settings, inventory and debug controllers in this focused page fixture.
        var shell = GD.Load<PackedScene>("res://Scenes/App/SystemPanel.tscn").Instantiate();
        var scroll = shell.GetNode<ScrollContainer>("Panel/RootVBox/Scroll");
        scroll.GetParent().RemoveChild(scroll);
        foreach (Node child in scroll.GetChildren()) { scroll.RemoveChild(child); child.Free(); }
        shell.Free();
        scroll.Size = new Vector2(396, 420);
        page.Configure(provider, null);
        page.DirectoryInteractionForSmoke = () => false;
        scroll.AddChild(page);
        parent.AddChild(scroll);
        var rows = page.GetNode<VBoxContainer>("Lobby/RoomList");
        async Task Frame()
        {
            for (int i = 0; i < 3; i++) await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        async Task Due()
        {
            service.Now += InGameRoomPreview.DirectoryRefreshIntervalSeconds;
            await Frame();
        }
        try
        {
            service.Directory = Enumerable.Range(0, 16).Select(i => new RoomListing($"R{i}", $"Room {i}", "social", 2, 6)).ToArray();
            service.Complete(RoomFailure.None);
            await Frame();
            var client = page.CurrentClient;
            Check(service.SearchRequests == 1 && rows.GetChildCount() == 16, "initial directory is loaded once");
            scroll.ScrollVertical = 500;
            await Frame();
            var stableRow = rows.GetChild<InGameRoomDirectoryRow>(7);
            float stableY = stableRow.GetGlobalRect().Position.Y;
            Check(scroll.ScrollVertical > 0, "fixture exercises the real scroll container");
            service.Now += InGameRoomPreview.DirectoryRefreshIntervalSeconds - 1;
            await Frame();
            Check(service.SearchRequests == 1, "visible page waits for the full refresh interval");
            service.Now++;
            await Frame();
            long searchId = client.ActiveRequestId;
            Check(service.SearchRequests == 2 && client.IsBackgroundSearch && !client.IsBusy
                && client.RequestState == RoomRequestState.Succeeded && rows.GetChildCount() == 16,
                "automatic query preserves foreground status, controls and cached list");
            page.RefreshRooms(); page.RefreshRooms();
            Check(service.SearchRequests == 2 && client.ActiveRequestId == searchId,
                "manual and automatic refresh coalesce into the same in-flight request");
            service.Directory = service.Directory.Skip(1).Reverse().Select(room => room with { Count = 4 })
                .Append(new RoomListing("NEW", "New room", "work", 1, 6)).ToArray();
            service.Complete(RoomFailure.None);
            await Frame();
            Check(rows.GetChild(6) == stableRow && rows.GetChild(0).GetNode<Label>("Name").Text == "Room 1"
                && rows.GetChild(15).GetNode<Label>("Name").Text == "New room"
                && stableRow.GetNode<Label>("Count").Text == "4/6",
                "surviving rows keep identity and order while counts and additions update");
            Check(Math.Abs(stableRow.GetGlobalRect().Position.Y - stableY) <= 2,
                "removing a row above the scroll position preserves the visible anchor");

            page.DirectoryInteractionForSmoke = () => true;
            await Due();
            var disappearing = rows.GetChild<InGameRoomDirectoryRow>(0);
            service.Directory = service.Directory.Where(room => room.Code != "R1").ToArray();
            service.Complete(RoomFailure.None);
            await Frame();
            Check(rows.GetChild(0) == disappearing && disappearing.GetNode<Button>("Join").Disabled,
                "hover/press defers removal but makes a vanished room unavailable");
            disappearing.GetNode<Button>("Join").EmitSignal(BaseButton.SignalName.Pressed);
            Check(service.JoinRequests == 0, "a queued click on a removed room cannot join a different row");
            page.DirectoryInteractionForSmoke = () => false;
            await Frame();
            Check(rows.GetChildCount() == 15 && rows.GetChild(0).GetNode<Label>("Name").Text == "Room 2",
                "deferred edits apply once the pointer leaves the list");

            await Due();
            long timedOut = client.ActiveRequestId;
            service.Now += RoomRules.RequestTimeout + 1;
            await Frame();
            Check(client.DirectoryState == RoomRequestState.TimedOut && !client.IsBusy
                && client.RequestState == RoomRequestState.Succeeded && rows.GetChildCount() == 15
                && !client.TryComplete(timedOut, () => new RoomResult(RoomFailure.None, [])),
                "automatic timeout preserves foreground status and rows; late results are rejected");
            await Due(); service.Complete(RoomFailure.Unavailable); await Frame();
            Check(rows.GetChildCount() == 15 && !client.HasSuccessfulDirectory,
                "background error keeps cached real rooms and never fabricates a companion entry");
            await Due(); service.Directory = []; service.Complete(RoomFailure.None); await Frame();
            Check(rows.GetChildCount() == 1 && client.HasSuccessfulDirectory, "only successful empty refresh offers a companion room");
            await Due();
            Check(rows.GetChildCount() == 1 && rows.GetChild(0).GetNode<Button>("Join").Disabled,
                "existing companion invitation stays visible but cannot use an unconfirmed pending result");
            service.Complete(RoomFailure.Unavailable); await Frame();
            Check(rows.GetChildCount() == 0, "failed refresh does not keep offering an unconfirmed companion room");

            page.RefreshRooms(); service.Complete(RoomFailure.Unavailable); await Frame();
            Check(client.Failure == RoomFailure.Unavailable, "manual query failure is visible");
            await Due(); service.Complete(RoomFailure.None); await Frame();
            Check(client.Failure == RoomFailure.None && client.RequestState == RoomRequestState.Succeeded,
                "a successful retry clears the previous directory error");

            page.Hide();
            int requests = service.SearchRequests;
            service.Now += 120;
            await Frame();
            Check(service.SearchRequests == requests, "hidden page stops periodic requests");
            page.Show(); await Frame();
            Check(service.SearchRequests == requests + 1, "reopening a stale page submits exactly one refresh");
            long superseded = client.ActiveRequestId;
            Check(client.Create("My room") && service.Pending.Operation == RoomOperation.Create,
                "explicit creation supersedes a background query");
            Check(!client.TryComplete(superseded, () => new RoomResult(RoomFailure.None, [])),
                "superseded search cannot affect the create operation");
            service.Complete(RoomFailure.None); await Frame();
            requests = service.SearchRequests;
            service.Now += 120; await Frame();
            Check(service.SearchRequests == requests, "room detail view does not poll the directory");
            page.OnBrowse(); service.Complete(RoomFailure.None); await Frame();
            var roomCode = client.JoinedCode;
            await Due();
            Check(client.IsBackgroundSearch && !client.IsBusy && page.PokerLobby.ActionsAvailable
                && client.SetAccess(RoomAccess.Public) == "ok" && client.JoinedCode == roomCode,
                "browsing refresh leaves membership and poker/host actions available");
            long beforeJoin = client.ActiveRequestId;
            Check(client.Join("R3") && service.Pending.Operation == RoomOperation.Join
                && !client.TryComplete(beforeJoin, () => new RoomResult(RoomFailure.None, [])),
                "explicit joining preempts refresh and invalidates its delayed result");
            service.Complete(RoomFailure.Full); await Frame();
            await Due();
            Check(client.IsBackgroundSearch, "failed membership operation can be followed by background refresh");
            service.Complete(RoomFailure.None); await Frame();
            Check(client.Failure == RoomFailure.Full && client.RequestState == RoomRequestState.Failed,
                "successful automatic refresh cannot erase a foreground join failure notice");
            await Due();
            long beforeRandom = client.ActiveRequestId;
            Check(client.FindAndJoin() && !client.TryComplete(beforeRandom, () => new RoomResult(RoomFailure.None, [])),
                "random join starts its own query rather than inheriting the background result");
            service.Directory = [new RoomListing("TARGET", "Target", "social", 2, 6)];
            service.Complete(RoomFailure.None);
            Check(service.Pending?.Operation == RoomOperation.Join && service.Pending.Value == "TARGET",
                "random join keeps its join intent after cancelling an automatic query");
            service.Complete(RoomFailure.Full); await Frame();
            await Due();
            long obsolete = client.ActiveRequestId;
            provider.RoomService = null; page.Configure(provider, null);
            Check(!client.TryComplete(obsolete, () => new RoomResult(RoomFailure.None, [])),
                "service replacement invalidates an old automatic response");
            await Frame();
            GD.Print("[RoomDirectoryAutoRefreshChecks] PASS cadence, visibility, coalescing, background status, stable rows/scroll, pointer deferral, failures/timeouts, priority and stale callbacks.");
        }
        finally { scroll.Free(); }
    }

    private sealed class Provider : IGamePlatformService, IPlatformRoomServiceProvider
    {
        public IRoomService RoomService { get; set; }
        public bool RoomRestartRequired => false;
        public string ProviderName => "Steam";
        public string StatusMessage => "";
        public bool IsAvailable => RoomService != null;
        public uint AppId => 2583700;
        public string PersonaName => "Empty directory test";
        public string AccountProvider => "steam";
        public string AccountId => "empty-directory-page-test";
        public event Action UserStatsReady { add { } remove { } }
        public void RunCallbacks() { }
        public bool OpenFriendsOverlay() => false;
        public PlatformAchievementReadResult ReadAchievementStates(IEnumerable<string> names)
            => new(false, "Test provider", Array.Empty<PlatformAchievementState>());
        public void Dispose() { }
    }

    private sealed class Service : IRoomService
    {
        public double Now { get; set; } = 1000;
        public RoomRequest Pending;
        public RoomListing[] Directory = Array.Empty<RoomListing>();
        public int CreateRequests;
        public int JoinRequests;
        public int SearchRequests;
        private RoomClient _client;
        private RoomCompanionPlan _companions;
        private long _revision;
        public void Request(RoomClient client, RoomRequest request)
        {
            _client = client;
            Pending = request;
            if (request.Operation == RoomOperation.Create) CreateRequests++;
            if (request.Operation == RoomOperation.Join) JoinRequests++;
            if (request.Operation == RoomOperation.Search) SearchRequests++;
        }
        public void Complete(RoomFailure failure)
        {
            var request = Pending;
            Pending = null;
            _client.TryComplete(request.Id, () =>
            {
                if (failure != RoomFailure.None) return new RoomResult(failure);
                if (request.Operation == RoomOperation.Search) return new RoomResult(RoomFailure.None, Directory);
                _client.BeginSession("CREATED" + CreateRequests);
                _companions = request.Arrival?.Plan.ForArrivalHandoff(request.Arrival.HostMemberId);
                Publish(_client);
                return RoomResult.Success;
            });
        }
        public void Publish(RoomClient client)
        {
            var members = new[] { new RoomMember(client.Id, client.Name, client.SkinId,
                client.HeadwearId, client.Reaction, 1) }.Concat(_companions?.MembersAt((long)Now) ?? Array.Empty<RoomMember>()).ToArray();
            client.Receive(new RoomSnapshot(client.JoinedCode, "Created room", "social", client.Id, ++_revision, members));
        }
        public void Cancel(RoomClient client, long requestId)
        {
            if (Pending?.Id == requestId) Pending = null;
        }
        public void Leave(RoomClient client) => client.BeginSession("");
        public void UpdateActivity(RoomClient client) { }
        public void UpdateAppearance(RoomClient client) { }
        public string SetGame(RoomClient client, string gameId) => "";
        public string SetAccess(RoomClient client, RoomAccess access) => "ok";
        public string Kick(RoomClient client, int memberId, long presence) => "";
        public string SendChat(RoomClient client, string text) => "";
    }
}
#endif
