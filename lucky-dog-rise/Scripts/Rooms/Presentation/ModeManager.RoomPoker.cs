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
    private RoomPokerLayout _roomPokerLayout;
    private Vector2 _roomPokerOrigin;
    private Vector2 _roomPokerTableDragStart;
    private int _roomPokerDragMember;
    private long _roomPokerDragPresence;
    private long _roomPokerDragSession;
    private Vector2 _roomPokerDogDragStart;
    private Vector2 _roomPokerMouseDragStart;
    private RoomDesktopPreview.RemotePlacementSnapshot _roomPokerDesktopPositions;
    private Rect2I _roomPokerLastUsable;
    private Vector2I _roomPokerLastScreenSize;

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
        _roomPokerDisplay = _settingsPanel.RoomDisplayPreference == RoomDisplayMode.HalfScreenCompact
            ? RoomPokerDisplay.Row : RoomPokerDisplay.Desktop;
        _roomPokerScreen = CurrentMode == Mode.BossKey ? _roomScreen : DisplayServer.WindowGetCurrentScreen();
        _roomPokerDogs?.Clear();
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
        if (_roomPokerDisplay == RoomPokerDisplay.Desktop)
        {
            var screen = new Rect2I(DisplayServer.ScreenGetPosition(_roomPokerScreen), DisplayServer.ScreenGetSize(_roomPokerScreen));
            var host = new Rect2I(screen.Position + Vector2I.One, screen.Size - Vector2I.One * 2);
            if (!_roomPokerWindowActive)
                _roomPokerOrigin = (Vector2)(usable.Position - host.Position)
                    + new Vector2((usable.Size.X - GetPlayInfoPanelWidth() - GetPlayGameSize()) / 2f,
                        (usable.Size.Y - GetPlayGameSize()) / 2f);
            DisplayServer.WindowSetSize(host.Size);
            DisplayServer.WindowSetPosition(host.Position);
            ClampRoomPokerOrigin();
            _contentOffset = _roomPokerOrigin;
        }
        else
        {
            _roomPokerLayout = RoomPokerLayout.Calculate(usable.Size - Vector2I.One * 2,
                GetPlayInfoPanelWidth(), GetPlayGameSize(), (int)_panelSize.X,
                GetRoomPokerAccessoryHeight(), _desktopPetScaleFactor);
            _contentOffset = _roomPokerLayout.Origin;
            DisplayServer.WindowSetSize(_roomPokerLayout.Size);
            if (!_roomPokerWindowActive)
                DisplayServer.WindowSetPosition(usable.Position + (usable.Size - _roomPokerLayout.Size) / 2);
            KeepRoomPokerRowWithinScreen();
        }
        _roomPokerWindowActive = true;
#if DEBUG
        _steamMockPanel?.SetPanelBottom(_roomPokerDisplay == RoomPokerDisplay.Row
            ? GetRoomPokerAccessoryHeight() : _contentOffset.Y);
