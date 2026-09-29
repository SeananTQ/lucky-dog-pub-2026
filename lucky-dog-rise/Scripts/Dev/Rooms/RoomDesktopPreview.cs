#if DEBUG && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LuckyDogRise.Rooms;

// Only remote presentation. The existing desktop dog retains its equipment,
// activity, counter and rewards. These copies have no GameData or input tracker.
public partial class RoomDesktopPreview : Node2D
{
    [Export] private PackedScene _dogScene = null!;
    [Export] private CanvasLayer _remoteLayer = null!;
    private readonly Dictionary<int, RoomDogView> _dogs = new();
    private sealed class Placement(long presence, int slot, Vector2 position, bool taskbarSnapped)
    {
        public readonly long Presence = presence;
        public readonly int Slot = slot;
        public Vector2 Position = position;
        public bool TaskbarSnapped = taskbarSnapped;
    }
    private readonly Dictionary<int, Placement> _placements = new();
    private long _session = -1;
    public IReadOnlyCollection<RoomDogView> RemoteDogs => _dogs.Values;

    public override void _Notification(int what)
    {
        // CanvasLayer does not inherit a Node2D ancestor's visibility.
        if (what == NotificationVisibilityChanged && IsInstanceValid(_remoteLayer))
            _remoteLayer.Visible = IsVisibleInTree();
    }

    public Vector2 Present(RoomClient client, Rect2 usable, float scale, float spacing,
        float taskbarAnchorOffsetY, bool snapEnabled)
    {
        if (_session != client.Session)
        {
            Clear();
            _session = client.Session;
        }
        _remoteLayer.Visible = IsVisibleInTree();
        var members = client.View?.Members ?? Array.Empty<RoomMember>();
        foreach (var id in _placements.Keys.Where(id => !members.Any(member =>
                     member.Id == id && member.Presence == _placements[id].Presence)).ToArray())
            _placements.Remove(id);
        foreach (var id in _dogs.Keys.Where(id => !members.Any(member => member.Id == id)).ToArray())
        {
            _dogs[id].QueueFree();
            _dogs.Remove(id);
        }
        float snappedDogY = usable.End.Y - taskbarAnchorOffsetY;
        var center = new Vector2(usable.GetCenter().X, snapEnabled ? snappedDogY : usable.End.Y - 75 * scale);
        var localPosition = center;
        foreach (var member in members)
        {
            if (!_placements.TryGetValue(member.Id, out var placement))
            {
                // Stable seats: membership/appearance updates never reorder the
                // remaining dogs. Seat zero belongs to the observer, not the host.
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
                dog = _dogScene.Instantiate<RoomDogView>();
                dog.Name = $"RemoteDog{member.Id}";
                _remoteLayer.AddChild(dog);
                dog.MakePassivePreview();
                _dogs.Add(member.Id, dog);
            }
            dog.Scale = Vector2.One * scale;
            dog.Position = placement.Position;
            dog.Visible = !client.HiddenMembers.Contains(member.Id);
            dog.Display(member, false, member.Id == client.View.OwnerId, client.ShowNames,
                client.Bubbles.TryGetValue(member.Id, out var bubble) ? bubble.Text : "");
        }
        return localPosition;
    }

    public int HitTest(Vector2 windowPoint)
    {
        foreach (var dog in _dogs.Values.Reverse())
            if (dog.ContainsDogPoint(windowPoint)) return dog.MemberId;
        return 0;
    }

    public void UpdateNameBars(Rect2 idleRect, Font font, StyleBox style, bool centerOnTaskbar,
        float taskbarHeight, float taskbarAnchorY, float tongueClearance)
    {
        foreach (var (id, dog) in _dogs)
            dog.ApplyNameBarLayout(idleRect, font, style, centerOnTaskbar && IsTaskbarSnapped(id),
                taskbarHeight, taskbarAnchorY, tongueClearance);
    }

    public Vector2 GetPosition(int memberId) => _placements[memberId].Position;
    public bool HasMember(long session, int memberId, long presence) => _session == session
        && _placements.TryGetValue(memberId, out var placement) && placement.Presence == presence;
    public bool IsTaskbarSnapped(int memberId) =>
        _placements.TryGetValue(memberId, out var placement) && placement.TaskbarSnapped;

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
        foreach (var dog in _dogs.Values) { dog.Hide(); dog.QueueFree(); }
        _dogs.Clear();
        _placements.Clear();
        _session = -1;
    }
}
#endif
