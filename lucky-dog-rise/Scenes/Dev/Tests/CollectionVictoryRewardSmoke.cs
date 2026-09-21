#if DEBUG
using Godot;
using DataTables;
using System;

namespace LuckyDogRise;

public partial class CollectionVictoryRewardSmoke : Node
{
    public override void _Ready()
    {
        try
        {
            foreach (var equipped in new[] { false, true })
            {
                // Never attach GameData: no platform startup, account load or save subscriptions.
                using var data = new GameData();
                if (data.IsUsingLocalSave)
                    throw new InvalidOperationException("Test requires memory-only inventory.");
                data.Inventory.Equip(2001);
                if (!equipped)
                    data.Inventory.ToggleEquip(2001);
                var champagne = data.Inventory.GetCount(9004);
                var hats = data.Inventory.GetCount(2034);
                if (!data.TryClaimCollectionEventVictoryReward("reward-smoke", 9004, 7, 2034)
                    || data.Inventory.GetCount(9004) != champagne + 7
                    || data.Inventory.GetCount(2034) != hats + 1
                    || !data.Inventory.IsEquipped(2034))
                    throw new InvalidOperationException($"Grant/equip failed, existing hat={equipped}");
                if (data.TryClaimCollectionEventVictoryReward("reward-smoke", 9004, 7, 2034)
                    || data.Inventory.GetCount(2034) != hats + 1
                    || data.Inventory.GetCount(9004) != champagne + 7)
                    throw new InvalidOperationException("Duplicate claim granted rewards.");
                GD.Print($"[CollectionVictorySmoke] Existing hat={equipped}: grant, equip, duplicate rejection passed.");
            }
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }
}
#endif
