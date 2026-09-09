using Godot;
using DataTables;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LuckyDogRise;

public sealed record CollectionEventDefinition(
    string EventId,
    IReadOnlyList<int> BlindBoxIds,
    IReadOnlyList<int> GrandPrizeItemIds,
    IReadOnlyList<int> CollectionItemIds,
    int VictoryRewardItemId,
    int VictoryRewardQuantity);

public enum CollectionEventEntryScrollTarget
{
    Default,
    Collection,
    VictoryReward,
    CallToAction,
}

public partial class CollectionEventPageController : VBoxContainer
{
    private const double RevealTransitionSeconds = 1.0;
    private static readonly PackedScene RewardCellScene =
        GD.Load<PackedScene>("res://Scenes/Prefabs/CollectionEventRewardCell.tscn");

    [Export] private Control _collectionModuleAnchor = null!;
    [Export] private CollectionEventRewardCellController _grandPrizeLeft = null!;
    [Export] private CollectionEventRewardCellController _grandPrizeCenter = null!;
    [Export] private CollectionEventRewardCellController _grandPrizeRight = null!;
    [Export] private GridContainer _collectionGrid = null!;
    [Export] private Control _victoryRewardModule = null!;
    [Export] private Button _victoryRewardButton = null!;
    [Export] private Label _victoryRewardTitle = null!;
    [Export] private Label _victoryRewardDetail = null!;
    [Export] private Control _wishlistCallToActionModule = null!;
    [Export] private Button _wishlistCallToActionBannerButton = null!;
    [Export] private Button _wishlistCallToActionButton = null!;
    [Export] private Label _wishlistCallToActionMessage = null!;
    [Export] private RichTextLabel _roadmapBody = null!;
    [Export] private Control _debugToolsModule = null!;
    [Export] private Button _celebrationTestButton = null!;
    [Export] private Button _nameplateStateTestButton = null!;

    private readonly Dictionary<int, CollectionEventRewardCellController> _cellsByItemId = new();
    private GameData _gameData;
    private CollectionEventDefinition _definition;
    private int _revealRequestVersion;
    private int _debugNameplateState = -1;

    public event Action<IReadOnlyList<int>> RewardsRevealed;
    public event Action VictoryRewardClaimRequested;
    public event Action VictoryRewardClaimed;
    public event Action CelebrationTestRequested;

    public string EventId => _definition?.EventId ?? string.Empty;

    public override void _Ready()
    {
        L10n.Changed += RefreshPresentation;
        VisibilityChanged += OnVisibilityChanged;
        _victoryRewardButton.Pressed += RequestVictoryRewardClaim;
        _wishlistCallToActionBannerButton.Pressed += OpenWishlistCallToAction;
        _wishlistCallToActionButton.Pressed += OpenWishlistCallToAction;
        _debugToolsModule.Visible = OS.IsDebugBuild();
        if (_debugToolsModule.Visible)
        {
            _celebrationTestButton.Pressed += () => CelebrationTestRequested?.Invoke();
            _nameplateStateTestButton.Pressed += CycleDebugNameplateState;
        }
        RefreshPresentation();
    }

    public override void _ExitTree()
    {
        L10n.Changed -= RefreshPresentation;
        UnbindGameData();
    }

    public void Configure(GameData gameData, CollectionEventDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        ArgumentNullException.ThrowIfNull(definition);

        UnbindGameData();
        _gameData = gameData;
        _definition = definition;
        _gameData.InventoryChanged += OnInventoryChanged;
        _gameData.CollectionEventStateChanged += OnCollectionEventStateChanged;
        RebuildRewardCells();
        RefreshPresentation();
    }

    public void NotifyPageEntered()
    {
        if (!IsVisibleInTree())
            return;

        RefreshPresentation();
        QueuePendingReveals();
    }

