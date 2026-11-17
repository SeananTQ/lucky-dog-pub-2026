#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Godot;
using LuckyDogRise.Rooms.Poker;

namespace LuckyDogRise.Rooms;

// Remote players and companion presentation. The existing desktop dog retains
// its equipment, activity, counter and rewards. Copies have no inventory/input.
public partial class RoomDesktopPreview : Node2D
{
    [Export] private PackedScene _dogScene = null!;
    [Export] private CanvasLayer _remoteLayer = null!;
    [Export] private CanvasLayer _chatLayer = null!;
    [Export] public RoomChatView LocalChat { get; private set; } = null!;
    [Export] public RoomBoundaryView Boundary { get; private set; } = null!;
    [Export] public RoomPokerHandView LocalPokerHand { get; private set; } = null!;
    private RoomClient _client;

    public override void _Ready()
    {
        LocalChat.InteractionAllowed = () => _client?.ChatAllowed == true;
        LocalChat.ManualSendDelay = () => _client?.AutomaticChatDelay ?? 0;
        LocalChat.SendSession = () => _client?.Session ?? -1;
        LocalChat.SendRequested += text =>
        {
            if (LocalChat.RefreshInteractionAvailability())
                LocalChat.SetSendResult(_client?.SendChat(text) ?? "Rooms_ChatUnavailable");
        };
    }
    private readonly Dictionary<int, RoomDogView> _dogs = new();
    private readonly List<RoomDogView> _drawOrder = new();
    private readonly List<RoomDogView> _departing = new();
    // Desktop/poker use separate views of one membership. Weak ownership prevents
    // a disposed client from being kept alive, and mode switches never replay entry.
    private sealed class SeenMembers
    {
        public long Session = -1;
        public readonly HashSet<(int Id, long Presence)> Members = new();
        public Guid PokerTable;
        // At most the current hand's six appearances; shared across desktop/poker views.
        public readonly Dictionary<int, RoomMember> PokerMembers = new();
    }
    private static readonly ConditionalWeakTable<RoomClient, SeenMembers> Seen = new();
    private sealed class Placement(long presence, int slot, Vector2 position, bool taskbarSnapped)
    {
        public long Presence = presence;
        public readonly int Slot = slot;
        public Vector2 Position = position;
        public bool TaskbarSnapped = taskbarSnapped;
    }
    private readonly Dictionary<int, Placement> _placements = new();
    private long _session = -1;
    private RoomArrivalState _awaitingArrivalFrame;
    public IReadOnlyCollection<RoomDogView> RemoteDogs => _dogs.Values;
    private readonly Dictionary<int, int> _pokerDisplayIds = new();
    private readonly HashSet<int> _retainedPokerMembers = new();

    private RoomMember[] PresentationMembers(RoomClient client, PokerNetworkSession poker)
    {
        var members = client.View?.Members ?? Array.Empty<RoomMember>();
        var seen = Seen.GetOrCreateValue(client);
        if (seen.Session != client.LayoutSession)
        {
            seen.Members.Clear(); seen.PokerMembers.Clear(); seen.Session = client.LayoutSession;
        }
        _pokerDisplayIds.Clear(); _retainedPokerMembers.Clear();
        if (client.View?.GameId != RoomRules.MischiefPoker || poker?.Context is not { } context
            || poker.State is not { Status: PokerTableStatus.Playing, Hand: { } hand } state)
        { seen.PokerMembers.Clear(); return members; }
        if (seen.PokerTable != state.Table)
        { seen.PokerMembers.Clear(); seen.PokerTable = state.Table; }
        foreach (int id in seen.PokerMembers.Keys.Where(id => !hand.Public.ActionOrder.Contains(id)).ToArray())
            seen.PokerMembers.Remove(id);
        foreach (var member in members)
        {
            var peer = context.Members.FirstOrDefault(p => p.LocalId == member.Id);
            var player = state.Players.FirstOrDefault(p => p.Key == peer?.Key && p.Ticket == peer.Ticket);
            if (player != null && hand.Public.ActionOrder.Contains(player.Id)) seen.PokerMembers[player.Id] = member;
        }
        bool retain = hand.Public.Phase != PokerPhase.Settled
            || state.SettlementElapsed < PokerNetworkSession.PayoutDuration(hand.Public);
        var visible = members.ToList();
        foreach (var (id, member) in seen.PokerMembers)
        {
            bool present = members.Any(m => m.Id == member.Id && m.Presence == member.Presence);
            if (!present)
            {
                // Do not reserve Steam capacity or mistake a reused local ID for the departed player.
                if (!retain || member.Id == client.Id || members.Any(m => m.Id == member.Id)) continue;
                visible.Add(member);
                _retainedPokerMembers.Add(member.Id);
            }
            _pokerDisplayIds[member.Id] = PokerNetworkProtocol.DisplayId(state, context, id);
        }
        return visible.ToArray();
    }

