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
        CheckPublishingFailure();
        CheckOrdinaryCreate();
        CheckMockHandoff();
        return "[RoomArrivalChecks] PASS presentation-gated observation, three host choices, private-before-public handoff, stable identities and appearance, local chat, retries, cancellation, timeout, invitation priority and Mock parity (fake Steam).";
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
            test.Step(22.999);
            Check(test.Transport.CreateCalls == 0, "the full observation interval elapses before native creation");
            test.Step(23);
            Check(test.Transport.PrivateCreateCalls == 1 && test.Transport.CreateCalls == 1
                && test.Client.Arrival.Phase == RoomArrivalPhase.Creating,
                "observation dispatches exactly one private native create");
            test.Step(23.1);
            Check(test.Transport.CreateCalls == 1, "waiting for a native callback never submits another create");
            ulong lobby = Lobby + (ulong)(-host);
            test.Transport.SucceedMembership(lobby);
            Check(test.Client.IsLocalArrival && test.Client.Arrival.Phase == RoomArrivalPhase.Departing
                && test.Transport.NativeAccess[lobby] == RoomAccess.InviteOnly
                && !test.Transport.AccessWrites.Any(w => w.Access == RoomAccess.Public),
                "native success remains private while the local host departs");
            test.Step(23.849);
            Check(test.Client.IsLocalArrival && test.Transport.NativeAccess[lobby] == RoomAccess.InviteOnly,
                "a partial departure cannot expose the new room publicly");
            test.Step(23.851);
            Check(!test.Client.IsLocalArrival && test.Client.JoinedCode == SteamRoomProtocol.Encode(lobby)
                && test.Client.View.OwnerId == test.Client.Id && test.Client.View.Members.Length == 3
                && test.Client.View.Access == RoomAccess.Public && test.Transport.NativeAccess[lobby] == RoomAccess.Public,
                "finished departure commits one real player and two companions to the public lobby");
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
        long layout = test.Client.LayoutSession;
        test.Transport.FailMembership(RoomFailure.Unavailable);
        Check(test.Client.Arrival?.Phase == RoomArrivalPhase.Failed && !test.Client.IsBusy
            && test.Client.View.OwnerId == -3 && test.Client.View.Members.Length == 4,
            "creation failure retains the visible local companions and offers an explicit retry");
        test.Step(100);
        Check(test.Transport.CreateCalls == 1 && !test.Service.ReconnectContext.IsPending,
            "failed arrival does not restart itself or arm room recovery");
        Check(test.Client.RetryArrival() && !test.Client.RetryArrival() && test.Transport.PrivateCreateCalls == 2,
            "one explicit retry immediately makes one fresh private create");
        test.Transport.SucceedMembership(Lobby);
        test.Step(100.751);
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
            if (stage > 0) test.Step(3);
            if (stage > 1) test.Transport.SucceedMembership(Lobby);
            test.Client.Leave();
            if (stage == 1) test.Transport.SucceedMembership(Lobby);
            test.Step(30);
            Check(!test.Client.IsLocalArrival && test.Client.View == null && test.Client.JoinedCode.Length == 0
                && !test.Service.ReconnectContext.IsPending
                && !test.Transport.AccessWrites.Any(w => w.Access == RoomAccess.Public),
                "leaving at each arrival phase cancels the local flow without exposing or restoring a room");
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
            test.Step(3);
            Check(test.Transport.CreateCalls == 0 && (dispose || test.Client.View == null),
                "a cancellation or disposal in the creation notification prevents the native request");
        }
    }

    private static void CheckTimeoutAndInvitation()
    {
        using (var test = new Fixture())
        {
            test.Begin(-1);
            test.Step(3 + RoomRules.RequestTimeout);
            Check(test.Client.Arrival?.Phase == RoomArrivalPhase.Failed && !test.Client.IsBusy,
                "missing creation callback fails at the bounded request deadline");
            test.Transport.SucceedMembership(Lobby);
            test.Step(30);
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
            test.Step(30);
            Check(!test.Client.IsLocalArrival && test.Client.JoinedCode == SteamRoomProtocol.Encode(Lobby + 10)
                && test.Transport.CreateCalls == 1, "new invitation wins permanently over the abandoned arrival");
        }
    }

    private static void CheckPublishingFailure()
    {
        using var test = new Fixture();
        test.Begin(-1);
        test.Transport.SucceedMembership(Lobby);
        test.Transport.AccessNativeResults.Enqueue(true); // Existing private-owner reconciliation.
        test.Transport.AccessNativeResults.Enqueue(false);
        test.Step(3.751);
        Check(test.Client.Arrival?.Phase == RoomArrivalPhase.Failed && test.Client.View.OwnerId == -1
            && test.Transport.Left.Contains(Lobby) && test.Transport.NativeAccess[Lobby] == RoomAccess.InviteOnly
            && !test.Service.ReconnectContext.IsPending,
            "failure to publish leaves the private room and returns to retryable local presentation");
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
            && !test.Service.ReconnectContext.IsPending, "native loss retains local arrival without treating it as real membership recovery");
        var nextTransport = new SteamRoomChecks.FakeTransport { Now = 100 };
        using var nextService = new SteamRoomService(nextTransport, 10, _ => true, _ => true, _ => true,
            () => nextTransport.Now);
        Check(test.Client.RebindArrivalService(nextService) && ReferenceEquals(test.Client.Arrival.Plan, localPlan)
            && test.Client.LayoutSession == layout && test.Client.Arrival.Phase == RoomArrivalPhase.Failed
            && nextTransport.CreateCalls == 0, "binding a new service preserves local presentation without silently retrying");
        Check(test.Client.RetryArrival() && nextTransport.PrivateCreateCalls == 1, "explicit retry uses the replacement service");
        test.Transport.SucceedMembership(Lobby);
        Check(test.Transport.Left.Contains(Lobby) && test.Client.Arrival.Phase == RoomArrivalPhase.Creating
            && test.Client.IsBusy, "old native callback only cleans its membership and cannot cancel the new request");
        test.Service.Dispose();
        Check(test.Client.IsBusy && test.Client.IsLocalArrival, "old service disposal cannot clear the rebound local client");
        nextTransport.SucceedMembership(Lobby + 20);
        nextTransport.Now = 100.751;
        test.Client.AdvanceTo(nextTransport.Now);
        nextService.Tick();
        Check(!test.Client.IsLocalArrival && test.Client.LayoutSession == layout
            && test.Client.JoinedCode == SteamRoomProtocol.Encode(Lobby + 20)
            && test.Client.View.Members.All(m => m.Id != -2),
            "replacement service completes the same selected-host presentation using its own clock epoch");
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
            mock.Tick(2.999);
            Check(client.IsLocalArrival && client.Arrival.Phase == RoomArrivalPhase.Waiting,
                "Mock uses the same observation interval");
            mock.Tick(0.002);
            mock.Tick(0.751);
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
            Step(3);
        }
        public void Step(double time) { Transport.Now = time; Client.AdvanceTo(time); Service.Tick(); }
        public void Dispose() { Client.Dispose(); Service.Dispose(); }
    }
}
#endif
