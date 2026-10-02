#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Linq;
using Godot;

namespace LuckyDogRise.Rooms;

public partial class InGameRoomPreview
{
    private static readonly string[] SelectableGames = ["social", "work"];
    private OptionButton _gameChoice;
    private TextureRect _gameIcon;
    private Button _gameApply;
    private RoomClient _gameDraftClient;
    private long _gameDraftSession;
    private int _gameDraftOwner;
    private string _gameConfirmed = "";
    private string _gameDraft = "social";
    private RoomClient _gameSelectionClient;
    private long _gameSelectionSession;
    private long _gameSelectionEpoch;
    private RoomGameProposalOverlay _gameProposalOverlay;
    private RoomClient _proposalClient;
    private long _proposalSession;
    private int _proposalOwner;
    private string _proposalId = "";
    private bool _proposalPendingPresentation;
    private bool _proposalRespondedLocally;
    private string _proposalError = "";

    public PopupMenu GamePopup => GodotObject.IsInstanceValid(_gameChoice) ? _gameChoice.GetPopup() : null;
    public bool GameProposalVisible => GodotObject.IsInstanceValid(_gameProposalOverlay) && _gameProposalOverlay.Visible;
    public bool HasPendingGameProposalPresentation => _proposalPendingPresentation && ProposalIsCurrent;
    private bool ProposalIsCurrent => ReferenceEquals(_proposalClient, _client) && _client != null
        && _client.Session == _proposalSession && _client.View?.GameChange?.Id == _proposalId
        && _client.View.OwnerId == _proposalOwner && _client.GameChangeSecondsRemaining > 0;
    private bool CanReviewGameProposal => _client?.View?.GameChange is { } proposal && !_client.IsBusy
        && _client.GameChangeSecondsRemaining > 0 && (_client.View.OwnerId == _client.Id
            || (!_proposalRespondedLocally && _client.View.Members.Any(member => member.Id == _client.Id
                && proposal.Votes.Any(vote => vote.MemberId == member.Id && vote.Presence == member.Presence
                    && vote.State == RoomGameVoteState.Pending))));

    public static Texture2D GameIcon(string gameId) => GD.Load<Texture2D>(gameId == "work"
        ? "res://Assets/UI/Icon/Icon_RoomGameWork.svg" : "res://Assets/UI/Icon/Icon_RoomGame.svg");

