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
    private readonly Dictionary<int, RoomDogView> _dogs = new();
    public IReadOnlyCollection<RoomDogView> RemoteDogs => _dogs.Values;
    public Vector2 Present(RoomClient client, Rect2 usable, float scale)
    {
        var members = client.View?.Members ?? Array.Empty<RoomMember>();
        foreach (var id in _dogs.Keys.Where(id => !members.Any(member => member.Id == id)).ToArray())
        {
            _dogs[id].QueueFree();
            _dogs.Remove(id);
        }
        // Fixed member order for stage A; no observer-dependent center seat yet.
        var columns = Math.Min(3, Math.Max(1, members.Length));
        var rows = Math.Max(1, (members.Length + columns - 1) / columns);
        var bottom = usable.End.Y - 75 * scale;
        var rowSpacing = Math.Min(240 * scale,
            Math.Max(0, usable.Size.Y - 230 * scale) / Math.Max(1, rows - 1));
        Vector2 PositionFor(int i) => new(usable.Position.X + usable.Size.X / columns * (i % columns + 0.5f),
            bottom - (rows - 1 - i / columns) * rowSpacing);
        var localPosition = PositionFor(0);
        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            if (member.Id == client.Id) { localPosition = PositionFor(i); continue; }
            if (!_dogs.TryGetValue(member.Id, out var dog))
            {
                dog = _dogScene.Instantiate<RoomDogView>();
                dog.Name = $"RemoteDog{member.Id}";
                AddChild(dog);
                dog.MakePassivePreview();
                _dogs.Add(member.Id, dog);
            }
            dog.Scale = Vector2.One * scale;
            dog.Position = PositionFor(i);
            dog.Visible = !client.HiddenMembers.Contains(member.Id);
            dog.Display(member, false, member.Id == client.View.OwnerId, client.ShowNames,
                client.Bubbles.TryGetValue(member.Id, out var bubble) ? bubble.Text : "");
        }
        return localPosition;
    }
    public void Clear()
    {
        foreach (var dog in _dogs.Values) dog.QueueFree();
        _dogs.Clear();
    }
}
#endif
