#if !DEMO_BUILD && !RECORDING_BUILD
using System.Linq;
using Godot;
using LuckyDogRise.Rooms;

namespace LuckyDogRise;

public partial class ModeManager
{
    // Runs only through the existing isolated exported-package diagnostic entry.
    // Exercise scene bindings after obfuscation without joining Steam or touching inventory.
    private void VerifyRoomExportSmoke()
    {
        if (!BuildCapabilities.SteamRooms) return;
        var tab = _settingsPanel.GetNodeOrNull<Button>("Panel/RootVBox/TitleRow/RoomTab");
        if (tab == null || !tab.Visible)
            throw new System.InvalidOperationException("Exported room tab is missing.");
        tab.EmitSignal(BaseButton.SignalName.Pressed);
        var page = _settingsPanel.GetNode<VBoxContainer>("Panel/RootVBox/Scroll/ContentVBox/RoomContent")
            .GetChild<InGameRoomPreview>(0);
        if (L10n.Tr("Rooms_Create") == "Rooms_Create")
            throw new System.InvalidOperationException("Exported room translations are missing.");

        var row = GD.Load<PackedScene>("res://Scenes/Rooms/InGameRoomDirectoryRow.tscn")
            .Instantiate<InGameRoomDirectoryRow>();
        var desktop = GD.Load<PackedScene>("res://Scenes/Rooms/RoomDesktopPreview.tscn")
            .Instantiate<RoomDesktopPreview>();
        var notice = GD.Load<PackedScene>("res://Scenes/Rooms/RoomJoinNotice.tscn").Instantiate<CanvasLayer>();
        var proposal = GD.Load<PackedScene>("res://Scenes/Rooms/RoomGameProposalOverlay.tscn")
            .Instantiate<RoomGameProposalOverlay>();
        AddChild(row);
        AddChild(desktop);
        AddChild(notice);
        AddChild(proposal);
        try
        {
            row.Hide();
            desktop.Hide();
            notice.GetChild<RoomChatView>(0).Present(false, false, "Export validation");
            row.Bind(new RoomListing("diagnostic", "Export validation", "social", 2, RoomRules.Capacity), false, () => { });
            row.SetBusy(true);
            var skin = LubanData.Tables.TbDogSkin.DataList[0].Id;
            // No service is needed: only feed the same read-only snapshot used by the renderer.
            var client = new RoomClient(null, 1, "Export validation", skin);
            client.BeginSession("diagnostic");
            client.Receive(new RoomSnapshot("diagnostic", "Export validation", "social", 1, 1,
                new[] { new RoomMember(1, "Local", skin, 0, 1001, 1), new RoomMember(2, "Remote", skin, 0, 1001, 2) }));
            desktop.Present(client, new Rect2(0, 0, 1280, 720), 1, 420, 0, true);
            if (desktop.RemoteDogs.Count != 1 || desktop.RemoteDogs.Single().Dog.GameData != null)
                throw new System.InvalidOperationException("Exported remote dog scene could not be initialized.");
            proposal.Present(client.View with { GameChange = new("diagnostic", "work", 60,
                new[] { new RoomGameVote(1, 1, RoomGameVoteState.Accepted), new RoomGameVote(2, 2, RoomGameVoteState.Pending) }) },
                client.Id, 60, false, false, "");
            proposal.Hide();
            GD.Print("[RoomExportSmoke] Room page, translations, directory row, join notice, game proposal and remote dog bindings passed.");
        }
        finally
        {
            desktop.Free();
            row.Free();
            notice.Free();
            proposal.Free();
        }
    }

