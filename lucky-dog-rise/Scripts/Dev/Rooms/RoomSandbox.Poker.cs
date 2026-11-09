#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using LuckyDogRise.Rooms.Poker;

namespace LuckyDogRise.Rooms;

public sealed partial class RoomSandbox : IRoomPokerLinkProvider
{
    private sealed class PokerLink(RoomSandbox hub, RoomClient client) : IPokerLink
    {
        private readonly Queue<(string Sender, byte[] Data)> _queue = new();
        public event Action<string, byte[]> Received = delegate { };
        public PokerRoomContext Context => client.View is { } room && !client.IsLocalArrival
            ? new(room.Code + ":" + room.GameId, client.Id.ToString(), room.OwnerId.ToString(), room.Members.Select(m =>
                new PokerPeer(m.Id.ToString(), m.Presence.ToString(), m.Id, m.Name, m.IsCompanion)).ToArray()) : null;
        public bool Send(string recipient, byte[] payload)
        {
            if (!int.TryParse(recipient, out var id) || !hub._pokerLinks.TryGetValue(id, out var link)
                || Context == null || link.Context?.Room != Context.Room) return false;
            if (link._queue.Count >= 128 || hub.Settings(client).SendingPaused) return false;
            link._queue.Enqueue((client.Id.ToString(), (byte[])payload.Clone())); return true;
        }
        public void Poll()
        {
            for (int i = 0; i < 32 && _queue.TryDequeue(out var message); i++)
                if (!hub.Settings(client).Paused) Received(message.Sender, message.Data);
        }
        public void Dispose()
        { if (hub._pokerLinks.GetValueOrDefault(client.Id) == this) hub._pokerLinks.Remove(client.Id); _queue.Clear(); }
    }
    private readonly Dictionary<int, PokerLink> _pokerLinks = new();
    private readonly Dictionary<int, PokerNetworkSession> _pokerSimulated = new();
    private readonly Dictionary<int, double> _pokerActionAt = new();
    public int PokerSimulationLocalId { get; set; }
    public bool ProposeSimulatedPoker(int ownerId) => _pokerSimulated.TryGetValue(ownerId, out var session)
        && session.IsOwner && session.State?.Status is PokerTableStatus.Idle or PokerTableStatus.Interrupted && session.Propose();
    public IPokerLink OpenPokerLink(RoomClient client)
    {
        var link = new PokerLink(this, client); _pokerLinks[client.Id] = link; return link;
    }
    public void DisposePokerSimulation()
    {
        foreach (var session in _pokerSimulated.Values) session.Dispose();
        _pokerSimulated.Clear(); _pokerActionAt.Clear();
        foreach (var link in _pokerLinks.Values.ToArray()) link.Dispose();
    }
    partial void TickPokerSimulation(double delta)
    {
        if (PokerSimulationLocalId == 0) return;
        foreach (var client in _clients.Values.Where(c => c.Id != PokerSimulationLocalId))
        {
            if (!_pokerSimulated.TryGetValue(client.Id, out var session))
                _pokerSimulated[client.Id] = session = new(OpenPokerLink(client));
            session.Tick(delta);
            if (Now < _pokerActionAt.GetValueOrDefault(client.Id)) continue;
            _pokerActionAt[client.Id] = Now + 1.2;
            if (session.State?.Status == PokerTableStatus.Preparing)
            {
                if (!session.IsReady) session.SetReady(true);
                if (session.IsOwner && session.CanStart && session.State.Players
                    .Any(p => p.Key == PokerSimulationLocalId.ToString() && p.Ready)) session.Start();
            }
            if (session.State?.Status == PokerTableStatus.Playing && session.State.Hand is { } v)
            {
                if (v.Public.Phase == PokerPhase.Settled) session.FinishPresentation();
                else if (v.Public.ActivePlayerId == v.PlayerId) session.Act(PokerAction.EndTurn);
            }
        }
    }
}
#endif
