#if DEBUG && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;

namespace LuckyDogRise.Rooms;

// Pure contract checks: these validate local control flow, not Steam callbacks.
internal static class RoomRequestChecks
{
    public static string Run()
    {
        PendingAndFailureChecks();
        MembershipRaceChecks();
        SearchAndLifecycleChecks();
        LateCallbackChecks();
        RemovalRaceChecks();
        JoinFailureNoticeChecks();
        return "ROOM_REQUEST_PASS: single-flight, cancellation, timeout, retained membership, closed/full targets, late callbacks, search/join, disposal, separate request/receive delays, removal cancellation, local join failure notices and reentrancy.";
    }

    private static void JoinFailureNoticeChecks()
    {
        var service = new LateResultService();
        using var client = new RoomClient(service, 1, "Local", 1012);
        var notices = new List<(RoomFailure Reason, bool Timeout)>();
        client.JoinFailed += (reason, timeout) => notices.Add((reason, timeout));
        foreach (var reason in new[] { RoomFailure.Full, RoomFailure.NotFound, RoomFailure.Banned, RoomFailure.Unavailable })
        {
            client.Join("target");
            var id = client.ActiveRequestId;
            var before = notices.Count;
            client.TryComplete(id, () => new RoomResult(reason));
            Check(notices.Count == before + 1 && notices.Last() == (reason, false), "join failure reports exact reason once");
            client.TryComplete(id, () => new RoomResult(reason));
            client.SetReaction(1002);
            Check(notices.Count == before + 1, "duplicate completion and appearance do not repeat notice");
        }
        client.Search();
        client.TryComplete(client.ActiveRequestId, () => new RoomResult(RoomFailure.Unavailable));
        client.Create("failed");
        client.TryComplete(client.ActiveRequestId, () => new RoomResult(RoomFailure.Unavailable));
        client.Join("cancelled");
        var cancelled = client.ActiveRequestId;
        client.Cancel();
        client.TryComplete(cancelled, () => new RoomResult(RoomFailure.Full));
        Check(notices.Count == 4, "search/create failures and cancelled join remain silent");
        client.FindAndJoin();
        client.TryComplete(client.ActiveRequestId, () => RoomResult.Success);
        Check(notices.Last() == (RoomFailure.NoMatchingRoom, false), "random join with no match reports failure");
        client.FindAndJoin();
        client.TryComplete(client.ActiveRequestId, () => new RoomResult(RoomFailure.Unavailable));
        Check(notices.Last() == (RoomFailure.Unavailable, false), "random search service failure reports failure");
        client.Join("timeout");
        var timedOut = client.ActiveRequestId;
        client.AdvanceTo(RoomRules.RequestTimeout);
        Check(notices.Last() == (RoomFailure.None, true), "join timeout reported separately");
        var count = notices.Count;
        client.AdvanceTo(RoomRules.RequestTimeout + 1);
        service.RemoteSuccess(client, timedOut, "late");
        Check(notices.Count == count && client.JoinedCode == "", "late completion never renews notice or joins");
        client.Join("original");
        service.RemoteSuccess(client, client.ActiveRequestId, "original");
        client.Join("full");
        client.TryComplete(client.ActiveRequestId, () => new RoomResult(RoomFailure.Full));
        Check(client.JoinedCode == "original" && client.Bubbles.Count == 0 && service.ChatCalls == 0,
            "failed switch retains room and notice never enters chat transport or shared bubbles");

        // The old failure is delivered before Changed can start a new request,
        // so that new request's pending event can retract the obsolete notice.
        bool showing = false, retry = false;
        client.JoinFailed += (_, _) => showing = true;
        client.Changed += () =>
        {
            if (client.IsBusy) showing = false;
            else if (!retry && client.RequestState == RoomRequestState.Failed)
            { retry = true; client.Join("retry"); }
        };
        client.Join("reentrant");
        client.TryComplete(client.ActiveRequestId, () => new RoomResult(RoomFailure.Full));
        Check(client.IsBusy && retry && !showing, "retry inside Changed clears old failure instead of reviving it");
        client.Cancel();
        service.OnCancel = () => client.Join("adapter retry");
        client.Join("adapter timeout");
        count = notices.Count;
        client.AdvanceTo(RoomRules.RequestTimeout * 2 + 1);
        Check(client.IsBusy && notices.Count == count, "reentrant adapter suppresses superseded timeout notice");
        service.OnCancel = null;
    }