    private RoomClient _desktopRoomClient;
    private RoomDesktopPreview _roomDesktop;
    private bool _roomWindowActive;
    private int _roomScreen;
    private Vector2I _roomDesktopHostPosition;
    private Rect2I _roomDesktopHost;
    private int? _roomCompactHostX;
    private long _roomCompactSession = -1;
    private int _roomCompactScreen = -1;
    private Sprite2D[] _roomLocalFootprintSprites;
    private Vector2 _roomBlindBoxHintBasePosition;
    private bool RoomCompactDesktop => _settingsPanel.RoomDisplayPreference == RoomDisplayMode.HalfScreenCompact;
    private Rect2 RoomLocalFootprint => RoomDogView.MeasureFootprint(_roomLocalFootprintSprites,
        _bossStatusPanel, _bossDogVisual.GlobalPosition);
    private Vector2I _beforeRoomDogScreenPosition;
    private bool _beforeRoomTaskbarSnapped;
    private RoomSnapshot _roomLastLayoutSnapshot;
    private float _roomLastLayoutScale;
    private Rect2I _roomLastWorkArea;
    private Button[] _roomLocalDogHits;
    private int _roomDragMember;
    private long _roomDragPresence;
    private long _roomDragSession;
    private Vector2 _roomDragMouseStart;
    private Vector2 _roomDragDogStart;
    private const double RoomChatBlindBoxRestoreDelaySeconds = 5;
    private bool _roomChatBubbleActive;
    private double _roomChatBlindBoxRestoreRemaining;
    private CanvasLayer _roomJoinNoticeLayer;
    private RoomChatView _roomJoinNotice;
    private string _roomJoinNoticeKey = "";
    private double _roomJoinNoticeRemaining;
    private bool _roomJoinNoticeWasVisible;
    private bool _roomNoticeIsHostChange;
    private string _roomNoticeHostName = "";
    private int _roomNoticeOwnerId;
    private long _roomNoticeLayoutSession = -1;
    private bool RoomChatSuppressesBlindBoxHint => _roomChatBubbleActive || _roomChatBlindBoxRestoreRemaining > 0;
    // Includes the loading handoff and pending/restored rewards, not just the
    // overlay's animation tween. A ready, unopened box does not block chatting.
    private bool RoomBubblePresentationAllowed => CurrentMode == Mode.BossKey && !_hiddenByFullscreenApp
        && !_blindBoxOpeningUiActive && _gameData?.PendingBlindBoxReward == null
        && _bossBlindBoxOverlay?.Visible != true;
    private bool RoomChatInteractionAllowed => RoomBubblePresentationAllowed
        && _desktopRoomClient?.ChatAllowed == true && _settingsPanel?.HasRoomModal != true;

    private void UpdateRoomGameProposalPresentation()
    {
        if (_settingsPanel?.HasPendingRoomGameProposal != true
            || _startupState != StartupState.Interactive || _hiddenByFullscreenApp
            || CurrentMode == Mode.Immersive || UiInteractionOccupancy.IsOccupied(this)) return;

        // A pending proposal keeps its original deadline while a higher-priority
        // presentation is busy. Opening it never changes the player's poker mode.
        CancelWindowDrag();
        RefreshSettingsPanelModeActions();
        PositionPanelInBestSlot();
        if (!_settingsPanel.TryShowPendingRoomGameProposal()) return;
        _settingsPanelOpenedAtSeconds = Time.GetTicksMsec() / 1000.0;
        _roomDesktop?.LocalChat.RefreshInteractionAvailability();
    }
#if DEBUG
    private bool? _roomSnapEnabledForSmoke;
#endif
    private bool RoomSnapEnabled =>
#if DEBUG
        _roomSnapEnabledForSmoke ??
#endif
        SettingsManager.LoadSnapToWindowsTaskbar();
    private float RoomTaskbarAnchorOffsetY =>
        (_bossTaskBarAnchor.Position.Y - _bossDogVisual.Position.Y) * _desktopPetScaleFactor;

