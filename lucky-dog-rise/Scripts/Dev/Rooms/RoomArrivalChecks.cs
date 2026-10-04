#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Linq;

namespace LuckyDogRise.Rooms;

// Production client and Steam service, controlled native callbacks and clock.
// No Steam session, Godot runtime, owned inventory or persistent state is used.
public static class RoomArrivalChecks
{
    private const ulong Lobby = 109775243348104001;

    public static string Run()
    {
        CheckPresentationGateAndHandoffs();
        CheckLocalInteraction();
        CheckFailureAndRetry();
        CheckCancelAndLateCallback();
        CheckTimeoutAndInvitation();
        CheckServiceReplacement();
        CheckRecoveryBeforePresentation();
        CheckPublishingFailure();
        CheckJoinDuringDeparture();
        CheckDisconnectDuringDeparture();
        CheckOrdinaryCreate();
        CheckMockHandoff();
        return "[RoomArrivalChecks] PASS presentation-gated observation, three host choices, public commit before local departure, stable identities, silent bounded retries, availability recovery, concurrent arrival, cancellation, timeout, invitation priority and Mock parity (fake Steam).";
    }

    private static void CheckPresentationGateAndHandoffs()
    {
        foreach (int host in new[] { -1, -2, -3 })
        {
            using var test = new Fixture();
            Check(test.Client.BeginArrival("Arrival room", test.Plan, host), "local entry is accepted");
            var appearance = test.Client.View.Members.Where(m => m.IsCompanion).ToDictionary(m => m.Id);
            long layout = test.Client.LayoutSession;
            Check(test.Client.IsLocalArrival && test.Client.Arrival.Phase == RoomArrivalPhase.Waiting
                && test.Client.View.OwnerId == host && test.Client.View.Members.Length == 4,
                "selected local companion owns the visible arrival room");
            test.Step(20);
            Check(test.Transport.CreateCalls == 0 && test.Client.Arrival.Elapsed == 0,
                "time before the first presented frame never creates a Steam lobby");
            test.Client.MarkArrivalPresented();
            double createdAt = 20 + RoomRules.ArrivalObserveSeconds;
            test.Step(createdAt - 0.001);
            Check(test.Transport.CreateCalls == 0, "the full observation interval elapses before native creation");
            test.Step(createdAt);
            Check(test.Transport.PrivateCreateCalls == 1 && test.Transport.CreateCalls == 1
                && test.Client.Arrival.Phase == RoomArrivalPhase.Creating,
                "observation dispatches exactly one private native create");
            test.Step(createdAt + 0.1);
            Check(test.Transport.CreateCalls == 1, "waiting for a native callback never submits another create");
            ulong lobby = Lobby + (ulong)(-host);
            test.Transport.SucceedMembership(lobby);
            Check(test.Client.IsLocalArrival && test.Client.Arrival.Phase == RoomArrivalPhase.Departing
                && test.Client.Arrival.Committed && !test.Client.IsBusy
                && test.Client.JoinedCode == SteamRoomProtocol.Encode(lobby)
                && test.Client.View.OwnerId == host && test.Transport.NativeAccess[lobby] == RoomAccess.Public,
                "native membership is public and committed before the local host starts departing");
            int accessWrites = test.Transport.AccessWrites.Count;
            test.Step(createdAt + 0.1 + RoomRules.ArrivalDepartureSeconds - 0.001);
            Check(test.Client.IsLocalArrival && test.Client.View.OwnerId == host
                && test.Transport.NativeAccess[lobby] == RoomAccess.Public,
                "partial departure retains the old visible host over already committed membership");
            test.Step(createdAt + 0.1 + RoomRules.ArrivalDepartureSeconds + 0.001);
            Check(!test.Client.IsLocalArrival && test.Client.JoinedCode == SteamRoomProtocol.Encode(lobby)
                && test.Client.View.OwnerId == test.Client.Id && test.Client.View.Members.Length == 3
                && test.Client.View.Access == RoomAccess.Public && test.Transport.NativeAccess[lobby] == RoomAccess.Public,
                "finished departure commits one real player and two companions to the public lobby");
            Check(test.Transport.AccessWrites.Count == accessWrites,
                "finishing the visual departure has no fallible native publication step");
            Check(test.Client.LayoutSession == layout, "native handoff preserves the local layout session");
            var bots = test.Client.View.Members.Where(m => m.IsCompanion).ToArray();
            Check(bots.Length == 2 && bots.All(m => m.Id != host && SameIdentityAndLook(m, appearance[m.Id])),
                "only the selected host retires; the other two companions retain identity and appearance");
            Check(RoomCompanionPlan.TryRead(test.Transport.Rooms[lobby].Companions, out var shared)
                && shared.TargetMemberCount == 3 && shared.MembersAt(test.Transport.ServerTime).All(m => m.Id != host),
                "the published companion contract preserves the three-character target and retired host");
            var events = test.Transport.ArrivalEvents;
            int publicAt = events.IndexOf("access:0");
            Check(events[0] == "create-private" && events.IndexOf("initialize:2") > 0
                && events.IndexOf("companions") > events.IndexOf("initialize:2")
                && publicAt > events.IndexOf("companions"),
                "public admission happens after private creation and complete companion metadata");

            test.Transport.SetRemote(lobby, new SteamRoomMemberData(88, "second", "1:10:0:1001"));
            Check(test.Client.View.Members.Length == 3 && test.Client.View.Members.Count(m => m.IsCompanion) == 1,
                "the second real player replaces one of the remaining companions");
            test.Transport.SetRemote(lobby, new SteamRoomMemberData(99, "third", "1:10:0:1001"));
            Check(test.Client.View.Members.Length == 3 && test.Client.View.Members.All(m => !m.IsCompanion),
                "the third real player replaces the final companion");
            test.Transport.RemoveRemote(lobby, 99);
            Check(test.Client.View.Members.Length == 2 && test.Client.View.Members.All(m => !m.IsCompanion),
                "departed real players do not respawn retired companions");
        }
    }