    private static void PendingAndFailureChecks()
    {
        var server = new RoomSandbox();
        var a = server.AddClient(1, "A", 1012);
        var b = server.AddClient(2, "B", 1012);
        server.Create(a, "Original");
        server.Join(b, a.JoinedCode);
        server.Tick(0);
        var original = a.JoinedCode;
        var settings = server.Settings(a);
        settings.RequestDelay = 2;
        Check(a.Create("New") && !a.Create("Double click") && !a.Search(), "one in-flight request");
        Check(server.PendingRequestCount == 1 && a.JoinedCode == original, "pending keeps original room");
        a.Cancel();
        server.Tick(3);
        Check(a.RequestState == RoomRequestState.Cancelled && a.JoinedCode == original
            && server.Search().Length == 1 && server.PendingRequestCount == 0, "cancelled create never allocates room");

        settings.NextFailure = MockRoomFailure.Unavailable;
        a.Create("Failed create");
        server.Tick(2);
        Check(a.Failure == RoomFailure.Unavailable && a.JoinedCode == original, "service failure retains original room");
        Check(settings.NextFailure == MockRoomFailure.None, "fault is consumed once");
        a.Join("not-found");
        server.Tick(2);
        Check(a.Failure == RoomFailure.NotFound && a.JoinedCode == original, "missing target retains original room");
        a.Create("   ");
        server.Tick(2);
        Check(a.Failure == RoomFailure.InvalidName && a.JoinedCode == original, "invalid name retains original room");

        settings.NextFailure = MockRoomFailure.NoResponse;
        server.SetPaused(a, true);
        a.Search();
        server.Tick(RoomRules.RequestTimeout - 0.5);
        Check(a.IsBusy, "no-response remains pending before deadline");
        server.Tick(0.5);
        Check(a.RequestState == RoomRequestState.TimedOut && !a.IsBusy
            && server.PendingRequestCount == 0 && a.JoinedCode == original, "paused receiver still times out and cleans request");
        server.SetPaused(a, false);

        settings.Latency = 5;
        a.Create("Request then snapshot");
        server.Tick(1.9);
        Check(a.JoinedCode == original, "request delay does not change membership early");
        server.Tick(0.1);
        Check(a.RequestState == RoomRequestState.Succeeded && a.JoinedCode != original && a.View == null,
            "request completion is independent of delayed snapshot");
        server.Tick(5);
        Check(a.View?.Name == "Request then snapshot", "snapshot arrives on separate receive timer");

        var lateClient = server.AddClient(3, "Added later", 1012);
        lateClient.Search();
        server.Tick(0);
        Check(lateClient.RequestState == RoomRequestState.Succeeded, "new client starts at current service clock");

        settings.RequestDelay = RoomRules.RequestTimeout;
        a.Create("At deadline");
        var current = a.JoinedCode;
        server.Tick(RoomRules.RequestTimeout);
        Check(a.RequestState == RoomRequestState.TimedOut && a.JoinedCode == current
            && !server.Search().Any(r => r.Name == "At deadline"), "deadline wins over same-tick completion");
    }

    private static void MembershipRaceChecks()
    {
        var server = new RoomSandbox();
        var a = server.AddClient(1, "A", 1012);
        var b = server.AddClient(2, "B", 1012);
        server.Create(a, "Keep me");
        server.Create(b, "Closing target");
        server.Tick(0);
        var original = a.JoinedCode;
        var target = b.JoinedCode;
        server.Settings(a).RequestDelay = 1;
        a.Join(target);
        b.Search();
        server.CloseRoom(target);
        Check(b.JoinedCode == "" && !b.IsBusy && b.View == null && b.RequestState == RoomRequestState.Idle,
            "closed room clears members, pending requests and completed status");
        server.Tick(1);
        Check(a.Failure == RoomFailure.NotFound && a.JoinedCode == original, "target closes while join waits");

        server.Create(b, "Becomes full");
        target = b.JoinedCode;
        a.Join(target);
        for (var id = 3; id <= 7; id++) server.Join(server.AddClient(id, $"P{id}", 1012), target);
        server.Tick(1);
        Check(a.Failure == RoomFailure.Full && a.JoinedCode == original
            && server.Search().Single(r => r.Code == target).Count == RoomRules.Capacity,
            "capacity checked at completion rather than request time");

        server.Settings(a).RequestDelay = 0;
        a.Join(original);
        server.Tick(0);
        Check(a.RequestState == RoomRequestState.Succeeded
            && server.Search().Single(r => r.Code == original).Count == 1, "joining own room is idempotent");
    }

