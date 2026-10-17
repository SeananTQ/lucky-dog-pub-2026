#if DEBUG
using Godot;
using DataTables;
using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace LuckyDogRise;

public partial class CollectionVictoryRewardSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            // Runtime-only settings isolate test toggles from the player's settings and saves.
            // Launch with --loot-validation-headless to skip the normal account mutex autoload.
            ProjectSettings.SetSetting("application/config/use_custom_user_dir", true);
            ProjectSettings.SetSetting("application/config/custom_user_dir_name",
                "LuckyDogRise/Tests/NewOutfits/" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(OS.GetUserDataDir());
            SaveManager.Configure(AccountStorageContext.ForDevelopment("new-outfit-smoke"));
            Require(SettingsManager.LoadAutoEquipNewOutfits(), "Fresh settings must default to enabled.");
            VerifyNewMarkersAndReplacement();
            VerifySavedNewMarkers();
            VerifyPlatformNewMarkers();
            await VerifySettingsToggle();
            VerifyRewardSources();
            VerifyCelebrationReward();
            GD.Print("[NewOutfitSmoke] PASS: new/duplicate outfits, old saves, platform sync, settings, reward sources.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private static void ResetInventory(PlayerInventory inventory)
    {
        var profile = SaveManager.CreateDefaultProfile();
        inventory.LoadState(profile.OwnedItemCounts, profile.EquippedItemIdsByType,
            profile.NewItemIds, emitChanged: false);
    }

    private static void VerifyNewMarkersAndReplacement()
    {
        var inventory = new PlayerInventory();
        ResetInventory(inventory);
        var equipmentEvents = 0;
        inventory.EquipmentChanged += () => equipmentEvents++;
        inventory.AddItem(2001);
        Require(inventory.IsNew(2001) && inventory.IsEquipped(2001), "First hat must equip into an empty slot.");
        inventory.AddItem(2002);
        Require(inventory.IsNew(2002) && inventory.IsEquipped(2002), "New hat must replace an occupied slot.");
        inventory.AddItem(2001);
        Require(inventory.GetCount(2001) == 2 && inventory.IsNew(2001) && inventory.IsEquipped(2002),
            "Unread duplicate must preserve New without switching outfits.");
        inventory.ClearNew(2001);
        inventory.AddItem(2001);
        Require(!inventory.IsNew(2001) && inventory.IsEquipped(2002) && equipmentEvents == 2,
            "Seen duplicate must not regain New or emit an equipment change.");
        inventory.AddItem(2003, markNew: false);
        Require(!inventory.IsNew(2003) && inventory.IsEquipped(2002), "Silent grants must not equip.");
        inventory.ToggleEquip(2002);
        Require(!inventory.IsNew(2002) && inventory.IsEquipped(2002), "First click must acknowledge New without unequipping.");
        inventory.AddItem(6002);
        inventory.AddItem(6003);
        Require(inventory.IsNew(6003) && inventory.IsEquipped(6003), "New outfit must replace a non-default required slot.");
        GD.Print("[NewOutfitSmoke] First acquisition, duplicates, silent grants, optional/required slots: PASS.");
    }

    private static void VerifySavedNewMarkers()
    {
        var profile = SaveManager.CreateDefaultProfile();
        profile.OwnedItemCounts[2001] = 4; // Legacy saves can have unread duplicate items.
        profile.OwnedItemCounts[2002] = 1;
        profile.NewItemIds.Add(2001);
        profile.EquippedItemIdsByType[nameof(EItemType.Headwear)] = 2002;
        var loaded = SaveManager.CreateNormalizedDetachedSnapshotForTesting(
            JsonSerializer.Deserialize<SaveProfile>(JsonSerializer.Serialize(profile))!);
        var inventory = new PlayerInventory();
        inventory.LoadState(loaded.OwnedItemCounts, loaded.EquippedItemIdsByType, loaded.NewItemIds);
        Require(inventory.IsNew(2001) && inventory.IsEquipped(2002), "Loading an old New marker must preserve it and the outfit.");
        inventory.AddItem(2001);
        Require(inventory.IsNew(2001) && inventory.IsEquipped(2002), "Legacy unread duplicate must not equip.");
        inventory.ClearNew(2001);
        loaded.OwnedItemCounts = inventory.GetOwnedItemCounts();
        loaded.EquippedItemIdsByType = inventory.GetEquippedIdsByTypeName();
        loaded.NewItemIds = inventory.GetNewItemIds().ToList();
        loaded = SaveManager.CreateNormalizedDetachedSnapshotForTesting(
            JsonSerializer.Deserialize<SaveProfile>(JsonSerializer.Serialize(loaded))!);
        inventory.LoadState(loaded.OwnedItemCounts, loaded.EquippedItemIdsByType, loaded.NewItemIds);
        inventory.AddItem(2001);
        Require(!inventory.IsNew(2001) && inventory.IsEquipped(2002), "Acknowledged New must stay cleared after save/load and another copy.");
        GD.Print("[NewOutfitSmoke] Existing save markers and acknowledged-marker round trip: PASS.");
    }

    private static void VerifyPlatformNewMarkers()
    {
        using var data = new GameData(); // Never attach: no platform startup or save subscriptions.
        ResetInventory(data.Inventory);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(GameData).GetField("_saveDataMode", flags)!.SetValue(data, SettingsManager.SaveDataMode.LocalSave);
        typeof(GameData).GetField("_accountStorageFrozen", flags)!.SetValue(data, true); // Suppress persistence.
        var reconcile = typeof(GameData).GetMethod("ReconcilePlatformInventory", flags)!;
        var itemDef = LubanData.Tables.TbItem.GetOrDefault(2001).SteamItemDefId;
        for (uint count = 1; count <= 3; count++)
        {
            typeof(GameData).GetField("_suppressRecoveredItemsOnNextPlatformReconciliation", flags)!.SetValue(data, true);
            reconcile.Invoke(data, new object[] { new[] { new PlatformInventoryItem(1, itemDef, count) } });
            Require(data.Inventory.GetCount(2001) == count && data.Inventory.IsNew(2001) == (count <= 2),
                "Platform first/duplicate acquisition did not preserve New semantics.");
            if (count == 2)
                data.Inventory.ClearNew(2001);
        }
        data.Free(); // Unattached Godot Nodes need explicit native cleanup, not only Dispose.
        GD.Print("[NewOutfitSmoke] Platform inventory first/unread duplicate/seen duplicate: PASS (no Steam calls).");
    }

    private async Task VerifySettingsToggle()
    {
        var scene = GD.Load<PackedScene>("res://Scenes/App/SystemPanel.tscn");
        const string togglePath = "Panel/RootVBox/Scroll/ContentVBox/SettingsContent/AutoEquipRow/AutoEquipToggle";
        var panel = scene.Instantiate<SystemPanelController>();
        AddChild(panel);
        var toggle = panel.GetNode<CheckButton>(togglePath);
        Require(toggle.ButtonPressed, "Settings UI must show enabled by default.");
        toggle.ButtonPressed = false; // Exercise the actual UI Toggled -> settings save binding.
        Require(!SettingsManager.LoadAutoEquipNewOutfits(), "Disabling the UI toggle must persist.");
        panel.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        panel = scene.Instantiate<SystemPanelController>();
        AddChild(panel);
        toggle = panel.GetNode<CheckButton>(togglePath);
        Require(!toggle.ButtonPressed, "Recreated settings UI must restore the disabled setting.");
        toggle.ButtonPressed = true;
        Require(SettingsManager.LoadAutoEquipNewOutfits(), "Re-enabling the UI toggle must persist.");
        panel.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print("[NewOutfitSmoke] Real settings UI default, disable, reload and enable: PASS.");
    }

    private static void VerifyRewardSources()
    {
        foreach (var enabled in new[] { false, true })
        foreach (var occupied in new[] { false, true })
        foreach (var source in new[] { PlayerProgressSource.Gameplay, PlayerProgressSource.BlindBox, PlayerProgressSource.Debug })
        {
            SettingsManager.SaveAutoEquipNewOutfits(enabled);
            using var data = new GameData();
            ResetInventory(data.Inventory);
            if (occupied)
            {
                data.Inventory.AddItem(2001, markNew: false, autoEquipNewOutfit: false);
                data.Inventory.Equip(2001);
            }
            data.AddItem(2002, source: source);
            var shouldEquip = enabled && source != PlayerProgressSource.Debug;
            Require(data.Inventory.IsNew(2002) && data.Inventory.IsEquipped(2002) == shouldEquip,
                $"Reward setting failed: {source}, enabled={enabled}, occupied={occupied}.");
            if (!shouldEquip)
                Require(data.Inventory.GetEquipped(EItemType.Headwear)?.Id == (occupied ? 2001 : (int?)null),
                    "Disabled auto-equip changed the existing outfit.");
            data.Free();
        }
        GD.Print("[NewOutfitSmoke] Gameplay/blind-box/debug grants with both switch and slot states: PASS.");
    }

    private static void VerifyCelebrationReward()
    {
        foreach (var enabled in new[] { false, true })
        foreach (var equipped in new[] { false, true })
        foreach (var alreadyOwned in new[] { false, true })
        {
            SettingsManager.SaveAutoEquipNewOutfits(enabled);
            using var data = new GameData();
            ResetInventory(data.Inventory);
            if (equipped)
            {
                data.Inventory.AddItem(2001, markNew: false, autoEquipNewOutfit: false);
                data.Inventory.Equip(2001);
            }
            if (alreadyOwned)
                data.Inventory.AddItem(2034, markNew: false, autoEquipNewOutfit: false);
            var champagne = data.Inventory.GetCount(9004);
            var hats = data.Inventory.GetCount(2034);
            Require(data.TryClaimCollectionEventVictoryReward("reward-smoke", 9004, 7, 2034)
                && data.Inventory.GetCount(9004) == champagne + 7
                && data.Inventory.GetCount(2034) == hats + 1
                && data.Inventory.IsNew(2034) == !alreadyOwned
                && data.Inventory.IsEquipped(2034) == (enabled && !alreadyOwned),
                $"Celebration grant failed: enabled={enabled}, existing hat={equipped}, duplicate={alreadyOwned}.");
            Require(!data.TryClaimCollectionEventVictoryReward("reward-smoke", 9004, 7, 2034)
                && data.Inventory.GetCount(2034) == hats + 1
                && data.Inventory.GetCount(9004) == champagne + 7,
                "Duplicate celebration claim granted rewards.");
            data.Free();
        }
        GD.Print("[CollectionVictorySmoke] Settings, new/duplicate hats, occupied/empty slots and duplicate claims: PASS.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
#endif
