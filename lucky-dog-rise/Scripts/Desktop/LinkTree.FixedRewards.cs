using System.Collections.Generic;

namespace DataTables;

public sealed partial class LinkTree
{
    public bool HasFixedItemReward => RewardType is ELinkTreeRewardType.FixedItem or ELinkTreeRewardType.FixedItemList;

    public IReadOnlyList<int> FixedRewardItemIds => RewardType switch
    {
        ELinkTreeRewardType.FixedItem => [RewardItemId],
        ELinkTreeRewardType.FixedItemList => RewardItemIds,
        _ => [],
    };
}
