#if DEBUG && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace LuckyDogRise.Rooms;

// Exercises the production provider path without opening Steam or touching saves.
// Deliberately not RoomSandbox: the page must construct its own local client.
internal static class SteamRoomPageChecks
{
    public static async Task Run(Node parent)
    {
        var provider = new Provider { RoomService = new Service() };
        var firstService = (Service)provider.RoomService;
        var page = GD.Load<PackedScene>("res://Scenes/Rooms/InGameRoomPage.tscn")
            .Instantiate<InGameRoomPreview>();
        page.Configure(provider, null);
        var changes = new List<RoomClient>();
        page.ClientChanged += changes.Add;
        parent.AddChild(page);
        page.Hide();
        async Task Frame()
        {
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        try
        {
            await Frame();
            var first = page.CurrentClient;
            Check(first != null && first.Name == "Steam page test", "provider persona constructs local client");
            Check(first.Listings.Length == 1 && first.Listings[0].Code == "PROVIDER1",
                "production page searches supplied service without seeding mock dogs");
            first.Join("PROVIDER1");
            await Frame();
            Check(first.View?.Members.Length == 2 && first.View.Members[1].Name == "Remote member",
                "production page receives service membership");
            Check(page.GetNode<Control>("Room").Visible, "joined production room is displayed");
            first.Join("MISSING");
            await Frame();
            Check(first.JoinedCode == "PROVIDER1" && first.RequestState == RoomRequestState.Failed,
                "failed join retains current production room");
            first.SetAppearance(1001, 0, 1001);
            Check(firstService.AppearanceUpdates > 0, "appearance reaches supplied service");

            provider.RoomService = null;
            await Frame();
            Check(page.CurrentClient == null && first.JoinedCode.Length == 0,
                "lost provider clears old local membership");
            Check(changes.Contains(null) && page.GetNode<Button>("Lobby/CreateRow/Create").Disabled,
                "unavailable provider emits detach and disables room actions");
            provider.RoomRestartRequired = true;
            await Frame();
            Check(page.GetNode<Label>("Notice").Text == L10n.Tr("Rooms_SteamRestartRequired")
                && page.GetNode<Label>("Notice").Text != "Rooms_SteamRestartRequired",
                "unsettled native request shows translated restart guidance even while provider stays null");
            provider.RoomRestartRequired = false;
            provider.RoomService = new Service();
            await Frame();
            var second = page.CurrentClient;
            Check(second != null && !ReferenceEquals(first, second) && second.JoinedCode.Length == 0,
                "reconnected provider starts a fresh unjoined client");
            Check(second.Listings.Length == 1 && changes.Contains(second),
                "reconnected provider can search and attach again");
            second.Create("Reconnected room");
            await Frame();
            Check(second.View?.Members.Length == 2, "reconnected provider can create room");
            page.Free();
            Check(second.JoinedCode.Length == 0, "page disposal leaves its current service");
            GD.Print("[SteamRoomPageChecks] PASS production provider, join failure, appearance dispatch, disconnect, reconnect, disposal (fake transport).");
        }
        finally
        {
            if (GodotObject.IsInstanceValid(page)) page.Free();
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Provider : IGamePlatformService, IPlatformRoomServiceProvider
    {
        public IRoomService RoomService { get; set; }
        public bool RoomRestartRequired { get; set; }
        public string ProviderName => "Steam";
        public string StatusMessage => "";
        public bool IsAvailable => RoomService != null;
        public uint AppId => 2583700;
        public string PersonaName => "Steam page test";
        public string AccountProvider => "steam";
        public string AccountId => "page-test";
        public event Action UserStatsReady { add { } remove { } }
        public void RunCallbacks() { }
        public bool OpenFriendsOverlay() => false;
        public PlatformAchievementReadResult ReadAchievementStates(IEnumerable<string> names)
            => new(false, "Test provider", Array.Empty<PlatformAchievementState>());
        public void Dispose() { }
    }

    private sealed class Service : IRoomService
    {
        public int AppearanceUpdates;
        private long _revision;
        public void Request(RoomClient client, RoomRequest request)
            => client.TryComplete(request.Id, () =>
            {
                if (request.Operation == RoomOperation.Search)
                    return new RoomResult(RoomFailure.None,
                        new[] { new RoomListing("PROVIDER1", "Provider room", "social", 1, 6) });
                if (request.Operation == RoomOperation.Join && request.Value != "PROVIDER1")
                    return new RoomResult(RoomFailure.NotFound);
                client.BeginSession("PROVIDER1");
                Publish(client);
                return RoomResult.Success;
            });
        private void Publish(RoomClient client) => client.Receive(new RoomSnapshot("PROVIDER1",
            "Provider room", "social", 2, ++_revision, new[]
            {
                new RoomMember(1, client.Name, client.SkinId, client.HeadwearId, client.Reaction, 1),
                new RoomMember(2, "Remote member", 1001, 0, 1001, 2),
            }));
        public void Cancel(RoomClient client, long id) { }
        public void Leave(RoomClient client) => client.BeginSession("");
        public void UpdateActivity(RoomClient client) { }
        public void UpdateAppearance(RoomClient client)
        {
            AppearanceUpdates++;
            if (client.JoinedCode.Length > 0) Publish(client);
        }
        public string SetGame(RoomClient client, string gameId) => "";
        public string SendChat(RoomClient client, string text) => "Not part of this test";
    }
}
#endif
