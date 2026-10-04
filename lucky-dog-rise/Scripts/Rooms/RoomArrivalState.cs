#if !DEMO_BUILD && !RECORDING_BUILD
namespace LuckyDogRise.Rooms;

public enum RoomArrivalPhase { Waiting, Creating, Departing, Failed }

// This is a local presentation session, never a Steam membership or a recovery target.
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
    internal double PresentedAt;
    internal double LastAppearanceAt;
    internal RoomArrivalState(string name, RoomCompanionPlan plan, int hostMemberId)
        => (Name, Plan, HostMemberId) = (name, plan, hostMemberId);
}
#endif
