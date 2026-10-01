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

            // Drive the actual page's _Process, never AdvanceTo directly. This
            // service epoch deliberately differs from Godot's process uptime.
            page.Show();
            await Frame();
            double firstSentAt = firstService.Now;
            Check(first.SendChat("local clock check") == "", "provider accepts local chat");
            firstService.DeliverRemote(first, "remote clock check", age: 2);
            await Frame();
            Check(first.Bubbles.Count == 2, "production page initially retains local and aged remote bubbles");
            firstService.Now = firstSentAt + 3.99;
            await Frame();
            Check(first.Bubbles.ContainsKey(1) && first.Bubbles.ContainsKey(2),
                "remote bubble retains its remaining four-second lifetime before the deadline");
            page.Hide();
            firstService.Now = firstSentAt + 4.01;
            await Frame();
            Check(!page.Visible && !first.Bubbles.ContainsKey(2) && first.Bubbles.ContainsKey(1),
                "hidden production page expires aged remote bubble using the service clock");
            firstService.Now = firstSentAt + 5.99;
            await Frame();
            Check(first.Bubbles.ContainsKey(1), "local bubble remains just before its six-second deadline");
            firstService.Now = firstSentAt + 6.01;
            await Frame();
            Check(first.Bubbles.Count == 0,
                "hidden production page expires local bubble after six seconds without manual client clock advancement");

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
            var secondService = new Service { Now = 100 };
            provider.RoomService = secondService;
            await Frame();
            var second = page.CurrentClient;
            Check(second != null && !ReferenceEquals(first, second) && second.JoinedCode.Length == 0,
                "reconnected provider starts a fresh unjoined client");
            Check(second.Listings.Length == 1 && changes.Contains(second),
                "reconnected provider can search and attach again");
            second.Create("Reconnected room");
            await Frame();
            Check(second.View?.Members.Length == 2, "reconnected provider can create room");
            double secondSentAt = secondService.Now;
            Check(second.SendChat("new service epoch") == "", "reconnected provider accepts local chat");
            await Frame();
            Check(second.Bubbles.ContainsKey(1), "new client accepts the replacement service clock epoch");
            secondService.Now = secondSentAt + 5.99;
            await Frame();
            Check(second.Bubbles.ContainsKey(1), "reconnected bubble remains before its deadline");
            secondService.Now = secondSentAt + 6.01;
            await Frame();
            Check(second.Bubbles.Count == 0, "reconnected hidden page expires chat with its new service clock");
            page.Free();
            Check(second.JoinedCode.Length == 0, "page disposal leaves its current service");
            await CheckKickInteraction(parent);
            await CheckChatInteraction(parent);
            GD.Print("[SteamRoomPageChecks] PASS production provider, join failure, appearance dispatch, service-clock chat expiry while hidden, reconnect clock reset, disposal (fake transport).");
        }
        finally
        {
            if (GodotObject.IsInstanceValid(page)) page.Free();
        }
    }

    private static async Task CheckKickInteraction(Node parent)
    {
        var service = new Service();
        var provider = new Provider { RoomService = service };
        var page = GD.Load<PackedScene>("res://Scenes/Rooms/InGameRoomPage.tscn")
            .Instantiate<InGameRoomPreview>();
        page.Configure(provider, null);
        parent.AddChild(page);
        async Task Frame()
        {
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        try
        {
            await Frame();
            var client = page.CurrentClient;
            client.Join("PROVIDER1");
            await Frame();
            var members = page.GetNode<VBoxContainer>("Room/Members");
            Button Kick(int index) => members.GetChild(index).GetNode<Button>("Kick");
            var confirm = page.GetNode<Control>("Room/KickConfirm");
            var accept = confirm.GetNode<Button>("Actions/Confirm");
            var cancel = confirm.GetNode<Button>("Actions/Cancel");
            var status = page.GetNode<Label>("Status");
            Check(!Kick(0).Visible && !Kick(1).Visible, "non-host has no removal controls");

            service.OwnerId = client.Id;
            service.Publish(client);
            await Frame();
            Check(!Kick(0).Visible && Kick(1).Visible,
                "host migration exposes removal controls only for other members");
            // Godot keeps the source key in Button.Text and translates its
            // shaped display. Compare that display size to literal translated
            // text before any language switch can refresh a stale text cache.
            var kick = Kick(1);
            var translatedKick = L10n.Tr(kick.Text);
            Check(translatedKick != kick.Text, "removal translation is registered before its first use");
            var expectedKick = new Button
            {
                Text = translatedKick,
                ThemeTypeVariation = kick.ThemeTypeVariation,
                AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
                Visible = false,
            };
            kick.GetParent().AddChild(expectedKick);
            await Frame();
            Check(Mathf.IsEqualApprox(kick.GetMinimumSize().X, expectedKick.GetMinimumSize().X),
                "first removal button is shaped with translated text without changing language");
            expectedKick.Free();
            Kick(1).EmitSignal(BaseButton.SignalName.Pressed);
            Check(confirm.Visible && service.KickRequests == 0
                && confirm.GetNode<Label>("Name").Text == "Remote member", "removal waits for a named confirmation");
            cancel.EmitSignal(BaseButton.SignalName.Pressed);
            Check(!confirm.Visible && service.KickRequests == 0, "canceling confirmation does not remove anyone");

            Kick(1).EmitSignal(BaseButton.SignalName.Pressed);
            service.OwnerId = 2;
            service.Publish(client);
            // No frame between authority loss and an already queued UI signal.
            accept.EmitSignal(BaseButton.SignalName.Pressed);
            await Frame();
            Check(!confirm.Visible && service.KickRequests == 0 && !Kick(1).Visible,
                "host loss closes confirmation and rejects stale accept signals");

            service.OwnerId = client.Id;
            service.Publish(client);
            await Frame();
            var staleRowButton = Kick(1);
            staleRowButton.EmitSignal(BaseButton.SignalName.Pressed);
            service.RemotePresence++;
            service.Publish(client);
            staleRowButton.EmitSignal(BaseButton.SignalName.Pressed);
            accept.EmitSignal(BaseButton.SignalName.Pressed);
            await Frame();
            Check(!confirm.Visible && service.KickRequests == 0,
                "rejoined target cannot be removed by an old row or confirmation");

            Kick(1).EmitSignal(BaseButton.SignalName.Pressed);
            client.Leave();
            client.Join("PROVIDER1");
            accept.EmitSignal(BaseButton.SignalName.Pressed);
            await Frame();
            Check(!confirm.Visible && service.KickRequests == 0, "new room session invalidates old confirmation");

            Kick(1).EmitSignal(BaseButton.SignalName.Pressed);
            accept.EmitSignal(BaseButton.SignalName.Pressed);
            await Frame();
            Check(service.KickRequests == 1 && service.LastKickMember == 2
                && service.LastKickPresence == service.RemotePresence && !confirm.Visible
                && status.Text == L10n.Tr("Rooms_KickSent"), "confirmed action passes the current membership and shows request result");
            service.KickError = "Rooms_KickUnavailable";
            Kick(1).EmitSignal(BaseButton.SignalName.Pressed);
            accept.EmitSignal(BaseButton.SignalName.Pressed);
            await Frame();
            Check(status.Text == L10n.Tr("Rooms_KickUnavailable") && status.Text != "Rooms_KickUnavailable",
                "failed removal displays translated error in the room page");

            int searches = service.SearchRequests;
            client.RemoveFromRoom(RoomFailure.Removed);
            await Frame();
            Check(page.GetNode<Control>("Lobby").Visible && !page.GetNode<Control>("Room").Visible
                && status.Visible && status.Text == L10n.Tr("Rooms_Removed") && status.Text != "Rooms_Removed"
                && service.SearchRequests == searches && !client.IsBusy,
                "forced exit returns to directory with persistent reason and no automatic search or rejoin");
            await Frame();
            Check(status.Text == L10n.Tr("Rooms_Removed"), "ordinary frames retain the removal reason");
            service.RejoinFailure = RoomFailure.Banned;
            client.Join("PROVIDER1");
            await Frame();
            Check(client.JoinedCode.Length == 0 && status.Text == L10n.Tr("Rooms_Banned")
                && status.Text != "Rooms_Banned" && service.SearchRequests == searches,
                "denied reentry stays in directory and shows its own reason");
            page.RefreshRooms();
            await Frame();
            Check(!status.Visible && service.SearchRequests == searches + 1,
                "explicit refresh clears the old notice without implicitly joining");
            GD.Print("[RoomKickPageChecks] PASS ownership, confirmation, stale membership/session, translated errors and forced-exit notice (fake service).");
        }
        finally { page.Free(); }
    }

    private static async Task CheckChatInteraction(Node parent)
    {
        var sandbox = new RoomSandbox();
        int skin = LubanData.Tables.TbDogSkin.DataList[0].Id;
        using var client = sandbox.AddClient(1, "local", skin);
        sandbox.Create(client, "chat interaction check");
        sandbox.Tick(0);
        var desktop = GD.Load<PackedScene>("res://Scenes/Rooms/RoomDesktopPreview.tscn")
            .Instantiate<RoomDesktopPreview>();
        parent.AddChild(desktop);
        async Task Frame()
        {
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        try
        {
            desktop.Present(client, new Rect2(0, 0, 800, 600), 1, 420, 75, true);
            var chat = desktop.LocalChat;
            var open = chat.GetNode<Button>("Open");
            var send = chat.GetNode<Button>("Composer/Content/Actions/Send");
            var input = chat.GetNode<LineEdit>("Composer/Content/Input");
            bool blocked = false;
            chat.InteractionAllowed = () => !blocked;
            int sendEvents = 0;
            chat.SendRequested += _ => sendEvents++;
            chat.Present(true, true, "");
            await Frame();
            Check(open.Visible, "available desktop chat responds to dog hover");
            var entryPoint = open.GetGlobalRect().GetCenter();

            foreach (var trigger in new[] { "button", "enter", "frame" })
            {
                blocked = false;
                chat.OpenChat();
                input.Text = "preserved draft";
                Check(chat.Editing, "chat opens before reveal starts");
                var composerPoint = chat.ComposerRectForSmoke.GetCenter();
                // Deliberately change eligibility without a frame between it and
                // the signal: stale clicks/Enter must not reach the room service.
                blocked = true;
                if (trigger == "button") send.EmitSignal(BaseButton.SignalName.Pressed);
                else if (trigger == "enter") input.EmitSignal(LineEdit.SignalName.TextSubmitted, input.Text);
                else await Frame();
                sandbox.Tick(0);
                Check(!chat.Editing && !input.HasFocus() && input.Text == "preserved draft",
                    "reveal suspension closes composer and preserves unfocused draft: " + trigger);
                Check(sendEvents == 0 && client.Bubbles.Count == 0 && sandbox.PendingCount == 0,
                    "blocked send never dispatches or queues a chat: " + trigger);
                Check(!chat.ContainsPoint(entryPoint) && !chat.ContainsPoint(composerPoint),
                    "suspended chat removes its entry and composer from window hit testing");
                chat.Present(true, true, "");
                await Frame();
                open.EmitSignal(BaseButton.SignalName.Pressed);
                chat.OpenChat();
                Check(!open.Visible && !chat.Editing, "repeated hover and open attempts stay blocked");
            }

            using var remote = sandbox.AddClient(2, "remote", skin);
            sandbox.Join(remote, client.JoinedCode);
            sandbox.Tick(0);
            Check(remote.SendChat("incoming during reveal") == "", "remote chat remains available");
            sandbox.Tick(0);
            Check(client.Bubbles.ContainsKey(remote.Id), "reveal suspension does not block incoming messages");
            sandbox.Tick(7);
            Check(client.Bubbles.Count == 0, "incoming messages expire normally during reveal");

            blocked = false;
            await Frame();
            Check(!chat.Editing && sendEvents == 0 && client.Bubbles.Count == 0,
                "ending or canceling reveal never reopens or auto-sends the draft");
            chat.Present(true, true, "");
            await Frame();
            Check(open.Visible, "hover entry recovers after reveal ends");
            chat.OpenChat();
            Check(input.Text == "preserved draft", "manual reopen restores the draft");
            input.EmitSignal(LineEdit.SignalName.TextSubmitted, input.Text);
            sandbox.Tick(0);
            Check(sendEvents == 1 && !chat.Editing && input.Text.Length == 0
                && client.Bubbles[client.Id].Text == "preserved draft",
                "explicit sending recovers through the real desktop forwarding handler");
            GD.Print("[RoomChatInteractionChecks] PASS hover, stale button/Enter, frame suspension, draft, hit testing, incoming expiry and manual recovery.");
        }
        finally { desktop.Free(); }
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
        public double Now { get; set; } = 1_000_000;
        public int AppearanceUpdates;
        public int OwnerId = 2;
        public long RemotePresence = 2;
        public int SearchRequests;
        public int KickRequests;
        public int LastKickMember;
        public long LastKickPresence;
        public string KickError = "";
        public RoomFailure RejoinFailure;
        private long _revision;
        private long _chatSequence;
        public void Request(RoomClient client, RoomRequest request)
            => client.TryComplete(request.Id, () =>
            {
                if (request.Operation == RoomOperation.Search)
                {
                    SearchRequests++;
                    return new RoomResult(RoomFailure.None,
                        new[] { new RoomListing("PROVIDER1", "Provider room", "social", 1, 6) });
                }
                if (request.Operation == RoomOperation.Join && request.Value != "PROVIDER1")
                    return new RoomResult(RoomFailure.NotFound);
                if (request.Operation == RoomOperation.Join && RejoinFailure != RoomFailure.None)
                    return new RoomResult(RejoinFailure);
                client.BeginSession("PROVIDER1");
                Publish(client);
                return RoomResult.Success;
            });
        public void Publish(RoomClient client) => client.Receive(new RoomSnapshot("PROVIDER1",
            "Provider room", "social", OwnerId, ++_revision, new[]
            {
                new RoomMember(1, client.Name, client.SkinId, client.HeadwearId, client.Reaction, 1),
                new RoomMember(2, "Remote member", 1001, 0, 1001, RemotePresence),
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
        public string Kick(RoomClient client, int memberId, long presence)
        {
            KickRequests++;
            LastKickMember = memberId;
            LastKickPresence = presence;
            return KickError;
        }
        public string SendChat(RoomClient client, string text)
        {
            client.Receive(new RoomChat(++_chatSequence, client.JoinedCode, 1, 1,
                text, Now + RoomRules.ChatLifetime), Now);
            return "";
        }
        public void DeliverRemote(RoomClient client, string text, double age)
            => client.Receive(new RoomChat(++_chatSequence, client.JoinedCode, 2, 2,
                text, Now + RoomRules.ChatLifetime - age), Now);
    }
}
#endif