    private void AttachRoomDesktopPreview(RoomClient client)
    {
        if (ReferenceEquals(_desktopRoomClient, client)) return;
        StopRoomPokerPresentation(restoreWindow: CurrentMode == Mode.Play);
        _roomPokerDesktopPositions = null;
        if (IsInstanceValid(_roomPokerDogs) && _roomPokerDogs.IsInsideTree()) _roomPokerDogs.Clear();
        else _roomPokerDogs = null;
        if (_desktopRoomClient != null)
        {
            _desktopRoomClient.JoinFailed -= OnRoomJoinFailed;
            _desktopRoomClient.Changed -= OnRoomJoinNoticeStateChanged;
        }
        if (client != null) ClearRoomJoinNotice();
        RestoreRoomDesktopWindow();
        _roomDesktop?.Clear();
        _roomLastLayoutSnapshot = null;
        _desktopRoomClient = client;
        _roomNoticeOwnerId = client?.View?.OwnerId ?? 0;
        _roomNoticeLayoutSession = client?.LayoutSession ?? -1;
        if (client != null)
        {
            client.JoinFailed += OnRoomJoinFailed;
            client.Changed += OnRoomJoinNoticeStateChanged;
        }
        _roomLocalDogHits = new[] { "HitButton", "ClawLeftHitButton", "ClawRightHitButton" }
            .Select(path => _bossDogVisual.GetNode<Button>(path)).ToArray();
        _roomLocalFootprintSprites = _bossDogVisual.FindChildren("*", "Sprite2D", true, false).OfType<Sprite2D>().ToArray();
        _roomBlindBoxHintBasePosition = _bossBlindBoxHint.Position;
    }

    private void UpdateRoomDesktopPreview()
    {
        if (_desktopRoomClient == null) return;
        bool joined = _desktopRoomClient.JoinedCode.Length > 0;
        UpdateRoomPokerDisplayPreference(joined);
        if (CurrentMode == Mode.Play)
        {
            RestoreRoomDesktopWindow();
            UpdateRoomPokerPreview(joined);
            if (!joined) _roomDesktop?.Clear();
            return;
        }
        if (!joined || CurrentMode != Mode.BossKey)
        {
            RestoreRoomDesktopWindow();
            if (!joined)
            {
                _roomDesktop?.Clear();
                _roomPokerDesktopPositions = null;
            }
            return;
        }
        // During the poker-to-desktop transparent handoff, wait until the desktop
        // is revealed. A fullscreen-app hide keeps an already active layout alive.
        if (!_roomWindowActive && !_bossKeyContent.Visible) return;
        if (!_roomWindowActive)
        {
            _beforeRoomDogScreenPosition = DisplayServer.WindowGetPosition()
                + RoundToVector2I(_bossDogVisual.GlobalPosition);
            _beforeRoomTaskbarSnapped = _taskbarSnapped;
            _roomScreen = FindScreenIndexAtPoint(_beforeRoomDogScreenPosition);
            if (_roomScreen < 0) _roomScreen = DisplayServer.WindowGetCurrentScreen();
            CancelWindowDrag();
            _roomWindowActive = true;
            if (_roomDesktop == null)
            {
                _roomDesktop = GD.Load<PackedScene>("res://Scenes/Rooms/RoomDesktopPreview.tscn")
                    .Instantiate<RoomDesktopPreview>();
                _bossKeyContent.AddChild(_roomDesktop);
                _roomDesktop.LocalChat.InteractionAllowed = () => RoomChatInteractionAllowed;
                _roomDesktop.LocalChat.Opened += () => { CancelWindowDrag(); _settingsPanel.CloseImmediate(); };
                _roomDesktop.LocalChat.BubbleActivityChanged += _ => RefreshRoomBubbleActivity();
            }
            _roomDesktop.Show();
            ApplyRoomDesktopLayout(force: true);
        }
        else ApplyRoomDesktopLayout();
        if (_roomDragMember != 0 && (_hiddenByFullscreenApp || !RoomDragMemberIsCurrent()))
            CancelWindowDrag();
    }

    private void SyncRoomActivity()
    {
        if (_desktopRoomClient == null) return;
        if (CurrentMode == Mode.BossKey)
            _desktopRoomClient.SetReaction((int)_bossDogVisual.CurrentReaction);
        if (CurrentMode != Mode.BossKey || _hiddenByFullscreenApp || !_desktopTongueFeedbackEnabled)
            _desktopRoomClient.StopInputActivity();
    }

    private void OnRoomChatBubbleActivityChanged(bool active)
    {
        if (_roomChatBubbleActive == active) return;
        _roomChatBubbleActive = active;
        _roomChatBlindBoxRestoreRemaining = active ? 0 : RoomChatBlindBoxRestoreDelaySeconds;
        // Chat and reward balloons share the same space. Remove the old balloon
        // before the first chat frame, including any in-progress fade-in tween.
        _bossBlindBoxHint?.SetDisplayVisible(false, animate: false);
    }

