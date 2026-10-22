namespace LuckyDogRise;

/// <summary>One account's acknowledgement, with a non-persistent replay mode for acceptance.</summary>
public sealed class PokerWelcomeProgress
{
    private readonly AccountStateManager _accountState;
    private readonly bool _sessionOnly;
    public bool IsAcknowledged { get; private set; }

    public PokerWelcomeProgress(AccountStateManager accountState, bool sessionOnly)
    {
        _accountState = accountState;
        _sessionOnly = sessionOnly;
        IsAcknowledged = !sessionOnly && accountState.LoadPokerWelcomeAcknowledged();
    }

    public void Acknowledge(bool allowPersistence = true)
    {
        if (IsAcknowledged) return;
        if (!_sessionOnly && allowPersistence)
            _accountState.SavePokerWelcomeAcknowledged();
        IsAcknowledged = true;
    }
}