    public CollectionEventEntryScrollTarget GetEntryScrollTarget()
    {
        if (_gameData == null || _definition == null)
            return CollectionEventEntryScrollTarget.Default;

        var ownedIds = _gameData.Inventory.GetOwnedIds().ToHashSet();
        var revealedIds = _gameData.GetCollectionEventRevealedItemIds(_definition.EventId);
        var rewardItemIds = _cellsByItemId.Keys.ToArray();
        if (rewardItemIds.Any(itemId => ownedIds.Contains(itemId) && !revealedIds.Contains(itemId)))
            return CollectionEventEntryScrollTarget.Collection;
        if (rewardItemIds.Length > 0
            && rewardItemIds.All(ownedIds.Contains)
            && !_gameData.IsCollectionEventVictoryRewardClaimed(_definition.EventId))
            return CollectionEventEntryScrollTarget.VictoryReward;
        if (_gameData.IsCollectionEventVictoryRewardClaimed(_definition.EventId))
            return CollectionEventEntryScrollTarget.CallToAction;
        return CollectionEventEntryScrollTarget.Default;
    }

    public Control GetScrollAnchor(CollectionEventEntryScrollTarget target) => target switch
    {
        CollectionEventEntryScrollTarget.Collection => _collectionModuleAnchor,
        CollectionEventEntryScrollTarget.VictoryReward => _victoryRewardModule,
        CollectionEventEntryScrollTarget.CallToAction => _wishlistCallToActionModule,
        _ => this,
    };

    public bool ContainsRewardItem(int itemId) => _cellsByItemId.ContainsKey(itemId);

    public bool ContainsBlindBox(int blindBoxId) =>
        _definition?.BlindBoxIds.Contains(blindBoxId) == true;

    public bool IsItemRevealed(int itemId) =>
        _gameData != null
        && _definition != null
        && _gameData.GetCollectionEventRevealedItemIds(_definition.EventId).Contains(itemId);

    public bool IsVictoryRewardClaimed() =>
        _gameData != null
        && _definition != null
        && _gameData.IsCollectionEventVictoryRewardClaimed(_definition.EventId);

    private void UnbindGameData()
    {
        if (_gameData == null)
            return;

        _gameData.InventoryChanged -= OnInventoryChanged;
        _gameData.CollectionEventStateChanged -= OnCollectionEventStateChanged;
        _gameData = null;
    }

    private void RebuildRewardCells()
    {
        _revealRequestVersion++;
        _cellsByItemId.Clear();

        foreach (var child in _collectionGrid.GetChildren())
            child.QueueFree();

        var grandPrizeCells = new[] { _grandPrizeLeft, _grandPrizeCenter, _grandPrizeRight };
        for (var i = 0; i < grandPrizeCells.Length; i++)
        {
            var item = i < _definition.GrandPrizeItemIds.Count
                ? LubanData.Tables.TbItem.GetOrDefault(_definition.GrandPrizeItemIds[i])
                : null;
            grandPrizeCells[i].Visible = item != null;
            if (item != null)
            {
                grandPrizeCells[i].Setup(
                    item,
                    CollectionEventRewardVisualState.Covered,
                    revealVariant: i + 1,
                    shineVariant: i + 1);
                _cellsByItemId[item.Id] = grandPrizeCells[i];
            }
        }

        for (var i = 0; i < _definition.CollectionItemIds.Count; i++)
        {
            var item = LubanData.Tables.TbItem.GetOrDefault(_definition.CollectionItemIds[i]);
            if (item == null || _cellsByItemId.ContainsKey(item.Id))
                continue;

            var cell = RewardCellScene.Instantiate<CollectionEventRewardCellController>();
            cell.CustomMinimumSize = new Vector2(58f, 58f);
            cell.Setup(
                item,
                CollectionEventRewardVisualState.Covered,
                revealVariant: i + 1,
                shineVariant: i + 1);
            _collectionGrid.AddChild(cell);
            _cellsByItemId[item.Id] = cell;
        }
    }

    private void RefreshPresentation()
    {
        _wishlistCallToActionMessage.AddThemeFontSizeOverride(
            "font_size",
            GetWishlistCallToActionFontSize(L10n.CurrentLocale));
        _wishlistCallToActionMessage.Text = L10n.Tr(L10nKey.CollectionEvent_WishlistShyRequest);
        _wishlistCallToActionButton.Text = L10n.Tr(L10nKey.CollectionEvent_WishlistButton);
        _roadmapBody.Text = FormatRoadmapBody(L10n.Tr(L10nKey.CollectionEvent_RoadmapBody));

        if (_gameData == null || _definition == null)
            return;

        var revealedIds = _gameData.GetCollectionEventRevealedItemIds(_definition.EventId);
        var victoryClaimed = _gameData.IsCollectionEventVictoryRewardClaimed(_definition.EventId);
        foreach (var (itemId, cell) in _cellsByItemId)
        {
            var state = victoryClaimed
                ? CollectionEventRewardVisualState.Shining
                : revealedIds.Contains(itemId)
                    ? CollectionEventRewardVisualState.Revealed
                    : CollectionEventRewardVisualState.Covered;
            cell.SetVisualState(state);
        }
        RefreshVictoryRewardPresentation(revealedIds, victoryClaimed);
    }

