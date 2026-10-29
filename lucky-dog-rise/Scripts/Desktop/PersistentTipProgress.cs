using System;
using System.Collections.Generic;

namespace LuckyDogRise;

// Dismissed is the durable completed state: acknowledged manually or made unnecessary by its own rule.
public enum PersistentTipState { Unseen, Active, Dismissed }

/// <summary>Account-scoped sticky notes. Visibility is independent of durable activation.</summary>
public sealed class PersistentTipProgress
{
    public const string PokerDraw = "poker_draw";
    public const string PokerHelpBar = "poker_help_bar";
    private readonly Func<string, PersistentTipState> _load;
    private readonly Action<string, PersistentTipState> _save;
    private readonly Dictionary<string, PersistentTipState> _states = new();

    // No delegates means an isolated in-memory preview/sandbox.
    public PersistentTipProgress(Func<string, PersistentTipState> load = null,
        Action<string, PersistentTipState> save = null)
    {
        _load = load;
        _save = save;
    }

    public PersistentTipState GetState(string key)
    {
        if (!_states.TryGetValue(key, out var state))
            _states[key] = state = _load?.Invoke(key) ?? PersistentTipState.Unseen;
        return state;
    }

    public void Activate(string key)
    {
        if (GetState(key) == PersistentTipState.Unseen) SetState(key, PersistentTipState.Active);
    }

    public void Dismiss(string key)
    {
        if (GetState(key) == PersistentTipState.Active) SetState(key, PersistentTipState.Dismissed);
    }

    // Business completion may happen before the note has ever been shown.
    public void Complete(string key)
    {
        if (GetState(key) != PersistentTipState.Dismissed) SetState(key, PersistentTipState.Dismissed);
    }

    private void SetState(string key, PersistentTipState state)
    {
        // Persist before changing memory: a failed write leaves the action retryable.
        _save?.Invoke(key, state);
        _states[key] = state;
    }
}
