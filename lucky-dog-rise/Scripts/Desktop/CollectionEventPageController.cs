using Godot;
using DataTables;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    private const float WishlistModuleDefaultHeight = 176f;
    private const float WishlistModuleWithShareHeight = 232f;
    private const int NameplateTitleDefaultFontSize = 18;
    private const int NameplateTitleFrenchFontSize = 15;
    private const int NameplateTitleMinimumFontSize = 12;
    private const float NameplateTitleHorizontalInset = 66f;
    private const double NameplateSwayRestSeconds = 1.5;
#if DEBUG
    private const string DebugLongPersonaName =
        "Captain Lucky Paws and Friends";
#endif
    private static readonly PackedScene RewardCellScene =
        GD.Load<PackedScene>("res://Scenes/Prefabs/CollectionEventRewardCell.tscn");
    private static readonly Texture2D NameplateInProgressTexture =
        GD.Load<Texture2D>("res://Assets/Event/Collection/CollectionVictory_Nameplate_2x_InProgress.png");
    private static readonly Texture2D NameplateClaimableTexture =
        GD.Load<Texture2D>("res://Assets/Event/Collection/CollectionVictory_Nameplate_2x_Claimable.png");
    private static readonly Texture2D NameplateClaimedTexture =
        GD.Load<Texture2D>("res://Assets/Event/Collection/CollectionVictory_Nameplate_2x_Claimed.png");
    private static readonly Texture2D LaurelLeftInProgressTexture =
        GD.Load<Texture2D>("res://Assets/Event/Collection/CollectionVictory_LaurelLeft_InProgress.png");
    private static readonly Texture2D LaurelLeftClaimableTexture =
        GD.Load<Texture2D>("res://Assets/Event/Collection/CollectionVictory_LaurelLeft_Claimable.png");
    private static readonly Texture2D LaurelLeftClaimedTexture =
        GD.Load<Texture2D>("res://Assets/Event/Collection/CollectionVictory_LaurelLeft_Claimed.png");
    private static readonly Texture2D LaurelRightInProgressTexture =
        GD.Load<Texture2D>("res://Assets/Event/Collection/CollectionVictory_LaurelRight_InProgress.png");
    private static readonly Texture2D LaurelRightClaimableTexture =
        GD.Load<Texture2D>("res://Assets/Event/Collection/CollectionVictory_LaurelRight_Claimable.png");
    private static readonly Texture2D LaurelRightClaimedTexture =
        GD.Load<Texture2D>("res://Assets/Event/Collection/CollectionVictory_LaurelRight_Claimed.png");

    [Export] private Control _collectionModuleAnchor = null!;
    [Export] private CollectionEventRewardCellController _grandPrizeLeft = null!;
    [Export] private CollectionEventRewardCellController _grandPrizeCenter = null!;
    [Export] private CollectionEventRewardCellController _grandPrizeRight = null!;
    [Export] private GridContainer _collectionGrid = null!;
    [Export] private Control _victoryRewardModule = null!;
    [Export] private Control _victoryRewardNameplate = null!;
    [Export] private NinePatchRect _victoryRewardNameplateBackground = null!;
    [Export] private Button _victoryRewardButton = null!;
    [Export] private Label _victoryRewardTitle = null!;
    [Export] private Label _victoryRewardDetail = null!;
    [Export] private Control _wishlistCallToActionModule = null!;
    [Export] private Button _wishlistCallToActionBannerButton = null!;
    [Export] private Button _wishlistCallToActionButton = null!;
    [Export] private Button _wishlistShareButton = null!;
    [Export] private Label _wishlistCallToActionMessage = null!;
    [Export] private RichTextLabel _roadmapBody = null!;
    [Export] private Button _backToTopButton = null!;
    [Export] private Control _debugToolsModule = null!;
    [Export] private Button _celebrationTestButton = null!;
    [Export] private Button _nameplateStateTestButton = null!;

    private readonly Dictionary<int, CollectionEventRewardCellController> _cellsByItemId = new();
    private GameData _gameData;
    private CollectionEventDefinition _definition;
    private Func<string> _playerDisplayNameProvider = static () => string.Empty;
    private string _pendingClaimedTitle = string.Empty;
    private VictoryNameplateVisualState _currentNameplateVisualState;
    private int _revealRequestVersion;
    private int _debugNameplateState = -1;
    private double _nameplateSwaySeconds;
    private Control _laurelLeftPivot;
    private Control _laurelRightPivot;
    private TextureRect _laurelLeft;
    private TextureRect _laurelRight;
    private Label _claimableUnderlineLabel;
    private float _nameplateUnderlineOpacity;

    public event Action<IReadOnlyList<int>> RewardsRevealed;
    public Func<Task> PreparePendingRevealAsync { get; set; }
    public Func<IPlatformScreenshotService> ScreenshotServiceProvider { get; set; }
    private bool _savingScreenshot;
    private string _screenshotResultKey;
    public event Action VictoryRewardClaimRequested;
    public event Action VictoryRewardClaimed;
    public event Action CelebrationTestRequested;
    public event Action BackToTopRequested;

    public string EventId => _definition?.EventId ?? string.Empty;

    public override void _Ready()
    {
        SetProcess(false);
        var contentLayer = _victoryRewardNameplate.GetNode<Control>("ContentMargins/ContentLayer");
        _laurelLeftPivot = contentLayer.GetNode<Control>("LaurelLeftPivot");
        _laurelRightPivot = contentLayer.GetNode<Control>("LaurelRightPivot");
        _laurelLeft = _laurelLeftPivot.GetNode<TextureRect>("LaurelLeft");
        _laurelRight = _laurelRightPivot.GetNode<TextureRect>("LaurelRight");
        _claimableUnderlineLabel = contentLayer.GetNode<Label>("ClaimableCopy/Detail");
        _claimableUnderlineLabel.Draw += DrawClaimableUnderline;
        L10n.Changed += RefreshPresentation;
        VisibilityChanged += OnVisibilityChanged;
        _victoryRewardNameplate.Resized += OnVictoryRewardNameplateResized;
        _victoryRewardButton.Pressed += RequestVictoryRewardClaim;
        // Grid cells are rebuilt on every Configure, so only the persistent prize cells bind here.
        _grandPrizeLeft.ClaimRequested += RequestVictoryRewardClaim;
        _grandPrizeCenter.ClaimRequested += RequestVictoryRewardClaim;
        _grandPrizeRight.ClaimRequested += RequestVictoryRewardClaim;
        _wishlistCallToActionBannerButton.Pressed += OpenWishlistCallToAction;
        _wishlistCallToActionButton.Pressed += OpenWishlistCallToAction;
        _wishlistShareButton.Pressed += OnWishlistSharePressed;
        _backToTopButton.Pressed += () => BackToTopRequested?.Invoke();
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
        _victoryRewardNameplate.Resized -= OnVictoryRewardNameplateResized;
        UnbindGameData();
    }

    public void Configure(
        GameData gameData,
        CollectionEventDefinition definition,
        Func<string> playerDisplayNameProvider = null)
    {
        ArgumentNullException.ThrowIfNull(gameData);
        ArgumentNullException.ThrowIfNull(definition);

        UnbindGameData();
        _gameData = gameData;
        _definition = definition;
        _playerDisplayNameProvider = playerDisplayNameProvider ?? (() => string.Empty);
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
            cell.ClaimRequested += RequestVictoryRewardClaim;
            _cellsByItemId[item.Id] = cell;
        }
    }

    private void RefreshPresentation()
    {
        _wishlistCallToActionMessage.AddThemeFontSizeOverride(
            "font_size",
            WishlistCallToAction.GetShyRequestFontSize(L10n.CurrentLocale));
        _wishlistCallToActionMessage.Text = L10n.Tr(L10nKey.CollectionEvent_WishlistShyRequest);
        _wishlistCallToActionButton.Text = L10n.Tr(L10nKey.CollectionEvent_WishlistButton);
        _wishlistShareButton.Text = L10n.Tr(_screenshotResultKey ?? L10nKey.CollectionEvent_SaveScreenshot);
        _roadmapBody.Text = FormatRoadmapBody(L10n.Tr(L10nKey.CollectionEvent_RoadmapBody));
        _backToTopButton.Text = L10n.Tr(L10nKey.CollectionEvent_BackToTop);

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
        UpdateNameplateAttention();
    }

    private void OpenWishlistCallToAction()
    {
        if (WishlistCallToAction.OpenConfiguredUrl("collection_page") == Error.Ok)
            _gameData?.SetWishlistCallToActionPageOpened(true);
    }

    private async void OnWishlistSharePressed()
    {
        if (_savingScreenshot || _currentNameplateVisualState != VictoryNameplateVisualState.Claimed)
            return;
        _savingScreenshot = true;
        _screenshotResultKey = null;
        _wishlistShareButton.Text = L10n.Tr(L10nKey.CollectionEvent_SaveScreenshot);
        _wishlistShareButton.Disabled = true;
        var saved = false;
        try
        {
            var service = ScreenshotServiceProvider?.Invoke();
            if (service != null)
            {
                using var image = await CollectionCelebrationScreenshot.CaptureAsync(this);
                // Recheck provider after rendering; never send to a replacement account/session.
                if (ReferenceEquals(service, ScreenshotServiceProvider?.Invoke()))
                    saved = await service.SaveScreenshotAsync(image.GetData(), image.GetWidth(), image.GetHeight());
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning("[CollectionEvent] Screenshot was not confirmed: " + ex.Message);
        }
        finally
        {
            _savingScreenshot = false;
            if (IsInstanceValid(_wishlistShareButton))
            {
                _wishlistShareButton.Disabled = false;
                _screenshotResultKey = saved
                    ? "CollectionEvent_ScreenshotSaved" : "CollectionEvent_ScreenshotUnconfirmed";
                _wishlistShareButton.Text = L10n.Tr(_screenshotResultKey);
            }
        }
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
        if (PreparePendingRevealAsync != null)
            await PreparePendingRevealAsync();
        else
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
        var state = victoryClaimed ? VictoryNameplateVisualState.Claimed
            : allRevealed ? VictoryNameplateVisualState.Claimable
            : VictoryNameplateVisualState.InProgress;
        var preview = OS.IsDebugBuild() && _debugNameplateState >= 0;
        if (preview)
            state = (VictoryNameplateVisualState)_debugNameplateState;
        RenderNameplate(state, preview, !preview && allOwned && !allRevealed);
    }

    private void CycleDebugNameplateState()
    {
        _debugNameplateState = (_debugNameplateState + 1) % 3;
        RefreshPresentation();
    }

    private void RenderNameplate(
        VictoryNameplateVisualState state, bool preview, bool revealing)
    {
        var contentLayer = _victoryRewardNameplate.GetNode<Control>("ContentMargins/ContentLayer");
        var groupNames = new[] { "Copy", "ClaimableCopy", "ClaimedCopy" };
        for (var index = 0; index < groupNames.Length; index++)
            contentLayer.GetNode<Control>(groupNames[index]).Visible = index == (int)state;
        var copy = contentLayer.GetNode<Control>(groupNames[(int)state]);
        _victoryRewardTitle = copy.GetNode<Label>("Title");
        _victoryRewardDetail = copy.GetNode<Label>("Detail");

        ApplyNameplateVisualState(state);
        var claimable = state == VictoryNameplateVisualState.Claimable;
        // The debug preview must not become claimable, but it still shows the real affordance;
        // only the cursor follows the plaque state.
        _victoryRewardButton.Disabled = preview || !claimable;
        SetVictoryRewardCursor(claimable);
        SetRewardCellsClaimable(claimable);
        _victoryRewardButton.TooltipText = preview
            ? "当前为调试预览，不会发放奖励"
            : claimable ? "点击铭牌，一起欢庆" : string.Empty;

        if (state == VictoryNameplateVisualState.Claimed)
        {
            string previewName = null;
#if DEBUG
            if (preview)
                previewName = DebugLongPersonaName;
#endif
            ApplyClaimedNameplateCopy(
                preview ? DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    : _gameData.GetOrCreateCollectionEventVictoryCompletedAtUnixSeconds(_definition.EventId),
                previewName);
        }
        else
        {
            var titleKey = state == VictoryNameplateVisualState.Claimable
                ? L10nKey.CollectionEvent_VictoryReadyTitle
                : L10nKey.CollectionEvent_VictoryInProgressTitle;
            var detailKey = state == VictoryNameplateVisualState.Claimable
                ? L10nKey.CollectionEvent_VictoryReadyDetail
                : revealing ? L10nKey.CollectionEvent_VictoryRevealingDetail
                : L10nKey.CollectionEvent_VictoryInProgressDetail;
            ApplyStandardNameplateCopy(L10n.Tr(titleKey), L10n.Tr(detailKey));
        }
        if (preview)
            _nameplateStateTestButton.Text = state switch
            {
                VictoryNameplateVisualState.InProgress => "铭牌：未完成",
                VictoryNameplateVisualState.Claimable => "铭牌：待点亮",
                _ => "铭牌：已点亮",
            };
    }

    // The reward icons claim through the same path as the plaque, so they share its affordance.
    private void SetRewardCellsClaimable(bool claimable)
    {
        foreach (var cell in _cellsByItemId.Values)
            cell.SetClaimable(claimable);
    }

    // A disabled Button still reports mouse_default_cursor_shape, so the hand cursor
    // must be driven explicitly instead of being baked into the scene.
    private void SetVictoryRewardCursor(bool claimable)
    {
        _victoryRewardButton.MouseDefaultCursorShape = claimable
            ? Control.CursorShape.PointingHand
            : Control.CursorShape.Arrow;
    }

    private void ApplyStandardNameplateCopy(string title, string detail)
    {
        ResetNameplateCopyLayout();
        _victoryRewardTitle.Text = title;
        _victoryRewardDetail.Text = detail;
    }

    private void ResetNameplateCopyLayout()
    {
        _pendingClaimedTitle = string.Empty;
        // Clipping a wrapped Label can collapse its minimum height in a VBox.
        // Keep natural text height; the fixed outer plaque provides the final boundary.
        _victoryRewardTitle.ClipText = false;
        _victoryRewardTitle.MaxLinesVisible = -1;
        _victoryRewardDetail.ClipText = false;
        _victoryRewardTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _victoryRewardTitle.AddThemeFontSizeOverride(
            "font_size",
            GetDefaultNameplateTitleFontSize());
        _victoryRewardDetail.AddThemeFontSizeOverride(
            "font_size",
            string.Equals(
                L10n.CurrentLocale,
                L10n.FrenchLocale,
                StringComparison.OrdinalIgnoreCase)
                ? 12
                : 14);
    }

    private void ApplyClaimedNameplateCopy(
        long completedAtUnixSeconds,
        string personaNameOverride = null)
    {
        var personaName = (personaNameOverride ?? _playerDisplayNameProvider()).Trim();
        ResetNameplateCopyLayout();
        _pendingClaimedTitle = string.IsNullOrWhiteSpace(personaName)
            ? L10n.Tr(L10nKey.CollectionEvent_VictoryClaimedTitle)
            : personaName;
        _victoryRewardTitle.Text = _pendingClaimedTitle;

        var completedAt = completedAtUnixSeconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(completedAtUnixSeconds).ToLocalTime()
            : DateTimeOffset.Now;
        _victoryRewardDetail.Text = L10n.Format(
            L10nKey.CollectionEvent_VictoryClaimedDetail,
            completedAt.ToString("yyyy.M.d", CultureInfo.InvariantCulture));

        Callable.From(FitClaimedNameplateTitle).CallDeferred();
    }

    private void OnVictoryRewardNameplateResized()
    {
        if (_currentNameplateVisualState == VictoryNameplateVisualState.Claimed
            && !string.IsNullOrEmpty(_pendingClaimedTitle))
            Callable.From(FitClaimedNameplateTitle).CallDeferred();
    }

    private void FitClaimedNameplateTitle()
    {
        if (_currentNameplateVisualState != VictoryNameplateVisualState.Claimed
            || string.IsNullOrEmpty(_pendingClaimedTitle)
            || !IsInstanceValid(_victoryRewardTitle))
            return;

        var availableWidth = _victoryRewardNameplate.Size.X
                             - NameplateTitleHorizontalInset * 2f;
        if (availableWidth < _victoryRewardTitle.CustomMinimumSize.X)
            return;
        var font = _victoryRewardTitle.GetThemeFont("font");
        var initialFontSize = GetDefaultNameplateTitleFontSize();

        for (var fontSize = initialFontSize;
             fontSize >= NameplateTitleMinimumFontSize;
             fontSize--)
        {
            if (TryFitNameplateTitle(
                    _pendingClaimedTitle,
                    font,
                    fontSize,
                    availableWidth,
                    out var fittedText))
            {
                ApplyFittedNameplateTitle(fittedText, fontSize);
                return;
            }
        }

        ApplyFittedNameplateTitle(
            TruncateNameplateTitle(
                _pendingClaimedTitle,
                font,
                NameplateTitleMinimumFontSize,
                availableWidth),
            NameplateTitleMinimumFontSize);
    }

    private void ApplyFittedNameplateTitle(string text, int fontSize)
    {
        _victoryRewardTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _victoryRewardTitle.AddThemeFontSizeOverride("font_size", fontSize);
        _victoryRewardTitle.Text = text;
    }

    private static int GetDefaultNameplateTitleFontSize() =>
        string.Equals(
            L10n.CurrentLocale,
            L10n.FrenchLocale,
            StringComparison.OrdinalIgnoreCase)
            ? NameplateTitleFrenchFontSize
            : NameplateTitleDefaultFontSize;

    private static bool TryFitNameplateTitle(
        string text,
        Font font,
        int fontSize,
        float availableWidth,
        out string fittedText)
    {
        if (MeasureTextWidth(font, text, fontSize) <= availableWidth)
        {
            fittedText = text;
            return true;
        }

        var elementOffsets = StringInfo.ParseCombiningCharacters(text);
        var bestText = string.Empty;
        var bestScore = float.MaxValue;
        for (var elementIndex = 1; elementIndex < elementOffsets.Length; elementIndex++)
        {
            var splitOffset = elementOffsets[elementIndex];
            var firstLine = text[..splitOffset].TrimEnd();
            var secondLine = text[splitOffset..].TrimStart();
            if (firstLine.Length == 0 || secondLine.Length == 0)
                continue;

            var firstWidth = MeasureTextWidth(font, firstLine, fontSize);
            var secondWidth = MeasureTextWidth(font, secondLine, fontSize);
            if (firstWidth > availableWidth || secondWidth > availableWidth)
                continue;

            var breaksAtWhitespace = char.IsWhiteSpace(text[splitOffset - 1])
                                     || char.IsWhiteSpace(text[splitOffset]);
            var score = Math.Abs(firstWidth - secondWidth)
                        + (breaksAtWhitespace ? 0f : availableWidth);
            if (score >= bestScore)
                continue;

            bestScore = score;
            bestText = $"{firstLine}\n{secondLine}";
        }

        fittedText = bestText;
        return bestText.Length > 0;
    }

    private static string TruncateNameplateTitle(
        string text,
        Font font,
        int fontSize,
        float availableWidth)
    {
        const string ellipsis = "…";
        var elementOffsets = StringInfo.ParseCombiningCharacters(text);
        var firstLineEnd = FindFittingElementCount(
            text,
            elementOffsets,
            0,
            font,
            fontSize,
            availableWidth,
            string.Empty);
        if (firstLineEnd >= elementOffsets.Length)
            return text;

        var firstLine = SliceTextElements(text, elementOffsets, 0, firstLineEnd).TrimEnd();
        var secondLineEnd = FindFittingElementCount(
            text,
            elementOffsets,
            firstLineEnd,
            font,
            fontSize,
            availableWidth,
            ellipsis);
        var secondLine = SliceTextElements(
            text,
            elementOffsets,
            firstLineEnd,
            secondLineEnd).TrimStart();
        return $"{firstLine}\n{secondLine}{ellipsis}";
    }

    private static int FindFittingElementCount(
        string text,
        int[] elementOffsets,
        int startElement,
        Font font,
        int fontSize,
        float availableWidth,
        string suffix)
    {
        var endElement = startElement;
        for (var candidate = startElement + 1; candidate <= elementOffsets.Length; candidate++)
        {
            var candidateText = SliceTextElements(
                text,
                elementOffsets,
                startElement,
                candidate) + suffix;
            if (MeasureTextWidth(font, candidateText, fontSize) > availableWidth)
                break;
            endElement = candidate;
        }

        return endElement;
    }

    private static string SliceTextElements(
        string text,
        int[] elementOffsets,
        int startElement,
        int endElement)
    {
        var startOffset = startElement < elementOffsets.Length
            ? elementOffsets[startElement]
            : text.Length;
        var endOffset = endElement < elementOffsets.Length
            ? elementOffsets[endElement]
            : text.Length;
        return text[startOffset..endOffset];
    }

    private static float MeasureTextWidth(Font font, string text, int fontSize) =>
        font.GetStringSize(text, fontSize: fontSize).X;

    private void ApplyNameplateVisualState(VictoryNameplateVisualState state)
    {
        _currentNameplateVisualState = state;
        _victoryRewardNameplateBackground.Texture = state switch
        {
            VictoryNameplateVisualState.Claimable => NameplateClaimableTexture,
            VictoryNameplateVisualState.Claimed => NameplateClaimedTexture,
            _ => NameplateInProgressTexture,
        };
        _laurelLeft.Texture = state switch
        {
            VictoryNameplateVisualState.Claimable => LaurelLeftClaimableTexture,
            VictoryNameplateVisualState.Claimed => LaurelLeftClaimedTexture,
            _ => LaurelLeftInProgressTexture,
        };
        _laurelRight.Texture = state switch
        {
            VictoryNameplateVisualState.Claimable => LaurelRightClaimableTexture,
            VictoryNameplateVisualState.Claimed => LaurelRightClaimedTexture,
            _ => LaurelRightInProgressTexture,
        };

        var showShareButton = state == VictoryNameplateVisualState.Claimed;
        _wishlistShareButton.Visible = showShareButton;
        _wishlistCallToActionModule.CustomMinimumSize = new Vector2(
            _wishlistCallToActionModule.CustomMinimumSize.X,
            showShareButton ? WishlistModuleWithShareHeight : WishlistModuleDefaultHeight);
        UpdateNameplateAttention();
    }

    private void UpdateNameplateAttention()
    {
        var active = _currentNameplateVisualState == VictoryNameplateVisualState.Claimable
                     && IsVisibleInTree();
        if (active == IsProcessing())
            return; // Ordinary inventory/localization refreshes must not restart the gesture.

        SetProcess(active);
        ResetNameplateSway();
    }

    private void ResetNameplateSway()
    {
        _nameplateSwaySeconds = 0;
        _nameplateUnderlineOpacity = 0;
        _claimableUnderlineLabel?.QueueRedraw();
        if (_laurelLeftPivot != null) _laurelLeftPivot.Rotation = 0;
        if (_laurelRightPivot != null) _laurelRightPivot.Rotation = 0;
    }

    public override void _Process(double delta)
    {
        // Two inward-and-back gestures (7°, then 5°), followed by a configurable rest.
        // Bottom pivots and mirrored angles preserve the plaque/text/hitbox geometry.
        _nameplateSwaySeconds = (_nameplateSwaySeconds + delta) % (1.1 + NameplateSwayRestSeconds);
        var angle = 0f;
        _nameplateUnderlineOpacity = 0;
        if (_nameplateSwaySeconds < 1.1)
        {
            var second = _nameplateSwaySeconds >= 0.55;
            var phase = (_nameplateSwaySeconds - (second ? 0.55 : 0)) / 0.55;
            var ease = (1f - (float)Math.Cos(phase * Math.Tau)) * 0.5f;
            _nameplateUnderlineOpacity = ease;
            angle = Mathf.DegToRad(second ? 5f : 7f) * ease;
        }
        _laurelLeftPivot.Rotation = angle;
        _laurelRightPivot.Rotation = -angle;
        _claimableUnderlineLabel.QueueRedraw();
    }

    // Draw on the existing Label: decoration never contributes to minimum size or wrapping.
    // Character bounds come from Godot's actual shaping, including automatic line breaks.
    private List<Rect2> GetClaimableUnderlineLines()
    {
        var lines = new SortedDictionary<float, Rect2>();
        var index = 0;
        foreach (var rune in _claimableUnderlineLabel.Text.EnumerateRunes())
        {
            var bounds = _claimableUnderlineLabel.GetCharacterBounds(index++);
            if (Rune.IsWhiteSpace(rune) || !bounds.HasArea())
                continue;
            var y = bounds.Position.Y;
            lines[y] = lines.TryGetValue(y, out var line) ? line.Merge(bounds) : bounds;
        }
        return lines.Values.ToList();
    }

    private void DrawClaimableUnderline()
    {
        if (_currentNameplateVisualState != VictoryNameplateVisualState.Claimable
            || _nameplateUnderlineOpacity <= 0)
            return;
        var color = _claimableUnderlineLabel.GetThemeColor("font_color");
        color.A *= _nameplateUnderlineOpacity;
        foreach (var line in GetClaimableUnderlineLines())
        {
            var y = line.End.Y - 0.5f;
            _claimableUnderlineLabel.DrawLine(new Vector2(line.Position.X, y),
                new Vector2(line.End.X, y), color, 1f, antialiased: true);
        }
    }

    private enum VictoryNameplateVisualState
    {
        InProgress,
        Claimable,
        Claimed,
    }

    private void RequestVictoryRewardClaim()
    {
        if (_gameData == null || _definition == null || _victoryRewardButton.Disabled)
            return;

        _victoryRewardButton.Disabled = true;
        SetVictoryRewardCursor(false);
        SetRewardCellsClaimable(false);
        SetProcess(false);
        ResetNameplateSway();
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
