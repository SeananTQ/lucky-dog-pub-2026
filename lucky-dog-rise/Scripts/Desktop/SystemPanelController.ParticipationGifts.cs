using System.Linq;
using Godot;

namespace LuckyDogRise;

public partial class SystemPanelController
{
    private readonly BuildChannel _participationChannel = BuildInfo.Channel;
    private ConfirmOverlayController _participationNotice;
    private LinkTreeRewardEntry _participationTrialEntry;

    private bool UsesSessionParticipationGift(LinkTreeRewardEntry entry) =>
        entry.Data.ParticipationAppId > 0 && _participationChannel == BuildChannel.Dev;

    private bool HasClaimedParticipationGift(LinkTreeRewardEntry entry) =>
        UsesSessionParticipationGift(entry)
            ? _gameData?.HasSessionParticipationGift(entry.Data.Id) == true
            : entry.State == LinkTreeRewardState.Claimed || _gameData?.HasAppliedLinkTreeReward(entry.Data.Id) == true
                || (_inventoryService?.IsInventoryReady == true && _inventoryService.InventoryItems.Any(item =>
                    item.ItemDefId == entry.Data.SteamReceiptItemDefId && item.Quantity > 0));

    private bool CanShowParticipationGift(LinkTreeRewardEntry entry) =>
        entry.RewardFeedbackPending || entry.RewardFeedbackPlaying || entry.ClaimPending
        || !HasClaimedParticipationGift(entry);

    private bool MeetsLinkTreeVisibilityCondition(LinkTreeRewardEntry entry) =>
        !entry.Data.HideWhenIneligible || entry.ClaimPending || entry.RewardFeedbackPending || entry.RewardFeedbackPlaying
        || UsesSessionParticipationGift(entry) || entry.State is LinkTreeRewardState.ReadyToClaim or LinkTreeRewardState.Claimed;

    private bool CanRequestParticipationGift(LinkTreeRewardEntry entry) =>
        _gameData != null && !HasClaimedParticipationGift(entry) && !entry.ClaimPending
        && (UsesSessionParticipationGift(entry)
            || (_inventoryService?.IsInventoryReady == true && !IsSharedInventoryWritePending()));

    private void RefreshParticipationGift(LinkTreeRewardEntry entry)
    {
        var preview = UsesSessionParticipationGift(entry);
        entry.Banner.Disabled = !CanRequestParticipationGift(entry);
        entry.BannerImage.Modulate = entry.ClaimPending ? LinkTreeBusyImageModulate : Colors.White;
        entry.LoadingIndicator.SetLoading(entry.ClaimPending);
        entry.GiftBadge.Visible = !HasClaimedParticipationGift(entry) && !entry.ClaimPending;
        entry.GiftBadge.Modulate = preview ? LinkTreeGiftReadyColor : LinkTreeGiftLockedColor;
        entry.ParticipationTitle.Text = ResolveLinkTreeTooltip(entry.Data);
        entry.ParticipationSubtitle.Text = L10n.Tr(L10nKey.LinkTree_GiftThanks);
        var action = preview ? L10nKey.BlindBox_TapToClaim : L10nKey.LinkTree_CheckAndClaim;
        if (entry.ClaimPending)
            action = L10nKey.LinkTree_CheckingParticipation;
        entry.Banner.TooltipText = entry.ParticipationTitle.Text + "\n" + L10n.Tr(action);
    }

    private void OnParticipationGiftPressed(LinkTreeRewardEntry entry)
    {
        if (!CanRequestParticipationGift(entry))
            return;
        if (UsesSessionParticipationGift(entry))
        {
            if (!_gameData.TryGrantSessionParticipationGift(entry.Data))
                return;
            MarkLinkTreeEntryClaimed(entry);
            SetupLinkTreeRewardPreview(entry);
            QueueLinkTreeRewardFeedback(entry);
            return;
        }
        ClaimLinkTreeReward(entry);
    }

    private void ShowParticipationClaimNotice(LinkTreeRewardEntry entry, bool noGrantConfirmed)
    {
        if (entry.Data.ParticipationAppId <= 0 || _closingPanel || !entry.Banner.IsVisibleInTree())
            return;
        var title = ResolveLinkTreeTooltip(entry.Data);
        if (noGrantConfirmed && !string.IsNullOrWhiteSpace(entry.Data.PreClaimUrl))
        {
            _participationTrialEntry = entry;
            _participationNotice.ShowConfirm(title, L10n.Tr(L10nKey.LinkTree_DemoNotGranted),
                L10n.Tr(L10nKey.LinkTree_TryDemo), L10n.Tr(L10nKey.Common_Cancel));
            return;
        }
        _participationTrialEntry = null;
        var message = noGrantConfirmed ? L10nKey.LinkTree_ParticipationRequired : L10nKey.LinkTree_RewardsUnavailable;
        _participationNotice.ShowNotice(title, L10n.Tr(message), L10n.Tr(L10nKey.Common_Confirm));
    }

    private void OpenParticipationTrial()
    {
        var entry = _participationTrialEntry;
        _participationTrialEntry = null;
        if (entry != null)
            OpenLinkTreeUrl(entry.Data.Key, entry.Data.PreClaimUrl);
    }
}
