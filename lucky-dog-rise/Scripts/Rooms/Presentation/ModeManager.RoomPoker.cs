#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Linq;
using Godot;
using LuckyDogRise.Rooms;

namespace LuckyDogRise;

public partial class ModeManager
{
    private RoomPokerDisplay _roomPokerDisplay;
    private RoomDesktopPreview _roomPokerDogs;
    private bool _roomPokerWindowActive;
    private int _roomPokerScreen;
    private float _roomPokerRenderScale = 1;
    private Vector2 _roomPokerOrigin;
    private Vector2 _roomPokerTableDragStart;
    private int _roomPokerDragMember;
    private long _roomPokerDragPresence;
    private long _roomPokerDragSession;
    private Vector2 _roomPokerDogDragStart;
    private Vector2 _roomPokerMouseDragStart;
    private RoomDesktopPreview.RemotePlacementSnapshot _roomPokerDesktopPositions;
    private RoomDesktopPreview.RemotePlacementSnapshot _roomPokerEntryPositions;
    private bool _roomResumeDesktop;
    private Rect2I _roomPokerLastUsable;
    private Vector2I _roomPokerLastScreenSize;
    private Texture2D _roomPokerChatIcon;

    private bool RoomPokerChatBaseAllowed => CurrentMode == Mode.Play && _roomPokerWindowActive
        && !_hiddenByFullscreenApp && _desktopRoomClient?.JoinedCode.Length is > 0
        && !_blindBoxOpeningUiActive && _gameData?.PendingBlindBoxReward == null;
    private bool RoomPokerChatInteractionAllowed => RoomPokerChatBaseAllowed
        && _desktopRoomClient?.ChatAllowed == true && !UiInteractionOccupancy.IsOccupied(this)
        && !_potentialDrag && !_isDragging;

    private void RefreshRoomPokerChatEntry()
    {
        if (_gameManager == null) return;
        bool visible = CurrentMode == Mode.Play && _desktopRoomClient?.JoinedCode.Length is > 0;
        if (visible) _roomPokerChatIcon ??= GD.Load<Texture2D>("res://Assets/UI/Icon/Icon_RoomChat.svg");
        _gameManager.SetRoomChatEntry(visible, RoomPokerChatBaseAllowed
            && _desktopRoomClient?.ChatAllowed == true && _gameManager.CanUseRoomChatShortcut,
            _roomPokerChatIcon, InGameRoomPreview.ChatText(_desktopRoomClient?.ChatAllowed == true
                ? "Rooms_ChatOpen" : "Rooms_ChatDisabled"));
    }

    private void OpenRoomPokerChat()
    {
        if (!RoomPokerChatInteractionAllowed || _roomPokerDogs == null) return;
        // Refresh the anchor and local/client context before a click queued on
        // the same frame as entry or welcome dismissal opens the composer.
        UpdateRoomPokerPreview(joined: true);
        _roomPokerDogs.LocalChat.OpenChat();
    }

    private int GetRoomPokerAccessoryHeight()
    {
#if DEBUG
        return _steamMockPanelRequestedVisible ? GetPlayTopAccessoryHeight() : 0;
#else
        return 0;
#endif
    }

    private bool TryPrepareRoomPokerEntry()
    {
        if (CurrentMode != Mode.BossKey || _desktopRoomClient?.JoinedCode.Length is not > 0) return true;
        if (UiInteractionOccupancy.IsOccupied(this)) return false;
        ApplyRoomPokerDisplayPreference();
        return true;
    }

    private void ApplyRoomPokerDisplayPreference()
    {
        _roomPokerDisplay = RoomPokerDisplay.Free;
        _roomPokerScreen = CurrentMode == Mode.BossKey ? _roomScreen : DisplayServer.WindowGetCurrentScreen();
    }

    // Joined mode changes keep the native host. Reusing renderers also avoids
    // recreating all remote avatars each time the player opens the poker table.
    private void SuspendRoomDesktopForPoker()
    {
        if (!_roomWindowActive || _roomPokerDisplay == RoomPokerDisplay.Hidden)
        { RestoreRoomDesktopWindow(); return; }
        CancelWindowDrag();
        _roomPokerEntryPositions = _roomDesktop.CaptureRemotePlacements(DisplayServer.WindowGetPosition());
        _roomWindowActive = false;
        _roomDesktop.Hide();
        _bossBlindBoxHint.Position = _roomBlindBoxHintBasePosition;
    }

