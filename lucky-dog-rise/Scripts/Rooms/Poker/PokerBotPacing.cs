#if !DEMO_BUILD && !RECORDING_BUILD
#nullable enable
using System;

namespace LuckyDogRise.Rooms.Poker;

// Authority-side pacing, independent of any viewer's animation or visibility.
// A new turn and the second action both receive a fresh thinking interval.
internal sealed class PokerBotPacing
{
    public const double MinimumSeconds = 2;
    public const double MaximumSeconds = 5;
    private readonly Random _random;
    private (Guid Session, long Hand, int Player, int Flips)? _pending;
    private double _actionAt;

    public PokerBotPacing(Random? random = null) => _random = random ?? Random.Shared;

    public bool TryAct(PokerPublicView state, double now)
    {
        if (state.Phase != PokerPhase.Acting || state.ActivePlayerId is not int player)
        { Reset(); return false; }
        var action = (state.SessionId, state.HandNumber, player, state.FlipsRemaining);
        if (_pending != action)
        {
            _pending = action;
            Schedule(now);
            return false;
        }
        if (now < _actionAt) return false;
        // Failed sends may retry later, but never burst multiple actions to catch up.
        Schedule(now);
        return true;
    }

    public void Reset() { _pending = null; _actionAt = 0; }
    private void Schedule(double now) => _actionAt = now
        + MinimumSeconds + _random.NextDouble() * (MaximumSeconds - MinimumSeconds);
}
#endif