    private static void SearchAndLifecycleChecks()
    {
        var server = new RoomSandbox();
        var a = server.AddClient(1, "A", 1012);
        var b = server.AddClient(2, "B", 1012);
        server.Create(a, "Original");
        server.Create(b, "Destination");
        server.Tick(0);
        var destination = b.JoinedCode;
        Check(a.FindAndJoin(), "random join starts by searching");
        server.Tick(0);
        Check(a.IsBusy && a.Operation == RoomOperation.Join && a.JoinedCode != destination,
            "random selection enters a separate join request");
        server.Tick(0);
        Check(a.JoinedCode == destination && a.RequestState == RoomRequestState.Succeeded, "random join reaches matching room");
        a.FindAndJoin();
        server.Tick(0);
        Check(a.Failure == RoomFailure.NoMatchingRoom && a.JoinedCode == destination,
            "no other room preserves current membership");

        server.Settings(a).RequestDelay = 2;
        a.Create("Leave cancels");
        a.Leave();
        server.Tick(3);
        Check(a.JoinedCode == "" && !a.IsBusy && server.PendingRequestCount == 0
            && !server.Search().Any(r => r.Name == "Leave cancels"), "leave cancels pending operation");
        a.Join(destination);
        a.Dispose();
        a.Dispose();
        server.Tick(3);
        Check(a.JoinedCode == "" && !a.IsBusy && !a.Create("After disposal")
            && server.PendingRequestCount == 0, "dispose is terminal and idempotent");

        var c = server.AddClient(3, "Reentrant observer", 1012);
        var leftAfterSuccess = false;
        c.Changed += () =>
        {
            if (leftAfterSuccess || c.RequestState != RoomRequestState.Succeeded) return;
            leftAfterSuccess = true;
            c.Leave();
        };
        c.Join(destination);
        server.Tick(0);
        Check(leftAfterSuccess && c.JoinedCode == "" && c.View == null
            && server.Search().Single(r => r.Code == destination).Count == 1,
            "success observer can leave without commit rejoining afterwards");
    }

    private static void LateCallbackChecks()
    {
        var service = new LateResultService();
        var client = new RoomClient(service, 1, "A", 1012);
        client.Search();
        var oldSearch = client.ActiveRequestId;
        client.Cancel();
        client.Search();
        var newSearch = client.ActiveRequestId;
        var current = new[] { new RoomListing("new", "Newest", "social", 1, 6) };
        Check(client.TryComplete(newSearch, () => new RoomResult(RoomFailure.None, current)), "current search completes");
        var staleCommit = false;
        Check(!client.TryComplete(oldSearch, () => { staleCommit = true; return new RoomResult(RoomFailure.None); })
            && !staleCommit && client.Listings.Single().Code == "new", "late search never commits or overwrites list");

        client.Join("same-room");
        var oldJoin = client.ActiveRequestId;
        client.Cancel();
        client.Join("same-room");
        var newJoin = client.ActiveRequestId;
        service.RemoteSuccess(client, newJoin, "same-room");
        service.RemoteSuccess(client, oldJoin, "same-room");
        Check(client.JoinedCode == "same-room" && service.LiveResources.SetEquals(new[] { newJoin })
            && service.Compensated.SequenceEqual(new[] { oldJoin }), "late success cleanup targets only its own resource");
        Check(service.Cancelled.Contains(oldJoin), "adapter receives cancellation");

        client.Create("Timeout");
        var timedOut = client.ActiveRequestId;
        client.AdvanceTo(RoomRules.RequestTimeout);
        service.RemoteSuccess(client, timedOut, "too-late");
        Check(client.RequestState == RoomRequestState.TimedOut && client.JoinedCode == "same-room"
            && !service.LiveResources.Contains(timedOut), "late timed-out success is compensated without changing current room");
    }