    public override void _Notification(int what)
    {
        // CanvasLayer does not inherit a Node2D ancestor's visibility.
        if (what == NotificationVisibilityChanged && IsInstanceValid(_remoteLayer))
        {
            _remoteLayer.Visible = IsVisibleInTree();
            UpdateRemoteCanvases();
        }
        if (what == NotificationVisibilityChanged && IsInstanceValid(_chatLayer))
        {
            _chatLayer.Visible = IsVisibleInTree();
            if (!IsVisibleInTree() && LocalChat.IsNodeReady()) { LocalChat.Reset(); Boundary.Clear(); }
        }
    }

    public Vector2 Present(RoomClient client, Rect2 usable, float scale, float spacing,
        float taskbarAnchorOffsetY, bool snapEnabled, bool showLocalChat = true, PokerNetworkSession poker = null)
    {
        if (_session != client.LayoutSession)
        {
            Clear();
            _session = client.LayoutSession;
        }
        _remoteLayer.Visible = IsVisibleInTree();
        _chatLayer.Visible = IsVisibleInTree();
        _client = client;
        // Promotion can invalidate the protocol snapshot before its replacement
        // arrives (notably under simulated receive latency). This is still the
        // same visible room; keep all seats until the new membership is known.
        if (client.View == null && _placements.TryGetValue(client.Id, out var previousSelf))
        {
            UpdateRemoteCanvases();
            return previousSelf.Position;
        }
        var members = PresentationMembers(client, poker);
        var seen = Seen.GetOrCreateValue(client);
        seen.Members.RemoveWhere(key => !members.Any(m => m.Id == key.Id && m.Presence == key.Presence));
        var self = members.FirstOrDefault(member => member.Id == client.Id);
        if (self != null && _placements.TryGetValue(client.Id, out var selfPlacement))
            selfPlacement.Presence = self.Presence;
        foreach (var id in _placements.Keys.Where(id => !members.Any(member =>
                     member.Id == id && member.Presence == _placements[id].Presence)).ToArray())
            _placements.Remove(id);
        foreach (var id in _dogs.Keys.Where(id => !members.Any(member => member.Id == id && member.Presence == _dogs[id].Presence)).ToArray())
        {
            var dog = _dogs[id];
            if (IsVisibleInTree() && dog.Visible && dog.BeginExit()) _departing.Add(dog);
            else RemoveRemoteCanvas(dog);
            _dogs.Remove(id);
        }
        // Rapid join/leave churn cannot accumulate unbounded visual shells.
        while (_departing.Count > RoomRules.Capacity) RemoveRemoteCanvas(_departing[0]);
        float snappedDogY = usable.End.Y - taskbarAnchorOffsetY;
        var center = new Vector2(usable.GetCenter().X, snapEnabled ? snappedDogY : usable.End.Y - 75 * scale);
        var localPosition = center;
        foreach (var member in members)
        {
            if (!_placements.TryGetValue(member.Id, out var placement))
            {
                // Stable seats: membership/appearance updates never reorder the
                // remaining dogs. Freed companion seats can be reused by real
                // arrivals without moving the others. Zero belongs to the observer.
                int slot = member.Id == client.Id ? 0 : 1;
                while (slot > 0 && _placements.Values.Any(p => p.Slot == slot)) slot++;
                int side = (slot + 1) / 2 * (slot % 2 == 1 ? 1 : -1);
                var initialPosition = new Vector2(
                    Mathf.Clamp(center.X + side * spacing, usable.Position.X, usable.End.X - 1), center.Y);
                placement = new Placement(member.Presence, slot, initialPosition, snapEnabled);
                _placements.Add(member.Id, placement);
            }
            if (!snapEnabled) placement.TaskbarSnapped = false;
            // Each dog owns its attachment. Work-area/scale changes only move
            // attached dogs vertically; free positions stay where they were put.
            if (placement.TaskbarSnapped)
                placement.Position = new Vector2(placement.Position.X, snappedDogY);
            if (member.Id == client.Id) { localPosition = placement.Position; continue; }
            if (!_dogs.TryGetValue(member.Id, out var dog))
            {
                var canvas = _dogScene.Instantiate<CanvasLayer>();
                canvas.Name = $"RemoteDog{member.Id}";
                _remoteLayer.AddChild(canvas);
                // Set visibility before measuring the original pose for entry.
                // The source is hidden again before the first rendered frame.
                canvas.Visible = IsVisibleInTree();
                dog = canvas.GetNode<RoomDogView>("Dog");
                dog.MakePassivePreview();
                _dogs.Add(member.Id, dog);
                _drawOrder.Add(dog);
            }
            dog.Scale = Vector2.One * scale;
            dog.Position = placement.Position;
            dog.Visible = !client.HiddenMembers.Contains(member.Id);
            dog.PokerPlayerId = _pokerDisplayIds.GetValueOrDefault(member.Id, member.Id);
            dog.RetainedForPoker = _retainedPokerMembers.Contains(member.Id);
            dog.Display(member, false, member.Id == client.View.OwnerId, client.ShowNames,
                !dog.RetainedForPoker && client.Bubbles.TryGetValue(member.Id, out var bubble) ? bubble.Text : "",
                !dog.RetainedForPoker && client.IsTongueActive(member.Id));
            if (client.Arrival is { Phase: RoomArrivalPhase.Departing } arrival && member.Id == arrival.HostMemberId)
                dog.SetArrivalDepartureProgress((float)arrival.DepartureProgress);
            else if (IsVisibleInTree() && dog.Visible && seen.Members.Add((member.Id, member.Presence))) dog.BeginEntry();
        }
        UpdateRemoteCanvases();
        // Same anchor as RoomDogView, aligned to the original countdown balloon.
        // ModeManager temporarily retracts that balloon while local chat is active.
        LocalChat.Position = localPosition + new Vector2(-49, -140) * scale;
        LocalChat.Scale = Vector2.One * scale;
        PositionLocalPokerHand(localPosition + new Vector2(100, -150) * scale, scale, usable);
        LocalChat.Visible = showLocalChat;
        if (showLocalChat)
            LocalChat.Present(true, false, client.Bubbles.TryGetValue(client.Id, out var localBubble) ? localBubble.Text : "");
        if (client.Arrival is { Presented: false } pending && IsVisibleInTree()
            && DisplayServer.GetName() != "headless" && !ReferenceEquals(_awaitingArrivalFrame, pending))
        {
            _awaitingArrivalFrame = pending;
            ObserveArrivalAfterDraw(client, pending);
        }
        return localPosition;
    }