    private void RefreshRoomBubbleActivity() => OnRoomChatBubbleActivityChanged(
        _roomDesktop?.LocalChat.HasBubble == true || _roomJoinNotice?.HasBubble == true);

    private void OnRoomJoinFailed(RoomFailure failure, bool timedOut)
    {
        _roomNoticeIsHostChange = false;
        _roomJoinNoticeKey = timedOut ? "Rooms_TimedOut" : InGameRoomPreview.FailureKey(failure);
        _roomJoinNoticeRemaining = RoomRules.ChatLifetime;
        _roomJoinNoticeWasVisible = false;
    }

    private void OnRoomJoinNoticeStateChanged()
    {
        if (_desktopRoomClient == null) return;
        var client = _desktopRoomClient;
        int owner = client.View?.OwnerId ?? 0;
        bool sameRoom = _roomNoticeLayoutSession == client.LayoutSession;
        bool transferred = sameRoom && _roomNoticeOwnerId != 0 && owner != 0 && owner != _roomNoticeOwnerId;
        _roomNoticeLayoutSession = client.LayoutSession;
        // BeginSession briefly clears the snapshot during the local-to-real
        // commit. Preserve its old owner until the first committed snapshot.
        if (owner != 0 || !sameRoom) _roomNoticeOwnerId = owner;
        if (transferred)
        {
            _roomNoticeIsHostChange = true;
            _roomNoticeHostName = client.View.Members.FirstOrDefault(m => m.Id == owner)?.Name ?? "";
            _roomJoinNoticeKey = owner == client.Id ? "Rooms_HostTransferredSelf" : "Rooms_HostTransferredOther";
            _roomJoinNoticeRemaining = RoomRules.ChatLifetime;
            _roomJoinNoticeWasVisible = false;
            return;
        }
        if (_roomNoticeIsHostChange && sameRoom && client.JoinedCode.Length > 0 && !client.IsBusy) return;
        // A new attempt, successful switch or explicit cancellation supersedes an old error.
        if (_desktopRoomClient.IsBusy || _desktopRoomClient.Operation == RoomOperation.None
            || _desktopRoomClient.RequestState is RoomRequestState.Succeeded or RoomRequestState.Cancelled)
            ClearRoomJoinNotice();
    }

    private void ClearRoomJoinNotice()
    {
        _roomJoinNoticeKey = "";
        _roomNoticeIsHostChange = false;
        _roomNoticeHostName = "";
        _roomJoinNoticeRemaining = 0;
        _roomJoinNoticeWasVisible = false;
        _roomJoinNotice?.Hide();
        if (_roomJoinNoticeLayer != null) _roomJoinNoticeLayer.Visible = false;
        _roomDesktop?.LocalChat.SetDisplaySuppressed(false);
        RefreshRoomBubbleActivity();
    }

    private void UpdateRoomJoinNotice(double delta)
    {
        if (_roomJoinNoticeRemaining <= 0) return;
        // A system notice cannot cover a reveal or run out while the desktop dog is hidden.
        bool visible = RoomBubblePresentationAllowed && _bossDogVisual.IsVisibleInTree();
        if (!visible)
        {
            _roomJoinNoticeWasVisible = false;
            _roomJoinNotice?.Hide();
            if (_roomJoinNoticeLayer != null) _roomJoinNoticeLayer.Visible = false;
            return;
        }
        if (_roomJoinNoticeWasVisible) _roomJoinNoticeRemaining = System.Math.Max(0, _roomJoinNoticeRemaining - delta);
        if (_roomJoinNoticeRemaining <= 0) { ClearRoomJoinNotice(); return; }
        if (_roomJoinNoticeLayer == null)
        {
            // Reuse the chat prefab above the settings layer, without stealing input/focus.
            _roomJoinNoticeLayer = GD.Load<PackedScene>("res://Scenes/Rooms/RoomJoinNotice.tscn")
                .Instantiate<CanvasLayer>();
            AddChild(_roomJoinNoticeLayer);
            _roomJoinNotice = _roomJoinNoticeLayer.GetChild<RoomChatView>(0);
            _roomJoinNotice.BubbleActivityChanged += _ => RefreshRoomBubbleActivity();
        }
        _roomJoinNotice.Scale = Vector2.One * _desktopPetScaleFactor;
        _roomJoinNotice.Position = _bossDogVisual.GlobalPosition + new Vector2(-49, -140) * _desktopPetScaleFactor;
        _roomJoinNoticeLayer.Visible = true;
        _roomJoinNotice.Show();
        string text = _roomNoticeIsHostChange
            ? string.Format(InGameRoomPreview.ChatText(_roomJoinNoticeKey), _roomNoticeHostName)
            : string.Format(InGameRoomPreview.ChatText("Rooms_JoinFailedNotice"), InGameRoomPreview.ChatText(_roomJoinNoticeKey));
        _roomJoinNotice.Present(false, false, text);
        // Announce the notice before retracting chat so their shared blind-box delay stays continuous.
        _roomDesktop?.LocalChat.SetDisplaySuppressed(true);
        _roomJoinNoticeWasVisible = true;
    }

