#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace LuckyDogRise.Rooms;

internal static class RoomGameChangePageChecks
{
    public static async Task Run(Node parent)
    {
        var service = new Service();
        var page = GD.Load<PackedScene>("res://Scenes/Rooms/InGameRoomPage.tscn").Instantiate<InGameRoomPreview>();
        page.Theme = GD.Load<Theme>("res://Themes/DefaultTheme.tres");
        page.Size = new Vector2(396, 700);
        page.Configure(new Provider(service), null);
        var overlay = GD.Load<PackedScene>("res://Scenes/Rooms/RoomGameProposalOverlay.tscn").Instantiate<RoomGameProposalOverlay>();
        parent.AddChild(overlay);
        page.BindGameProposal(overlay);
        parent.AddChild(page);
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
            var choice = page.GetNode<OptionButton>("Room/Game/Choices/Current");
            var apply = page.GetNode<Button>("Room/Game/Choices/Apply");
            var age = page.GetNode<Label>("Notice");
            var content = overlay.GetNode<VBoxContainer>("OverlayPanel/Margin/Content");
            var primary = content.GetNode<Button>("Actions/Primary");
            var secondary = content.GetNode<Button>("Actions/Secondary");
            void SelectGame(int index)
            {
                choice.GrabFocus();
                choice.ShowPopup();
                choice.Select(index);
                choice.EmitSignal(OptionButton.SignalName.ItemSelected, index);
                choice.GetPopup().Hide();
            }
            Check(!choice.Disabled && apply.Disabled && choice.Selected == 0, "owner starts with the confirmed room activity");
            foreach (string nextLocale in new[] { "zh_CN", "en" })
            {
                L10n.SetLocale(nextLocale, save: false);
                service.Now = 3665;
                service.Publish(client);
                await Frame();
                string expected = string.Format(L10n.Tr("Rooms_Age"), string.Format(L10n.Tr("Rooms_AgeHoursMinutes"), 1, 1));
                Check(age.Visible && age.Text == expected && age.LabelSettings == GD.Load<LabelSettings>("res://Themes/Label_SettingsGroupTitle.tres"),
                    "room age uses the settings heading style and omits zero days and seconds");
                var row = page.GetNode<HBoxContainer>("Room/Game");
                var controls = page.GetNode<HBoxContainer>("Room/Game/Choices");
                Check(row.Size.X <= 396.1f && controls.Position.X + controls.Size.X <= row.Size.X + .1f
                    && apply.Position.X + apply.Size.X <= controls.Size.X + .1f,
                    "Chinese and English activity controls fit a single settings-panel row");
            }
            service.Now = 90065;
            service.Publish(client);
            await Frame();
            Check(age.Text == string.Format(L10n.Tr("Rooms_Age"), string.Format(L10n.Tr("Rooms_AgeDays"), 1,
                    string.Format(L10n.Tr("Rooms_AgeHoursMinutes"), 1, 1))),
                "elapsed days are included once the room survives a full day");

            SelectGame(1);
            service.Publish(client);
            await Frame();
            Check(choice.Selected == 1 && client.View.GameId == "social" && !apply.Disabled,
                "background snapshots retain a proposed selection without changing the confirmed activity");
            Click(apply);
            await Frame();
            Check(page.HasPendingGameProposalPresentation && client.View.GameId == "social",
                "an accepted proposal request queues presentation without switching early");
            Check(page.TryShowPendingGameProposal() && overlay.Visible && primary.Disabled && !secondary.Disabled,
                "host sees a proposal with cancellation available before strict majority");
            Check(content.GetNode<Label>("Countdown").Text == string.Format(L10n.Tr("Rooms_GameChangeCountdown"), 60),
                "proposal shows the service-owned remaining time");
            Check(!primary.HasFocus() && !secondary.HasFocus(), "proposal arrival does not preselect an answer");
            service.SetOtherVotes(RoomGameVoteState.Accepted);
            service.Publish(client);
            await Frame();
            Check(!primary.Disabled && client.View.GameId == "social", "majority enables a manual switch rather than switching automatically");
            page.HideGameProposal();
            service.Publish(client);
            await Frame();
            Check(!overlay.Visible && !page.HasPendingGameProposalPresentation, "closing the host overlay does not repeatedly auto-open it");
            Click(apply);
            await Frame();
            Check(page.TryShowPendingGameProposal(), "host can explicitly review an active proposal again");
            Click(primary);
            await Frame();
            Check(client.View.GameId == "work" && client.View.GameChange == null && !overlay.Visible,
                "manual confirmation applies the activity and clears the overlay");
            SelectGame(0);
            Click(apply);
            await Frame();
            Check(page.TryShowPendingGameProposal(), "a later proposal has a fresh presentation token");
            Click(secondary);
            await Frame();
            Check(client.View.GameChange == null && client.View.GameId == "work", "host cancellation keeps the confirmed activity");

            service.Owner = 2;
            service.StartProposal("social");
            service.Publish(client);
            await Frame();
            Check(choice.Disabled && page.TryShowPendingGameProposal(), "a non-host receives a vote but cannot choose the room activity");
            Check(primary.Text == L10n.Tr("Rooms_GameChangeAccept") && secondary.Text == L10n.Tr("Rooms_GameChangeDecline")
                && !primary.Disabled && !secondary.Disabled && !primary.HasFocus() && !secondary.HasFocus(),
                "guests have explicit accept and decline buttons with no default selection");
            overlay._Input(new InputEventKey { Pressed = true, Keycode = Key.Escape });
            await Frame();
            Check(!overlay.Visible && !page.HasPendingGameProposalPresentation && !apply.Disabled,
                "an unanswered guest can dismiss the overlay without voting and retains a review entry");
            Click(apply);
            await Frame();
            Check(page.TryShowPendingGameProposal(), "an unanswered guest can reopen the same proposal explicitly");
            Click(primary);
            await Frame();
            Check(service.ResponseWrites == 1 && !overlay.Visible && !page.HasPendingGameProposalPresentation,
                "acceptance closes the guest overlay and is not reopened by its own snapshot");
            service.Publish(client);
            await Frame();
            Check(!page.HasPendingGameProposalPresentation && !overlay.Visible, "later activity updates never ask the same accepted vote again");

            service.StartProposal("social");
            service.Publish(client);
            await Frame();
            Check(page.TryShowPendingGameProposal(), "a new guest proposal can be answered independently");
            Click(secondary);
            await Frame();
            Check(service.ResponseWrites == 2 && !overlay.Visible, "declining does not leave the room or create a new vote");

            service.StartProposal("social");
            service.Publish(client);
            await Frame();
            Check(page.TryShowPendingGameProposal(), "expiry test starts from a visible proposal");
            service.Now += 60;
            client.AdvanceTo(service.Now);
            await Frame();
            Click(primary);
            Check(!overlay.Visible && !page.HasPendingGameProposalPresentation && service.ResponseWrites == 2,
                "an expired proposal hides and queued answer signals cannot submit");
            service.StartProposal("social");
            service.Publish(client);
            await Frame();
            Check(page.TryShowPendingGameProposal(), "stale-session test starts from a fresh proposal");
            client.BeginSession("OTHER");
            Click(primary);
            Check(service.ResponseWrites == 2, "a queued answer cannot cross the captured room session");
            await Frame();
            Check(!overlay.Visible && !page.HasPendingGameProposalPresentation, "session replacement removes old proposal presentation");
            GD.Print("[RoomGameChangePageChecks] PASS host/guest proposals, manual majority, cancellation, response/dismiss/review, expiry/session guards, draft retention, localized room age and single-row layout (fake service).");
        }
        finally
        {
            L10n.SetLocale(locale, save: false);
            page.Free(); overlay.Free();
        }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Provider(Service service) : IGamePlatformService, IPlatformRoomServiceProvider
    {
        public IRoomService RoomService => service;
        public bool RoomRestartRequired => false;
        public string ProviderName => "Steam";
        public string StatusMessage => "";
        public bool IsAvailable => true;
        public uint AppId => 2583700;
        public string PersonaName => "Local player";
        public string AccountProvider => "steam";
        public string AccountId => "room-game-ui-test";
        public event Action UserStatsReady { add { } remove { } }
        public void RunCallbacks() { }
        public bool OpenFriendsOverlay() => false;
        public PlatformAchievementReadResult ReadAchievementStates(IEnumerable<string> names)
            => new(false, "Test provider", Array.Empty<PlatformAchievementState>());
        public void Dispose() { }
    }