    // Both host sizes share free screen-space positions with the desktop renderer.
    public void PresentPoker(RoomClient client, Rect2 usable, float scale,
        RoomDesktopPreview desktopSeed, Vector2 desktopSeedOffset, Rect2 table, bool snapEnabled, float taskbarAnchorOffsetY,
        bool foreground, PokerNetworkSession poker = null)
    {
        // Poker content is on canvas 0, InfoPanel on 1, and the system panel on
        // 100. Poker's modal overlays live inside its SubViewport, so yield to
        // them while occupied without changing the desktop instance's layer.
        int layer = foreground ? 2 : -1;
        if (_remoteLayer.Layer != layer) _remoteLayer.Layer = layer;
        var fresh = PresentationMembers(client, poker).Where(m => !HasMember(client.LayoutSession, m.Id, m.Presence)).ToArray();
        Present(client, usable, scale, 220 * scale, taskbarAnchorOffsetY, snapEnabled, poker: poker);
        PositionLocalPokerHand(table.Position + table.Size * new Vector2(.72f, .48f), scale, usable);
        foreach (var member in fresh)
        {
            if (member.Id == client.Id || !_placements.TryGetValue(member.Id, out var placement)) continue;
            if (desktopSeed?.HasMember(client.LayoutSession, member.Id, member.Presence) == true)
            {
                placement.Position = desktopSeed.GetPosition(member.Id) + desktopSeedOffset;
                placement.TaskbarSnapped = snapEnabled && desktopSeed.IsTaskbarSnapped(member.Id);
                if (placement.TaskbarSnapped)
                    placement.Position = new Vector2(placement.Position.X, usable.End.Y - taskbarAnchorOffsetY);
                // Do not move a saved dog just because the poker table overlaps it.
                continue;
            }
            // Seed dogs outside the table; subsequent free drags belong to the player.
            if (new Rect2(placement.Position - new Vector2(100, 150) * scale,
                    new Vector2(200, 220) * scale).Intersects(table))
                placement.Position = new Vector2(placement.Position.X, usable.End.Y - taskbarAnchorOffsetY);
            placement.Position = new Vector2(
                Mathf.Clamp(placement.Position.X, usable.Position.X + 100 * scale,
                    Mathf.Max(usable.Position.X + 100 * scale, usable.End.X - 100 * scale)),
                Mathf.Clamp(placement.Position.Y, usable.Position.Y + 150 * scale,
                    Mathf.Max(usable.Position.Y + 150 * scale, usable.End.Y - taskbarAnchorOffsetY)));
        }
        foreach (var dog in _dogs.Values) dog.Position = _placements[dog.MemberId].Position;
    }