    private void UpdateRoomChatBlindBoxDelay(double delta)
    {
        if (_roomChatBubbleActive || _roomChatBlindBoxRestoreRemaining <= 0) return;
        _roomChatBlindBoxRestoreRemaining = System.Math.Max(0, _roomChatBlindBoxRestoreRemaining - delta);
        if (_roomChatBlindBoxRestoreRemaining == 0)
            RefreshBossBlindBoxHint();
    }

    private void ApplyRoomDesktopLayout(bool force = false)
    {
        if (_roomScreen < 0 || _roomScreen >= DisplayServer.GetScreenCount()) _roomScreen = DisplayServer.GetPrimaryScreen();
        var screen = new Rect2I(DisplayServer.ScreenGetPosition(_roomScreen), DisplayServer.ScreenGetSize(_roomScreen));
        if (_roomCompactSession != _desktopRoomClient.LayoutSession || _roomCompactScreen != _roomScreen)
        {
            _roomCompactHostX = null;
            _roomCompactSession = _desktopRoomClient.LayoutSession;
            _roomCompactScreen = _roomScreen;
        }
        var host = RoomDesktopGeometry.Host(screen, RoomCompactDesktop, _roomCompactHostX);
        if (RoomCompactDesktop) _roomCompactHostX = host.Position.X;
        bool resized = _roomDesktopHost != host;
        _roomDesktopHost = host;
        _roomDesktopHostPosition = host.Position;
        var usable = DisplayServer.ScreenGetUsableRect(_roomScreen);
        // In extreme UI scales a panel may be wider than half a monitor. Fit only
        // this panel locally; never change saved UI scale or grow the host.
        UpdateRoomPanelScale(RoomCompactDesktop ? host.Size.X - 10 : null);
        bool changed = force || resized || _roomLastLayoutSnapshot != _desktopRoomClient.View
            || _roomLastLayoutScale != _desktopPetScaleFactor || _roomLastWorkArea != usable;
        // An ordinary transparent window, never the OS fullscreen mode.
        if (DisplayServer.WindowGetSize() != host.Size)
        {
            DisplayServer.WindowSetSize(host.Size);
            changed = true;
        }
        if (DisplayServer.WindowGetPosition() != host.Position)
        {
            DisplayServer.WindowSetPosition(host.Position);
            changed = true;
        }
        var dogPosition = _roomDesktop.Present(_desktopRoomClient,
            new Rect2(usable.Intersection(host).Position - host.Position, usable.Intersection(host).Size), _desktopPetScaleFactor, _panelSize.X,
            RoomTaskbarAnchorOffsetY, RoomSnapEnabled);
        if (_roomPokerDesktopPositions != null && _desktopRoomClient.View != null)
        {
            // Restore before the first revealed frame, including dogs that joined
            // during poker and therefore have no previous desktop placement.
            _roomDesktop.RestoreRemotePlacements(_roomPokerDesktopPositions, host.Position,
                RoomSnapEnabled, usable.End.Y - host.Position.Y - RoomTaskbarAnchorOffsetY);
            _roomPokerDesktopPositions = null;
        }
        if (!_hiddenByFullscreenApp && IsRoomLocalDogHit(GetViewport().GetMousePosition()))
            _roomDesktop.LocalChat.Present(true, true,
                _desktopRoomClient.Bubbles.TryGetValue(_desktopRoomClient.Id, out var ownBubble) ? ownBubble.Text : "");
        _roomDesktop.UpdateNameBars(
            new Rect2(_bossStatusPanelBasePosition - _bossDogVisual.Position, _bossStatusPanelBaseSize),
            _mainText.GetThemeFont("font"), _bossStatusPanel.GetThemeStylebox("panel"),
            RoomSnapEnabled && SettingsManager.LoadCenterCounterOnTaskbar(),
            Mathf.Max(0, screen.End.Y - usable.End.Y) / _desktopPetScaleFactor,
            _bossTaskBarAnchor.Position.Y - _bossDogVisual.Position.Y, BossCounterTongueClearance);
        if (changed)
        {
            var movementBounds = new Rect2(Vector2.Zero, host.Size);
            _roomDesktop.ConstrainRemotes(movementBounds);
            if (_desktopRoomClient.View?.Members.Any(m => m.Id == _desktopRoomClient.Id) == true)
                dogPosition = _roomDesktop.ConstrainMember(_desktopRoomClient.Id, RoomLocalFootprint, movementBounds);
        }
        var offset = dogPosition - _bossDogVisual.Position * _desktopPetScaleFactor;
        bool localSnapped = _roomDesktop.IsTaskbarSnapped(_desktopRoomClient.Id);
        changed |= _bossContentOffset != offset || _taskbarSnapped != localSnapped;
        if (!changed) return;
        _roomLastLayoutSnapshot = _desktopRoomClient.View;
        _roomLastLayoutScale = _desktopPetScaleFactor;
        _roomLastWorkArea = usable;
        _bossContentOffset = offset;
        _bossContentA.Position = offset;
        _bossCanvasLayer.Offset = offset;
        _bossBubbleLayer.Offset = offset;
        _taskbarSnapped = localSnapped;
        _bossWorkAreaSnapshotReady = false;
        UpdateBossInteractionRects();
        ApplyBossCounterLayout();
        ConfigureBossRiseIntro();
        UpdateBossBlindBoxOverlayPosition();
        if (_settingsPanel.IsOpen) PositionPanelInBestSlot();
    }