    private Rect2I RoomPokerHost => new(DisplayServer.WindowGetPosition(), DisplayServer.WindowGetSize());
    private Rect2I RoomPokerScreenRect => new(DisplayServer.ScreenGetPosition(_roomPokerScreen),
        DisplayServer.ScreenGetSize(_roomPokerScreen));
    private Rect2 RoomPokerTableFootprint => new(0, -GetRoomPokerAccessoryHeight(),
        GetPlayInfoPanelWidth() + GetPlayGameSize(), GetPlayGameSize() + GetRoomPokerAccessoryHeight());

    private void ApplyPokerRenderScale(float scale)
    {
        _roomPokerRenderScale = scale;
        if (_playViewport != null) _playViewport.Scale = Vector2.One * (.5f * scale);
        _infoPanel?.SetRenderScale(scale);
    }

    private void UpdateRoomPokerDisplayPreference(bool joined)
    {
        // Joining while a hand is in progress only updates the presentation. Wait
        // for overlays to finish; never re-enter poker or reset cards to apply it.
        if (!joined || CurrentMode != Mode.Play || _roomPokerDisplay != RoomPokerDisplay.Hidden
            || _desktopRoomClient.View == null || _startupState != StartupState.Interactive
            || _hiddenByFullscreenApp || UiInteractionOccupancy.IsOccupied(this)) return;
        CancelWindowDrag();
        ApplyRoomPokerDisplayPreference();
        SetupRoomPokerWindow();
        UpdatePlayLayout();
        if (_settingsPanel.IsOpen) PositionPanelInBestSlot();
    }

    // Called in the ordinary poker setup path, including UI-scale changes.
    private bool SetupRoomPokerWindow()
    {
        if (_roomPokerDisplay == RoomPokerDisplay.Hidden || _desktopRoomClient?.JoinedCode.Length is not > 0)
            return false;
        if (_roomPokerScreen < 0 || _roomPokerScreen >= DisplayServer.GetScreenCount())
            _roomPokerScreen = DisplayServer.GetPrimaryScreen();
        var usable = DisplayServer.ScreenGetUsableRect(_roomPokerScreen);
        _roomPokerLastUsable = usable;
        _roomPokerLastScreenSize = DisplayServer.ScreenGetSize(_roomPokerScreen);
        if (_roomCompactSession != _desktopRoomClient.LayoutSession || _roomCompactScreen != _roomPokerScreen)
        {
            _roomCompactHostX = null;
            _roomCompactSession = _desktopRoomClient.LayoutSession;
            _roomCompactScreen = _roomPokerScreen;
        }
        var host = RoomDesktopGeometry.Host(RoomPokerScreenRect, RoomUsesCompactHost, _roomCompactHostX);
        if (RoomUsesCompactHost) _roomCompactHostX = host.Position.X;
        var available = usable.Intersection(host);
        ApplyPokerRenderScale(RoomPokerLayout.FitScale(available.Size, _otherUiScaleFactor,
            PlayInfoPanelBaseWidth, PlayGameBaseSize, GetRoomPokerAccessoryHeight()));
        UpdateRoomPanelScale(available.Size.X - 10, available.Size.Y - GetRoomPokerAccessoryHeight() - 10);
        if (!_roomPokerWindowActive)
            _roomPokerOrigin = (Vector2)(available.Position - host.Position)
                + new Vector2((available.Size.X - GetPlayInfoPanelWidth() - GetPlayGameSize()) / 2f,
                    (available.Size.Y - GetPlayGameSize() + GetRoomPokerAccessoryHeight()) / 2f);
        if (DisplayServer.WindowGetSize() != host.Size) DisplayServer.WindowSetSize(host.Size);
        if (DisplayServer.WindowGetPosition() != host.Position) DisplayServer.WindowSetPosition(host.Position);
        ClampRoomPokerOrigin();
        _contentOffset = _roomPokerOrigin;
        _roomPokerWindowActive = true;
#if DEBUG
        // Debug controls belong to the host, not the draggable poker table.
        _steamMockPanel?.SetPanelBottom(GetRoomPokerAccessoryHeight());
#endif
        return true;
    }

