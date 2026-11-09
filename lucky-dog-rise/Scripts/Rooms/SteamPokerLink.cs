#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Steamworks;
using LuckyDogRise.Rooms.Poker;

namespace LuckyDogRise.Rooms;

public interface IRoomPokerLinkProvider
{
    IPokerLink OpenPokerLink(RoomClient client);
}

// Uses the existing initialized Steam session and callback pump. Never broadcasts private cards via Lobby data/chat.
public sealed class SteamPokerLink : IPokerLink
{
    private const int Channel = 7;
    private readonly Func<PokerRoomContext> _context;
    private readonly Func<bool> _available;
    private readonly Callback<SteamNetworkingMessagesSessionRequest_t> _requests;
    private readonly HashSet<ulong> _peers = new();
    private readonly IntPtr[] _incoming = new IntPtr[32];
    private bool _disposed;
    public PokerRoomContext Context => !_disposed && _available() ? _context() : null;
    public event Action<string, byte[]> Received = delegate { };
    public SteamPokerLink(Func<PokerRoomContext> context, Func<bool> available)
    {
        _context = context; _available = available;
        _requests = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(request =>
        {
            var id = request.m_identityRemote.GetSteamID64();
            if (!Allowed(id)) return;
            _peers.Add(id);
            var identity = request.m_identityRemote;
            SteamNetworkingMessages.AcceptSessionWithUser(ref identity);
        });
    }
    private bool Allowed(ulong id) => id != 0 && Context?.Members.Any(m => !m.Companion && m.Key == id.ToString()) == true;
    public bool Send(string recipient, byte[] payload)
    {
        if (!ulong.TryParse(recipient, out var id) || !Allowed(id) || payload.Length > PokerNetworkProtocol.MaxBytes) return false;
        var identity = new SteamNetworkingIdentity(); identity.SetSteamID64(id);
        var pin = GCHandle.Alloc(payload, GCHandleType.Pinned);
        try
        {
            _peers.Add(id);
            return SteamNetworkingMessages.SendMessageToUser(ref identity, pin.AddrOfPinnedObject(),
                (uint)payload.Length, Constants.k_nSteamNetworkingSend_Reliable, Channel) == EResult.k_EResultOK;
        }
        finally { pin.Free(); }
    }
    public void Poll()
    {
        if (_disposed || !_available()) return;
        foreach (var peer in _peers.Where(id => !Allowed(id)).ToArray())
        {
            var identity = new SteamNetworkingIdentity(); identity.SetSteamID64(peer);
            SteamNetworkingMessages.CloseChannelWithUser(ref identity, Channel); _peers.Remove(peer);
        }
        int count = SteamNetworkingMessages.ReceiveMessagesOnChannel(Channel, _incoming, _incoming.Length);
        for (int i = 0; i < count; i++)
        {
            var pointer = _incoming[i];
            try
            {
                var message = Marshal.PtrToStructure<SteamNetworkingMessage_t>(pointer);
                var sender = message.m_identityPeer.GetSteamID64();
                if (!Allowed(sender) || message.m_cbSize is < 2 or > PokerNetworkProtocol.MaxBytes || message.m_pData == IntPtr.Zero) continue;
                var bytes = new byte[message.m_cbSize];
                Marshal.Copy(message.m_pData, bytes, 0, bytes.Length);
                Received(sender.ToString(), bytes);
            }
            finally { SteamNetworkingMessage_t.Release(pointer); _incoming[i] = IntPtr.Zero; }
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _requests.Dispose();
        if (_available()) foreach (var peer in _peers)
        {
            var identity = new SteamNetworkingIdentity(); identity.SetSteamID64(peer);
            SteamNetworkingMessages.CloseChannelWithUser(ref identity, Channel);
        }
        _peers.Clear();
    }
}
#endif
