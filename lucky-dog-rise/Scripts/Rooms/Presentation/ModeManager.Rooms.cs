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
        AddChild(row);
        AddChild(desktop);
        try
        {
            row.Hide();
            desktop.Hide();
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
            GD.Print("[RoomExportSmoke] Room page, translations, directory row and remote dog bindings passed.");
        }
        finally
        {
            desktop.Free();
            row.Free();
        }
    }

    private RoomClient _desktopRoomClient;
    private RoomDesktopPreview _roomDesktop;
    private bool _roomWindowActive;
    private int _roomScreen;
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
        RestoreRoomDesktopWindow();
        _roomDesktop?.Clear();
        _roomLastLayoutSnapshot = null;
        _desktopRoomClient = client;
        _roomLocalDogHits = new[] { "HitButton", "ClawLeftHitButton", "ClawRightHitButton" }
            .Select(path => _bossDogVisual.GetNode<Button>(path)).ToArray();
    }

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
                _roomDesktop = GD.Load<PackedScene>("res://Scenes/Rooms/RoomDesktopPreview.tscn")
                    .Instantiate<RoomDesktopPreview>();
                _bossKeyContent.AddChild(_roomDesktop);
            }
            _roomDesktop.Show();
            ApplyRoomDesktopLayout(force: true);
        }
        else ApplyRoomDesktopLayout();
        if (_roomDragMember != 0 && (_hiddenByFullscreenApp || !RoomDragMemberIsCurrent()))
            CancelWindowDrag();
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
            new Rect2(usable.Position - host.Position, usable.Size), _desktopPetScaleFactor, _panelSize.X,
            RoomTaskbarAnchorOffsetY, RoomSnapEnabled);
        _roomDesktop.UpdateNameBars(
            new Rect2(_bossStatusPanelBasePosition - _bossDogVisual.Position, _bossStatusPanelBaseSize),
            _mainText.GetThemeFont("font"), _bossStatusPanel.GetThemeStylebox("panel"),
            RoomSnapEnabled && SettingsManager.LoadCenterCounterOnTaskbar(),
            Mathf.Max(0, screen.End.Y - usable.End.Y) / _desktopPetScaleFactor,
            _bossTaskBarAnchor.Position.Y - _bossDogVisual.Position.Y, BossCounterTongueClearance);
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

    private void RestoreRoomDesktopWindow()
    {
        if (!_roomWindowActive) return;
        CancelWindowDrag();
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
        && _roomDesktop.HasMember(_roomDragSession, _roomDragMember, _roomDragPresence);

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
            if (_settingsPanel.ContainsPoint(point)
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
            if (member == null || !_roomDesktop.HasMember(_desktopRoomClient.Session, memberId, member.Presence)) return;
            _roomDragMember = memberId;
            _roomDragSession = _desktopRoomClient.Session;
            _roomDragPresence = member.Presence;
            _roomDragMouseStart = point;
            _roomDragDogStart = _roomDesktop.GetPosition(memberId);
            _potentialDrag = true;
        }
        else if (@event is InputEventMouseMotion motion && _roomDragMember != 0)
        {
            if ((motion.ButtonMask & MouseButtonMask.Left) == 0 || !RoomDragMemberIsCurrent())
            {
                CancelWindowDrag();
                return;
            }
            var delta = motion.Position - _roomDragMouseStart;
            if (!_isDragging && delta.LengthSquared() < DefaultDragThreshold * DefaultDragThreshold) return;
            _isDragging = true;
            SetClickThrough(false);
            float snappedDogY = DisplayServer.ScreenGetUsableRect(_roomScreen).End.Y
                - DisplayServer.WindowGetPosition().Y - RoomTaskbarAnchorOffsetY;
            _roomDesktop.MoveMember(_roomDragMember, _roomDragDogStart + delta, snappedDogY,
                RoomSnapEnabled, SnapThreshold, BreakawayThreshold);
            // Reuse the local dog's counter, bubble and panel layout chain. The
            // room host stays still; remote moves do not move the local panel.
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
    public void RoomSnapForSmoke(bool enabled)
    {
        _roomSnapEnabledForSmoke = enabled;
        if (_roomWindowActive) ApplyRoomDesktopLayout(force: true);
    }
    public bool RoomHitTestForSmoke(Vector2I point) => IsScreenPointOverInteractiveContent(point);
    public void RoomModeForSmoke(bool poker) { if (poker) SwitchToPlay(); else SwitchToBossKey(); }
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