#endif
        return true;
    }

    private void ClampRoomPokerOrigin()
    {
        var usable = DisplayServer.ScreenGetUsableRect(_roomPokerScreen);
        var min = (Vector2)(usable.Position - DisplayServer.WindowGetPosition()) + new Vector2(5, 5);
        _roomPokerOrigin.X = Mathf.Clamp(_roomPokerOrigin.X, min.X,
            Math.Max(min.X, min.X + usable.Size.X - GetPlayInfoPanelWidth() - GetPlayGameSize() - 10));
        _roomPokerOrigin.Y = Mathf.Clamp(_roomPokerOrigin.Y, min.Y + GetRoomPokerAccessoryHeight(),
            Math.Max(min.Y + GetRoomPokerAccessoryHeight(), min.Y + usable.Size.Y - GetPlayGameSize() - 10));
    }

    private void KeepRoomPokerRowWithinScreen()
    {
        var usable = GetBestScreenUsableRect(new Rect2I(DisplayServer.WindowGetPosition(), DisplayServer.WindowGetSize()));
        var pos = DisplayServer.WindowGetPosition();
        var size = DisplayServer.WindowGetSize();
        // A large poker UI can itself exceed the screen: keep its top/left controls accessible.
        pos.X = Math.Clamp(pos.X, usable.Position.X, Math.Max(usable.Position.X, usable.End.X - size.X));
        pos.Y = Math.Clamp(pos.Y, usable.Position.Y, Math.Max(usable.Position.Y, usable.End.Y - size.Y));
        DisplayServer.WindowSetPosition(pos);
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
        }
        float scale = _roomPokerDisplay == RoomPokerDisplay.Row ? _roomPokerLayout.DogScale : _desktopPetScaleFactor;
        _roomPokerDogs.Visible = !_hiddenByFullscreenApp && scale > 0;
        if (!_roomPokerDogs.Visible) return;
        var usable = DisplayServer.ScreenGetUsableRect(_roomPokerScreen);
        var bounds = new Rect2(usable.Position - DisplayServer.WindowGetPosition(), usable.Size);
        _roomPokerDogs.PresentPoker(_desktopRoomClient, bounds, scale,
            _roomPokerDisplay == RoomPokerDisplay.Row ? _roomPokerLayout.Row : null, _roomDesktop,
            _roomDesktopHostPosition - DisplayServer.WindowGetPosition(),
            new Rect2(_contentOffset, new Vector2(GetPlayInfoPanelWidth() + GetPlayGameSize(), GetPlayGameSize())),
            RoomSnapEnabled, RoomTaskbarAnchorOffsetY);
        var screen = new Rect2I(DisplayServer.ScreenGetPosition(_roomPokerScreen), DisplayServer.ScreenGetSize(_roomPokerScreen));
        _roomPokerDogs.UpdateNameBars(
            new Rect2(_bossStatusPanelBasePosition - _bossDogVisual.Position, _bossStatusPanelBaseSize),
            _mainText.GetThemeFont("font"), _bossStatusPanel.GetThemeStylebox("panel"),
            _roomPokerDisplay == RoomPokerDisplay.Desktop && RoomSnapEnabled && SettingsManager.LoadCenterCounterOnTaskbar(),
            Mathf.Max(0, screen.End.Y - usable.End.Y) / scale,
            _bossTaskBarAnchor.Position.Y - _bossDogVisual.Position.Y, BossCounterTongueClearance);
        if (_roomPokerDisplay == RoomPokerDisplay.Desktop)
            _roomPokerDogs.ConstrainRemotes(new Rect2(Vector2.Zero, DisplayServer.WindowGetSize()));
        if (_roomPokerDragMember != 0 && !RoomPokerDragIsCurrent()) CancelWindowDrag();
    }

    private void StopRoomPokerPresentation(bool restoreWindow = false)
    {
        bool rendererAlive = IsInstanceValid(_roomPokerDogs) && _roomPokerDogs.IsInsideTree();
        if (_desktopRoomClient?.JoinedCode.Length is not > 0) _roomPokerDesktopPositions = null;
        else if (rendererAlive && _roomPokerWindowActive && _roomPokerDisplay == RoomPokerDisplay.Desktop)
            _roomPokerDesktopPositions = _roomPokerDogs.CaptureRemotePlacements(DisplayServer.WindowGetPosition());
        if (rendererAlive) _roomPokerDogs.Hide();
        bool wasActive = _roomPokerWindowActive;
        _roomPokerWindowActive = false;
        _roomPokerDisplay = RoomPokerDisplay.Hidden;
        _roomPokerDragMember = 0;
        if (!wasActive || !restoreWindow || !rendererAlive) return;
        var anchor = DisplayServer.WindowGetPosition() + RoundToVector2I(_playViewport.Position);
        CancelWindowDrag();
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
                || new Rect2(_contentOffset, new Vector2(GetPlayInfoPanelWidth() + GetPlayGameSize(), GetPlayGameSize())).HasPoint(button.Position)) return false;
            int id = _roomPokerDogs.HitTest(button.Position);
            if (id == 0) return false;
            if (_roomPokerDisplay == RoomPokerDisplay.Desktop && !UiInteractionOccupancy.IsOccupied(this))
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
            if ((motion.ButtonMask & MouseButtonMask.Left) == 0 || !RoomPokerDragIsCurrent())
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
        if (!_roomPokerWindowActive || _roomPokerDisplay != RoomPokerDisplay.Desktop) return false;
        _roomPokerOrigin = _roomPokerTableDragStart + delta;
        ClampRoomPokerOrigin();
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
    public void RoomMovePokerTableForSmoke(Vector2I delta)
    { _roomPokerTableDragStart = _roomPokerOrigin; MoveRoomPokerTable(delta); }
#endif
}
#endif
