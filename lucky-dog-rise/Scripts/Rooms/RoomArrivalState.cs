#if !DEMO_BUILD && !RECORDING_BUILD
namespace LuckyDogRise.Rooms;

public enum RoomArrivalPhase { Waiting, Creating, Departing, Failed }

// Before Committed this is only a local presentation. Once the real membership
// is accepted, its snapshots wait behind the short local departure animation.
public sealed class RoomArrivalState
{
    public string Name { get; }
    public RoomCompanionPlan Plan { get; }
    public int HostMemberId { get; }
    public RoomArrivalPhase Phase { get; internal set; } = RoomArrivalPhase.Waiting;
    public bool Presented { get; internal set; }
    public double Elapsed { get; internal set; }
    public double DepartureProgress { get; internal set; }
    public RoomFailure Failure { get; internal set; }
    public bool WaitingForConnection { get; internal set; }
    public bool Committed { get; internal set; }
    public int FailureCount { get; internal set; }
    public double RetrySecondsRemaining { get; internal set; }
    internal double PresentedAt;
    internal double LastAppearanceAt;
    internal double FirstFailureAt = double.NaN;
    internal double RetryAt;
    internal double DepartureAt;
    internal RoomArrivalState(string name, RoomCompanionPlan plan, int hostMemberId)
        => (Name, Plan, HostMemberId) = (name, plan, hostMemberId);
}
#endif