    private static void CheckLocalInteraction()
    {
        using var test = new Fixture();
        test.Transport.ChatFilter = (_, _) => "filtered greeting";
        test.Client.BeginArrival("Local chat", test.Plan, -2);
        Check(test.Client.SendChat("greeting") == "" && test.Client.Bubbles[test.Client.Id].Text == "filtered greeting"
            && test.Transport.FilterCalls.Count == 1 && test.Transport.Chats.Count == 0,
            "local arrival chat applies the real display filter without sending a lobby message");
        Check(test.Client.SendChat("too soon") == "Rooms_ChatTooFast", "local chat keeps the regular rate limit");
        test.Client.SetAppearance(20, 40, 1005);
        test.Client.NotifyInputActivity();
        Check(test.Client.View.Members.Single(m => m.Id == test.Client.Id) is { SkinId: 20, HeadwearId: 40 }
            && test.Transport.AppearanceWrites.Count == 0 && test.Transport.ActivityWrites.Count == 0,
            "local outfit and activity changes remain local before native creation");
        Check(test.Client.SetAccess(RoomAccess.Public) == "Rooms_AccessUnavailable"
            && test.Client.SetName("rename") == "Rooms_NameUnavailable"
            && test.Client.Kick(-1, test.Plan.Seed) == "Rooms_KickUnavailable"
            && test.Service.InviteFriends(test.Client) == "Rooms_InviteUnavailable",
            "local arrival cannot mutate or invite people to a nonexistent Steam room");
    }