    private void UpdateRoomPanelScale(float? availableWidth)
    {
        float designWidth = _settingsPanel.PanelSize.X / _settingsPanel.RenderScale;
        float scale = availableWidth is { } width ? Mathf.Min(_otherUiScaleFactor, width / designWidth) : _otherUiScaleFactor;
        if (Mathf.IsEqualApprox(_settingsPanel.RenderScale, scale)) return;
        _settingsPanel.SetRenderScale(scale);
        _panelSize = _settingsPanel.PanelSize;
    }

    private void ConstrainRoomRewardBubbles()
    {
        // Opening a bubble never shifts the room or its dogs. Chat already fits
        // to the viewport; give the existing reward balloon/shell the same limit.
        var bounds = new Rect2(Vector2.Zero, DisplayServer.WindowGetSize()).Grow(-4);
        _bossBlindBoxHint.Position = _roomBlindBoxHintBasePosition;
        var hint = GetBossBlindBoxHintRect();
        var hintOrigin = RoomDesktopGeometry.Clamp(hint.Position, new Rect2(Vector2.Zero, hint.Size), bounds);
        _bossBlindBoxHint.Position += (hintOrigin - hint.Position) / _desktopPetScaleFactor;
        var reveal = GetBossBlindBoxOverlayRect();
        _bossBlindBoxOverlay.Offset += RoomDesktopGeometry.Clamp(reveal.Position,
            new Rect2(Vector2.Zero, reveal.Size), bounds) - reveal.Position;
    }

