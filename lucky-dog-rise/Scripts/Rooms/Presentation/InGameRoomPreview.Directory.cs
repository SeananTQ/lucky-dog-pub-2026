#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LuckyDogRise.Rooms;

public partial class InGameRoomPreview
{
    internal const double DirectoryRefreshIntervalSeconds = 15;
    private readonly Dictionary<string, InGameRoomDirectoryRow> _directoryRows = new();
    private RoomRequestState _observedDirectoryState;
    private double _nextDirectoryRefresh;
    private long _directorySession = -1;
    private long _directoryRenderVersion;
    private bool _directoryRenderPending;
    private bool _directoryPollingEnabled;
    private string _directoryPollingState = "";
#if DEBUG
    internal Func<bool> DirectoryInteractionForSmoke;
#endif

    private void ResetDirectoryRefresh()
    {
        _observedDirectoryState = RoomRequestState.Idle;
        _nextDirectoryRefresh = (_client?.Clock ?? 0) + DirectoryRefreshIntervalSeconds;
        _directorySession = -1;
        _directoryRenderVersion++;
        _directoryRenderPending = false;
        _directoryPollingEnabled = false;
        _directoryPollingState = "";
        if (GodotObject.IsInstanceValid(_rooms)) Clear(_rooms);
        _directoryRows.Clear();
    }

    private void ObserveDirectoryRequest()
    {
        if (_client == null || _observedDirectoryState == _client.DirectoryState) return;
        _observedDirectoryState = _client.DirectoryState;
        if (_observedDirectoryState == RoomRequestState.Pending) _directoryPollingEnabled = true;
        // Count from completion (including failure), not request start. Slow
        // responses and hidden time never accumulate a burst of catch-up calls.
        _nextDirectoryRefresh = _client.Clock + DirectoryRefreshIntervalSeconds;
    }

    private void UpdateDirectoryRefresh()
    {
        if (_client == null) return;
        ObserveDirectoryRequest();
        // Recovery owns the user's current intent. After it stops, wait for an
        // explicit directory request before polling again, even if it failed.
        if (Reconnect?.IsPending == true)
        { _directoryPollingEnabled = false; RecordDirectoryPollingState("reconnecting"); return; }
        string blocked = !IsVisibleInTree() || !_lobby.IsVisibleInTree() ? "hidden"
            : !_browsing && _client.JoinedCode.Length > 0 ? "room_details"
            : _waitingForRoomConnection ? "connection_unavailable"
            : _client.IsLocalArrival ? "companion_arrival"
            : !_directoryPollingEnabled ? "awaiting_initial_search" : "";
        if (blocked.Length > 0) { RecordDirectoryPollingState(blocked); return; }
        if (_directoryRenderPending && !DirectoryInteractionActive()) _dirty = true;
        // This only fetches a directory; it does not open unsolicited UI. An
        // unrelated welcome/reward occupancy must not stop background queries.
        // Row updates below protect existing click targets independently.
        blocked = _client.IsBusy || _client.IsBackgroundSearch ? "request_in_flight"
            : DisplayModePopup.Visible ? "display_menu"
            : _client.Clock < _nextDirectoryRefresh ? "interval" : "";
        if (blocked.Length > 0) { RecordDirectoryPollingState(blocked); return; }
        RecordDirectoryPollingState("refreshing");
        _nextDirectoryRefresh = _client.Clock + DirectoryRefreshIntervalSeconds;
        _client.SearchInBackground();
    }

    private void RecordDirectoryPollingState(string state)
    {
        if (_directoryPollingState == state) return;
        _directoryPollingState = state;
        // Native playtest reports need to distinguish a paused timer from a
        // failed Steam request. No names, room codes or chat text are recorded.
        if (!_isMock && _gameData != null)
            DiagnosticLog.Record("rooms_directory_polling", new Dictionary<string, object>
            {
                ["state"] = state,
                ["directory"] = _client.DirectoryState.ToString(),
                ["remaining"] = Math.Max(0, _nextDirectoryRefresh - _client.Clock),
            });
    }

    private bool DirectoryInteractionActive(bool structuralChange = true)
    {
#if DEBUG
        if (DirectoryInteractionForSmoke != null) return DirectoryInteractionForSmoke();
#endif
        if (Input.IsMouseButtonPressed(MouseButton.Left)) return true;
        if (!structuralChange) return false;
        if (!_rooms.IsVisibleInTree()) return false;
        // Defer structural edits under the pointer, including a click that has
        // not yet been pressed. Clipped rows outside the scroll area do not count.
        return new Rect2(Vector2.Zero, _rooms.Size).HasPoint(_rooms.GetLocalMousePosition())
            && (_pageScroll == null || new Rect2(Vector2.Zero, _pageScroll.Size)
                .HasPoint(_pageScroll.GetLocalMousePosition()));
    }

