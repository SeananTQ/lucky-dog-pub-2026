#if DEBUG && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;

namespace LuckyDogRise.Rooms;

// Stage A only: no Steam, GameData, settings or persistence. Immutable wire values
// are delivered into independent client replicas, never a shared UI backing list.
public sealed record RoomMember(int Id, string Name, int SkinId, int HeadwearId,
    int Reaction, long Presence);
public sealed record RoomListing(string Code, string Name, string GameId, int Count, int Capacity);
public sealed record RoomSnapshot(string Code, string Name, string GameId, int OwnerId,
    long Revision, RoomMember[] Members);
public sealed record RoomChat(long Id, string Code, int SenderId, long Presence,
    string Text, double ExpiresAt);

public sealed class RoomSandbox
{
    public const int Capacity = 6;
    public const int MaxChatCharacters = 120;
    public const double ChatLifetime = 6;
    public const double ChatCooldown = 1.5;
    private sealed class Room
    {
        public string Code = "";
        public string Name = "";
        public string GameId = "social";
        public int Owner;
        public long Revision;
        public readonly Dictionary<int, RoomMember> Members = new();
    }
    private sealed record Delivery(int ClientId, long Session, double Due,
        RoomSnapshot Snapshot, RoomChat Chat);
    private readonly Dictionary<string, Room> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, RoomClient> _clients = new();
    private readonly List<Delivery> _pending = new();
    private readonly Dictionary<int, double> _lastChat = new();
    private int _nextRoom;
    private long _nextPresence;
    private long _nextChat;
    public double Now { get; private set; }
    public int PendingCount => _pending.Count;

    public RoomClient AddClient(int id, string name, int skinId)
    {
        if (_clients.ContainsKey(id)) throw new ArgumentException("Duplicate client identity.");
        var client = new RoomClient(this, id, name, skinId);
        _clients.Add(id, client);
        return client;
    }

    public RoomListing[] Search() => _rooms.Values.OrderBy(r => r.Code)
        .Select(r => new RoomListing(r.Code, r.Name, r.GameId, r.Members.Count, Capacity)).ToArray();

    public string Create(RoomClient client, string name)
    {
        name = (name ?? "").Trim();
        if (name.Length is < 1 or > 40) return "房间名称需要 1–40 个字符。";
        Leave(client);
        var room = new Room { Code = $"MOCK{++_nextRoom:000}", Name = name, Owner = client.Id };
        _rooms.Add(room.Code, room);
        return Join(client, room.Code);
    }

    public string Join(RoomClient client, string code)
    {
        if (!_rooms.TryGetValue((code ?? "").Trim(), out var room)) return "房间不存在，请刷新列表。";
        if (client.JoinedCode == room.Code) return "";
        if (room.Members.Count >= Capacity) return "房间已满。";
        Leave(client);
        client.BeginSession(room.Code);
        room.Members.Add(client.Id, new RoomMember(client.Id, client.Name,
            client.SkinId, client.HeadwearId, client.Reaction, ++_nextPresence));
        Broadcast(room);
        return "";
    }

    public void Leave(RoomClient client)
    {
        var previous = client.JoinedCode;
        client.BeginSession("");
        _pending.RemoveAll(d => d.ClientId == client.Id);
        if (!_rooms.TryGetValue(previous, out var room)) return;
        room.Members.Remove(client.Id);
        if (room.Members.Count == 0) _rooms.Remove(room.Code);
        else
        {
            if (room.Owner == client.Id) room.Owner = room.Members.Keys.Min();
            Broadcast(room);
        }
    }

    internal void UpdateAppearance(RoomClient client)
    {
        if (!_rooms.TryGetValue(client.JoinedCode, out var room)
            || !room.Members.TryGetValue(client.Id, out var member)) return;
        room.Members[client.Id] = member with { SkinId = client.SkinId,
            HeadwearId = client.HeadwearId, Reaction = client.Reaction };
        Broadcast(room);
    }

    public string SetGame(RoomClient client, string gameId)
    {
        if (!_rooms.TryGetValue(client.JoinedCode, out var room)) return "请先进入房间。";
        if (room.Owner != client.Id) return "只有房主可以修改房间玩法。";
        if (string.IsNullOrWhiteSpace(gameId) || gameId.Length > 32) return "玩法标识无效。";
        room.GameId = gameId;
        Broadcast(room);
        return "";
    }

    public string SendChat(RoomClient client, string text)
    {
        if (!_rooms.TryGetValue(client.JoinedCode, out var room)
            || !room.Members.TryGetValue(client.Id, out var member)) return "请先进入房间。";
        if (string.IsNullOrWhiteSpace(text)) return "请输入文字。";
        if (text.Length > MaxChatCharacters) return $"最多 {MaxChatCharacters} 个字符。";
        if (text.Any(char.IsControl)) return "请发送单行文字。";
        if (_lastChat.TryGetValue(client.Id, out var last) && Now - last < ChatCooldown)
            return "发送太快，请稍后再试。";
        _lastChat[client.Id] = Now;
        var chat = new RoomChat(++_nextChat, room.Code, client.Id, member.Presence,
            text.Trim(), Now + ChatLifetime);
        foreach (var id in room.Members.Keys)
        {
            var recipient = _clients[id];
            if (recipient.Paused) continue;
            // One pending bubble per sender/recipient; cannot grow an unbounded queue.
            _pending.RemoveAll(d => d.ClientId == id && d.Chat?.SenderId == client.Id);
            _pending.Add(new Delivery(id, recipient.Session, Now + recipient.Latency,
                null, chat));
        }
        return "";
    }

