using System.Collections.Generic;
using System.Linq;

namespace DataTables;

public sealed partial class LinkTree
{
    public IEnumerable<int> FixedRewardItemIds =>
        AdditionalRewardItemIds.Prepend(RewardItemId);
}