    private void ClampRoomPokerOrigin()
    {
        var usable = DisplayServer.ScreenGetUsableRect(_roomPokerScreen).Intersection(RoomPokerHost);
        var bounds = new Rect2(usable.Position - RoomPokerHost.Position, usable.Size).Grow(-5);
        _roomPokerOrigin = RoomDesktopGeometry.Clamp(_roomPokerOrigin, RoomPokerTableFootprint, bounds);
    }

    private void UpdateRoomPokerPreview(bool joined)
    {
        if (!joined || CurrentMode != Mode.Play)
        {
            StopRoomPokerPresentation(restoreWindow: CurrentMode == Mode.Play);
            return;
        }
        if (_roomPokerDisplay == RoomPokerDisplay.Hidden) return;
        if (!_roomPokerWindowActive) { SetupRoomPokerWindow(); UpdatePlayLayout(); }
        else if (_roomPokerScreen >= DisplayServer.GetScreenCount()
            || _roomPokerLastUsable != DisplayServer.ScreenGetUsableRect(_roomPokerScreen)
            || _roomPokerLastScreenSize != DisplayServer.ScreenGetSize(_roomPokerScreen))
        {
            CancelWindowDrag();
            SetupRoomPokerWindow();
            UpdatePlayLayout();
            if (_settingsPanel.IsOpen) PositionPanelInBestSlot();
        }
        if (_roomPokerDogs == null)
        {
            _roomPokerDogs = GD.Load<PackedScene>("res://Scenes/Rooms/RoomDesktopPreview.tscn")
                .Instantiate<RoomDesktopPreview>();
            _roomPokerDogs.Name = "RoomPokerDogs";
            AddChild(_roomPokerDogs);
            _roomPokerDogs.LocalChat.HoverEntryEnabled = false;
            _roomPokerDogs.LocalChat.InteractionAllowed = () => RoomPokerChatInteractionAllowed;
            _roomPokerDogs.LocalChat.Opened += () => { CancelWindowDrag(); _settingsPanel.CloseImmediate(); };
        }
        RefreshRoomPokerChatEntry();
        float scale = _desktopPetScaleFactor;
        _roomPokerDogs.Visible = !_hiddenByFullscreenApp && scale > 0;
        if (!_roomPokerDogs.Visible) return;
        var usable = DisplayServer.ScreenGetUsableRect(_roomPokerScreen);
        var available = usable.Intersection(RoomPokerHost);
        var bounds = new Rect2(available.Position - RoomPokerHost.Position, available.Size);
        _roomPokerDogs.PresentPoker(_desktopRoomClient, bounds, scale,
            _roomDesktop,
            _roomDesktopHostPosition - DisplayServer.WindowGetPosition(),
            new Rect2(_contentOffset, new Vector2(GetPlayInfoPanelWidth() + GetPlayGameSize(), GetPlayGameSize())),
            RoomSnapEnabled, RoomTaskbarAnchorOffsetY, foreground: !UiInteractionOccupancy.IsOccupied(this, _settingsPanel));
        var chat = _roomPokerDogs.LocalChat;
        chat.Position = _playViewport.GetGlobalTransformWithCanvas() * _gameManager.RoomChatAnchorPosition;
        chat.Scale = Vector2.One * _roomPokerRenderScale;
        chat.SetDisplaySuppressed(!RoomPokerChatBaseAllowed || UiInteractionOccupancy.IsOccupied(this));
        if (_roomPokerEntryPositions != null && _desktopRoomClient.View != null)
        {
            _roomPokerDogs.RestoreRemotePlacements(_roomPokerEntryPositions, RoomPokerHost.Position,
                RoomSnapEnabled, usable.End.Y - RoomPokerHost.Position.Y - RoomTaskbarAnchorOffsetY);
            _roomPokerEntryPositions = null;
        }
        var screen = new Rect2I(DisplayServer.ScreenGetPosition(_roomPokerScreen), DisplayServer.ScreenGetSize(_roomPokerScreen));
        _roomPokerDogs.UpdateNameBars(
            new Rect2(_bossStatusPanelBasePosition - _bossDogVisual.Position, _bossStatusPanelBaseSize),
            _mainText.GetThemeFont("font"), _bossStatusPanel.GetThemeStylebox("panel"),
            RoomSnapEnabled && SettingsManager.LoadCenterCounterOnTaskbar(),
            Mathf.Max(0, screen.End.Y - usable.End.Y) / scale,
            _bossTaskBarAnchor.Position.Y - _bossDogVisual.Position.Y, BossCounterTongueClearance);
        _roomPokerDogs.ConstrainRemotes(new Rect2(Vector2.Zero, DisplayServer.WindowGetSize()));
        if (_roomPokerDragMember != 0 && !RoomPokerDragIsCurrent()) CancelWindowDrag();
    }

