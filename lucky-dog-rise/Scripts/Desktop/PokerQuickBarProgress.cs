namespace LuckyDogRise;

/// <summary>Account discovery progress, with a non-persistent replay mode for acceptance.</summary>
public sealed class PokerQuickBarProgress
{
    private readonly AccountStateManager _accountState;
    private readonly bool _sessionOnly;
    public bool HasUsedFunction { get; private set; }

    public PokerQuickBarProgress(AccountStateManager accountState, bool sessionOnly)
    {
        _accountState = accountState;
        _sessionOnly = sessionOnly;
        HasUsedFunction = !sessionOnly && accountState.LoadPokerQuickBarUsed();
    }

    public void MigrateLegacyGuidance(bool legacyNeedsPokerBasicsGuidance)
    {
        if (!_sessionOnly)
            HasUsedFunction = _accountState.LoadOrMigratePokerQuickBarUsed(legacyNeedsPokerBasicsGuidance);
    }

    public void RecordUse()
    {
        if (HasUsedFunction) return;
        if (!_sessionOnly)
            _accountState.SavePokerQuickBarUsed();
        HasUsedFunction = true;
    }

    public void Reset(bool allowPersistence = true)
    {
        if (!_sessionOnly && allowPersistence)
            _accountState.SavePokerQuickBarUsed(false);
        HasUsedFunction = false;
    }
}
