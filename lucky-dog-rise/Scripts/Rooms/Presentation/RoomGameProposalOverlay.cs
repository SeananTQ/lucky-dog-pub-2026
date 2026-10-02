#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LuckyDogRise.Rooms;

// Presentation only: the room service owns the deadline, electorate and result.
public partial class RoomGameProposalOverlay : Control
{
    public event Action ConfirmRequested;
    public event Action CancelRequested;
    public event Action AcceptRequested;
    public event Action DeclineRequested;
    public event Action DismissRequested;
    private Label _title;
    private Label _target;
    private Label _countdown;
    private Label _summary;
    private Label _hint;
    private Label _error;
    private VBoxContainer _members;
    private Button _primary;
    private Button _secondary;
    private readonly Dictionary<(int, long), HBoxContainer> _rows = new();
    private bool _host;
    private bool _canRespond;
    private bool _canConfirm;

    public override void _Ready()
    {
        var content = GetNode<VBoxContainer>("OverlayPanel/Margin/Content");
        _title = content.GetNode<Label>("Title");
        _target = content.GetNode<Label>("Target");
        _countdown = content.GetNode<Label>("Countdown");
        _summary = content.GetNode<Label>("Summary");
        _hint = content.GetNode<Label>("Hint");
        _error = content.GetNode<Label>("Error");
        _members = content.GetNode<VBoxContainer>("MemberScroll/Members");
        _primary = content.GetNode<Button>("Actions/Primary");
        _secondary = content.GetNode<Button>("Actions/Secondary");
        _primary.Pressed += () =>
        {
            if (!IsVisibleInTree()) return;
            if (_host && _canConfirm) ConfirmRequested?.Invoke();
            else if (!_host && _canRespond) AcceptRequested?.Invoke();
        };
        _secondary.Pressed += () =>
        {
            if (!IsVisibleInTree()) return;
            if (_host) CancelRequested?.Invoke();
            else if (_canRespond) DeclineRequested?.Invoke();
        };
        FocusMode = FocusModeEnum.All;
        AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        FocusNext = FocusNeighborBottom = GetPathTo(_primary);
        FocusPrevious = FocusNeighborTop = GetPathTo(_secondary);
        foreach (var (button, other) in new[] { (_primary, _secondary), (_secondary, _primary) })
        {
            var path = button.GetPathTo(other);
            button.FocusNext = button.FocusPrevious = path;
            button.FocusNeighborLeft = button.FocusNeighborRight = path;
            button.FocusNeighborTop = button.FocusNeighborBottom = path;
        }
        Hide();
    }

    public void SetOverlayRect(Vector2 position, Vector2 size)
    { Position = position; Size = size; }

    public void Present(RoomSnapshot snapshot, int localId, double secondsRemaining,
        bool canConfirm, bool busy, string errorKey, bool takeFocus = false)
    {
        var proposal = snapshot.GameChange;
        if (proposal == null) { Hide(); return; }
        _host = snapshot.OwnerId == localId;
        var local = snapshot.Members.FirstOrDefault(member => member.Id == localId);
        var ownVote = local == null ? null : proposal.Votes.FirstOrDefault(vote =>
            vote.MemberId == localId && vote.Presence == local.Presence);
        _canRespond = !busy && secondsRemaining > 0 && ownVote?.State == RoomGameVoteState.Pending;
        _canConfirm = !busy && canConfirm && secondsRemaining > 0;
        _title.Text = L10n.Tr(_host ? "Rooms_GameChangeHostTitle" : "Rooms_GameChangeGuestTitle");
        _target.Text = string.Format(L10n.Tr("Rooms_GameChangeTarget"), InGameRoomPreview.GameName(proposal.TargetGameId));
        _countdown.Text = string.Format(L10n.Tr("Rooms_GameChangeCountdown"), Math.Max(0, (int)Math.Ceiling(secondsRemaining)));
        _summary.Text = string.Format(L10n.Tr("Rooms_GameChangeVotes"),
            proposal.AcceptedCount, proposal.ElectorateCount, proposal.RequiredCount);
        _hint.Text = L10n.Tr("Rooms_GameChangeFollowHint");
        _error.Text = string.IsNullOrEmpty(errorKey) ? "" : L10n.Tr(errorKey);
        _error.Visible = _error.Text.Length > 0;
        _primary.Text = L10n.Tr(_host ? "Rooms_GameChangeSwitch" : "Rooms_GameChangeAccept");
        _primary.Disabled = _host ? !_canConfirm : !_canRespond;
        _primary.TooltipText = _host && !_canConfirm ? L10n.Tr("Rooms_GameChangeNoMajority") : "";
        _secondary.Text = L10n.Tr(_host ? "Common_Cancel" : "Rooms_GameChangeDecline");
        _secondary.Disabled = !_host && !_canRespond;
        RenderVotes(snapshot, proposal);
        Show();
        // Do not preselect either answer when a remote request arrives.
        if (takeFocus) GrabFocus();
    }

    private void RenderVotes(RoomSnapshot snapshot, RoomGameChange proposal)
    {
        var identities = proposal.Votes.Select(vote => (vote.MemberId, vote.Presence)).ToHashSet();
        foreach (var identity in _rows.Keys.Where(key => !identities.Contains(key)).ToArray())
        {
            var removed = _rows[identity];
            _members.RemoveChild(removed);
            removed.QueueFree();
            _rows.Remove(identity);
        }
        for (int i = 0; i < proposal.Votes.Length; i++)
        {
            var vote = proposal.Votes[i];
            var identity = (vote.MemberId, vote.Presence);
            if (!_rows.TryGetValue(identity, out var row))
            {
                row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 28) };
                row.AddThemeConstantOverride("separation", 8);
                var name = new Label
                {
                    Name = "Name", SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    AutoTranslateMode = AutoTranslateModeEnum.Disabled,
                    LabelSettings = GD.Load<LabelSettings>("res://Themes/Label_SettingsOption.tres")
                };
                var state = new Label
                {
                    Name = "State", VerticalAlignment = VerticalAlignment.Center,
                    AutoTranslateMode = AutoTranslateModeEnum.Disabled,
                    LabelSettings = GD.Load<LabelSettings>("res://Themes/Label_KeyText.tres")
                };
                row.AddChild(name); row.AddChild(state); _members.AddChild(row); _rows.Add(identity, row);
            }
            if (row.GetIndex() != i) _members.MoveChild(row, i);
            var member = snapshot.Members.FirstOrDefault(value => value.Id == vote.MemberId && value.Presence == vote.Presence);
            row.GetNode<Label>("Name").Text = member?.Name ?? L10n.Tr("Rooms_You");
            row.GetNode<Label>("State").Text = L10n.Tr(vote.State switch
            {
                RoomGameVoteState.Accepted => "Rooms_GameVoteAccepted",
                RoomGameVoteState.Declined => "Rooms_GameVoteDeclined",
                _ => "Rooms_GameVotePending"
            });
        }
    }

    public override void _Input(InputEvent input)
    {
        if (IsVisibleInTree() && input is InputEventKey { Pressed: true, Keycode: Key.Escape })
        { DismissRequested?.Invoke(); GetViewport().SetInputAsHandled(); }
    }
}
#endif
