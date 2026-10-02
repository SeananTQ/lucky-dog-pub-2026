#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace LuckyDogRise.Rooms;

internal static class RoomSettingsPageChecks
{
    public static async Task Run(Node parent)
    {
        var service = new Service();
        var page = GD.Load<PackedScene>("res://Scenes/Rooms/InGameRoomPage.tscn").Instantiate<InGameRoomPreview>();
        page.Configure(new Provider(service), null);
        parent.AddChild(page);
        var confirm = GD.Load<PackedScene>("res://Scenes/Prefabs/ConfirmOverlay.tscn")
            .Instantiate<ConfirmOverlayController>();
        parent.AddChild(confirm);
        page.BindKickConfirmation(confirm);
        var accept = confirm.GetNode<Button>("OverlayPanel/Margin/Content/ButtonRow/ConfirmButton");
        var cancel = confirm.GetNode<Button>("OverlayPanel/Margin/Content/ButtonRow/CancelButton");
        async Task Frame()
        {
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        static void Click(Button button) => button.EmitSignal(BaseButton.SignalName.Pressed);
        string locale = TranslationServer.GetLocale();
        try
        {
            await Frame();
            var client = page.CurrentClient;
            client.Create("Rooms_Social");
            await Frame();
            var name = page.GetNode<LineEdit>("Room/TitleRow/Name");
            var count = page.GetNode<Label>("Room/TitleRow/Count");
            var members = page.GetNode<VBoxContainer>("Room/Members");
            var code = page.GetNode<LinkButton>("Room/CodeRow/Code");
            void Draft(string text) { name.GrabFocus(); name.Text = text; }
            void Submit() => name.EmitSignal(LineEdit.SignalName.TextSubmitted, name.Text);
            Check(name.Editable && name.Text == "Rooms_Social" && count.Text == "3/6"
                && name.AutoTranslateMode == Node.AutoTranslateModeEnum.Disabled,
                "room name is editable host content with a separate population count");
            Check(members.GetChild(0).GetNode<Label>("Name").Text == "♛ Rooms_Social",
                "member list preserves the local Steam persona instead of replacing it with You");
            var originalRows = members.GetChildren().Select(row => row.GetInstanceId()).ToArray();
            var originalButtons = members.GetChildren().Select(row => row.GetNode<Button>("Kick").GetInstanceId()).ToArray();
            void CheckOriginalRows(string reason)
            {
                Check(members.GetChildren().Select(row => row.GetInstanceId()).SequenceEqual(originalRows)
                    && members.GetChildren().Select(row => row.GetNode<Button>("Kick").GetInstanceId()).SequenceEqual(originalButtons),
                    reason);
            }
            foreach (string nextLocale in new[] { "zh_CN", "en" })
            {
                L10n.SetLocale(nextLocale, save: false);
                await Frame();
                Check(name.Text == "Rooms_Social" && name.PlaceholderText == L10n.Tr("Rooms_Name")
                    && page.GetNode<Label>("Room/Game/Name").Text == string.Format(L10n.Tr("Rooms_GameFormat"), L10n.Tr("Rooms_Social")),
                    "locale changes translate game labels and placeholders but never the room name");
                CheckOriginalRows("locale changes preserve live member rows and their buttons");
            }
            Draft("  New room name  ");
            service.ActivitySequence++;
            service.Reaction++;
            service.TongueActive = true;
            service.Publish(client);
            await Frame();
            Check(name.Text == "  New room name  " && service.NameWrites == 0,
                "background companion/member snapshots preserve an active name draft");
            CheckOriginalRows("expression and activity refreshes preserve buttons between pointer press and release");
            name.ReleaseFocus();
            await Frame();
            Check(service.Name == "New room name" && name.Text == service.Name && service.NameWrites == 1,
                "focus loss commits one trimmed name");
            CheckOriginalRows("room name changes preserve live member buttons");
            service.Access = RoomAccess.FriendsOnly;
            service.RemoteName = "Updated Steam persona";
            service.Publish(client);
            await Frame();
            CheckOriginalRows("permission and persona updates change presentation without replacing member buttons");
            Check(members.GetChild(1).GetNode<Label>("Name").Text == service.RemoteName,
                "a stable member row still receives the latest persona");
            Draft("Enter name");
            Submit();
            await Frame();
            Check(service.Name == "Enter name" && service.NameWrites == 2 && !name.HasFocus(),
                "Enter commits once and releases editing");
            Draft("cancel this");
            page._Input(new InputEventKey { Pressed = true, Keycode = Key.Escape });
            await Frame();
            Check(name.Text == "Enter name" && service.NameWrites == 2, "Escape restores the confirmed name without writing");
            Draft(" ");
            Submit();
            await Frame();
            Check(name.Text == "Enter name" && service.NameWrites == 2
                && page.GetNode<Label>("Status").Text == L10n.Tr("Rooms_InvalidName"),
                "blank edits roll back with the existing validation message");
            name.MaxLength = 0; // Allow a hostile programmatic over-limit input.
            Draft(new string('x', 41));
            Submit();
            await Frame();
            Check(name.Text == "Enter name" && service.NameWrites == 2, "over-limit text cannot bypass name validation");
            name.MaxLength = 40;
            service.NameError = "Rooms_NameUpdateFailed";
            Draft("failed name");
            Submit();
            await Frame();
            Check(name.Text == "Enter name" && service.Name == "Enter name" && service.NameWrites == 3,
                "backend failure restores the confirmed name");
            service.NameError = "";
            Draft("candidate still composing");
            bool composing = true;
            page.NameImeForSmoke = () => composing;
            page._Input(new InputEventKey { Pressed = true, Keycode = Key.Enter });
            composing = false; // IME finalizes its candidate before TextSubmitted.
            Submit();
            Check(service.NameWrites == 3, "IME candidate confirmation cannot also submit the room name");
            page._Input(new InputEventKey { Pressed = true, Keycode = Key.A });
            composing = true;
            name.ReleaseFocus();
            await Frame();
            Check(name.Text == "Enter name" && service.NameWrites == 3,
                "blur during unfinished composition rolls back rather than publishing partial text");
            composing = false;
            page.NameImeForSmoke = null;
            Draft("former host edit");
            service.Owner = 2;
            service.Publish(client);
            Submit();
            await Frame();
            Check(!name.Editable && service.NameWrites == 3 && name.Text == "Enter name",
                "host transfer invalidates the old owner's captured draft");
            CheckOriginalRows("host transfer updates existing member rows without replacing them");
            Check(members.GetChildren().All(row => !row.GetNode<Button>("Kick").Visible)
                && members.GetChild(1).GetNode<Label>("Name").Text == "♛ " + service.RemoteName,
                "host transfer updates both kick visibility and the host crown");
            service.Owner = 1;
            service.Publish(client);
            await Frame();
            CheckOriginalRows("regaining host authority reuses the original member buttons");
            Check(members.GetChild(1).GetNode<Button>("Kick").Visible
                && members.GetChild(2).GetNode<Button>("Kick").Visible,
                "regaining host authority enables human and companion removal");
            Draft("previous session edit");
            client.BeginSession("SECOND_ROOM");
            service.Name = "Other room";
            service.Publish(client);
            Submit();
            await Frame();
            Check(name.Text == "Other room" && service.NameWrites == 3,
                "session changes cannot submit an old draft to the new room");
            Check(members.GetChildren().Select(row => row.GetInstanceId()).All(id => !originalRows.Contains(id)),
                "a new room session replaces old member callback contexts");
            client.BeginSession("WWWWWWWWWWWWW");
            service.Publish(client);
            await Frame();
            Check(code.Text.Length == 13 && code.Underline == LinkButton.UnderlineMode.Always
                && code.GetParent() == page.GetNode("Room/CodeRow")
                && page.GetNodeOrNull("Room/CodeHeader") == null,
                "full room code is an underlined copy link on the action row with no redundant title");
            code.EmitSignal(BaseButton.SignalName.Pressed);
            Check(page.CurrentClient.JoinedCode == code.Text, "clicking the code leaves room identity unchanged");

            var kick = members.GetChild(2).GetNode<Button>("Kick");
            var beforeReplacementRows = members.GetChildren().Select(row => row.GetInstanceId()).ToArray();
            ulong beforeReplacementButton = kick.GetInstanceId();
            Check(kick.Visible && !kick.Disabled, "host can remove a companion using the member-row action");
            Click(kick);
            Check(confirm.Visible && confirm.GetNode<Label>("OverlayPanel/Margin/Content/MessageBg/Message").Text
                .StartsWith("熬夜的程序员\n", StringComparison.Ordinal),
                "companion removal uses the shared named confirmation");
            Click(cancel);
            Check(!confirm.Visible && service.KickWrites == 0 && client.View.Members.Any(member => member.IsCompanion),
                "canceling the companion confirmation leaves its membership unchanged");
            Click(kick);
            service.CompanionPresence++;
            service.Publish(client);
            await Frame();
            Check(members.GetChild(0).GetInstanceId() == beforeReplacementRows[0]
                && members.GetChild(1).GetInstanceId() == beforeReplacementRows[1]
                && members.GetChild(2).GetInstanceId() != beforeReplacementRows[2]
                && members.GetChild(2).GetNode<Button>("Kick").GetInstanceId() != beforeReplacementButton,
                "only the replaced companion generation gets a new row and callback");
            Click(accept);
            Check(service.KickWrites == 0, "a replaced companion generation invalidates old confirmation");
            Click(members.GetChild(2).GetNode<Button>("Kick"));
            Click(accept);
            await Frame();
            Check(service.KickWrites == 1 && client.View.Members.All(member => !member.IsCompanion),
                "confirmed companion removal dispatches its negative identity and current presence");
            Check(members.GetChild(0).GetInstanceId() == beforeReplacementRows[0]
                && members.GetChild(1).GetInstanceId() == beforeReplacementRows[1],
                "removing a companion preserves every remaining member button");
            GD.Print("[RoomSettingsPageChecks] PASS editable names, draft retention, blur/Enter/Escape, validation/IME guards, rollback, owner/session changes, literal personas, code link, stable member buttons and companion removal (fake service).");
        }
        finally { L10n.SetLocale(locale, save: false); page.Free(); confirm.Free(); }
    }

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private sealed class Provider(Service service) : IGamePlatformService, IPlatformRoomServiceProvider
    {
        public IRoomService RoomService => service;
        public bool RoomRestartRequired => false;
        public string ProviderName => "Steam";
        public string StatusMessage => "";
        public bool IsAvailable => true;
        public uint AppId => 2583700;
        public string PersonaName => "Rooms_Social";
        public string AccountProvider => "steam";
        public string AccountId => "room-settings-test";
        public event Action UserStatsReady { add { } remove { } }
        public void RunCallbacks() { }
        public bool OpenFriendsOverlay() => false;
        public PlatformAchievementReadResult ReadAchievementStates(IEnumerable<string> names)
            => new(false, "Test provider", Array.Empty<PlatformAchievementState>());
        public void Dispose() { }
    }
    private sealed class Service : IRoomService
    {
        public double Now => 1000;
        public string Name = "Rooms_Social";
        public string NameError = "";
        public int Owner = 1;
        public int NameWrites;
        public int KickWrites;
        public string RemoteName = "Another Steam persona";
        public int Reaction = 1001;
        public long ActivitySequence;
        public bool TongueActive;
        public RoomAccess Access = RoomAccess.Public;
        public long CompanionPresence = 91;
        private bool _companion = true;
        private long _revision;
        public void Request(RoomClient client, RoomRequest request) => client.TryComplete(request.Id, () =>
        {
            if (request.Operation == RoomOperation.Search) return new RoomResult(RoomFailure.None, Array.Empty<RoomListing>());
            client.BeginSession("WWWWWWWWWWWWW");
            Publish(client);
            return RoomResult.Success;
        });
        public void Publish(RoomClient client)
        {
            var members = new List<RoomMember>
            {
                new(1, client.Name, client.SkinId, 0, Reaction, 1, ActivitySequence, TongueActive),
                new(2, RemoteName, client.SkinId, 0, Reaction, 2, ActivitySequence, TongueActive)
            };
            if (_companion) members.Add(new(-1, "熬夜的程序员", client.SkinId, 0, Reaction, CompanionPresence,
                ActivitySequence, TongueActive, IsCompanion: true));
            client.Receive(new RoomSnapshot(client.JoinedCode, Name, "social", Owner, ++_revision, members.ToArray(), Access));
        }
        public string SetName(RoomClient client, string name)
        {
            NameWrites++;
            if (NameError.Length > 0) return NameError;
            Name = name;
            Publish(client);
            return "ok";
        }
        public string Kick(RoomClient client, int memberId, long presence)
        {
            if (memberId != -1 || presence != CompanionPresence) return "Rooms_KickInvalidTarget";
            KickWrites++;
            _companion = false;
            Publish(client);
            return "";
        }
        public void Cancel(RoomClient client, long id) { }
        public void Leave(RoomClient client) => client.BeginSession("");
        public void UpdateActivity(RoomClient client) { }
        public void UpdateAppearance(RoomClient client) { }
        public string SetGame(RoomClient client, string gameId) => "";
        public string SetAccess(RoomClient client, RoomAccess access) => "ok";
        public string SendChat(RoomClient client, string text) => "";
    }
}
#endif
