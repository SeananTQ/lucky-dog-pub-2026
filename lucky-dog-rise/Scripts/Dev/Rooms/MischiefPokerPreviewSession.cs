#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
#nullable enable
using System;
using System.Linq;
using LuckyDogRise.Rooms.Poker;

namespace LuckyDogRise.Rooms;

// In-memory authority for the local preview only. Never a Steam transport or an economic ledger.
public sealed class MischiefPokerPreviewSession
{
    private readonly MischiefPokerMatch _match;
    private readonly Random _random = new();
    private readonly int _localId;
    private double _now, _nextAction;
    public bool Paused { get; set; }
    public int NextDealPreset { get; set; }
    // Visual fixture only. Does not simulate admission, connection failure or auto-play authority.
    public int PanelStatePreview { get; set; }
    private int _activePreset;
    public PokerError LastError { get; private set; }
    public PokerPlayerView View => _match.GetPlayerView(_localId);

    public MischiefPokerPreviewSession(int[] players, int owner, int localId)
    {
        _match = new(players, owner);
        _localId = localId;
    }

    public void NextHand()
    {
        var state = _match.GetPublicView();
        int dealer = state.Settlement?.NextDealer.PlayerId ?? state.Dealer.PlayerId;
        int[]? deck = null;
        if (NextDealPreset >= 4 && NextDealPreset <= 8)
            deck = ExplanationDeck(state.Seats.Select(s => s.PlayerId).ToArray(), NextDealPreset);
        else if (NextDealPreset != 0)
        {
            var seats = state.Seats.Select(s => s.PlayerId).ToArray();
            int target = NextDealPreset == 3 ? seats.First(id => id != dealer) : dealer;
            var board = NextDealPreset == 1 ? new[] { 0, 9, 10, 11, 12 } : new[] { 26, 2, 19, 34, 50 };
            int[] holes = new int[seats.Length * 2];
            var available = Enumerable.Range(0, 52).Except(board).Except(new[] { 0, 13 }).ToList();
            for (int i = 0; i < seats.Length; i++)
            for (int c = 0; c < 2; c++)
                if (NextDealPreset != 1 && seats[i] == target) holes[i + c * seats.Length] = c == 0 ? 0 : 13;
                else { holes[i + c * seats.Length] = available[0]; available.RemoveAt(0); }
            var prefix = holes.Concat(board).ToArray();
            deck = prefix.Concat(Enumerable.Range(0, 52).Except(prefix)).ToArray();
        }
        LastError = _match.StartHand(_now, deck);
        if (LastError == PokerError.None) _activePreset = NextDealPreset;
        _nextAction = _now + 1.2;
    }

    private int[] ExplanationDeck(int[] seats, int preset)
    {
        // Deliberately readable fixtures for the agreed UI examples. Never used outside Mock.
        string board;
        string[] hands;
        switch (preset)
        {
            case 5:
                board = "Ah As 9d 7s 3c";
                hands = new[] { "Qd 8c", "Kc Jh", "Kd Tc", "2h 4d", "5c 6h", "2c 4h" };
                break;
            case 6:
                board = "Ah As Kc 9d 3s";
                hands = new[] { "Qc Qd", "Kd Jh", "Kh Tc", "2c 4h", "5c 6d", "7h 8s" };
                break;
            case 7:
                board = "Ah As Jc 9h 3d";
                hands = new[] { "Qh 8d", "Kc 2c", "Kd 4d", "5s 6s", "7h 8s", "2d 4h" };
                break;
            case 8:
                board = "Kh 8s Kd Jd 7d";
                hands = new[] { "As Ts", "4s 2h", "2c Qc", "3c 6h", "5s 4h", "6c 9h" };
                break;
            default:
                board = "Ts 5s 9d 9s 5c";
                hands = new[] { "Th 2h", "Td Kc", "7h 8d", "3c 4d", "6h 7s", "8c 2d" };
                break;
        }
        int[] Cards(string text) => text.Split(' ').Select(t => "cdhs".IndexOf(t[1]) * 13 + "A23456789TJQK".IndexOf(t[0])).ToArray();
        int nextOther = 1;
        var holes = seats.Select(id => Cards(hands[id == _localId ? 0 : nextOther++])).ToArray();
        var prefix = Enumerable.Range(0, 2).SelectMany(pass => holes.Select(h => h[pass])).Concat(Cards(board)).ToArray();
        return prefix.Concat(Enumerable.Range(0, 52).Except(prefix)).ToArray();
    }

    public void Tick(double delta)
    {
        if (Paused || !double.IsFinite(delta) || delta < 0) return;
        _now += delta;
        _match.AdvanceTo(_now);
        if (_now >= _nextAction) Step();
    }

    public void Flip(int slot) => Act(_localId, PokerAction.Flip, slot);
    public void EndTurn() => Act(_localId, PokerAction.EndTurn);

    private void Act(int player, PokerAction action, int slot = -1)
    {
        var state = _match.GetPublicView();
        LastError = _match.Act(new(state.SessionId, state.HandNumber, state.Revision, player, action, slot), _now);
        if (LastError == PokerError.None) _nextAction = _now + 1.2;
    }

    public void Step()
    {
        var state = _match.GetPublicView();
        if (state.Phase == PokerPhase.AwaitingDraw) LastError = _match.ResolveHand();
        else if (state.ActivePlayerId is int id && id != _localId)
        {
            // Presets pass so their intended showdown is easy to inspect. Random play is deliberately simple.
            if (_activePreset != 0 || _random.Next(4) == 0) Act(id, PokerAction.EndTurn);
            else Act(id, PokerAction.Flip, _random.Next(5));
        }
        _nextAction = _now + 1.2;
    }

    public void ExpireTurn()
    {
        var state = _match.GetPublicView();
        if (state.Phase != PokerPhase.Acting) return;
        _now += state.SecondsRemaining;
        LastError = _match.AdvanceTo(_now);
        _nextAction = _now + 1.2;
    }
}
#endif
