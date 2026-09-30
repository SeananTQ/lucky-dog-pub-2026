#if DEBUG && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;

namespace LuckyDogRise.Rooms;

public enum MockRoomFailure { None, Unavailable, NoResponse }

// Transport faults belong only to the simulator, never the shared client contract.
public sealed class MockRoomSettings
{
    private double _latency;
    private double _requestDelay;
    public double Latency { get => _latency; set => _latency = double.IsFinite(value) ? Math.Clamp(value, 0, 10) : 0; }
    public bool Paused { get; internal set; }
    public bool SendingPaused { get; internal set; }
    public double RequestDelay { get => _requestDelay; set => _requestDelay = double.IsFinite(value) ? Math.Clamp(value, 0, 30) : 0; }
    public MockRoomFailure NextFailure { get; set; }
}

// Stage A only: no Steam, GameData or persistence. The same client contract can
// later be supplied by a Steam adapter without exposing these simulator controls.
public sealed class RoomSandbox : IRoomService
{
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
    private sealed record PendingRequest(RoomClient Client, RoomRequest Request,
        double Due, MockRoomFailure Failure);
    private readonly Dictionary<string, Room> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, RoomClient> _clients = new();
    private readonly Dictionary<int, MockRoomSettings> _settings = new();
    private readonly List<Delivery> _pending = new();
    private readonly List<PendingRequest> _requests = new();
    private readonly Dictionary<int, double> _lastChat = new();
    private int _nextRoom;
    private long _nextPresence;
    private long _nextChat;
    public double Now { get; private set; }
    public int PendingCount => _pending.Count;
    public int PendingRequestCount => _requests.Count;

    public RoomClient AddClient(int id, string name, int skinId)
    {
        if (_clients.ContainsKey(id)) throw new ArgumentException("Duplicate client identity.");
        var client = new RoomClient(this, id, name, skinId);
        _clients.Add(id, client);
        _settings.Add(id, new MockRoomSettings());
        client.AdvanceTo(Now);
        return client;
    }

    public MockRoomSettings Settings(RoomClient client)
    {
        if (!_clients.TryGetValue(client.Id, out var actual) || !ReferenceEquals(actual, client))
            throw new ArgumentException("Client does not belong to this room simulator.");
        return _settings[client.Id];
    }

    public void Request(RoomClient client, RoomRequest request)
    {
        var settings = Settings(client);
        var failure = settings.NextFailure;
        settings.NextFailure = MockRoomFailure.None;
        _requests.Add(new PendingRequest(client, request,
            failure == MockRoomFailure.NoResponse ? double.PositiveInfinity : Now + settings.RequestDelay,
            failure));
    }

    public void Cancel(RoomClient client, long requestId)
        => _requests.RemoveAll(r => ReferenceEquals(r.Client, client) && r.Request.Id == requestId);

    private RoomResult Complete(PendingRequest pending)
    {
        if (pending.Failure == MockRoomFailure.Unavailable) return new RoomResult(RoomFailure.Unavailable);
        return pending.Request.Operation switch
        {
            RoomOperation.Search => new RoomResult(RoomFailure.None, Search()),
            RoomOperation.Create => CreateResult(pending.Client, pending.Request.Value),
            RoomOperation.Join => JoinResult(pending.Client, pending.Request.Value),
            _ => new RoomResult(RoomFailure.Unavailable)
        };
    }

    public RoomListing[] Search() => _rooms.Values.OrderBy(r => r.Code)
        .Select(r => new RoomListing(r.Code, r.Name, r.GameId, r.Members.Count, RoomRules.Capacity)).ToArray();

    // Synchronous helpers only seed tests. Product-facing UI uses RoomClient requests.
    public string Create(RoomClient client, string name) => FailureText(CreateResult(client, name).Failure);
    private RoomResult CreateResult(RoomClient client, string name)
    {
        name = (name ?? "").Trim();
        if (name.Length is < 1 or > 40) return new RoomResult(RoomFailure.InvalidName);
        Leave(client);
        var room = new Room { Code = $"MOCK{++_nextRoom:000}", Name = name, Owner = client.Id };
        _rooms.Add(room.Code, room);
        return JoinResult(client, room.Code);
    }

    public string Join(RoomClient client, string code) => FailureText(JoinResult(client, code).Failure);
    private RoomResult JoinResult(RoomClient client, string code)
    {
        if (!_rooms.TryGetValue((code ?? "").Trim(), out var room)) return new RoomResult(RoomFailure.NotFound);
        if (client.JoinedCode == room.Code) return RoomResult.Success;
        if (room.Members.Count >= RoomRules.Capacity) return new RoomResult(RoomFailure.Full);
        Leave(client);
        client.BeginSession(room.Code);
        room.Members.Add(client.Id, new RoomMember(client.Id, client.Name,
            client.SkinId, client.HeadwearId, client.Reaction, ++_nextPresence, client.ActivitySequence));
        Broadcast(room);
        return RoomResult.Success;
    }

