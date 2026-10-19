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
            VerifyFirstDogNoticeIndependentOfWishlist();
            await VerifySixthBoxProgressReminder();
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

    private void VerifyFirstDogNoticeIndependentOfWishlist()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        const string eventId = "demo-next-fest-2026";
        var previousChannel = BuildInfo.SelectedDebugGameplayChannel;
        BuildInfo.ConfigureDebugGameplayChannel(DebugGameplayChannel.Demo);
        try
        {
            // Exercise the real page definition and entry guards, without starting GameData/Steam.
            foreach (var openedBeforeReward in new[] { false, true })
            {
                using var data = new GameData();
                ResetInventory(data.Inventory);
                typeof(GameData).GetField("_accountStorageFrozen", flags)!.SetValue(data, true);
                var dogId = LubanData.Tables.TbBlindBoxItemWeight.DataList
                    .Where(weight => weight.IsEnabled && weight.Weight > 0 && weight.BlindBoxId == 2003)
                    .Select(weight => weight.ItemId)
                    .First(id => LubanData.Tables.TbItem.GetOrDefault(id)?.ItemType == EItemType.Dog
                        && !data.Inventory.Owns(id));
                data.Inventory.AddItem(dogId, markNew: false, autoEquipNewOutfit: false);
                var panel = GD.Load<PackedScene>("res://Scenes/App/SystemPanel.tscn")
                    .Instantiate<SystemPanelController>();
                try
                {
                    AddChild(panel);
                    typeof(SystemPanelController).GetField("_gameData", flags)!.SetValue(panel, data);
                    typeof(SystemPanelController).GetMethod("ConfigureCollectionEventPage", flags)!
                        .Invoke(panel, null);
                    data.SetWishlistExitCallToActionSuppressed(true);
                    data.MarkWishlistCallToActionShown();
                    data.SetWishlistCallToActionPageOpened(openedBeforeReward);
                    Require(!panel.TryHandleCollectionEventBlindBoxReward(2003, dogId, false),
                        "Duplicate ownership must not queue a first-dog notice.");
                    Require(panel.TryHandleCollectionEventBlindBoxReward(2003, dogId, true)
                        && panel.HasPendingCollectionEventFirstDogCallToAction(),
                        "Wishlist state must not block the first-dog celebration notice.");
                    data.SetWishlistCallToActionPageOpened(true);
                    Require(data.GetPendingCollectionEventFirstDogItemId(eventId) == dogId
                        && panel.HasPendingCollectionEventFirstDogCallToAction(),
                        "Opening the store must preserve the pending celebration notice.");

                    var profile = SaveManager.CreateDefaultProfile();
                    profile.OwnedItemCounts[dogId] = 1;
                    profile.CollectionEventPendingFirstDogItemIdsByEvent = new() { [eventId] = dogId };
                    profile.WishlistCallToActionPageOpened = true;
                    profile = SaveManager.CreateNormalizedDetachedSnapshotForTesting(
                        JsonSerializer.Deserialize<SaveProfile>(JsonSerializer.Serialize(profile))!);
                    typeof(GameData).GetMethod("LoadCollectionEventState", flags)!
                        .Invoke(data, new object[] { profile });
                    Require(panel.HasPendingCollectionEventFirstDogCallToAction()
                        && !panel.TryShowWishlistCallToActionForExit(),
                        "Restored celebration must remain pending while wishlist requests stay suppressed.");
                    panel.AcknowledgeFirstDogProgressNotice();
                    Require(!panel.HasPendingCollectionEventFirstDogCallToAction()
                        && !panel.TryHandleCollectionEventBlindBoxReward(2003, dogId, true),
                        "Acknowledging the celebration must prevent repeated first-dog notices.");
                }
                finally
                {
                    panel.Free();
                    data.Free();
                }
            }
        }
        finally
        {
            BuildInfo.ConfigureDebugGameplayChannel(previousChannel);
        }
        GD.Print("[CollectionFirstDogSmoke] Wishlist before/after reward, pending recovery and one-time acknowledgement: PASS.");
    }

    private async Task VerifySixthBoxProgressReminder()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        const string eventId = "demo-next-fest-2026";
        var previousChannel = BuildInfo.SelectedDebugGameplayChannel;
        BuildInfo.ConfigureDebugGameplayChannel(DebugGameplayChannel.Demo);
        BlindBoxService.ConfigureDebugScheduleChannel(DebugGameplayChannel.Demo);
        try
        {
            var schedules = BlindBoxService.GetSequenceSchedules();
            foreach (var desktopMode in new[] { false, true })
            {
                using var data = new GameData();
                ResetInventory(data.Inventory);
                typeof(GameData).GetField("_accountStorageFrozen", flags)!.SetValue(data, true);
                typeof(GameData).GetField("_blindBoxService", flags)!.SetValue(data, new BlindBoxService(data));
                var state = new BlindBoxRuntimeState
                {
                    SequenceProgressInitialized = true,
                    SequenceProgressCheckpoint = schedules[3].ProgressCheckpoint,
                    SequenceIndex = 4,
                };
                typeof(GameData).GetField("_blindBoxRuntimeState", flags)!.SetValue(data, state);
                var itemId = LubanData.Tables.TbBlindBoxItemWeight.DataList
                    .Where(weight => weight.IsEnabled && weight.Weight > 0
                        && weight.BlindBoxId == schedules[5].BlindBoxId)
                    .Select(weight => weight.ItemId)
                    .First(id => LubanData.Tables.TbItem.GetOrDefault(id)?.ItemType != EItemType.Dog);
                data.Inventory.AddItem(itemId, markNew: false, autoEquipNewOutfit: false);
                data.MarkCollectionEventItemsRevealed(eventId, data.Inventory.GetOwnedIds());
                data.SetWishlistCallToActionPageOpened(true);
                var step = -1;
                var rewardEvents = 0;
                data.BlindBoxRewardClaimed += (_, _, firstOwnership, completedStep) =>
                {
                    Require(!firstOwnership, "Duplicate rewards must still report completed sequence progress.");
                    step = completedStep;
                    rewardEvents++;
                };
                void Claim(BlindBoxSchedule schedule, bool completesSchedule = true)
                {
                    typeof(GameData).GetProperty(nameof(GameData.PendingBlindBoxReward))!.SetValue(data,
                        new PendingBlindBoxReward
                        {
                            ScheduleId = schedule.Id, BlindBoxId = schedule.BlindBoxId,
                            ItemId = itemId, RewardShown = true, CompletesSchedule = completesSchedule,
                        });
                    data.ClaimPendingBlindBoxReward();
                }

                var panel = GD.Load<PackedScene>("res://Scenes/App/SystemPanel.tscn")
                    .Instantiate<SystemPanelController>();
                try
                {
                    AddChild(panel);
                    typeof(SystemPanelController).GetField("_gameData", flags)!.SetValue(panel, data);
                    typeof(SystemPanelController).GetMethod("ConfigureCollectionEventPage", flags)!
                        .Invoke(panel, null);
                    panel.SetCurrentMode(desktopMode);
                    Claim(schedules[4]);
                    Require(step == 5 && !panel.ShouldShowCollectionEventProgressReminder(schedules[4].BlindBoxId, step),
                        "The fifth reward must not open the progress reminder.");
                    Claim(schedules[5], completesSchedule: false);
                    Require(step == 0, "A late reward must not count as the sixth sequence claim.");
                    Claim(schedules[5]);
                    Require(step == 6 && panel.ShouldShowCollectionEventProgressReminder(schedules[5].BlindBoxId, step),
                        "The sixth reward must allow the progress page, even after opening the wishlist store.");
                    panel.ShowCollectionEventPage();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    var page = (CollectionEventPageController)typeof(SystemPanelController)
                        .GetField("_collectionEventContent", flags)!.GetValue(panel)!;
                    Require(panel.IsOpen && page.IsVisibleInTree()
                        && !panel.ShouldShowCollectionEventProgressReminder(schedules[5].BlindBoxId, 6),
                        $"Progress page must open once without re-entering it, desktop={desktopMode}.");
                    panel.CloseImmediate();
                    Require(!panel.ShouldShowCollectionEventProgressReminder(schedules[5].BlindBoxId, 6),
                        "A recently closed celebration page must not reopen immediately.");
                    typeof(SystemPanelController).GetField("_collectionEventLastVisitEndedAtSeconds", flags)!
                        .SetValue(panel, Time.GetTicksMsec() / 1000.0 - 61.0);
                    Require(panel.ShouldShowCollectionEventProgressReminder(schedules[5].BlindBoxId, 6),
                        "A visit older than the short suppression window must not block the reminder.");
                    var eventsBeforeDuplicateCallback = rewardEvents;
                    data.ClaimPendingBlindBoxReward();
                    Require(rewardEvents == eventsBeforeDuplicateCallback, "A duplicate claim callback must emit nothing.");
                    // Round-trip the persisted progress, then replay a completed reward.
                    state = JsonSerializer.Deserialize<BlindBoxRuntimeState>(JsonSerializer.Serialize(state))!;
                    typeof(GameData).GetField("_blindBoxRuntimeState", flags)!.SetValue(data, state);
                    Claim(schedules[5]);
                    Require(step == 0, "Restored sixth-step progress must not trigger another sixth-step reminder.");
                    Claim(schedules[6]);
                    Require(step == 7 && !panel.ShouldShowCollectionEventProgressReminder(schedules[6].BlindBoxId, step),
                        "The seventh reward must not repeat the sixth-step reminder.");
                }
                finally
                {
                    panel.Free();
                    data.Free();
                }
            }
        }
        finally
        {
            BuildInfo.ConfigureDebugGameplayChannel(previousChannel);
            BlindBoxService.ConfigureDebugScheduleChannel(previousChannel);
        }
        GD.Print("[CollectionSixthBoxSmoke] Real claims, duplicate/late rewards, restored progress, both modes and recent visits: PASS.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
#endif