    private static void CheckFailureAndRetry()
    {
        using var test = new Fixture();
        test.Begin(-3);
        int failures = 0;
        test.Client.JoinFailed += (_, _) => failures++;
        var plan = test.Client.Arrival.Plan;
        long layout = test.Client.LayoutSession;
        test.Transport.FailMembership(RoomFailure.Unavailable);
        Check(test.Client.Arrival?.Phase == RoomArrivalPhase.Failed && !test.Client.IsBusy
            && !test.Client.Arrival.WaitingForConnection && test.Client.Arrival.FailureCount == 1
            && test.Client.View.OwnerId == -3 && test.Client.View.Members.Length == 4 && failures == 0,
            "a transient failure silently retains the original cast without a join-failure notification");
        double now = RoomRules.ArrivalObserveSeconds;
        foreach (int delay in new[] { 5, 15, 30, 60, 60 })
        {
            int calls = test.Transport.CreateCalls;
            Check(Math.Abs(test.Client.Arrival.RetrySecondsRemaining - delay) < 0.001,
                "retry delay follows the bounded backoff sequence");
            test.Step(now + delay - 0.001);
            Check(test.Transport.CreateCalls == calls, "idle frames do not bypass retry backoff");
            now += delay;
            test.Step(now);
            Check(test.Transport.PrivateCreateCalls == calls + 1 && test.Client.IsBusy,
                "retry deadline dispatches exactly one new private creation");
            if (calls > 1) Check(test.Client.Arrival.WaitingForConnection,
                "prolonged preparation exposes connection waiting while retaining automatic retry");
            test.Transport.FailMembership(RoomFailure.Unavailable);
        }
        Check(!test.Service.ReconnectContext.IsPending && failures == 0,
            "uncommitted preparation never arms real-room recovery or emits ordinary join errors");
        test.Step(now + 60);
        test.Transport.SucceedMembership(Lobby);
        Check(test.Client.Arrival.Committed && !test.Client.Arrival.WaitingForConnection
            && ReferenceEquals(test.Client.Arrival.Plan, plan),
            "successful retry clears waiting while retaining the original companion plan");
        test.Step(now + 60 + RoomRules.ArrivalDepartureSeconds + 0.001);
        Check(!test.Client.IsLocalArrival && test.Client.View.Members.Length == 3
            && test.Client.LayoutSession == layout && test.Client.View.Members.All(m => m.Id != -3),
            "retry uses the same local identities and chosen departing host");
    }

    private static void CheckCancelAndLateCallback()
    {
        foreach (int stage in new[] { 0, 1, 2 })
        {
            using var test = new Fixture();
            test.Client.BeginArrival("cancel", test.Plan, -1);
            test.Client.MarkArrivalPresented();
            if (stage > 0) test.Step(RoomRules.ArrivalObserveSeconds);
            if (stage > 1) test.Transport.SucceedMembership(Lobby);
            test.Client.Leave();
            if (stage == 1) test.Transport.SucceedMembership(Lobby);
            test.Step(RoomRules.ArrivalObserveSeconds + 120);
            Check(!test.Client.IsLocalArrival && test.Client.View == null && test.Client.JoinedCode.Length == 0
                && !test.Service.ReconnectContext.IsPending
                && (stage == 2 || !test.Transport.AccessWrites.Any(w => w.Access == RoomAccess.Public)),
                "leaving every phase cancels arrival and recovery, including already committed native membership");
            Check(stage == 0 ? test.Transport.CreateCalls == 0 : test.Transport.Left.Contains(Lobby),
                "unstarted arrivals never create; late/private native memberships are cleaned up");
        }
        foreach (bool dispose in new[] { false, true })
        {
            using var test = new Fixture();
            test.Client.BeginArrival("reentrant cancel", test.Plan, -1);
            test.Client.MarkArrivalPresented();
            test.Client.Changed += () =>
            {
                if (test.Client.Arrival?.Phase != RoomArrivalPhase.Creating) return;
                if (dispose) test.Client.Dispose(); else test.Client.Leave();
            };
            test.Step(RoomRules.ArrivalObserveSeconds);
            Check(test.Transport.CreateCalls == 0 && (dispose || test.Client.View == null),
                "a cancellation or disposal in the creation notification prevents the native request");
        }
        using (var test = new Fixture())
        {
            test.Begin(-2);
            test.Transport.FailMembership(RoomFailure.Unavailable);
            test.Client.Leave();
            test.Step(RoomRules.ArrivalObserveSeconds + 120);
            Check(test.Transport.CreateCalls == 1 && test.Client.View == null && !test.Client.IsLocalArrival,
                "leaving during retry backoff prevents every later automatic create");
        }
    }

