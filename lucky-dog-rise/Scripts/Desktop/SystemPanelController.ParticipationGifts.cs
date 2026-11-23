using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LuckyDogRise;

public partial class SystemPanelController
{
    private double _nextParticipationCheck;
    private IReadOnlySet<int> _lastEligiblePromos;
    private readonly BuildChannel _participationChannel = BuildInfo.Channel;

    private bool UsesSessionParticipationGift(LinkTreeRewardEntry entry) =>
        entry.Data.ParticipationAppId > 0 && _participationChannel is BuildChannel.Dev or BuildChannel.Playtest;

    private bool? IsParticipationEligible(LinkTreeRewardEntry entry) =>
        _participationChannel == BuildChannel.Dev
            ? true
            : _inventoryService?.EligiblePromoItemDefs?.Contains(entry.Data.SteamClaimBundleItemDefId);

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
        || (entry.Data.ParticipationAppId > 0 ? IsParticipationEligible(entry) == true
            : entry.State is LinkTreeRewardState.ReadyToClaim or LinkTreeRewardState.Claimed);

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusIn && _linkTreeContent?.IsVisibleInTree() == true)
            RequestParticipationEligibility();
    }

    private void RequestParticipationEligibility()
    {
        if (_participationChannel is not (BuildChannel.Playtest or BuildChannel.Release))
            return;
        _inventoryService?.RefreshPromoEligibility();
        _nextParticipationCheck = 15;
    }

    private void UpdateParticipationEligibility(double delta)
    {
        if (_participationChannel is not (BuildChannel.Playtest or BuildChannel.Release)
            || _linkTreeContent?.IsVisibleInTree() != true)
            return;
        var eligible = _inventoryService?.EligiblePromoItemDefs;
        if (!ReferenceEquals(_lastEligiblePromos, eligible))
        {
            _lastEligiblePromos = eligible;
            RefreshLinkTreePagePresentation();
        }
        _nextParticipationCheck -= delta;
        if (_nextParticipationCheck <= 0)
            RequestParticipationEligibility();
    }

    private void RefreshParticipationGift(LinkTreeRewardEntry entry)
    {
        var claimed = HasClaimedParticipationGift(entry);
        var eligible = IsParticipationEligible(entry);
        var busy = _gameData == null || entry.ClaimPending
            || (!UsesSessionParticipationGift(entry) && IsSharedInventoryWritePending());
        var hasTrialLink = !string.IsNullOrWhiteSpace(entry.Data.PreClaimUrl);
        entry.Banner.Disabled = claimed || busy || eligible == null || (eligible == false && !hasTrialLink);
        entry.BannerImage.Modulate = busy ? LinkTreeBusyImageModulate : Colors.White;
        entry.LoadingIndicator.SetLoading(entry.ClaimPending);
        entry.GiftBadge.Visible = !claimed && !entry.ClaimPending;
        entry.GiftBadge.Modulate = eligible == true ? LinkTreeGiftReadyColor : LinkTreeGiftLockedColor;
        entry.ParticipationTitle.Text = ResolveLinkTreeTooltip(entry.Data);
        entry.ParticipationSubtitle.Text = L10n.Tr(L10nKey.LinkTree_GiftThanks);
        var action = claimed ? string.Empty : L10n.Tr(eligible switch
        {
            true => L10nKey.BlindBox_TapToClaim,
            false when hasTrialLink => L10nKey.LinkTree_TryDemo,
            false => L10nKey.LinkTree_ParticipationRequired,
            null => L10nKey.LinkTree_CheckingParticipation,
        });
        entry.Banner.TooltipText = claimed ? entry.ParticipationTitle.Text : entry.ParticipationTitle.Text + "\n" + action;
    }

    private void OnParticipationGiftPressed(LinkTreeRewardEntry entry)
    {
        if (_gameData == null || HasClaimedParticipationGift(entry) || entry.ClaimPending)
            return;
        var eligible = IsParticipationEligible(entry);
        if (eligible != true)
        {
            if (eligible == false && !string.IsNullOrWhiteSpace(entry.Data.PreClaimUrl))
                OpenLinkTreeUrl(entry.Data.Key, entry.Data.PreClaimUrl);
            RequestParticipationEligibility();
            return;
        }
        if (UsesSessionParticipationGift(entry))
        {
            if (!_gameData.TryGrantSessionParticipationGift(entry.Data))
                return;
            MarkLinkTreeEntryClaimed(entry);
            SetupLinkTreeRewardPreview(entry);
            QueueLinkTreeRewardFeedback(entry);
            return;
        }
        if (_inventoryService?.IsInventoryReady != true || IsSharedInventoryWritePending())
            return;
        entry.State = LinkTreeRewardState.ReadyToClaim;
        ClaimLinkTreeReward(entry);
    }
}
