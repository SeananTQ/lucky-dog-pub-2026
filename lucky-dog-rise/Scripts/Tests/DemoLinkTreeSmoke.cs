#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using DataTables;
using Godot;
using Luban.SimpleJSON;

namespace LuckyDogRise;

// Real panel/claim/ledger code, with in-memory GameData only: no Steam or player saves.
public partial class DemoLinkTreeSmoke : Node
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static object Field(object target, string name) => target.GetType().GetField(name, Members)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Members)!.SetValue(target, value);
    private static object Call(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, Members)!.Invoke(target, args)!;
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static GameData NewSandbox()
    {
        var data = new GameData();
        Set(data, "_blindBoxLocalTestMode", true);
        Set(data, "_blindBoxService", new BlindBoxService(data));
        return data;
    }

    public override async void _Ready()
    {
        GameData data = null;
        GameData restored = null;
        SystemPanelController panel = null;
        var previousChannel = BuildInfo.SelectedDebugGameplayChannel;
        try
        {
            BuildInfo.ConfigureDebugGameplayChannel(DebugGameplayChannel.Demo);
            data = NewSandbox();
            panel = GD.Load<PackedScene>("res://Scenes/App/SystemPanel.tscn").Instantiate<SystemPanelController>();
            AddChild(panel);
            panel.GameData = data;
            panel.Open();
            Call(panel, "SwitchTab", 1);
            await Frames();
            Require(BuildCapabilities.LinkTree && !BuildCapabilities.SteamInventory, "Demo capabilities incorrect");
            var entries = ((IList)Field(panel, "_linkTreeRewardEntries")).Cast<object>().ToArray();
            Require(entries.Select(entry => ((LinkTree)Field(entry, "Data")).Id).SequenceEqual(new[] { 2002, 2003, 2001, 2004 }),
                "Demo banner list/order incorrect");
            Require(Field(panel, "_linkTreePageState").ToString() == "Ready", "Demo waits for Steam inventory");
            foreach (var entry in entries)
            {
                var banner = (Button)Field(entry, "Banner");
                Require(banner.IsVisibleInTree() && !banner.Disabled, "Demo banner unavailable");
                Require(((TextureRect)Field(entry, "BannerImage")).Texture != null, "Missing banner texture");
                var row = (LinkTree)Field(entry, "Data");
                var before = data.Inventory.GetCount(row.RewardItemId);
                // Simulate the existing successful external-open stage, without opening browsers.
                var stateField = entry.GetType().GetField("State", Members)!;
                stateField.SetValue(entry, Enum.Parse(stateField.FieldType, "OpenedAwaitingReturn"));
                panel.OnGlobalMousePressed(Vector2I.Zero, true);
                Require(Field(entry, "State").ToString() == "ReadyToClaim", "External return not claimable");
                banner.EmitSignal(Button.SignalName.Pressed);
                Require(data.Inventory.GetCount(row.RewardItemId) == before + 1, "Consumable not granted once");
                Require(data.HasAppliedLinkTreeReward(row.Id), "Missing claim ledger");
                Require(Field(entry, "State").ToString() == "Claimed", "Claim UI not settled");
                Require((bool)Field(entry, "RewardFeedbackPlaying"), "Visible reward feedback did not start");
                Call(panel, "ClaimLinkTreeReward", entry);
                Require(data.Inventory.GetCount(row.RewardItemId) == before + 1, "Duplicate reward");
            }
            await ToSignal(GetTree().CreateTimer(2.5), SceneTreeTimer.SignalName.Timeout);
            Require(entries.All(entry => !(bool)Field(entry, "RewardFeedbackPlaying")), "Feedback never completed");

            var profile = new SaveProfile
            {
                AppliedLinkTreeRewardIds = ((HashSet<int>)Field(data, "_appliedLinkTreeRewardIds")).ToList(),
                OwnedItemCounts = data.Inventory.GetOwnedItemCounts(),
            };
            profile = JsonSerializer.Deserialize<SaveProfile>(JsonSerializer.Serialize(profile))!;
            restored = NewSandbox();
            restored.Inventory.LoadState(profile.OwnedItemCounts, profile.EquippedItemIdsByType, profile.NewItemIds, emitChanged: false);
            Call(restored, "LoadBlindBoxState", profile);
            Set(panel, "_gameData", restored);
            Call(panel, "BuildLinkTree");
            Call(panel, "SwitchTab", 1);
            Require(((IList)Field(panel, "_linkTreeRewardEntries")).Cast<object>()
                .All(entry => Field(entry, "State").ToString() == "Claimed"), "Loaded ledger did not restore UI");
            var first = LubanData.Tables.TbLinkTree.GetOrDefault(2001);
            var count = restored.Inventory.GetCount(first.RewardItemId);
            Require(restored.TryApplyDemoLinkTreeRewardOnce(first) && restored.Inventory.GetCount(first.RewardItemId) == count,
                "Restored save allows duplicate claim");
            Call(restored, "LoadBlindBoxState", new SaveProfile());
            Call(panel, "RefreshLinkTreePageFromPlatformState");
            Require(((IList)Field(panel, "_linkTreeRewardEntries")).Cast<object>()
                .All(entry => Field(entry, "State").ToString() == "Unopened"), "Fresh ledger did not reset UI");
            Require(restored.TryApplyDemoLinkTreeRewardOnce(first) && restored.Inventory.GetCount(first.RewardItemId) == count + 1,
                "Fresh save cannot claim again");

            var jsonRows = JSON.Parse(FileAccess.GetFileAsString("res://Data/Json/tblinktree.json")).AsArray;
            var multiJson = JSON.Parse(jsonRows.Children.First(row => row["Id"].AsInt == 2001).ToString());
            multiJson["Id"] = 2998;
            var extra = LubanData.Tables.TbItem.DataList.First(item => item.Id != first.RewardItemId
                && item.ItemType == EItemType.Refreshment && item.AcquisitionType == EAcquisitionType.RefreshmentBlindBox);
            multiJson["AdditionalRewardItemIds"].AsArray.Add(extra.Id);
            var multi = new LinkTree(multiJson);
            var previousCounts = multi.FixedRewardItemIds.ToDictionary(id => id, restored.Inventory.GetCount);
            Require(restored.TryApplyDemoLinkTreeRewardOnce(multi) && restored.TryApplyDemoLinkTreeRewardOnce(multi)
                && previousCounts.All(pair => restored.Inventory.GetCount(pair.Key) == pair.Value + 1),
                "Ordinary Demo LinkTree did not settle every configured item exactly once");
            var chipsJson = JSON.Parse(jsonRows.Children.First(row => row["Id"].AsInt == 2001).ToString());
            chipsJson["Id"] = 2999;
            chipsJson["RewardType"] = 2;
            chipsJson["RewardItemId"] = 0;
            chipsJson["RewardChips"] = 2500;
            var chips = new LinkTree(chipsJson);
            var balance = restored.Chips;
            Require(restored.TryApplyDemoLinkTreeRewardOnce(chips) && restored.Chips == balance + 2500, "Chips not granted");
            Require(restored.TryApplyDemoLinkTreeRewardOnce(chips) && restored.Chips == balance + 2500, "Duplicate chips");
            BuildInfo.ConfigureDebugGameplayChannel(DebugGameplayChannel.Standard);
            Require(!restored.TryApplyDemoLinkTreeRewardOnce(first), "Local rewards escaped Demo");
            Call(panel, "BuildLinkTree");
            Call(panel, "RefreshLinkTreePageFromPlatformState");
            Require(((IList)Field(panel, "_linkTreeRewardEntries")).Cast<object>()
                .All(entry => ((LinkTree)Field(entry, "Data")).BuildChannelMask != EBuildChannelMask.Demo), "Demo rows leaked into standard mode");
            Require(Field(panel, "_linkTreePageState").ToString() == "Unavailable", "Standard mode gained offline rewards");
            await VerifyParticipationGifts(panel, restored, data);
            GD.Print("[DemoLinkTreeSmoke] PASS: four banners, return/claim, consumables, chips, feedback, duplicate prevention, save JSON reload, fresh ledger, channel isolation.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError($"[DemoLinkTreeSmoke] FAIL: {error}");
            GetTree().Quit(1);
        }
        finally
        {
            panel?.Free();
            data?.Free();
            restored?.Free();
            BuildInfo.ConfigureDebugGameplayChannel(previousChannel);
        }
    }

    private async Task Frames()
    {
        for (var i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task VerifyParticipationGifts(SystemPanelController panel, GameData data, GameData freshSession)
    {
        var gifts = ((IList)Field(panel, "_linkTreeRewardEntries")).Cast<object>()
            .Where(entry => ((LinkTree)Field(entry, "Data")).ParticipationAppId > 0).ToArray();
        Require(gifts.Length == 2, "Expected two independent participation gifts");
        var banners = gifts.Select(entry => (Button)Field(entry, "Banner")).ToArray();
        var rows = gifts.Select(entry => (LinkTree)Field(entry, "Data")).ToArray();
        foreach (var banner in banners)
        {
            Require(banner.IsVisibleInTree() && !banner.Disabled, "Dev gift not claimable");
            Require(banner.GetNode<TextureRect>("BannerImage").Texture != null, "Temporary gift banner missing");
        }
        var expected = LubanData.Tables.TbBlindBoxItemWeight.DataList
            .Where(row => row.BlindBoxId is 2002 or 2003 && row.IsEnabled && row.Weight > 0)
            .Select(row => row.ItemId).Append(2034).ToHashSet();
        Require(expected.Count == 16 && rows[0].FixedRewardItemIds.Count() == 16
            && expected.SetEquals(rows[0].FixedRewardItemIds), "Demo gift differs from the celebration pool and graduation cap");
        var before = expected.ToDictionary(id => id, data.Inventory.GetCount);
        var modal = panel.GetNode<Control>("LinkTreeRewardsOverlay");
        var scroll = modal.GetNode<ScrollContainer>("OverlayPanel/Margin/Content/ItemsPanel/ItemsScroll");
        var grid = scroll.GetNode<GridContainer>("ItemGrid");
        var confirm = modal.GetNode<Button>("OverlayPanel/Margin/Content/ConfirmButton");
        banners[0].EmitSignal(Button.SignalName.Pressed);
        banners[0].EmitSignal(Button.SignalName.Pressed);
        await Frames();
        Require(modal.IsVisibleInTree() && confirm.IsVisibleInTree()
            && modal.GetNode<Label>("OverlayPanel/Margin/Content/Title").Text.Contains("16"), "Multi-item dialog missing");
        Require(modal.HasFocus() && !confirm.HasFocus(), "Reward dialog preselected its confirm button");
        var cells = grid.GetChildren().Cast<ItemCellController>().ToArray();
        Require(cells.Select(cell => cell.ItemId).SequenceEqual(rows[0].FixedRewardItemIds)
            && cells.All(cell => cell.GetNode<TextureRect>("Content/IconRect").Texture != null),
            "Reward grid has missing, duplicate, or misordered icons");
        var bar = scroll.GetVScrollBar();
        Require(bar.Page > 0 && bar.MaxValue > bar.Page, "All rows must be accessible via a vertical scrollbar");
        var pointer = scroll.GetGlobalRect().GetCenter();
        var viewport = panel.GetViewport();
        viewport.PushInput(new InputEventMouseMotion { Position = pointer, GlobalPosition = pointer }, true);
        viewport.PushInput(new InputEventMouseButton
        {
            Position = pointer, GlobalPosition = pointer, ButtonIndex = MouseButton.WheelDown, Pressed = true,
        }, true);
        await Frames();
        Require(scroll.ScrollVertical > 0, "Mouse wheel over the reward icons did not scroll the dialog");
        scroll.ScrollVertical = (int)bar.MaxValue;
        await Frames();
        Require(scroll.ScrollVertical > 0 && scroll.GetGlobalRect().HasPoint(cells[^1].GetGlobalRect().GetCenter()),
            "Last reward cannot be scrolled into view");
        panel.CloseImmediate();
        Require(!modal.Visible, "Closing the panel left a modal behind");
        panel.Open();
        await Frames();
        Require(modal.Visible && scroll.ScrollVertical == 0, "Reward summary lost on reopen");
        Require(modal.HasFocus() && !confirm.HasFocus(), "Reopening the dialog preselected the confirm button");
        void PressKey(Key key)
        {
            viewport.PushInput(new InputEventKey { Keycode = key, Pressed = true }, true);
            viewport.PushInput(new InputEventKey { Keycode = key, Pressed = false }, true);
        }
        PressKey(Key.Enter);
        Require(modal.Visible, "Enter confirmed the reward before selecting the button");
        PressKey(Key.Tab);
        Require(confirm.HasFocus(), "Tab did not select the confirm button");
        PressKey(Key.Enter);
        banners[0].EmitSignal(Button.SignalName.Pressed);
        Require(!modal.Visible && !banners[0].Visible && banners[1].Visible
            && expected.All(id => data.Inventory.GetCount(id) == before[id] + 1),
            "Demo confirmation did not settle all 16 distinct items once");
        var singleCount = data.Inventory.GetCount(rows[1].RewardItemId);
        banners[1].EmitSignal(Button.SignalName.Pressed);
        banners[1].EmitSignal(Button.SignalName.Pressed);
        Require(!modal.Visible && ((Control)Field(gifts[1], "RewardVisualRoot")).Visible
            && data.Inventory.GetCount(rows[1].RewardItemId) == singleCount + 1, "Single gift feedback or count incorrect");
        Require(rows.All(row => !data.HasAppliedLinkTreeReward(row.Id)), "Session gift persisted a claim receipt");
        await ToSignal(GetTree().CreateTimer(1.3), SceneTreeTimer.SignalName.Timeout);
        Require(banners.All(b => !b.Visible), "Claimed gifts stayed visible");
        Set(panel, "_gameData", freshSession);
        Call(panel, "BuildLinkTree");
        Call(panel, "RefreshLinkTreePageFromPlatformState");
        gifts = ((IList)Field(panel, "_linkTreeRewardEntries")).Cast<object>()
            .Where(entry => ((LinkTree)Field(entry, "Data")).ParticipationAppId > 0).ToArray();
        banners = gifts.Select(entry => (Button)Field(entry, "Banner")).ToArray();
        Require(banners.All(b => b.Visible && !b.Disabled), "Fresh session did not restore test gifts");

        var query = new GiftInventory();
        Set(panel, "_participationChannel", BuildChannel.Playtest);
        Set(panel, "_inventoryService", query);
        Call(panel, "RefreshLinkTreePagePresentation");
        Require(banners[0].Visible && banners[0].Disabled && !banners[1].Visible, "Unknown eligibility visibility");
        query.EligiblePromoItemDefs = new HashSet<int>();
        Call(panel, "RefreshLinkTreePagePresentation");
        Require(banners[0].Visible && !banners[0].Disabled && !banners[1].Visible, "Ineligible visibility");
        var beforePlaytest = freshSession.Inventory.GetCount(rows[1].RewardItemId);
        banners[1].EmitSignal(Button.SignalName.Pressed);
        Require(freshSession.Inventory.GetCount(rows[1].RewardItemId) == beforePlaytest, "Ineligible Playtest claim");
        query.EligiblePromoItemDefs = new HashSet<int> { rows[1].SteamClaimBundleItemDefId };
        Call(panel, "RefreshLinkTreePagePresentation");
        Require(banners[1].Visible && !banners[1].Disabled, "Qualified Playtest banner missing");
        banners[1].EmitSignal(Button.SignalName.Pressed);
        query.EligiblePromoItemDefs = new HashSet<int> { rows[0].SteamClaimBundleItemDefId };
        Call(panel, "RefreshLinkTreePagePresentation");
        var beforeDemo = freshSession.Inventory.GetCount(rows[0].RewardItemId);
        banners[0].EmitSignal(Button.SignalName.Pressed);
        Require(freshSession.Inventory.GetCount(rows[0].RewardItemId) == beforeDemo + 1
            && freshSession.Inventory.GetCount(rows[1].RewardItemId) == beforePlaytest + 1,
            "Real-eligibility branch did not grant both independent session rewards");
        Set(panel, "_participationChannel", BuildChannel.Release);
        Call(panel, "BuildLinkTree");
        query.EligiblePromoItemDefs = rows.Select(row => row.SteamClaimBundleItemDefId).ToHashSet();
        Call(panel, "RefreshLinkTreePagePresentation");
        gifts = ((IList)Field(panel, "_linkTreeRewardEntries")).Cast<object>()
            .Where(entry => ((LinkTree)Field(entry, "Data")).ParticipationAppId > 0).ToArray();
        banners = gifts.Select(entry => (Button)Field(entry, "Banner")).ToArray();
        Require(banners.All(b => b.Visible), "Test claims blocked Release eligibility");
        var receipt = new PlatformInventoryItem(123, rows[0].SteamReceiptItemDefId, 1);
        query.InventoryItems = [receipt];
        Call(panel, "OnPlatformInventorySnapshotChanged", new PlatformInventorySnapshot(
            true, "test receipt", new HashSet<int> { receipt.ItemDefId }, [receipt]));
        Require(!banners[0].Visible && banners[1].Visible && !(bool)Field(gifts[0], "RewardFeedbackPending"),
            "Release failed to restore the individual receipt without replaying feedback");
        var inFlight = NewSandbox();
        try
        {
            var gift = rows[0];
            Call(inFlight, "LoadBlindBoxState", new SaveProfile
            {
                PendingLinkTreeClaim = new PendingLinkTreeClaim
                {
                    LinkTreeId = gift.Id, SteamClaimBundleItemDefId = gift.SteamClaimBundleItemDefId,
                    SteamReceiptItemDefId = gift.SteamReceiptItemDefId,
                },
            });
            Set(panel, "_gameData", inFlight);
            Call(panel, "BuildLinkTree");
            var complete = gift.FixedRewardItemIds.Select(id => new PlatformInventoryItem(
                (ulong)id, LubanData.Tables.TbItem.Get(id).SteamItemDefId, 1)).Prepend(receipt).ToArray();
            var partial = complete.SkipLast(1).ToArray();
            Call(panel, "OnPlatformPromoItemGrantCompleted", new PlatformPromoItemGrantResult(
                gift.SteamClaimBundleItemDefId, receipt.ItemDefId, true, true, "partial bundle", partial));
            void Sync(PlatformInventoryItem[] items)
            {
                query.InventoryItems = items;
                Call(panel, "OnPlatformInventorySnapshotChanged", new PlatformInventorySnapshot(
                    true, "test bundle", items.Select(item => item.ItemDefId).ToHashSet(), items));
            }
            Sync(partial);
            Require(inFlight.PendingLinkTreeClaim != null && !modal.Visible, "Partial Steam reward shown as complete");
            Sync(complete);
            Require(modal.Visible && grid.GetChildCount() == 16 && inFlight.PendingLinkTreeClaim == null,
                "Complete Steam bundle did not display all rewards");
            confirm.EmitSignal(Button.SignalName.Pressed);
            Sync(complete);
            Require(!modal.Visible && inFlight.HasAppliedLinkTreeReward(gift.Id), "Steam snapshot replayed the reward dialog");
        }
        finally
        {
            Set(panel, "_gameData", freshSession);
            inFlight.Free();
        }
        GD.Print("[ParticipationGifts] PASS: 16 distinct Demo items, scroll to last icon, confirm/reopen, single Playtest animation, independent counts, fresh session, eligibility, Release receipt restore.");
    }

    private sealed class GiftInventory : IPlatformInventoryService
    {
        public IReadOnlySet<int> EligiblePromoItemDefs { get; set; }
        public event Action<PlatformInventorySnapshot> InventorySnapshotChanged { add { } remove { } }
        public event Action<PlatformPromoItemGrantResult> PromoItemGrantCompleted { add { } remove { } }
        public event Action<PlatformPlaytimeDropResult> PlaytimeDropCompleted { add { } remove { } }
        public bool IsInventoryReady => true;
        public bool IsPromoGrantPending => false;
        public bool IsPlaytimeDropPending => false;
        public IReadOnlyList<PlatformInventoryItem> InventoryItems { get; set; } = [];
        public void StartInventorySynchronization() { }
        public bool TryGrantPromoItem(int promoItemDefId, int receiptItemDefId, out string message)
            => throw new InvalidOperationException("Session reward attempted a Steam write");
        public bool TryTriggerPlaytimeDrop(int generatorItemDefId, out string message)
            => throw new InvalidOperationException();
    }
}
#endif