    private void RenderDirectory()
    {
        bool companionEntry = HasEmptySearchResult && Reconnect?.IsPending != true;
        bool changed = !ReferenceEquals(_lastListings, _client.Listings)
            || _lastCompanionEntry != companionEntry || _directorySession != _client.Session
            || _directoryRenderPending && !DirectoryInteractionActive();
        if (changed)
        {
            var wanted = _client.Listings.Select(room => room.Code).ToHashSet();
            if (companionEntry) wanted.Add("");
            bool structuralChange = wanted.Count != _directoryRows.Count
                || _directoryRows.Keys.Any(code => !wanted.Contains(code));
            // Keep obsolete rows disabled until the pointer/press leaves, but
            // append new rooms immediately. Previously hovering the companion
            // invitation could hide every newly found real room indefinitely.
            bool deferRemoval = _directoryRows.Keys.Any(code => !wanted.Contains(code))
                && DirectoryInteractionActive(structuralChange);
            _directoryRenderPending = deferRemoval;
            _lastListings = _client.Listings;
            _lastCompanionEntry = companionEntry;
            _directorySession = _client.Session;
            var listings = _client.Listings;
            int scroll = _pageScroll?.ScrollVertical ?? 0;
            var anchor = _pageScroll == null ? null : _rooms.GetChildren().OfType<InGameRoomDirectoryRow>()
                .FirstOrDefault(row => _directoryRows.Any(pair => pair.Value == row && wanted.Contains(pair.Key))
                    && row.GetGlobalRect().End.Y > _pageScroll.GetGlobalRect().Position.Y);
            float anchorY = anchor?.GetGlobalRect().Position.Y ?? 0;
            foreach (string code in _directoryRows.Keys.Where(code => !wanted.Contains(code) && !deferRemoval).ToArray())
            {
                var removed = _directoryRows[code];
                _directoryRows.Remove(code);
                _rooms.RemoveChild(removed);
                removed.QueueFree();
            }
            foreach (var room in listings)
            {
                var row = DirectoryRow(room.Code);
                var client = _client;
                long session = client.Session;
                row.Bind(room, client.IsBusy, () => JoinDirectoryEntry(row, client, session, room.Code));
            }
            if (companionEntry)
            {
                var row = DirectoryRow("");
                var client = _client;
                long session = client.Session;
                string name = L10n.Tr("Rooms_DefaultName");
                row.Bind(new RoomListing("", name, "social", RoomCompanionPlan.Count, RoomRules.Capacity),
                    false, () => JoinCompanionEntry(row, client, session, listings, name));
            }
            RestoreDirectoryScroll(scroll, anchor, anchorY);
        }
        foreach (var (code, row) in _directoryRows)
        {
            bool unavailable = code.Length == 0
                ? !companionEntry || _client.DirectoryState != RoomRequestState.Succeeded
                : !_client.Listings.Any(room => room.Code == code && !room.IsFull);
            row.SetBusy(_client.IsBusy || unavailable);
        }
    }

    private InGameRoomDirectoryRow DirectoryRow(string code)
    {
        if (_directoryRows.TryGetValue(code, out var row)) return row;
        row = _directoryRow.Instantiate<InGameRoomDirectoryRow>();
        _rooms.AddChild(row);
        _directoryRows.Add(code, row);
        // Keep surviving rows in their previous order; append newly found rooms.
        // Steam result order is not a reason to move a join button under a cursor.
        return row;
    }

    private void JoinDirectoryEntry(InGameRoomDirectoryRow row, RoomClient client, long session, string code)
    {
        if (!ReferenceEquals(client, _client) || client.Session != session || client.IsBusy
            || !_lobby.IsVisibleInTree() || !GodotObject.IsInstanceValid(row) || row.GetParent() != _rooms
            || !client.Listings.Any(room => room.Code == code && !room.IsFull)) return;
        _noticeKey = "";
        client.Join(code);
    }

    private void RestoreDirectoryScroll(int scroll, InGameRoomDirectoryRow anchor, float anchorY)
    {
        long version = ++_directoryRenderVersion;
        if (_pageScroll == null) return;
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || version != _directoryRenderVersion
                || !GodotObject.IsInstanceValid(_pageScroll) || Input.IsMouseButtonPressed(MouseButton.Left)) return;
            float offset = GodotObject.IsInstanceValid(anchor) && anchor.GetParent() == _rooms
                ? (anchor.GetGlobalRect().Position.Y - anchorY) / Math.Max(0.001f, _pageScroll.GetGlobalTransform().Scale.Y)
                : 0;
            _pageScroll.ScrollVertical = Math.Max(0, scroll + Mathf.RoundToInt(offset));
        }).CallDeferred();
    }
}
#endif
