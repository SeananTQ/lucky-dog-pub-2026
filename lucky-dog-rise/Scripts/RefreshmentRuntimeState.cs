namespace LuckyDogRise;

public enum TableRefreshmentStatus
{
    Empty = 0,
    ReadyToUse = 1,
    BuffActive = 2,
}

public sealed class RefreshmentRuntimeState
{
    public int CurrentItemId { get; set; }
    public TableRefreshmentStatus Status { get; set; }
    public int BuffSourceItemId { get; set; }
    public int BuffTotalHands { get; set; }

    public bool IsReadyToUse => Status == TableRefreshmentStatus.ReadyToUse && CurrentItemId > 0;
    public bool IsBuffActive => Status == TableRefreshmentStatus.BuffActive && BuffSourceItemId > 0;
}