    private static void CheckTimeoutAndInvitation()
    {
        using (var test = new Fixture())
        {
            test.Begin(-1);
            test.Step(RoomRules.ArrivalObserveSeconds + RoomRules.RequestTimeout);
            Check(test.Client.Arrival?.Phase == RoomArrivalPhase.Failed && !test.Client.IsBusy,
                "missing creation callback enters backoff at the bounded request deadline");
            test.Transport.SucceedMembership(Lobby);
            test.Step(RoomRules.ArrivalObserveSeconds + RoomRules.RequestTimeout + 0.1);
            Check(test.Client.IsLocalArrival && test.Transport.Left.Contains(Lobby)
                && !test.Transport.AccessWrites.Any(w => w.Access == RoomAccess.Public),
                "late success after timeout cannot commit or publish the stale private room");
        }
        using (var test = new Fixture())
        {
            test.Begin(-2);
            test.Transport.DeliverInvitation(Lobby + 10);
            Check(test.Service.TryTakeJoinRequest(out var code) && test.Client.Join(code),
                "accepted invitation can replace a local arrival");
            test.Transport.SucceedMembership(Lobby);
            Check(test.Transport.Left.Contains(Lobby) && !test.Transport.AccessWrites.Any(w => w.Access == RoomAccess.Public),
                "old arrival callback is cleaned while the invited join waits for serialization");
            test.Transport.SucceedMembership(Lobby + 10);
            test.Step(RoomRules.ArrivalObserveSeconds + 120);
            Check(!test.Client.IsLocalArrival && test.Client.JoinedCode == SteamRoomProtocol.Encode(Lobby + 10)
                && test.Transport.CreateCalls == 1, "new invitation wins permanently over the abandoned arrival");
        }
    }

    private static void CheckPublishingFailure()
    {
        using var test = new Fixture();
        test.Begin(-1);
        test.Transport.AccessNativeResults.Enqueue(true); // Existing private-owner reconciliation.
        test.Transport.AccessNativeResults.Enqueue(false);
        test.Transport.SucceedMembership(Lobby);
        Check(test.Client.Arrival?.Phase == RoomArrivalPhase.Failed && test.Client.View.OwnerId == -1
            && !test.Client.Arrival.Committed && test.Client.Arrival.DepartureProgress == 0
            && test.Transport.Left.Contains(Lobby) && test.Transport.NativeAccess[Lobby] == RoomAccess.InviteOnly
            && !test.Service.ReconnectContext.IsPending,
            "publication failure leaves the private room without ever starting the host departure");
        test.Step(RoomRules.ArrivalObserveSeconds + 5);
        test.Transport.SucceedMembership(Lobby + 1);
        test.Step(RoomRules.ArrivalObserveSeconds + 5 + RoomRules.ArrivalDepartureSeconds + 0.001);
        Check(!test.Client.IsLocalArrival && test.Client.View.OwnerId == test.Client.Id,
            "publication failure automatically retries and performs the single successful departure");
    }