    private async void ObserveArrivalAfterDraw(RoomClient client, RoomArrivalState arrival)
    {
        await GetTree().ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (!IsInstanceValid(this) || !IsInsideTree()) return;
        _awaitingArrivalFrame = null;
        if (IsVisibleInTree() && ReferenceEquals(_client, client) && ReferenceEquals(client.Arrival, arrival))
            client.MarkArrivalPresented();
    }

    public int HitTest(Vector2 windowPoint)
    {
        // Match the canvas order; dictionary slots can be reused after departures.
        for (int i = _drawOrder.Count - 1; i >= 0; i--)
            if (_drawOrder[i].ContainsDogPoint(windowPoint)) return _drawOrder[i].MemberId;
        return 0;
    }

    private void UpdateRemoteCanvases()
    {
        // ZIndex (including absolute claw/hat offsets) is scoped to a canvas.
        // Give each avatar its own canvas, without render textures or a CanvasGroup
        // back-buffer. All desktop canvases stay below the local dog (canvas 0);
        // poker canvases start above InfoPanel (1) and remain below system UI.
        int layer = _remoteLayer.Layer < 0 ? _remoteLayer.Layer - _drawOrder.Count + 1 : _remoteLayer.Layer;
        foreach (var dog in _drawOrder)
        {
            var canvas = dog.GetParent<CanvasLayer>();
            if (canvas.Layer != layer) canvas.Layer = layer;
            bool visible = IsVisibleInTree();
            if (canvas.Visible != visible) canvas.Visible = visible;
            layer++;
        }
        if (IsInstanceValid(_chatLayer)) _chatLayer.Layer = Math.Max(3, layer);
    }

    private void RemoveRemoteCanvas(RoomDogView dog)
    {
        _departing.Remove(dog);
        _drawOrder.Remove(dog);
        var canvas = dog.GetParent<CanvasLayer>();
        canvas.Hide();
        canvas.QueueFree();
    }

    public override void _Process(double delta)
    {
        for (int i = _departing.Count - 1; i >= 0; i--)
            if (_departing[i].DepartureFinished || !IsVisibleInTree()) RemoveRemoteCanvas(_departing[i]);
    }

    public void UpdateNameBars(Rect2 idleRect, Font font, StyleBox style, bool centerOnTaskbar,
        float taskbarHeight, float taskbarAnchorY, float tongueClearance)
    {
        foreach (var (id, dog) in _dogs)
            dog.ApplyNameBarLayout(idleRect, font, style, centerOnTaskbar && IsTaskbarSnapped(id),
                taskbarHeight, taskbarAnchorY, tongueClearance);
    }

    public Vector2 GetPosition(int memberId) => _placements[memberId].Position;
    internal Rect2 GetFootprint(int memberId) => _dogs[memberId].Footprint;