    private static string FailureText(RoomFailure failure) => failure switch
    {
        RoomFailure.None => "",
        RoomFailure.InvalidName => "房间名称需要 1–40 个字符。",
        RoomFailure.NotFound => "房间不存在，请刷新列表。",
        RoomFailure.Full => "房间已满。",
        _ => "房间服务暂不可用。"
    };

    public void CloseRoom(string code)
    {
        if (!_rooms.Remove(code, out var room)) return;
        foreach (var id in room.Members.Keys)
        {
            var client = _clients[id];
            client.Leave();
        }
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

    public void UpdateAppearance(RoomClient client)
    {
        if (Settings(client).SendingPaused || !_rooms.TryGetValue(client.JoinedCode, out var room)
            || !room.Members.TryGetValue(client.Id, out var member)) return;
        room.Members[client.Id] = member with { SkinId = client.SkinId,
            HeadwearId = client.HeadwearId, Reaction = client.Reaction };
        Broadcast(room);
    }

    public void UpdateActivity(RoomClient client)
    {
        if (Settings(client).SendingPaused || !_rooms.TryGetValue(client.JoinedCode, out var room)
            || !room.Members.TryGetValue(client.Id, out var member)) return;
        room.Members[client.Id] = member with { ActivitySequence = client.ActivitySequence,
            TongueActive = client.TongueActive };
        Broadcast(room);
    }

    public void SetSendingPaused(RoomClient client, bool paused)
    {
        Settings(client).SendingPaused = paused;
        if (!paused) { UpdateAppearance(client); UpdateActivity(client); }
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
        if (text.Length > RoomRules.MaxChatCharacters) return $"最多 {RoomRules.MaxChatCharacters} 个字符。";
        if (text.Any(char.IsControl)) return "请发送单行文字。";
        if (_lastChat.TryGetValue(client.Id, out var last) && Now - last < RoomRules.ChatCooldown)
            return "发送太快，请稍后再试。";
        _lastChat[client.Id] = Now;
        var chat = new RoomChat(++_nextChat, room.Code, client.Id, member.Presence,
            text.Trim(), Now + RoomRules.ChatLifetime);
        foreach (var id in room.Members.Keys)
        {
            var recipient = _clients[id];
            var settings = Settings(recipient);
            if (settings.Paused) continue;
            // One pending bubble per sender/recipient; cannot grow an unbounded queue.
            _pending.RemoveAll(d => d.ClientId == id && d.Chat?.SenderId == client.Id);
            _pending.Add(new Delivery(id, recipient.Session, Now + settings.Latency,
                null, chat));
        }
        return "";
    }

    public void SetPaused(RoomClient client, bool paused)
    {
        Settings(client).Paused = paused;
        _pending.RemoveAll(d => d.ClientId == client.Id);
        if (!paused && _rooms.TryGetValue(client.JoinedCode, out var room))
            QueueSnapshot(client, room);
    }

    public void Tick(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        Now += delta;
        // Deterministic boundary: a deadline reached on this tick expires before
        // a result is applied. Cancellation/timeout must not commit membership.
        foreach (var client in _clients.Values.ToArray()) client.AdvanceTo(Now);
        var requests = _requests.Where(r => r.Due <= Now).OrderBy(r => r.Due).ToArray();
        _requests.RemoveAll(r => r.Due <= Now);
        foreach (var pending in requests)
            pending.Client.TryComplete(pending.Request.Id, () => Complete(pending));
        var due = _pending.Where(d => d.Due <= Now).OrderBy(d => d.Due).ToArray();
        _pending.RemoveAll(d => d.Due <= Now);
        foreach (var delivery in due)
        {
            var client = _clients[delivery.ClientId];
            if (Settings(client).Paused || client.Session != delivery.Session) continue;
            if (delivery.Snapshot != null) client.Receive(delivery.Snapshot);
            else if (delivery.Chat != null) client.Receive(delivery.Chat, Now);
        }
    }

    private void Broadcast(Room room)
    {
        room.Revision++;
        foreach (var id in room.Members.Keys) QueueSnapshot(_clients[id], room);
    }

    private void QueueSnapshot(RoomClient client, Room room)
    {
        var settings = Settings(client);
        if (settings.Paused) return;
        // Keep the original due time when coalescing so continuous activity cannot
        // postpone delivery forever on a high-latency link.
        var old = _pending.FirstOrDefault(d => d.ClientId == client.Id && d.Snapshot != null);
        var due = old?.Due ?? Now + settings.Latency;
        _pending.RemoveAll(d => d.ClientId == client.Id && d.Snapshot != null);
        var snapshot = new RoomSnapshot(room.Code, room.Name, room.GameId, room.Owner,
            room.Revision, room.Members.Values.OrderBy(m => m.Id).ToArray());
        _pending.Add(new Delivery(client.Id, client.Session, due, snapshot, null));
    }
}

#endif