    private static void CheckServiceReplacement()
    {
        using var test = new Fixture();
        test.Begin(-2);
        var localPlan = test.Client.Arrival.Plan;
        long layout = test.Client.LayoutSession;
        test.Transport.IsAvailable = false;
        test.Service.CheckAvailability();
        Check(test.Client.Arrival?.Phase == RoomArrivalPhase.Failed && !test.Client.IsBusy
            && test.Client.Arrival.WaitingForConnection
            && !test.Service.ReconnectContext.IsPending, "native loss retains local arrival without treating it as real membership recovery");
        var nextTransport = new SteamRoomChecks.FakeTransport { Now = 100 };
        using var nextService = new SteamRoomService(nextTransport, 10, _ => true, _ => true, _ => true,
            () => nextTransport.Now);
        Check(test.Client.RebindArrivalService(nextService) && ReferenceEquals(test.Client.Arrival.Plan, localPlan)
            && test.Client.LayoutSession == layout, "binding a new service preserves the original local presentation");
        test.Client.AdvanceTo(nextTransport.Now);
        nextService.Tick();
        Check(nextTransport.PrivateCreateCalls == 1,
            "restored service automatically continues preparation without a user retry");
        test.Transport.SucceedMembership(Lobby);
        Check(test.Transport.Left.Contains(Lobby) && test.Client.Arrival.Phase == RoomArrivalPhase.Creating
            && test.Client.IsBusy, "old native callback only cleans its membership and cannot cancel the new request");
        test.Service.Dispose();
        Check(test.Client.IsBusy && test.Client.IsLocalArrival, "old service disposal cannot clear the rebound local client");
        nextTransport.SucceedMembership(Lobby + 20);
        nextTransport.Now = 100 + RoomRules.ArrivalDepartureSeconds + 0.001;
        test.Client.AdvanceTo(nextTransport.Now);
        nextService.Tick();
        Check(!test.Client.IsLocalArrival && test.Client.LayoutSession == layout
            && test.Client.JoinedCode == SteamRoomProtocol.Encode(Lobby + 20)
            && test.Client.View.Members.All(m => m.Id != -2),
            "replacement service completes the same selected-host presentation using its own clock epoch");
    }

    private static void CheckJoinDuringDeparture()
    {
        using var test = new Fixture();
        test.Begin(-2);
        test.Transport.SucceedMembership(Lobby);
        test.Transport.SetRemote(Lobby, new SteamRoomMemberData(88, "joined during fade", "1:10:0:1001"));
        Check(test.Client.Arrival is { Committed: true, Phase: RoomArrivalPhase.Departing }
            && test.Client.View.OwnerId == -2 && test.Client.View.Members.Length == 4,
            "native member updates during the fade do not prematurely replace the visible local cast");
        test.Step(RoomRules.ArrivalObserveSeconds + RoomRules.ArrivalDepartureSeconds + 0.001);
        Check(!test.Client.IsLocalArrival && test.Client.View.Members.Length == 3
            && test.Client.View.Members.Count(m => m.IsCompanion) == 1
            && test.Client.View.Members.Any(m => m.Name == "joined during fade"),
            "departure ends with the newest real membership instead of a stale creation snapshot");
    }

    private static void CheckRecoveryBeforePresentation()
    {
        using var test = new Fixture();
        test.Client.BeginArrival("not yet drawn", test.Plan, -1);
        test.Transport.IsAvailable = false;
        test.Service.CheckAvailability();
        test.Client.AdvanceTo(0);
        Check(test.Client.Arrival.WaitingForConnection, "unavailable Steam waits even before a cast has been presented");
        var transport = new SteamRoomChecks.FakeTransport { Now = 100 };
        using var service = new SteamRoomService(transport, 10, _ => true, _ => true, _ => true, () => transport.Now);
        Check(test.Client.RebindArrivalService(service), "a pre-presentation arrival can bind a recovered service");
        test.Client.AdvanceTo(transport.Now);
        Check(!test.Client.Arrival.WaitingForConnection && !test.Client.Arrival.Presented
            && transport.CreateCalls == 0,
            "recovery before the first frame exposes the cast without prematurely creating or trapping it behind waiting");
        test.Client.MarkArrivalPresented();
        transport.Now += RoomRules.ArrivalObserveSeconds - 0.001;
        test.Client.AdvanceTo(transport.Now);
        Check(transport.CreateCalls == 0, "recovery preserves the full observation interval for an unseen cast");
        transport.Now += 0.001;
        test.Client.AdvanceTo(transport.Now);
        Check(transport.PrivateCreateCalls == 1, "the recovered unseen cast begins preparation after its first observation interval");
    }

