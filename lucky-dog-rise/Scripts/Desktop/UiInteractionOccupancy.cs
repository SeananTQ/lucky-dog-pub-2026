using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace LuckyDogRise;

/// <summary>
/// Shared, scene-tree-scoped occupancy for unsolicited UI. Sources own their
/// registrations; callers keep their own pending requests and business deadlines.
/// This does not acknowledge, hide or queue another feature's dialog.
/// </summary>
public static class UiInteractionOccupancy
{
    private static readonly ConditionalWeakTable<SceneTree, List<Registration>> Sources = new();

    /// <summary>Call from _EnterTree; leaving the tree automatically unregisters.</summary>
    public static IDisposable TrackVisibility(Node owner) => Track(owner, () => IsVisible(owner));

    /// <summary>
    /// For nonvisual work such as opening/loading a reward. The predicate must
    /// only read state, never change visibility or query occupancy recursively.
    /// </summary>
    public static IDisposable Track(Node owner, Func<bool> isOccupied)
    {
        ArgumentNullException.ThrowIfNull(isOccupied);
        if (!GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree())
            throw new ArgumentException("Occupancy owner must be inside the scene tree.", nameof(owner));
        var sources = Sources.GetValue(owner.GetTree(), _ => new List<Registration>());
        return new Registration(sources, owner, isOccupied);
    }

    public static bool IsOccupied(Node requester, Node exceptRoot = null)
    {
        if (!GodotObject.IsInstanceValid(requester) || !requester.IsInsideTree()
            || !Sources.TryGetValue(requester.GetTree(), out var sources)) return false;
        bool hasException = GodotObject.IsInstanceValid(exceptRoot);
        foreach (var source in sources)
        {
            var owner = source.Owner;
            if (!GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree() || owner.IsQueuedForDeletion()) continue;
            if (hasException && (owner == exceptRoot || exceptRoot.IsAncestorOf(owner))) continue;
            if (source.IsOccupied()) return true;
        }
        return false;
    }

    private static bool IsVisible(Node owner)
    {
        // CanvasLayer/SubViewport boundaries do not share ordinary Control
        // visibility inheritance. Also check the logical parents: hiding poker
        // must release its overlays while preserving their local requested state.
        for (Node node = owner; node != null; node = node.GetParent())
        {
            if (node.IsQueuedForDeletion()) return false;
            if (node is CanvasItem item && !item.Visible) return false;
            if (node is CanvasLayer layer && !layer.Visible) return false;
        }
        return true;
    }

    private sealed class Registration : IDisposable
    {
        private readonly List<Registration> _sources;
        public Node Owner { get; }
        public Func<bool> IsOccupied { get; }
        private bool _disposed;

        public Registration(List<Registration> sources, Node owner, Func<bool> isOccupied)
        {
            _sources = sources;
            Owner = owner;
            IsOccupied = isOccupied;
            sources.Add(this);
            owner.TreeExiting += Dispose;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sources.Remove(this);
            if (GodotObject.IsInstanceValid(Owner)) Owner.TreeExiting -= Dispose;
        }
    }
}