    public void SetPaused(RoomClient client, bool paused)
    {
        client.Paused = paused;
        _pending.RemoveAll(d => d.ClientId == client.Id);
        if (!paused && _rooms.TryGetValue(client.JoinedCode, out var room))
            QueueSnapshot(client, room);
    }

    public void Tick(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        Now += delta;
        var due = _pending.Where(d => d.Due <= Now).OrderBy(d => d.Due).ToArray();
        _pending.RemoveAll(d => d.Due <= Now);
        foreach (var delivery in due)
        {
            var client = _clients[delivery.ClientId];
            if (client.Paused || client.Session != delivery.Session) continue;
            if (delivery.Snapshot != null) client.Receive(delivery.Snapshot);
            else if (delivery.Chat != null) client.Receive(delivery.Chat, Now);
        }
        foreach (var client in _clients.Values) client.ExpireBubbles(Now);
    }

    private void Broadcast(Room room)
    {
        room.Revision++;
        foreach (var id in room.Members.Keys) QueueSnapshot(_clients[id], room);
    }

    private void QueueSnapshot(RoomClient client, Room room)
    {
        if (client.Paused) return;
        // Keep the original due time when coalescing so continuous activity cannot
        // postpone delivery forever on a high-latency link.
        var old = _pending.FirstOrDefault(d => d.ClientId == client.Id && d.Snapshot != null);
        var due = old?.Due ?? Now + client.Latency;
        _pending.RemoveAll(d => d.ClientId == client.Id && d.Snapshot != null);
        var snapshot = new RoomSnapshot(room.Code, room.Name, room.GameId, room.Owner,
            room.Revision, room.Members.Values.OrderBy(m => m.Id).ToArray());
        _pending.Add(new Delivery(client.Id, client.Session, due, snapshot, null));
    }
}

public sealed class RoomClient
{
    private readonly RoomSandbox _server;
    private readonly Dictionary<int, RoomChat> _bubbles = new();
    private readonly Dictionary<int, long> _chatSequences = new();
    private readonly Dictionary<int, double> _receivedChatAt = new();
    private double _latency;
    public int Id { get; }
    public string Name { get; }
    public int SkinId { get; private set; }
    public int HeadwearId { get; private set; }
    public int Reaction { get; private set; } = 1001;
    public string JoinedCode { get; private set; } = "";
    public long Session { get; private set; }
    public RoomSnapshot View { get; private set; }
    public bool Paused { get; internal set; }
    public double Latency { get => _latency; set => _latency = double.IsFinite(value) ? Math.Clamp(value, 0, 10) : 0; }
    public float LocalScale { get; set; } = 1;
    public HashSet<int> HiddenMembers { get; } = new();
    public bool ShowNames { get; set; } = true;
    public IReadOnlyDictionary<int, RoomChat> Bubbles => _bubbles;
    public event Action Changed;

    internal RoomClient(RoomSandbox server, int id, string name, int skinId)
        => (_server, Id, Name, SkinId) = (server, id, name, skinId);

    public void SetAppearance(int skinId, int headwearId, int reaction)
    {
        SkinId = skinId;
        HeadwearId = headwearId;
        Reaction = reaction;
        _server.UpdateAppearance(this);
    }

    internal void BeginSession(string code)
    {
        Session++;
        JoinedCode = code;
        View = null;
        _bubbles.Clear();
        _chatSequences.Clear();
        _receivedChatAt.Clear();
        Changed?.Invoke();
    }

    internal void Receive(RoomSnapshot snapshot)
    {
        if (snapshot.Code != JoinedCode || snapshot.Revision <= (View?.Revision ?? -1)) return;
        View = snapshot with { Members = (RoomMember[])snapshot.Members.Clone() };
        foreach (var id in _bubbles.Keys.ToArray())
            if (!View.Members.Any(m => m.Id == id && m.Presence == _bubbles[id].Presence)) _bubbles.Remove(id);
        Changed?.Invoke();
    }

    internal void Receive(RoomChat chat, double now)
    {
        if (chat.Code != JoinedCode || chat.ExpiresAt <= now || string.IsNullOrWhiteSpace(chat.Text)
            || chat.Text.Length > RoomSandbox.MaxChatCharacters
            || chat.Text.Any(char.IsControl)
            || View == null || !View.Members.Any(m => m.Id == chat.SenderId && m.Presence == chat.Presence)
            || (_chatSequences.TryGetValue(chat.SenderId, out var last) && chat.Id <= last)
            || (_receivedChatAt.TryGetValue(chat.SenderId, out var receivedAt)
                && now - receivedAt < RoomSandbox.ChatCooldown)) return;
        _chatSequences[chat.SenderId] = chat.Id;
        _receivedChatAt[chat.SenderId] = now;
        _bubbles[chat.SenderId] = chat;
        Changed?.Invoke();
    }

    internal void ExpireBubbles(double now)
    {
        var expired = _bubbles.Where(p => p.Value.ExpiresAt <= now).Select(p => p.Key).ToArray();
        foreach (var id in expired) _bubbles.Remove(id);
        if (expired.Length > 0) Changed?.Invoke();
    }
}
#endif