    private static void RemovalRaceChecks()
    {
        var service = new LateResultService();
        using var client = new RoomClient(service, 1, "Removed player", 1012);
        client.Join("original");
        service.RemoteSuccess(client, client.ActiveRequestId, "original");
        client.FindAndJoin();
        var pendingSearch = client.ActiveRequestId;
        var session = client.Session;
        var notificationCount = 0;
        var attemptedRejoin = false;
        client.Changed += () =>
        {
            if (client.Failure != RoomFailure.Removed) return;
            notificationCount++;
            Check(client.Operation == RoomOperation.None && client.RequestState == RoomRequestState.Failed
                && client.JoinedCode == "" && client.View == null && !client.IsBusy,
                "observers see the complete terminal removal state");
            attemptedRejoin |= client.Join("original");
        };
        client.RemoveFromRoom(RoomFailure.Removed);
        Check(service.Cancelled.Contains(pendingSearch) && client.Session > session
            && service.LiveResources.Count == 0 && notificationCount == 1 && !attemptedRejoin,
            "removal cancels pending request, advances session and prevents reentrant join");
        var committed = false;
        Check(!client.TryComplete(pendingSearch, () =>
            {
                committed = true;
                return new RoomResult(RoomFailure.None, [new RoomListing("other", "Other", "social", 1, 6)]);
            }) && !committed && client.Failure == RoomFailure.Removed && !client.IsBusy,
            "late search completion cannot auto-join after removal");

        client.Join("second");
        var pendingJoin = client.ActiveRequestId;
        client.RemoveFromRoom(RoomFailure.Removed);
        service.RemoteSuccess(client, pendingJoin, "second");
        Check(client.JoinedCode == "" && client.Failure == RoomFailure.Removed
            && service.Compensated.Contains(pendingJoin) && service.LiveResources.Count == 0,
            "late join success is compensated after removal without resurrecting membership");

        client.Join("third");
        var current = client.ActiveRequestId;
        Check(!client.TryComplete(current, () =>
            {
                client.BeginSession("third");
                client.RemoveFromRoom(RoomFailure.Banned);
                return RoomResult.Success;
            }) && client.Failure == RoomFailure.Banned && client.RequestState == RoomRequestState.Failed
            && client.JoinedCode == "" && !client.IsBusy,
            "removal inside a membership commit cannot be overwritten by outer success");
        Check(client.Search(), "terminal removal releases mutation guard for later explicit actions");
    }

    // A controllable contract double models per-operation remote resources;
    // adapters must provide equivalent ownership-safe cleanup.
    private sealed class LateResultService : IRoomService
    {
        public double Now => 0;
        public readonly List<long> Cancelled = new();
        public readonly List<long> Compensated = new();
        public readonly HashSet<long> LiveResources = new();
        public Action OnCancel;
        public int ChatCalls;
        public void Request(RoomClient client, RoomRequest request) { }
        public void Cancel(RoomClient client, long requestId) { Cancelled.Add(requestId); OnCancel?.Invoke(); }
        public void Leave(RoomClient client) { LiveResources.Clear(); client.BeginSession(""); }
        public void UpdateAppearance(RoomClient client) { }
        public void UpdateActivity(RoomClient client) { }
        public string SetGame(RoomClient client, string gameId) => "";
        public string SetAccess(RoomClient client, RoomAccess access) => "Rooms_AccessUnavailable";
        public string SendChat(RoomClient client, string text) { ChatCalls++; return ""; }
        public string Kick(RoomClient client, int memberId, long presence) => "Rooms_KickUnavailable";
        public void RemoteSuccess(RoomClient client, long requestId, string code)
        {
            LiveResources.Add(requestId);
            if (client.TryComplete(requestId, () => { client.BeginSession(code); return RoomResult.Success; })) return;
            LiveResources.Remove(requestId);
            Compensated.Add(requestId);
        }
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException("Room request regression: " + message);
    }
}
#endif