    private void RestoreRoomDesktopWindow()
    {
        if (!_roomWindowActive) return;
        CancelWindowDrag();
        _roomWindowActive = false;
        _roomDesktop.Hide();
        _bossBlindBoxHint.Position = _roomBlindBoxHintBasePosition;
        UpdateRoomPanelScale(null);
        if (DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen
            or DisplayServer.WindowMode.ExclusiveFullscreen or DisplayServer.WindowMode.Maximized)
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        ApplyDesktopPetScaleGeometry(_desktopPetScaleStep);
        SetupFatWindow();
        DisplayServer.WindowSetPosition(_beforeRoomDogScreenPosition - RoundToVector2I(_bossDogVisual.GlobalPosition));
        _taskbarSnapped = _beforeRoomTaskbarSnapped;
        _bossWorkAreaSnapshotReady = false;
        ApplyBossCounterLayout();
        UpdateBossBlindBoxOverlayPosition();
        if (_settingsPanel.IsOpen) PositionPanelInBestSlot();
    }

    private bool IsRoomLocalDogHit(Vector2 point) => _roomLocalDogHits != null
        && _roomLocalDogHits.Any(hit => RoomDogView.ContainsWindowPoint(hit, point));

    private int HitRoomDog(Vector2 point)
    {
        if (_hiddenByFullscreenApp || !_roomDesktop.IsVisibleInTree()) return 0;
        // Match the drawing order even when the dogs overlap completely.
        if (IsRoomLocalDogHit(point) && _desktopRoomClient.View?.Members.Any(m => m.Id == _desktopRoomClient.Id) == true)
            return _desktopRoomClient.Id;
        return _roomDesktop.HitTest(point);
    }

    private bool RoomDragMemberIsCurrent() => _desktopRoomClient.Session == _roomDragSession
        && !_desktopRoomClient.HiddenMembers.Contains(_roomDragMember)
        && _desktopRoomClient.View?.Members.Any(m => m.Id == _roomDragMember && m.Presence == _roomDragPresence) == true
        && _roomDesktop.HasMember(_desktopRoomClient.LayoutSession, _roomDragMember, _roomDragPresence);

    private void HandleRoomPointerInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (!button.Pressed)
            {
                if (_isDragging) GetViewport().SetInputAsHandled();
                CancelWindowDrag();
                return;
            }
            CancelWindowDrag();
            var point = button.Position;
            if (_roomDesktop.LocalChat.ContainsPoint(point) || _settingsPanel.ContainsPoint(point)
#if DEBUG
                || (_steamMockPanel != null && _steamMockPanel.ContainsPoint(point))
#endif
                || GetBossStatusPanelRect().HasPoint(point)
                || (_bossBlindBoxHint is { Visible: true } && _bossBlindBoxHint.MouseFilter != Control.MouseFilterEnum.Ignore
                    && GetBossBlindBoxHintRect().HasPoint(point))
                || (_bossBlindBoxOverlay is { Visible: true } && GetBossBlindBoxOverlayRect().HasPoint(point))) return;
            int memberId = HitRoomDog(point);
            if (memberId == 0) return;
            // Network snapshots can change after the previous presentation frame.
            // Ignore a dog whose old node has not been removed/replaced yet.
            var member = _desktopRoomClient.View?.Members.FirstOrDefault(m => m.Id == memberId);
            if (member == null || !_roomDesktop.HasMember(_desktopRoomClient.LayoutSession, memberId, member.Presence)) return;
            _roomDragMember = memberId;
            _roomDragSession = _desktopRoomClient.Session;
            _roomDragPresence = member.Presence;
            _roomDragMouseStart = point + DisplayServer.WindowGetPosition();
            _roomDragDogStart = _roomDesktop.GetPosition(memberId) + DisplayServer.WindowGetPosition();
            _potentialDrag = true;
        }
        else if (@event is InputEventMouseMotion motion && _roomDragMember != 0)
        {
            if ((motion.ButtonMask & MouseButtonMask.Left) == 0 || !RoomDragMemberIsCurrent())
            {
                CancelWindowDrag();
                return;
            }
            var delta = motion.Position + DisplayServer.WindowGetPosition() - _roomDragMouseStart;
            if (!_isDragging && delta.LengthSquared() < DefaultDragThreshold * DefaultDragThreshold) return;
            _isDragging = true;
            SetClickThrough(false);
            var footprint = _roomDragMember == _desktopRoomClient.Id
                ? RoomLocalFootprint : _roomDesktop.GetFootprint(_roomDragMember);
            var desired = _roomDragDogStart + delta;
            var host = _roomDesktopHost;
            bool pushingContact = false;
            if (RoomCompactDesktop && _roomDragMember == _desktopRoomClient.Id)
            {
                var screen = new Rect2I(DisplayServer.ScreenGetPosition(_roomScreen), DisplayServer.ScreenGetSize(_roomScreen));
                host = RoomDesktopGeometry.Push(host, screen, desired, footprint);
                _roomCompactHostX = host.Position.X;
                // Window movement can produce another motion at the same screen
                // position. Also keep feedback when sliding vertically along the
                // pushing edge; only returning inside should start its fade.
                pushingContact = (desired.X + footprint.Position.X <= host.Position.X + .5f
                        && host.Position.X > screen.Position.X + 1)
                    || (desired.X + footprint.End.X >= host.End.X - .5f
                        && host.End.X < screen.End.X - 1);
            }
            bool pushed = host.Position != _roomDesktopHost.Position;
            if (pushed) DisplayServer.WindowSetPosition(host.Position);
            float snappedDogY = DisplayServer.ScreenGetUsableRect(_roomScreen).End.Y
                - host.Position.Y - RoomTaskbarAnchorOffsetY;
            var requested = desired - host.Position;
            _roomDesktop.MoveMember(_roomDragMember, requested, snappedDogY,
                RoomSnapEnabled, SnapThreshold, BreakawayThreshold);
            var snapped = _roomDesktop.GetPosition(_roomDragMember);
            var bounded = _roomDesktop.ConstrainMember(_roomDragMember, footprint, new Rect2(Vector2.Zero, host.Size));
            var edges = RoomDesktopGeometry.Blocked(snapped, bounded);
            if (pushed || pushingContact) edges |= RoomBoundaryEdges.Left | RoomBoundaryEdges.Right;
            _roomDesktop.Boundary.Indicate(new Rect2(Vector2.Zero, host.Size), edges);
            ApplyRoomDesktopLayout();
            GetViewport().SetInputAsHandled();
        }
    }

    // Development-only assertions use the actual window, dog nodes and hit test.