    private void InitializeGameControls()
    {
        _gameChoice = GetNode<OptionButton>("Room/Game/Choices/Current");
        _gameIcon = GetNode<TextureRect>("Room/Game/Choices/Icon");
        _gameApply = GetNode<Button>("Room/Game/Choices/Apply");
        for (int i = 0; i < SelectableGames.Length; i++) _gameChoice.AddItem(GameName(SelectableGames[i]), i);
        GamePopup.AboutToPopup += RememberGameContext;
        GamePopup.PopupHide += () =>
        {
            long epoch = _gameSelectionEpoch;
            Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(this) && IsInsideTree() && epoch == _gameSelectionEpoch
                    && !GamePopup.Visible) _gameSelectionClient = null;
            }).CallDeferred();
        };
        _gameChoice.FocusEntered += RememberGameContext;
        _gameChoice.ItemSelected += index =>
        {
            if (!ReferenceEquals(_gameSelectionClient, _client) || _client == null
                || _client.Session != _gameSelectionSession || GameEditError().Length > 0
                || index < 0 || index >= SelectableGames.Length) { RenderGameControls(); return; }
            _gameDraft = SelectableGames[(int)index];
            RenderGameControls();
        };
        _gameApply.Pressed += RequestGameChange;
    }

    private string GameEditError()
    {
        if (_client?.View == null || _client.JoinedCode.Length == 0 || _client.IsBusy)
            return "Rooms_GameChangeUnavailable";
        if (_client.View.OwnerId != _client.Id) return "Rooms_GameChangeNotOwner";
        return _client.View.GameChange != null ? "Rooms_GameChangeAlreadyPending" : "";
    }

    private void RememberGameContext()
    {
        if (GameEditError().Length > 0) { CloseGameChoices(); return; }
        _gameSelectionClient = _client;
        _gameSelectionSession = _client.Session;
        _gameSelectionEpoch++;
    }

    private void CloseGameChoices()
    {
        _gameSelectionClient = null;
        _gameSelectionEpoch++;
        GamePopup?.Hide();
    }

    public void CloseRoomDropdowns()
    { CloseAccessChoices(); CloseGameChoices(); }

    private void RenderGameControls()
    {
        if (_gameChoice == null) return;
        var view = _client?.View;
        string actual = view?.GameId ?? "social";
        if (!ReferenceEquals(_gameDraftClient, _client) || _gameDraftSession != (_client?.Session ?? 0)
            || _gameDraftOwner != (view?.OwnerId ?? 0) || _gameConfirmed != actual)
        {
            _gameDraftClient = _client;
            _gameDraftSession = _client?.Session ?? 0;
            _gameDraftOwner = view?.OwnerId ?? 0;
            _gameConfirmed = actual;
            _gameDraft = actual;
            CloseGameChoices();
        }
        for (int i = 0; i < SelectableGames.Length; i++) _gameChoice.SetItemText(i, GameName(SelectableGames[i]));
        _gameChoice.Select(Math.Max(0, Array.IndexOf(SelectableGames, _gameDraft)));
        _game.Text = L10n.Tr("Rooms_GameTitle");
        _gameIcon.Texture = GameIcon(_gameDraft);
        string error = GameEditError();
        _gameChoice.Disabled = error.Length > 0;
        _gameChoice.TooltipText = error.Length > 0 ? L10n.Tr(error)
            : string.Format(L10n.Tr(_gameDraft != actual ? "Rooms_GameDraftHint" : "Rooms_GameFormat"), GameName(actual));
        bool canReview = CanReviewGameProposal;
        _gameApply.Text = L10n.Tr(canReview ? "Rooms_GameChangeReview" : "Rooms_GameChangePropose");
        _gameApply.Disabled = !canReview && (error.Length > 0 || _gameDraft == actual);
        if (_gameChoice.Disabled) CloseGameChoices();
    }

    private void RequestGameChange()
    {
        if (_client?.View == null || GameProposalVisible) return;
        if (CanReviewGameProposal)
        {
            ProcessGameProposal();
            if (ProposalIsCurrent) _proposalPendingPresentation = true;
            return;
        }
        string error = GameEditError();
        if (error.Length == 0 && ReferenceEquals(_gameDraftClient, _client)
            && _gameDraftSession == _client.Session && _gameDraft != _client.View.GameId)
        {
            CommitNameEdit();
            CloseRoomDropdowns();
            string result = _client.ProposeGameChange(_gameDraft);
            error = result == "ok" ? "" : result;
        }
        _noticeKey = error;
        _dirty = true;
        ProcessGameProposal();
    }

    private void RefreshRoomAge()
    {
        bool show = _client?.View?.CreatedAt != null && _client.JoinedCode.Length > 0;
        _connection.Visible = show;
        if (!show) return;
        long seconds = (long)Math.Min(long.MaxValue / 2.0, Math.Max(0, _client.RoomAgeSeconds));
        long days = seconds / 86400;
        string time = string.Format(L10n.Tr("Rooms_AgeHoursMinutes"), seconds / 3600 % 24, seconds / 60 % 60);
        if (days > 0) time = string.Format(L10n.Tr("Rooms_AgeDays"), days, time);
        string text = string.Format(L10n.Tr("Rooms_Age"), time);
        if (_connection.Text != text) _connection.Text = text;
    }

    public void BindGameProposal(RoomGameProposalOverlay overlay)
    {
        HideGameProposal();
        if (GodotObject.IsInstanceValid(_gameProposalOverlay))
        {
            _gameProposalOverlay.ConfirmRequested -= ConfirmGameProposal;
            _gameProposalOverlay.CancelRequested -= CancelGameProposal;
            _gameProposalOverlay.AcceptRequested -= AcceptGameProposal;
            _gameProposalOverlay.DeclineRequested -= DeclineGameProposal;
            _gameProposalOverlay.DismissRequested -= HideGameProposal;
        }
        _gameProposalOverlay = overlay;
        if (overlay == null) return;
        overlay.ConfirmRequested += ConfirmGameProposal;
        overlay.CancelRequested += CancelGameProposal;
        overlay.AcceptRequested += AcceptGameProposal;
        overlay.DeclineRequested += DeclineGameProposal;
        overlay.DismissRequested += HideGameProposal;
    }

    public void HideGameProposal()
    {
        // Closing a local panel is not a vote or a cancellation of the proposal.
        if (GodotObject.IsInstanceValid(_gameProposalOverlay)) _gameProposalOverlay.Hide();
    }

    private void ResetGamePresentation()
    {
        _proposalClient = null;
        _proposalSession = 0;
        _proposalOwner = 0;
        _proposalId = "";
        _proposalPendingPresentation = false;
        _proposalRespondedLocally = false;
        _proposalError = "";
        CloseGameChoices();
        HideGameProposal();
    }

    private void ProcessGameProposal()
    {
        var proposal = _client?.View?.GameChange;
        if (proposal == null || _client.GameChangeSecondsRemaining <= 0)
        {
            if (_proposalId.Length > 0) ResetGamePresentation();
            return;
        }
        bool changed = !ReferenceEquals(_proposalClient, _client) || _proposalSession != _client.Session || _proposalId != proposal.Id;
        if (changed)
        {
            HideGameProposal();
            _proposalClient = _client;
            _proposalSession = _client.Session;
            _proposalOwner = _client.View.OwnerId;
            _proposalId = proposal.Id;
            _proposalError = "";
            _proposalRespondedLocally = false;
            _proposalPendingPresentation = true;
            _dirty = true;
        }
        if (!ProposalIsCurrent) { _proposalPendingPresentation = false; HideGameProposal(); return; }
        var local = _client.View.Members.FirstOrDefault(member => member.Id == _client.Id);
        var vote = local == null ? null : proposal.Votes.FirstOrDefault(v => v.MemberId == local.Id && v.Presence == local.Presence);
        if (_client.View.OwnerId != _client.Id && (vote?.State != RoomGameVoteState.Pending || _proposalRespondedLocally))
        { _proposalPendingPresentation = false; HideGameProposal(); return; }
        if (!IsVisibleInTree()) HideGameProposal();
        if (GameProposalVisible) RenderGameProposal();
    }

    public bool TryShowPendingGameProposal()
    {
        ProcessGameProposal();
        if (!HasPendingGameProposalPresentation || !GodotObject.IsInstanceValid(_gameProposalOverlay)
            || _exitingTree || !IsInsideTree() || !IsVisibleInTree()) return false;
        CancelKick();
        CommitNameEdit();
        CloseRoomDropdowns();
        if (!ProposalIsCurrent) return false;
        _browsing = false;
        _dirty = true;
        _proposalPendingPresentation = false;
        RenderGameProposal(takeFocus: true);
        NotifyPageChanged();
        return true;
    }

    private void RenderGameProposal(bool takeFocus = false)
    {
        if (!ProposalIsCurrent) { HideGameProposal(); return; }
        _gameProposalOverlay.Present(_client.View, _client.Id, _client.GameChangeSecondsRemaining,
            _client.CanConfirmGameChange, _client.IsBusy, _proposalError, takeFocus);
    }

    private void ConfirmGameProposal() => ActOnGameProposal(0);
    private void CancelGameProposal() => ActOnGameProposal(1);
    private void AcceptGameProposal() => ActOnGameProposal(2);
    private void DeclineGameProposal() => ActOnGameProposal(3);

    private void ActOnGameProposal(int action)
    {
        if (!GameProposalVisible || !ProposalIsCurrent) { HideGameProposal(); return; }
        if (action == 0 && !_client.CanConfirmGameChange) return;
        var client = _client;
        long session = client.Session;
        string id = _proposalId;
        string result = action switch
        {
            0 => client.ConfirmGameChange(id),
            1 => client.CancelGameChange(id),
            _ => client.RespondGameChange(id, action == 2)
        };
        // Backend callbacks can synchronously replace the active room/proposal.
        if (!ReferenceEquals(client, _client) || client.Session != session || _proposalId != id) return;
        if (result == "ok")
        {
            _proposalPendingPresentation = false;
            _proposalRespondedLocally = action is 2 or 3;
            _proposalError = "";
            HideGameProposal();
        }
        else { _proposalError = result; if (ProposalIsCurrent) RenderGameProposal(); }
        _noticeKey = result == "ok" ? "" : result;
        _dirty = true;
    }
}
#endif