    private sealed class Service : IRoomService
    {
        public double Now { get; set; } = 100;
        public int Owner = 1;
        public string Game = "social";
        public RoomGameChange Proposal;
        public int ResponseWrites;
        private long _revision;
        private int _proposalSerial;
        public void Request(RoomClient client, RoomRequest request) => client.TryComplete(request.Id, () =>
        {
            if (request.Operation == RoomOperation.Search) return new RoomResult(RoomFailure.None, Array.Empty<RoomListing>());
            client.BeginSession("ROOM_GAME_UI"); Publish(client); return RoomResult.Success;
        });
        public void Publish(RoomClient client)
        {
            client.AdvanceTo(Now);
            client.Receive(new RoomSnapshot(client.JoinedCode, "UI room", Game,
                Owner, ++_revision, [new(1, client.Name, client.SkinId, 0, 1001, 1),
                new(2, "Another player", client.SkinId, 0, 1001, 2),
                new(-1, "熬夜的程序员", client.SkinId, 0, 1001, 3, IsCompanion: true)],
                CreatedAt: 0, ClockNow: (long)Now, GameChange: Proposal));
        }
        public void StartProposal(string target) => Proposal = new RoomGameChange((++_proposalSerial).ToString(), target,
            Now + 60, [new(1, 1, Owner == 1 ? RoomGameVoteState.Accepted : RoomGameVoteState.Pending),
                new(2, 2, Owner == 2 ? RoomGameVoteState.Accepted : RoomGameVoteState.Pending),
                new(-1, 3, RoomGameVoteState.Pending)]);
        public void SetOtherVotes(RoomGameVoteState state) => Proposal = Proposal with
        { Votes = Proposal.Votes.Select(vote => vote.MemberId == Owner ? vote : vote with { State = state }).ToArray() };
        public string ProposeGameChange(RoomClient client, string target)
        { StartProposal(target); Publish(client); return "ok"; }
        public string RespondGameChange(RoomClient client, string id, bool accept)
        {
            if (Proposal?.Id != id || Proposal.ExpiresAt <= Now) return "Rooms_GameChangeInvalidProposal";
            ResponseWrites++;
            Proposal = Proposal with { Votes = Proposal.Votes.Select(vote => vote.MemberId != client.Id ? vote
                : vote with { State = accept ? RoomGameVoteState.Accepted : RoomGameVoteState.Declined }).ToArray() };
            Publish(client); return "ok";
        }
        public string ConfirmGameChange(RoomClient client, string id)
        {
            if (Proposal?.Id != id || !Proposal.HasMajority) return "Rooms_GameChangeNoMajority";
            Game = Proposal.TargetGameId; Proposal = null; Publish(client); return "ok";
        }
        public string CancelGameChange(RoomClient client, string id)
        { Proposal = null; Publish(client); return "ok"; }
        public void Cancel(RoomClient client, long id) { }
        public void Leave(RoomClient client) => client.BeginSession("");
        public void UpdateActivity(RoomClient client) { }
        public void UpdateAppearance(RoomClient client) { }
        public string SetGame(RoomClient client, string gameId) => "Rooms_GameChangeRequired";
        public string SetAccess(RoomClient client, RoomAccess access) => "ok";
        public string SendChat(RoomClient client, string text) => "";
        public string Kick(RoomClient client, int memberId, long presence) => "";
    }
}
#endif