    private void StopRoomPokerPresentation(bool restoreWindow = false)
    {
        bool rendererAlive = IsInstanceValid(_roomPokerDogs) && _roomPokerDogs.IsInsideTree();
        if (_desktopRoomClient?.JoinedCode.Length is not > 0) _roomPokerDesktopPositions = null;
        else if (rendererAlive && _roomPokerWindowActive)
            _roomPokerDesktopPositions = _roomPokerDogs.CaptureRemotePlacements(DisplayServer.WindowGetPosition());
        if (rendererAlive) _roomPokerDogs.Hide();
        bool wasActive = _roomPokerWindowActive;
        _roomPokerWindowActive = false;
        _roomPokerDisplay = RoomPokerDisplay.Hidden;
        _roomPokerEntryPositions = null;
        _roomPokerDragMember = 0;
        RefreshRoomPokerChatEntry();
        if (!wasActive || !restoreWindow || !rendererAlive) return;
        var anchor = DisplayServer.WindowGetPosition() + RoundToVector2I(_playViewport.Position);
        CancelWindowDrag();
        ApplyPokerRenderScale(_otherUiScaleFactor);
        UpdateRoomPanelScale(null);
        SetupPlayFatWindow();
        UpdatePlayLayout();
        DisplayServer.WindowSetPosition(anchor - RoundToVector2I(_playViewport.Position));
        KeepPlayContentWithinScreen();
        UpdatePlayLayout();
        if (_settingsPanel.IsOpen) PositionPanelInBestSlot();
    }

    private bool RoomPokerDragIsCurrent() => _desktopRoomClient != null
        && _desktopRoomClient.LayoutSession == _roomPokerDragSession
        && !_desktopRoomClient.HiddenMembers.Contains(_roomPokerDragMember)
        && _desktopRoomClient.View?.Members.Any(m => m.Id == _roomPokerDragMember && m.Presence == _roomPokerDragPresence) == true;