    internal Vector2 ConstrainMember(int memberId, Rect2 footprint, Rect2 bounds)
    {
        var placement = _placements[memberId];
        var position = RoomDesktopGeometry.Clamp(placement.Position, footprint, bounds);
        // Attachment owns vertical placement. Counter/tongue clearance can extend
        // below the work area (especially at high pet scales); a window clamp must
        // not silently undo the existing taskbar rule or its breakaway hysteresis.
        if (placement.TaskbarSnapped) position.Y = placement.Position.Y;
        placement.Position = position;
        if (_dogs.TryGetValue(memberId, out var dog)) dog.Position = position;
        if (memberId == _client.Id) LocalChat.Position = position + new Vector2(-49, -140) * LocalChat.Scale;
        return position;
    }

    internal void ConstrainRemotes(Rect2 bounds)
    {
        foreach (var dog in _dogs.Values)
            if (dog.Visible) ConstrainMember(dog.MemberId, dog.Footprint, bounds);
    }
    public bool HasMember(long session, int memberId, long presence) => _session == session
        && _placements.TryGetValue(memberId, out var placement) && placement.Presence == presence;
    public bool IsTaskbarSnapped(int memberId) =>
        _placements.TryGetValue(memberId, out var placement) && placement.TaskbarSnapped;

    internal readonly record struct RemotePlacement(int Id, long Presence, Vector2 ScreenPosition, bool TaskbarSnapped);
    internal sealed record RemotePlacementSnapshot(long Session, RemotePlacement[] Members);

    internal RemotePlacementSnapshot CaptureRemotePlacements(Vector2 windowPosition) => new(_session,
        _placements.Where(pair => pair.Key != _client?.Id).Select(pair => new RemotePlacement(
            pair.Key, pair.Value.Presence, pair.Value.Position + windowPosition, pair.Value.TaskbarSnapped)).ToArray());

    internal void RestoreRemotePlacements(RemotePlacementSnapshot snapshot, Vector2 windowPosition,
        bool snapEnabled, float snappedDogY)
    {
        foreach (var saved in snapshot.Members)
        {
            if (saved.Id == _client?.Id || !HasMember(snapshot.Session, saved.Id, saved.Presence)) continue;
            var placement = _placements[saved.Id];
            placement.Position = saved.ScreenPosition - windowPosition;
            placement.TaskbarSnapped = snapEnabled && saved.TaskbarSnapped;
            if (placement.TaskbarSnapped) placement.Position = new Vector2(placement.Position.X, snappedDogY);
            if (_dogs.TryGetValue(saved.Id, out var dog)) dog.Position = placement.Position;
        }
    }

    public void MoveMember(int memberId, Vector2 position, float snappedDogY,
        bool snapEnabled, float snapThreshold, float breakawayThreshold)
    {
        var placement = _placements[memberId];
        if (!snapEnabled) placement.TaskbarSnapped = false;
        else if (placement.TaskbarSnapped)
        {
            if (position.Y < snappedDogY - breakawayThreshold) placement.TaskbarSnapped = false;
            else position.Y = snappedDogY;
        }
        else if (Mathf.Abs(position.Y - snappedDogY) < snapThreshold)
        {
            placement.TaskbarSnapped = true;
            position.Y = snappedDogY;
        }
        placement.Position = position;
    }

    public void Clear()
    {
        LocalPokerHand?.Hide();
        Boundary?.Clear();
        LocalChat?.Reset();
        _client = null;
        foreach (var dog in _drawOrder.ToArray()) RemoveRemoteCanvas(dog);
        _dogs.Clear();
        _placements.Clear();
        _pokerDisplayIds.Clear(); _retainedPokerMembers.Clear();
        _awaitingArrivalFrame = null;
        _session = -1;
    }

    private void PositionLocalPokerHand(Vector2 position, float scale, Rect2 bounds)
    {
        LocalPokerHand.Scale = Vector2.One * scale;
        LocalPokerHand.Position = RoomDesktopGeometry.Clamp(position,
            new Rect2(Vector2.Zero, LocalPokerHand.Size * scale), bounds);
    }
    public bool IsLocalPokerHandHit(Vector2 point) => IsVisibleInTree() && LocalPokerHand.IsVisibleInTree()
        && LocalPokerHand.GetGlobalRect().HasPoint(point);
}
#endif