    private static void CheckDisconnectDuringDeparture()
    {
        using var test = new Fixture();
        test.Begin(-3);
        test.Transport.SucceedMembership(Lobby);
        test.Step(RoomRules.ArrivalObserveSeconds + RoomRules.ArrivalDepartureSeconds / 2);
        test.Transport.IsAvailable = false;
        test.Service.CheckAvailability();
        Check(!test.Client.IsLocalArrival && test.Client.View == null && test.Service.ReconnectContext.IsPending,
            "network loss after native commit follows ordinary membership recovery without resurrecting the companion host");
        test.Step(RoomRules.ArrivalObserveSeconds + 100);
        Check(test.Transport.CreateCalls == 1 && test.Client.View == null,
            "late animation frames never recreate a room after committed membership disconnects");
    }

    private static void CheckOrdinaryCreate()
    {
        using var test = new Fixture();
        Check(test.Client.Create("ordinary"), "ordinary create is accepted");
        test.Transport.SucceedMembership(Lobby);
        Check(!test.Client.IsLocalArrival && test.Transport.CreateCalls == 1 && test.Transport.PrivateCreateCalls == 0
            && test.Client.View.Members.Length == 4 && test.Client.View.Access == RoomAccess.Public,
            "ordinary explicit creation remains an immediate public room with its existing three companions");
    }

    private static void CheckMockHandoff()
    {
        foreach (int host in new[] { -1, -2, -3 })
        {
            var mock = new RoomSandbox();
            using var client = mock.AddClient(1, "local", 10);
            var plan = RoomCompanionPlan.Create([10, 20], [0, 30], 0, 701);
            client.BeginArrival("mock arrival", plan, host);
            client.MarkArrivalPresented();
            mock.Tick(RoomRules.ArrivalObserveSeconds - 0.001);
            Check(client.IsLocalArrival && client.Arrival.Phase == RoomArrivalPhase.Waiting,
                "Mock uses the same observation interval");
            mock.Tick(0.002);
            mock.Tick(RoomRules.ArrivalDepartureSeconds + 0.001);
            Check(!client.IsLocalArrival && client.View.OwnerId == 1 && client.View.Members.Length == 3
                && client.View.Members.All(m => m.Id != host), "Mock completes the same selected-host handoff");
            var activity = client.View.Members.Where(m => m.IsCompanion).ToDictionary(m => m.Id, m => m.ActivitySequence);
            mock.Tick(RoomCompanionPlan.MaxActivityCycleSeconds + 1);
            Check(client.View.Members.Where(m => m.IsCompanion).All(m => m.ActivitySequence > activity[m.Id]),
                "Mock companion activity continues after the handoff without mixing wall-clock epochs");
        }
    }

    private static bool SameIdentityAndLook(RoomMember a, RoomMember b)
        => a.Id == b.Id && a.Presence == b.Presence && a.Name == b.Name
            && a.SkinId == b.SkinId && a.HeadwearId == b.HeadwearId;

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Room arrival: " + message); }

    private sealed class Fixture : IDisposable
    {
        public readonly SteamRoomChecks.FakeTransport Transport = new();
        public readonly SteamRoomService Service;
        public readonly RoomClient Client;
        public readonly RoomCompanionPlan Plan;
        public Fixture()
        {
            Plan = RoomCompanionPlan.Create([10, 20], [0, 30, 40], Transport.ServerTime, 219);
            Service = new SteamRoomService(Transport, 10, id => id is 10 or 20,
                id => id is 0 or 30 or 40, _ => true, () => Transport.Now,
                companions: time => RoomCompanionPlan.Create([10, 20], [0, 30, 40], time, 301));
            Client = new RoomClient(Service, 1, "local", 10);
        }
        public void Begin(int host)
        {
            Check(Client.BeginArrival("Arrival room", Plan, host), "arrival fixture starts");
            Client.MarkArrivalPresented();
            Step(RoomRules.ArrivalObserveSeconds);
        }
        public void Step(double time) { Transport.Now = time; Client.AdvanceTo(time); Service.Tick(); }
        public void Dispose() { Client.Dispose(); Service.Dispose(); }
    }
}
#endif