    private bool HandleRoomPokerPointer(InputEvent input)
    {
        if (!_roomPokerWindowActive || _roomPokerDogs?.Visible != true) return false;
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (!button.Pressed && _roomPokerDragMember != 0)
            { CancelWindowDrag(); GetViewport().SetInputAsHandled(); return true; }
            if (!button.Pressed || _settingsPanel.ContainsPoint(button.Position)
                || UiInteractionOccupancy.IsOccupied(this)) return false;
#if DEBUG
            if (_steamMockPanel?.ContainsPoint(button.Position) == true) return false;
#endif
            // The foreground dog owns overlap clicks before the underlying table.
            int id = _roomPokerDogs.HitTest(button.Position);
            if (id == 0) return false;
            if (!UiInteractionOccupancy.IsOccupied(this))
            {
                var member = _desktopRoomClient.View?.Members.FirstOrDefault(m => m.Id == id);
                if (member == null) return true;
                _roomPokerDragMember = id;
                _roomPokerDragPresence = member.Presence;
                _roomPokerDragSession = _desktopRoomClient.LayoutSession;
                _roomPokerDogDragStart = _roomPokerDogs.GetPosition(id);
                _roomPokerMouseDragStart = button.Position;
            }
            GetViewport().SetInputAsHandled();
            return true;
        }
        if (input is InputEventMouseMotion motion && _roomPokerDragMember != 0)
        {
            if ((motion.ButtonMask & MouseButtonMask.Left) == 0 || !RoomPokerDragIsCurrent()
                || UiInteractionOccupancy.IsOccupied(this))
            { CancelWindowDrag(); return true; }
            var delta = motion.Position - _roomPokerMouseDragStart;
            if (delta.Length() < DefaultDragThreshold && !_isDragging) return true;
            _isDragging = true;
            SetClickThrough(false);
            var usable = DisplayServer.ScreenGetUsableRect(_roomPokerScreen);
            var min = (Vector2)(usable.Position - DisplayServer.WindowGetPosition());
            var max = min + usable.Size;
            var p = _roomPokerDogDragStart + delta;
            _roomPokerDogs.MoveMember(_roomPokerDragMember, p, max.Y - RoomTaskbarAnchorOffsetY,
                RoomSnapEnabled, SnapThreshold, BreakawayThreshold);
            var snapped = _roomPokerDogs.GetPosition(_roomPokerDragMember);
            var bounds = new Rect2(Vector2.Zero, DisplayServer.WindowGetSize());
            var accepted = _roomPokerDogs.ConstrainMember(_roomPokerDragMember,
                _roomPokerDogs.GetFootprint(_roomPokerDragMember), bounds);
            _roomPokerDogs.Boundary.Indicate(bounds, RoomDesktopGeometry.Blocked(snapped, accepted));
            GetViewport().SetInputAsHandled();
            return true;
        }
        return false;
    }

    private bool MoveRoomPokerTable(Vector2I delta)
    {
        if (!_roomPokerWindowActive) return false;
        // The drag anchor is in screen coordinates; moving the host must not
        // accumulate its translation into the next mouse event's delta.
        var desired = _roomPokerTableDragStart + delta;
        var host = RoomPokerHost;
        var footprint = RoomPokerTableFootprint;
        bool contact = false;
        if (RoomUsesCompactHost)
        {
            // Five-pixel inset is also used by ClampRoomPokerOrigin.
            host = RoomDesktopGeometry.Push(host, RoomPokerScreenRect, desired, footprint.Grow(5));
            contact = RoomDesktopGeometry.PushingContact(host, RoomPokerScreenRect, desired, footprint.Grow(5));
            _roomCompactHostX = host.Position.X;
        }
        bool pushed = host.Position != RoomPokerHost.Position;
        if (pushed) DisplayServer.WindowSetPosition(host.Position);
        _roomPokerOrigin = desired - host.Position;
        ClampRoomPokerOrigin();
        var edges = RoomDesktopGeometry.Blocked(desired, _roomPokerOrigin + host.Position);
        if (pushed || contact) edges |= RoomBoundaryEdges.Left | RoomBoundaryEdges.Right;
        _roomPokerDogs?.Boundary.Indicate(new Rect2(Vector2.Zero, host.Size), edges);
        _contentOffset = _roomPokerOrigin;
        UpdatePlayLayout();
        if (_settingsPanel.IsOpen) PositionPanelInBestSlot();
        return true;
    }

#if DEBUG
    public RoomDesktopPreview RoomPokerDogsForSmoke => _roomPokerDogs;
    public RoomPokerDisplay RoomPokerDisplayForSmoke => _roomPokerDisplay;
    public void RoomRequestPokerForSmoke() => SwitchToPlay();
    public void RoomOtherScaleForSmoke(int step) => ApplyOtherUiScaleStep(step, preserveContentAnchor: true);
    public void RoomMovePokerTableForSmoke(Vector2I delta, bool begin = true)
    {
        if (begin) _roomPokerTableDragStart = _roomPokerOrigin + DisplayServer.WindowGetPosition();
        MoveRoomPokerTable(delta);
    }
    public Rect2 RoomPokerTableRectForSmoke => new(_roomPokerOrigin, new Vector2(GetPlayInfoPanelWidth() + GetPlayGameSize(), GetPlayGameSize()));
    public float RoomPokerRenderScaleForSmoke => _roomPokerRenderScale;
#endif
}
#endif