#if DEBUG
    public bool RoomDesktopWindowActive => _roomWindowActive;
    public RoomDesktopPreview RoomDesktopForSmoke => _roomDesktop;
    public bool RoomDraggingForSmoke => _roomDragMember != 0 && _isDragging;
    public bool RoomClickThroughForSmoke => _isClickThrough;
    public bool RoomChatSuppressesHintForSmoke => RoomChatSuppressesBlindBoxHint;
    public RoomChatView RoomJoinNoticeForSmoke => _roomJoinNotice;
    public double RoomJoinNoticeRemainingForSmoke => _roomJoinNoticeRemaining;
    public void RoomAdvanceJoinNoticeForSmoke(double delta) => UpdateRoomJoinNotice(delta);
    public void RoomAdvanceChatHintForSmoke(double delta) => UpdateRoomChatBlindBoxDelay(delta);
    public void RoomRefreshHintForSmoke() => RefreshBossBlindBoxHint();
    public void RoomActivityForSmoke(int reaction, bool input)
    {
        _bossDogVisual.ApplyReaction((DataTables.EDogReactionTrigger)reaction);
        SyncRoomActivity();
        if (input) OnTypingInputOccurred(1);
    }
    public void RoomEquipmentRefreshForSmoke() => _gameData.EmitSignal(GameData.SignalName.EquipmentChanged);
    public void RoomSnapForSmoke(bool enabled)
    {
        _roomSnapEnabledForSmoke = enabled;
        if (_roomWindowActive) ApplyRoomDesktopLayout(force: true);
    }
    public bool RoomHitTestForSmoke(Vector2I point) => IsScreenPointOverInteractiveContent(point);
    public void RoomModeForSmoke(bool poker) { if (poker) EnterPlay(); else SwitchToBossKey(); }
    public void RoomFullscreenHideForSmoke(bool hidden)
    {
        // Hold the simulated OS result through a few frames without changing the
        // player's actual fullscreen-visibility setting.
        _fullscreenCheckTimer = 2;
        SetHiddenByFullscreenApp(hidden);
    }
#endif
}
#endif
