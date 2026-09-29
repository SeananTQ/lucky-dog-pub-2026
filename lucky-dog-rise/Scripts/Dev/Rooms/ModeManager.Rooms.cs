#if DEBUG && !RECORDING_BUILD
using Godot;
using LuckyDogRise.Rooms;

namespace LuckyDogRise;

public partial class ModeManager
{
    private RoomClient _desktopRoomClient;
    private RoomDesktopPreview _roomDesktop;
    private bool _roomWindowActive;
    private int _roomScreen;
    private Vector2I _beforeRoomDogScreenPosition;
    private bool _beforeRoomTaskbarSnapped;
    private RoomSnapshot _roomLastLayoutSnapshot;
    private float _roomLastLayoutScale;
    private Rect2I _roomLastWorkArea;

    private void AttachRoomDesktopPreview(RoomClient client) => _desktopRoomClient = client;

    private void UpdateRoomDesktopPreview()
    {
        if (_desktopRoomClient == null) return;
        bool joined = _desktopRoomClient.JoinedCode.Length > 0;
        if (!joined || CurrentMode != Mode.BossKey)
        {
            RestoreRoomDesktopWindow();
            if (!joined) _roomDesktop?.Clear();
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
                _roomDesktop = GD.Load<PackedScene>("res://Scenes/Dev/Rooms/RoomDesktopPreview.tscn")
                    .Instantiate<RoomDesktopPreview>();
                _bossKeyContent.AddChild(_roomDesktop);
            }
            _roomDesktop.Show();
            ApplyRoomDesktopLayout(force: true);
        }
        else ApplyRoomDesktopLayout();
    }

    private void ApplyRoomDesktopLayout(bool force = false)
    {
        if (_roomScreen >= DisplayServer.GetScreenCount()) _roomScreen = DisplayServer.GetPrimaryScreen();
        var screen = new Rect2I(DisplayServer.ScreenGetPosition(_roomScreen), DisplayServer.ScreenGetSize(_roomScreen));
        // Godot's Windows backend infers exclusive fullscreen from an exact
        // monitor-sized rectangle, then ignores subsequent resize/move calls.
        // Leave a physical pixel on every edge to stay an ordinary layered window.
        var host = new Rect2I(screen.Position + Vector2I.One, screen.Size - Vector2I.One * 2);
        var usable = DisplayServer.ScreenGetUsableRect(_roomScreen);
        bool changed = force || _roomLastLayoutSnapshot != _desktopRoomClient.View
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
            new Rect2(usable.Position - host.Position, usable.Size), _desktopPetScaleFactor);
        var offset = dogPosition - _bossDogVisual.Position * _desktopPetScaleFactor;
        changed |= _bossContentOffset != offset;
        if (!changed) return;
        _roomLastLayoutSnapshot = _desktopRoomClient.View;
        _roomLastLayoutScale = _desktopPetScaleFactor;
        _roomLastWorkArea = usable;
        _bossContentOffset = offset;
        _bossContentA.Position = offset;
        _bossCanvasLayer.Offset = offset;
        _bossBubbleLayer.Offset = offset;
        _taskbarSnapped = false;
        _bossWorkAreaSnapshotReady = false;
        UpdateBossInteractionRects();
        ApplyBossCounterLayout();
        ConfigureBossRiseIntro();
        UpdateBossBlindBoxOverlayPosition();
        if (_settingsPanel.IsOpen) PositionPanelInBestSlot();
    }

    private void RestoreRoomDesktopWindow()
    {
        if (!_roomWindowActive) return;
        _roomWindowActive = false;
        _roomDesktop.Hide();
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

    // Development-only assertions use the actual window, dog nodes and hit test.
    public bool RoomDesktopWindowActive => _roomWindowActive;
    public RoomDesktopPreview RoomDesktopForSmoke => _roomDesktop;
    public bool RoomHitTestForSmoke(Vector2I point) => IsScreenPointOverInteractiveContent(point);
    public void RoomModeForSmoke(bool poker) { if (poker) SwitchToPlay(); else SwitchToBossKey(); }
    public void RoomFullscreenHideForSmoke(bool hidden)
    {
        // Hold the simulated OS result through a few frames without changing the
        // player's actual fullscreen-visibility setting.
        _fullscreenCheckTimer = 2;
        SetHiddenByFullscreenApp(hidden);
    }
}
#endif
