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
            var failures = new List<(RoomFailure Reason, bool TimedOut)>();
            client.JoinFailed += (reason, timedOut) => failures.Add((reason, timedOut));
            Check(client.IsBusy && rows.GetChildCount() == 0 && service.CreateRequests == 0,
                "initial empty array during a pending search does not fabricate an entry");
            service.Complete(RoomFailure.Unavailable);
            await Frame();
            Check(rows.GetChildCount() == 0 && !page.GetNode<Label>("Lobby/Empty").Visible,
                "search error is not presented as an empty successful directory");

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
            Check(service.CreateRequests == 1 && service.Pending.Operation == RoomOperation.Create && client.IsBusy,
                "joining the local row dispatches one ordinary Create despite repeated clicks");
            await Frame();
            Check(rows.GetChildCount() == 0 && page.GetNode<Button>("Lobby/CreateRow/Create").Disabled,
                "pending creation hides the virtual row and locks duplicate operations");
            service.Complete(RoomFailure.None);
            await Frame();
            Check(client.View.OwnerId == client.Id && client.View.Members.Length == 4
                && client.View.Members.Count(member => member.IsCompanion) == 3 && service.JoinRequests == 0,
                "explicit virtual-row join becomes a real room owned by the player with three companions");
            string firstCode = client.JoinedCode;

            page.OnBrowse();
            service.Complete(RoomFailure.None);
            await Frame();
            Check(client.JoinedCode == firstCode && rows.GetChildCount() == 1,
                "browsing an empty directory while already in a room also offers the entry");
            var stableRow = rows.GetChild(0);
            service.Publish(client);
            await Frame();
            service.Publish(client);
            await Frame();
            Check(ReferenceEquals(stableRow, rows.GetChild(0)),
                "background room snapshots do not rebuild the empty-directory row or lose a pending click");
            Click(Entry());
            service.Complete(RoomFailure.Unavailable);
            await Frame();
            Check(client.JoinedCode == firstCode && client.View.Members.Length == 4
                && client.RequestState == RoomRequestState.Failed && rows.GetChildCount() == 0
                && failures.Count == 1 && failures[0] == (RoomFailure.Unavailable, false),
                "failed virtual creation retains the previous room and uses the join-failure notice path");

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
            GD.Print("[RoomEmptyDirectoryPageChecks] PASS honest empty-search gating, local 3/6 row, explicit single create, stale/hidden callbacks, stable row, retained old room and ordinary failures (fake service).");
        }
        finally { page.Free(); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
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
        private RoomClient _client;
        private RoomCompanionPlan _companions;
        private long _revision;
        public void Request(RoomClient client, RoomRequest request)
        {
            _client = client;
            Pending = request;
            if (request.Operation == RoomOperation.Create) CreateRequests++;
            if (request.Operation == RoomOperation.Join) JoinRequests++;
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
                _companions = RoomCompanionPlan.Create(new[] { _client.SkinId }, new[] { 0 }, (long)Now, 91);
                Publish(_client);
                return RoomResult.Success;
            });
        }
        public void Publish(RoomClient client)
        {
            var members = new[] { new RoomMember(client.Id, client.Name, client.SkinId,
                client.HeadwearId, client.Reaction, 1) }.Concat(_companions.MembersAt((long)Now)).ToArray();
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
