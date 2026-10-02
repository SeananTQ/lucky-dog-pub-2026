#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace LuckyDogRise.Rooms;

// Real room page, fake service: no Steam requests, accounts or saved preferences.
internal static class RoomAccessPageChecks
{
    public static async Task Run(Node parent)
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
        static void Click(Button button) => button.EmitSignal(BaseButton.SignalName.Pressed);
        string originalLocale = TranslationServer.GetLocale();
        try
        {
            await Frame();
            var client = page.CurrentClient;
            client.Create("Access test");
            await Frame();
            var current = page.GetNode<Button>("Room/Access/Current");
            var choices = page.GetNode<Control>("Room/Access/Choices");
            var publicChoice = page.GetNode<Button>("Room/Access/Choices/Public");
            var friendsChoice = page.GetNode<Button>("Room/Access/Choices/FriendsOnly");
            var inviteChoice = page.GetNode<Button>("Room/Access/Choices/InviteOnly");
            var status = page.GetNode<Label>("Status");

            Check(!current.Disabled && !choices.Visible && publicChoice.ButtonPressed,
                "new host can edit default public access without opening an OS popup");
            Click(current);
            Check(choices.Visible, "host opens inline room access choices");
            Click(friendsChoice);
            await Frame();
            Check(client.View.Access == RoomAccess.FriendsOnly && !choices.Visible
                && friendsChoice.ButtonPressed && service.AccessWrites == 1,
                "host change renders the accepted snapshot and closes the choices");

            service.AccessError = "Rooms_AccessUpdateFailed";
            Click(current);
            Click(inviteChoice);
            await Frame();
            Check(client.View.Access == RoomAccess.FriendsOnly && friendsChoice.ButtonPressed
                && !inviteChoice.ButtonPressed && current.Text.Contains(L10n.Tr("Rooms_AccessFriendsOnly"))
                && status.Visible && status.Text == L10n.Tr("Rooms_AccessUpdateFailed"),
                "failed update retains the prior selection and shows a translated error");

            service.AccessError = "";
            Click(current);
            service.OwnerId = 2;
            service.Publish(client);
            await Frame();
            int writes = service.AccessWrites;
            Check(current.Disabled && !choices.Visible, "host transfer closes and disables old host choices");
            Click(inviteChoice);
            Click(current);
            Check(service.AccessWrites == writes && !choices.Visible,
                "stale selection or forced disabled-button signal cannot change permissions");

            service.Access = RoomAccess.InviteOnly;
            service.Publish(client);
            await Frame();
            Check(inviteChoice.ButtonPressed && current.Text.Contains(L10n.Tr("Rooms_AccessInviteOnly")),
                "ordinary members can read permission changes from the room snapshot");
            foreach (string locale in new[] { "zh_CN", "zh_TW", "en" })
            {
                L10n.SetLocale(locale, save: false);
                await Frame();
                Check(current.Text.Contains(L10n.Tr("Rooms_AccessInviteOnly"))
                    && !current.Text.Contains("Rooms_")
                    && current.TooltipText == L10n.Tr("Rooms_AccessNotOwner")
                    && current.TooltipText != "Rooms_AccessNotOwner",
                    "permission selection and read-only explanation follow language changes");
            }

            service.OwnerId = client.Id;
            service.Publish(client);
            await Frame();
            Check(!current.Disabled && inviteChoice.ButtonPressed,
                "new host gains editing while preserving the current room permission");
            Click(current);
            service.HoldSearch = true;
            client.Search();
            await Frame();
            Check(current.Disabled && !choices.Visible, "pending room request disables permission editing");
            Click(friendsChoice);
            Check(service.AccessWrites == writes, "pending request rejects a stale selection signal");
            client.Cancel();
            await Frame();
            Check(!current.Disabled, "cancelled request restores editing on the current room");

            Click(current);
            page.Hide();
            await Frame();
            Check(!choices.Visible, "closing the page also closes its inline choices");
            page.Show();
            Click(current);
            client.Leave();
            await Frame();
            Click(publicChoice);
            Check(service.AccessWrites == writes && !choices.Visible,
                "leaving invalidates the menu's captured room session");

            Check(InGameRoomPreview.FailureKey(RoomFailure.AccessDenied) == "Rooms_AccessDenied",
                "entry permission errors have the shared local bubble/status translation");
            GD.Print("[RoomAccessPageChecks] PASS host/member controls, rollback, host transfer, stale signals, pending requests, locales and entry error mapping (fake service).");
        }
        finally
        {
            L10n.SetLocale(originalLocale, save: false);
            page.Free();
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Provider : IGamePlatformService, IPlatformRoomServiceProvider
    {
        public IRoomService RoomService { get; set; }
        public bool RoomRestartRequired => false;
        public string ProviderName => "Steam";
        public string StatusMessage => "";
        public bool IsAvailable => true;
        public uint AppId => 2583700;
        public string PersonaName => "Access test host";
        public string AccountProvider => "steam";
        public string AccountId => "room-access-page-test";
        public event Action UserStatsReady { add { } remove { } }
        public void RunCallbacks() { }
        public bool OpenFriendsOverlay() => false;
        public PlatformAchievementReadResult ReadAchievementStates(IEnumerable<string> names)
            => new(false, "Test provider", Array.Empty<PlatformAchievementState>());
        public void Dispose() { }
    }

    private sealed class Service : IRoomService
    {
        public double Now => 500;
        public int OwnerId = 1;
        public RoomAccess Access = RoomAccess.Public;
        public string AccessError = "";
        public int AccessWrites;
        public bool HoldSearch;
        private long _revision;
        public void Request(RoomClient client, RoomRequest request)
        {
            if (request.Operation == RoomOperation.Search && HoldSearch) return;
            client.TryComplete(request.Id, () =>
            {
                if (request.Operation == RoomOperation.Search)
                    return new RoomResult(RoomFailure.None, Array.Empty<RoomListing>());
                client.BeginSession("ACCESS1");
                Publish(client);
                return RoomResult.Success;
            });
        }
        public void Publish(RoomClient client) => client.Receive(new RoomSnapshot("ACCESS1", "Access test", "social",
            OwnerId, ++_revision, new[]
            {
                new RoomMember(1, client.Name, client.SkinId, client.HeadwearId, client.Reaction, 1),
                new RoomMember(2, "Remote host", 1001, 0, 1001, 2)
            }) { Access = Access });
        public string SetAccess(RoomClient client, RoomAccess access)
        {
            AccessWrites++;
            if (AccessError.Length > 0) return AccessError;
            Access = access;
            Publish(client);
            return "ok";
        }
        public void Cancel(RoomClient client, long id) { }
        public void Leave(RoomClient client) => client.BeginSession("");
        public void UpdateActivity(RoomClient client) { }
        public void UpdateAppearance(RoomClient client) { }
        public string SetGame(RoomClient client, string gameId) => "";
        public string Kick(RoomClient client, int memberId, long presence) => "";
        public string SendChat(RoomClient client, string text) => "";
    }
}
#endif