    private static string FormatRoadmapBody(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(" | ", StringComparison.Ordinal)
                || lines[i].Contains('｜'))
            {
                lines[i] = $"[color=#CEE0E0][font_size=13][b]{lines[i]}[/b][/font_size][/color]";
            }
        }

        return string.Join('\n', lines);
    }

    private void OnInventoryChanged()
    {
        RefreshPresentation();
        if (IsVisibleInTree())
            QueuePendingReveals();
    }

    private static int GetWishlistCallToActionFontSize(string locale)
    {
        return locale switch
        {
            L10n.SimplifiedChineseLocale => 14,
            L10n.TraditionalChineseLocale => 14,
            L10n.KoreanLocale => 13,
            _ => 12,
        };
    }

    private void OnCollectionEventStateChanged(string eventId)
    {
        if (_definition == null
            || !string.IsNullOrEmpty(eventId)
            && !string.Equals(eventId, _definition.EventId, StringComparison.Ordinal))
            return;

        RefreshPresentation();
    }

    private void OnVisibilityChanged()
    {
        if (IsVisibleInTree())
            NotifyPageEntered();
        else
            _revealRequestVersion++;
    }

    private void OpenWishlistCallToAction()
    {
        if (WishlistCallToAction.OpenConfiguredUrl("collection_page") == Error.Ok)
            _gameData?.SetWishlistCallToActionPageOpened(true);
    }

    private async void QueuePendingReveals()
    {
        if (_gameData == null || _definition == null || !IsVisibleInTree())
            return;

        var ownedIds = _gameData.Inventory.GetOwnedIds().ToHashSet();
        var revealedIds = _gameData.GetCollectionEventRevealedItemIds(_definition.EventId);
        var pendingIds = _cellsByItemId.Keys
            .Where(itemId => ownedIds.Contains(itemId) && !revealedIds.Contains(itemId))
            .ToArray();
        if (pendingIds.Length == 0)
            return;

        var requestVersion = ++_revealRequestVersion;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (requestVersion != _revealRequestVersion || !IsVisibleInTree() || _gameData == null)
            return;

        var cells = pendingIds
            .Where(_cellsByItemId.ContainsKey)
            .Select(itemId => _cellsByItemId[itemId])
            .ToArray();
        if (cells.Length == 0)
            return;

        foreach (var cell in cells)
            cell.PlayReveal();
        await ToSignal(
            GetTree().CreateTimer(RevealTransitionSeconds),
            SceneTreeTimer.SignalName.Timeout);
        if (requestVersion != _revealRequestVersion || _gameData == null)
            return;

        _gameData.MarkCollectionEventItemsRevealed(_definition.EventId, pendingIds);
        RewardsRevealed?.Invoke(pendingIds);
    }

    private void RefreshVictoryRewardPresentation(
        IReadOnlySet<int> revealedIds,
        bool victoryClaimed)
    {
        var rewardItemIds = _cellsByItemId.Keys.ToArray();
        var allOwned = rewardItemIds.Length > 0
                       && rewardItemIds.All(_gameData.Inventory.Owns);
        var allRevealed = allOwned && rewardItemIds.All(revealedIds.Contains);
        var isFrench = string.Equals(
            L10n.CurrentLocale,
            L10n.FrenchLocale,
            StringComparison.OrdinalIgnoreCase);
        _victoryRewardTitle.AddThemeFontSizeOverride("font_size", isFrench ? 15 : 18);
        _victoryRewardDetail.AddThemeFontSizeOverride("font_size", isFrench ? 12 : 14);
        _victoryRewardButton.Disabled = victoryClaimed || !allRevealed;
        _victoryRewardButton.TooltipText = victoryClaimed
            ? "本期庆典奖励已经领取"
            : allRevealed
                ? "点击领取庆典奖励"
                : "集齐并揭晓全部活动奖励后即可领取";

        if (victoryClaimed)
        {
            _victoryRewardTitle.Text = L10n.Tr(L10nKey.CollectionEvent_VictoryClaimedTitle);
            _victoryRewardDetail.Text = L10n.Format(
                L10nKey.CollectionEvent_VictoryClaimedDetail,
                _definition.VictoryRewardQuantity);
        }
        else if (allOwned && !allRevealed)
        {
            _victoryRewardTitle.Text = L10n.Tr(L10nKey.CollectionEvent_VictoryRevealingTitle);
            _victoryRewardDetail.Text = L10n.Tr(L10nKey.CollectionEvent_VictoryRevealingDetail);
        }
        else if (allRevealed)
        {
            _victoryRewardTitle.Text = L10n.Tr(L10nKey.CollectionEvent_VictoryReadyTitle);
            _victoryRewardDetail.Text = L10n.Format(
                L10nKey.CollectionEvent_VictoryReadyDetail,
                _definition.VictoryRewardQuantity);
        }
        else
        {
            _victoryRewardTitle.Text = L10n.Tr(L10nKey.CollectionEvent_VictoryInProgressTitle);
            _victoryRewardDetail.Text = L10n.Format(
                L10nKey.CollectionEvent_VictoryInProgressDetail,
                _definition.VictoryRewardQuantity);
        }

        ApplyDebugNameplateStateIfNeeded();
    }

    private void CycleDebugNameplateState()
    {
        _debugNameplateState = (_debugNameplateState + 1) % 3;
        ApplyDebugNameplateStateIfNeeded();
    }

    private void ApplyDebugNameplateStateIfNeeded()
    {
        if (!OS.IsDebugBuild() || _debugNameplateState < 0 || _definition == null)
            return;

        _victoryRewardButton.Disabled = true;
        _victoryRewardButton.TooltipText = "当前为调试预览，不会发放奖励";
        switch (_debugNameplateState)
        {
            case 0:
                _victoryRewardTitle.Text = L10n.Tr(L10nKey.CollectionEvent_VictoryInProgressTitle);
                _victoryRewardDetail.Text = L10n.Format(
                    L10nKey.CollectionEvent_VictoryInProgressDetail,
                    _definition.VictoryRewardQuantity);
                _nameplateStateTestButton.Text = "铭牌：收集中";
                break;
            case 1:
                _victoryRewardTitle.Text = L10n.Tr(L10nKey.CollectionEvent_VictoryReadyTitle);
                _victoryRewardDetail.Text = L10n.Format(
                    L10nKey.CollectionEvent_VictoryReadyDetail,
                    _definition.VictoryRewardQuantity);
                _nameplateStateTestButton.Text = "铭牌：可领取";
                break;
            default:
                _victoryRewardTitle.Text = L10n.Tr(L10nKey.CollectionEvent_VictoryClaimedTitle);
                _victoryRewardDetail.Text = L10n.Format(
                    L10nKey.CollectionEvent_VictoryClaimedDetail,
                    _definition.VictoryRewardQuantity);
                _nameplateStateTestButton.Text = "铭牌：已领取";
                break;
        }
    }

    private void RequestVictoryRewardClaim()
    {
        if (_gameData == null || _definition == null || _victoryRewardButton.Disabled)
            return;

        _victoryRewardButton.Disabled = true;
        if (VictoryRewardClaimRequested == null)
            CompleteVictoryRewardClaim();
        else
            VictoryRewardClaimRequested.Invoke();
    }

    public bool CompleteVictoryRewardClaim()
    {
        if (_gameData == null || _definition == null)
            return false;

        if (!_gameData.TryClaimCollectionEventVictoryReward(
                _definition.EventId,
                _definition.VictoryRewardItemId,
                _definition.VictoryRewardQuantity))
        {
            GD.PushWarning("[CollectionEvent] Victory reward claim was rejected.");
            RefreshPresentation();
            return false;
        }

        RefreshPresentation();
        VictoryRewardClaimed?.Invoke();
        return true;
    }
}
