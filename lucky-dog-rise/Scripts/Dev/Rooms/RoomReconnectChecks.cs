#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LuckyDogRise.Rooms;

// Deterministic clocks and the real Steam adapter; never starts Steam or touches saves.
public static class RoomReconnectChecks
{
    private const ulong Lobby = 109775243348102719;
    private const uint App = 2583700;

    public static string Run()
    {
        CheckBudget();
        CheckDeadline();
        CheckIdentityAndGeneration();
        CheckSameService();
        CheckCachedMembership();
        CheckClearedLocalCache();
        CheckMissedRosterCallback();
        CheckRetryFeedback();
        CheckServiceReplacement();
        CheckTerminalFailures();
        CheckUserIntent();
        CheckDepartureCauses();
        CheckMissingCallback();
        CheckDiagnostics();
        CheckPeerVerification();
        CheckVerificationCancellation();
        CheckPostJoinMembership();
        CheckMembershipProbeLifecycle();
        CheckUnstableRecoveryBudget();
        return "[RoomReconnectChecks] PASS bounded recovery, two-peer verification and post-commit late departure recovery, health probe replay/loss/owner/lifecycle guards, unstable recovery budget and identity isolation (fake Steam).";
    }

    private static void CheckBudget()
    {
        double now = 0;
        var context = new RoomReconnectContext(() => now);
        long source = context.Bind(77, App);
        context.Interrupt(source, Lobby);
        Assert(context.TryBeginAttempt(source, out var lobby, out var first) && lobby == Lobby && context.Attempts == 1,
            "first recovery immediately targets the interrupted lobby");
        context.CompleteAttempt(source, first, RoomFailure.Unavailable);
        now = 4.999;
        Assert(!context.TryBeginAttempt(source, out _, out _), "first failure waits five seconds");
        now = 5;
        Assert(context.TryBeginAttempt(source, out _, out var second) && context.Attempts == 2, "second attempt starts after five seconds");
        context.CompleteAttempt(source, first, RoomFailure.None);
        Assert(context.IsAttemptCurrent(source, second), "late successful callback cannot complete another attempt");
        context.CompleteAttempt(source, second, RoomFailure.Unavailable);
        now = 19.999;
        Assert(!context.TryBeginAttempt(source, out _, out _), "second failure waits fifteen seconds");
        now = 20;
        Assert(context.TryBeginAttempt(source, out _, out var third) && context.Attempts == 3, "third attempt starts after fifteen seconds");
        context.CompleteAttempt(source, third, RoomFailure.Unavailable);
        now = 100;
        Assert(!context.IsPending && context.StatusKey == "Rooms_ReconnectStopped"
            && !context.TryBeginAttempt(source, out _, out _), "three failed attempts terminate without a fourth");
    }

    private static void CheckDeadline()
    {
        double now = 0;
        var context = new RoomReconnectContext(() => now);
        long source = context.Bind(77, App);
        context.Interrupt(source, Lobby);
        now = 100;
        context.Interrupt(source, Lobby + 1);
        now = 179.9;
        Assert(context.TryBeginAttempt(source, out var lobby, out var attempt) && lobby == Lobby,
            "duplicate disconnect neither replaces the target nor extends its lifetime");
        now = 180;
        Assert(!context.IsAttemptCurrent(source, attempt), "attempt becomes invalid exactly at the overall deadline");
        context.CompleteAttempt(source, attempt, RoomFailure.None);
        Assert(!context.IsPending && context.StatusKey != "Rooms_ReconnectRestored", "late callback cannot restore an expired membership");

        context.Interrupt(source, Lobby);
        Assert(context.TryBeginAttempt(source, out _, out attempt), "new interruption receives its own budget");
        now = 195;
        context.Tick();
        Assert(context.IsPending && !context.IsAttempting, "individual request expires at fifteen seconds");
        now = 199.999;
        Assert(!context.TryBeginAttempt(source, out _, out _), "timed-out attempt also obeys backoff");
        now = 200;
        Assert(context.TryBeginAttempt(source, out _, out _), "timeout retry begins five seconds later");
        context.Cancel();
        Assert(!context.IsPending && context.StatusKey == "Rooms_ReconnectCancelled", "cancellation clears process-local intent");
    }

    private static void CheckIdentityAndGeneration()
    {
        double now = 0;
        var context = new RoomReconnectContext(() => now);
        long old = context.Bind(77, App);
        context.Interrupt(old, Lobby);
        Assert(context.TryBeginAttempt(old, out _, out var oldAttempt), "old source begins recovery");
        long current = context.Bind(77, App);
        context.CompleteAttempt(old, oldAttempt, RoomFailure.None);
        context.Interrupt(old, Lobby + 1);
        Assert(context.IsPending && !context.IsSourceCurrent(old) && !context.TryBeginAttempt(old, out _, out _),
            "replaced service cannot mutate or consume same-account intent");
        now = 5;
        Assert(context.TryBeginAttempt(current, out var target, out var next) && target == Lobby,
            "same account and App preserve target through a service replacement");
        context.CompleteAttempt(current, next, RoomFailure.None);
        Assert(!context.IsPending && context.StatusKey == "Rooms_ReconnectRestored", "current source can finish recovery");

        context.Interrupt(current, Lobby);
        current = context.Bind(88, App);
        Assert(!context.IsPending && !context.TryBeginAttempt(current, out _, out _), "another Steam account never inherits the target");
        context.Interrupt(current, Lobby);
        current = context.Bind(88, App + 1);
        Assert(!context.IsPending, "another AppID never inherits the target");
        context.Interrupt(current, Lobby);
        current = context.Bind(0, App + 1);
        Assert(!context.IsPending, "unknown identity cannot restore a previous account");
        context.Interrupt(current, Lobby);
        Assert(!context.IsPending, "unknown identity cannot acquire a recovery target");
    }

    private static void CheckSameService()
    {
        var transport = new SteamRoomChecks.FakeTransport();
        using var service = Service(transport);
        using var client = Client(service);
        var remoteSession = Guid.NewGuid();
        transport.Seed(Lobby, new SteamRoomMemberData(88, "remote", "1:10:0:1001", ChatSession: remoteSession.ToString("N")));
        client.Join(Code); transport.SucceedMembership(Lobby);
        string oldChatSession = transport.ChatSessions[Lobby];
        transport.DeliverChat(Lobby, 88, SteamRoomProtocol.EncodeChat(remoteSession, 1, transport.ServerTime, "old message"));
        client.NotifyInputActivity();
        Assert(client.Bubbles.Count == 1 && client.TongueActive, "fixture contains transient chat and activity");
        transport.IsAvailable = false;
        service.Tick();
        Assert(service.ReconnectContext.IsPending && client.View == null && client.JoinedCode == ""
            && client.Bubbles.Count == 0 && !client.TongueActive, "offline state records only lobby intent and clears transient presentation");
        Assert(!service.TryReconnect(client) && transport.MembershipCalls == 1, "no join calls while Steam is unavailable");
        transport.Now = 2; client.AdvanceTo(2); transport.IsAvailable = true;
        Assert(service.TryReconnect(client), "same Steam service can reconnect");
        StartRecoveryJoin(transport, service);
        transport.SucceedMembership(Lobby);
        ConfirmRecovery(transport, service);
        Assert(client.JoinedCode == Code && !service.ReconnectContext.IsPending && transport.ChatSessions[Lobby] != oldChatSession,
            "successful rejoin is a fresh membership in the original lobby");
        transport.DeliverChat(Lobby, 88, SteamRoomProtocol.EncodeChat(remoteSession, 2, transport.ServerTime - 2, "late old message"));
        Assert(client.Bubbles.Count == 0 && transport.CreateCalls == 0 && transport.SearchCalls == 0
            && transport.JoinTargets.All(id => id == Lobby), "recovery does not replay old chat, create or search for a replacement room");
        transport.DeliverChat(Lobby, 88, SteamRoomProtocol.EncodeChat(remoteSession, 3, transport.ServerTime, "new message"));
        Assert(client.Bubbles.Count == 1, "fresh post-rejoin chat still works");
    }

    private static void CheckCachedMembership()
    {
        var transport = new SteamRoomChecks.FakeTransport();
        using var service = Service(transport);
        using var client = Client(service);
        transport.Seed(Lobby, new SteamRoomMemberData(88, "owner", "1:10:0:1001"));
        transport.Rooms[Lobby] = transport.Rooms[Lobby] with { OwnerId = 88 };
        client.Join(Code); transport.SucceedMembership(Lobby);
        var joined = transport.Rooms[Lobby];
        // Model a local SDK roster that survives an offline LeaveLobby. The
        // remote owner has already removed this peer while this cache says two.
        transport.IsAvailable = false; service.Tick();
        bool clearedOnline = false;
        transport.AfterLeave = lobby =>
        {
            Assert(transport.IsAvailable && lobby == Lobby, "stale membership cleanup runs after connection returns");
            clearedOnline = true;
            transport.Rooms[lobby] = joined with { Count = 1, Members = joined.Members.Where(m => m.SteamId != transport.LocalSteamId).ToArray() };
        };
        transport.BeforeJoin = lobby => Assert(clearedOnline
            && !transport.Rooms[lobby].Members.Any(m => m.SteamId == transport.LocalSteamId),
            "recovery cannot accept the old cached membership as a fresh join");
        transport.IsAvailable = true;
        Assert(service.TryReconnect(client) && client.View == null, "recovery waits for a new join callback after cleanup");
        Assert(transport.MembershipCalls == 1, "online Leave and Join must not execute in the same pump turn");
        StartRecoveryJoin(transport, service);
        transport.Rooms[Lobby] = joined;
        transport.SucceedMembership(Lobby);
        Assert(client.View == null, "a local SDK callback alone cannot restore a peer room");
        ConfirmRecovery(transport, service);
        Assert(client.View?.Members.Length == 2 && !service.ReconnectContext.IsPending,
            "fresh peer acknowledgments restore both members");
        transport.AfterLeave = null;
    }

    private static void CheckClearedLocalCache()
    {
        foreach (bool emptyCache in new[] { true, false })
        {
            var transport = new SteamRoomChecks.FakeTransport();
            using var service = Service(transport);
            using var client = Client(service);
            transport.Seed(Lobby, new SteamRoomMemberData(88, "owner", "1:10:0:1001"));
            transport.Rooms[Lobby] = transport.Rooms[Lobby] with { OwnerId = 88 };
            client.Join(Code); transport.SucceedMembership(Lobby);
            var joined = transport.Rooms[Lobby];
            transport.IsAvailable = false; service.Tick();
            // Offline LeaveLobby may clear the local cache before peers receive
            // the leave. An empty cache is not proof of server-side cleanup.
            if (emptyCache) transport.Rooms.Remove(Lobby);
            else transport.Rooms[Lobby] = joined with
                { Count = 1, Members = joined.Members.Where(m => m.SteamId != transport.LocalSteamId).ToArray() };
            bool clearedOnline = false;
            transport.AfterLeave = lobby => clearedOnline = transport.IsAvailable && lobby == Lobby;
            transport.BeforeJoin = _ => Assert(clearedOnline, "online cleanup precedes join even without a cached local member");
            transport.IsAvailable = true;
            service.TryReconnect(client);
            Assert(clearedOnline && client.IsBusy && transport.MembershipCalls == 1 && client.View == null,
                "empty or partial SDK cache must not bypass online cleanup or publish a recovered room");
            StartRecoveryJoin(transport, service);
            transport.Rooms[Lobby] = joined;
            transport.SucceedMembership(Lobby);
            ConfirmRecovery(transport, service);
            Assert(client.View?.Members.Length == 2 && !service.ReconnectContext.IsPending,
                "recovery commits the new join after empty-cache cleanup");
            transport.AfterLeave = null;
        }
    }

    private static void CheckMissedRosterCallback()
    {
        var transport = new SteamRoomChecks.FakeTransport();
        using var service = Service(transport);
        using var client = Client(service);
        transport.Seed(Lobby, new SteamRoomMemberData(88, "remote", "1:10:0:1001"));
        client.Join(Code); transport.SucceedMembership(Lobby);
        var both = transport.Rooms[Lobby];
        transport.Rooms[Lobby] = both with { Count = 1, Members = both.Members.Where(m => m.SteamId == transport.LocalSteamId).ToArray() };
        transport.Now = SteamRoomService.MembershipRefreshSeconds - 0.01;
        service.Tick();
        Assert(client.View.Members.Length == 2, "roster cache reconciliation respects its interval");
        transport.Now += 0.01; service.Tick();
        Assert(client.View.Members.Length == 1, "missed remote departure callback cannot leave a permanent ghost dog");
        transport.Rooms[Lobby] = both;
        transport.Now += SteamRoomService.MembershipRefreshSeconds; service.Tick();
        Assert(client.View.Members.Length == 2 && transport.SearchCalls == 0 && transport.MembershipCalls == 1,
            "missed re-entry callback restores the peer using cache reads, without rejoining or searching");
        transport.Rooms[Lobby] = both with { OwnerId = 88, Count = 1,
            Members = both.Members.Where(m => m.SteamId != transport.LocalSteamId).ToArray() };
        transport.Now += SteamRoomService.MembershipRefreshSeconds; service.Tick();
        Assert(client.View == null && service.ReconnectContext.IsPending,
            "missing local membership triggers recovery even if BLoggedOn stayed true and no disconnect callback arrived");
        client.Leave();
        Assert(!service.ReconnectContext.IsPending && !service.TryReconnect(client),
            "explicit departure still cancels automatic recovery");
    }

    private static void CheckServiceReplacement()
    {
        double now = 0;
        var context = new RoomReconnectContext(() => now);
        var oldTransport = new SteamRoomChecks.FakeTransport();
        using var oldService = Service(oldTransport, context);
        using var oldClient = Client(oldService);
        oldClient.Join(Code); oldTransport.SucceedMembership(Lobby);
        oldTransport.Disconnect();
        Assert(oldService.TryReconnect(oldClient), "connection-lost event can recover even before polling notices unavailability");
        StartRecoveryJoin(oldTransport, oldService);

        var transport = new SteamRoomChecks.FakeTransport();
        using var service = Service(transport, context);
        using var client = Client(service);
        oldClient.Dispose();
        int priorLeaves = oldTransport.Left.Count;
        Assert(context.IsPending, "late disposal of replaced client does not cancel shared recovery");
        oldTransport.SucceedMembership(Lobby);
        Assert(context.IsPending && oldClient.View == null && oldTransport.Left.Count == priorLeaves + 1,
            "late old callback cleans native membership without restoring old UI or consuming new intent");
        now = transport.Now = 5; client.AdvanceTo(5);
        Assert(service.TryReconnect(client), "replacement service retries with the original budget");
        StartRecoveryJoin(transport, service);
        oldService.Dispose();
        Assert(context.IsAttempting, "late disposal of replaced service does not cancel the new attempt");
        transport.SucceedMembership(Lobby);
        ConfirmRecovery(transport, service);
        Assert(client.JoinedCode == Code && context.StatusKey == "Rooms_ReconnectRestored", "new service commits same-account recovery");
    }

    private static void CheckRetryFeedback()
    {
        var transport = new SteamRoomChecks.FakeTransport();
        using var service = Service(transport);
        using var client = Client(service);
        client.Join(Code); transport.SucceedMembership(Lobby); transport.Disconnect();
        int notices = 0;
        client.JoinFailed += (_, _) => notices++;
        int attempt = 0;
        foreach (double delay in new[] { 0d, 5d, 15d })
        {
            attempt++;
            transport.Now += delay; client.AdvanceTo(transport.Now); service.Tick();
            Assert(service.TryReconnect(client), "scheduled transient retry reaches the real service adapter");
            StartRecoveryJoin(transport, service);
            transport.FailMembership(RoomFailure.Unavailable);
            Assert(notices == (attempt == 3 ? 1 : 0), "transient retries stay quiet and only the terminal failure raises a notice");
            Assert(!service.TryReconnect(client), "repeated frame updates cannot bypass retry backoff");
        }
        Assert(transport.MembershipCalls == 4 && transport.JoinTargets.All(id => id == Lobby)
            && service.ReconnectContext.StatusKey == "Rooms_ReconnectStopped", "real adapter honors exactly three recovery requests");
    }

    private static void CheckTerminalFailures()
    {
        foreach (var failure in new[] { RoomFailure.Full, RoomFailure.NotFound, RoomFailure.AccessDenied, RoomFailure.Removed, RoomFailure.Banned })
        {
            var transport = new SteamRoomChecks.FakeTransport();
            using var service = Service(transport);
            using var client = Client(service);
            client.Join(Code); transport.SucceedMembership(Lobby); transport.Disconnect();
            Assert(service.TryReconnect(client), "terminal failure fixture begins a recovery");
            StartRecoveryJoin(transport, service);
            transport.FailMembership(failure);
            transport.Now = 100; service.Tick();
            Assert(!service.ReconnectContext.IsPending && !service.TryReconnect(client) && client.Failure == failure
                && transport.MembershipCalls == 2 && transport.CreateCalls == 0, failure + " terminates without fallback creation");
        }
        var incompatible = new SteamRoomChecks.FakeTransport();
        using var incompatibleService = Service(incompatible);
        using var incompatibleClient = Client(incompatibleService);
        incompatibleClient.Join(Code); incompatible.SucceedMembership(Lobby); incompatible.Disconnect();
        Assert(incompatibleService.TryReconnect(incompatibleClient), "incompatible fixture begins recovery");
        StartRecoveryJoin(incompatible, incompatibleService);
        incompatible.Rooms[Lobby] = incompatible.Rooms[Lobby] with { Protocol = "different-client-version" };
        incompatible.SucceedMembership(Lobby);
        Assert(!incompatibleService.ReconnectContext.IsPending && incompatibleClient.View == null
            && incompatible.Left.Count(id => id == Lobby) == 3, "incompatible recovered lobby is left after stale-cache cleanup and terminates retries");
    }

    private static void CheckUserIntent()
    {
        foreach (string action in new[] { "leave", "cancel", "join", "create", "invite" })
        {
            var transport = new SteamRoomChecks.FakeTransport();
            using var service = Service(transport);
            using var client = Client(service);
            client.Join(Code); transport.SucceedMembership(Lobby); transport.Disconnect();
            if (action == "leave") client.Leave();
            if (action == "cancel") client.Cancel();
            if (action == "join") client.Join(SteamRoomProtocol.Encode(Lobby + 1));
            if (action == "create") client.Create("new intentional room");
            if (action == "invite") transport.DeliverInvitation(Lobby + 1);
            Assert(!service.ReconnectContext.IsPending && !service.TryReconnect(client), action + " supersedes old recovery intent");
        }
        foreach (bool invitation in new[] { false, true })
        {
            var transport = new SteamRoomChecks.FakeTransport();
            using var service = Service(transport);
            using var client = Client(service);
            client.Join(Code); transport.SucceedMembership(Lobby); transport.Disconnect();
            Assert(service.TryReconnect(client), "cancellation fixture has an in-flight recovery");
            StartRecoveryJoin(transport, service);
            if (invitation) transport.DeliverInvitation(Lobby + 1); else client.Cancel();
            transport.SucceedMembership(Lobby);
            Assert(client.View == null && !client.IsBusy && !service.ReconnectContext.IsPending
                && transport.Left.Count(id => id == Lobby) == 3, "late success after cancel/new invitation cannot pull player back");
            if (invitation)
                Assert(service.TryTakeJoinRequest(out var code) && code == SteamRoomProtocol.Encode(Lobby + 1),
                    "accepted invitation remains available after old recovery settles");
        }
        var left = new SteamRoomChecks.FakeTransport();
        using var leftService = Service(left);
        using var leftClient = Client(leftService);
        leftClient.Join(Code); left.SucceedMembership(Lobby); leftClient.Leave(); left.Disconnect();
        Assert(!leftService.ReconnectContext.IsPending, "disconnect after explicit leave cannot resurrect departed room");
    }

    private static void CheckDepartureCauses()
    {
        foreach (var reason in Enum.GetValues<SteamRoomDepartureKind>())
        {
            var transport = new SteamRoomChecks.FakeTransport();
            using var service = Service(transport);
            using var client = Client(service);
            client.Join(Code); transport.SucceedMembership(Lobby);
            transport.Depart(Lobby, reason);
            Assert(client.View == null && service.ReconnectContext.IsPending == (reason == SteamRoomDepartureKind.Disconnected),
                "only disconnected departure preserves intent, not " + reason);
            if (reason is SteamRoomDepartureKind.Removed or SteamRoomDepartureKind.Banned)
            {
                int calls = transport.MembershipCalls;
                client.Join(Code);
                Assert(client.Failure == RoomFailure.Banned && transport.MembershipCalls == calls,
                    "native removal/ban cannot be bypassed by immediate rejoin");
            }
        }
    }

    private static void CheckMissingCallback()
    {
        var transport = new SteamRoomChecks.FakeTransport();
        using var service = Service(transport);
        using var client = Client(service);
        client.Join(Code); transport.SucceedMembership(Lobby); transport.Disconnect();
        Assert(service.TryReconnect(client), "missing callback fixture begins recovery");
        StartRecoveryJoin(transport, service);
        transport.Now = 15; client.AdvanceTo(15); service.Tick();
        transport.Now = 20; client.AdvanceTo(20); service.Tick();
        Assert(!service.TryReconnect(client) && transport.MembershipCalls == 2,
            "unsettled timed-out Steam operation prevents overlapping join calls");
        transport.Now = 46; client.AdvanceTo(46); service.Tick();
        Assert(service.RestartRequired && !service.ReconnectContext.IsPending && !service.TryReconnect(client),
            "unsettled native operation fails closed at forty-five seconds");
        transport.SucceedMembership(Lobby);
        Assert(client.View == null && transport.Left.Count(id => id == Lobby) == 3, "very late callback only cleans the native membership");
    }

    private static void CheckDiagnostics()
    {
        var events = new List<(string Name, object Data)>();
        var transport = new SteamRoomChecks.FakeTransport();
        using var service = Service(transport, diagnostics: (name, data) => events.Add((name, data)));
        using var client = Client(service);
        transport.Seed(Lobby, new SteamRoomMemberData(88, "remote", "1:10:0:1001"));
        client.Join(Code); transport.SucceedMembership(Lobby);
        transport.IsAvailable = false; service.Tick();
        transport.IsAvailable = true; service.TryReconnect(client);
        StartRecoveryJoin(transport, service);
        transport.SucceedMembership(Lobby);
        ConfirmRecovery(transport, service);
        Assert(client.JoinedCode == Code, "diagnostics preserve reconnect behavior");
        foreach (var name in new[] { "rooms_request_started", "rooms_membership_callback", "rooms_membership_snapshot",
            "rooms_membership_joined", "rooms_disconnected", "rooms_membership_leave", "rooms_reconnect_probe", "rooms_reconnect_ack" })
            Assert(events.Any(e => e.Name == name), "diagnostics retain lifecycle checkpoint " + name);
        // Assert outside the diagnostic sink: its exceptions are intentionally swallowed.
        foreach (var entry in events)
        {
            Assert(entry.Data is Dictionary<string, object>, "diagnostics must not depend on reflected application property names");
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(entry.Data));
            Assert(json.RootElement.EnumerateObject().Any(), "diagnostic payload remains serializable");
            if (entry.Name == "rooms_membership_snapshot")
                Assert(json.RootElement.GetProperty("members").GetArrayLength() == 2
                    && json.RootElement.GetProperty("lobby").GetUInt64() == Lobby, "fixed keys retain roster evidence");
            if (entry.Name == "rooms_disconnected")
                Assert(json.RootElement.GetProperty("reason").GetString() == "NetworkLost", "disconnect cause survives private enum obfuscation");
        }
        var brokenLogTransport = new SteamRoomChecks.FakeTransport();
        using var brokenLogService = Service(brokenLogTransport, diagnostics: (_, _) => throw new InvalidOperationException("log unavailable"));
        using var brokenLogClient = Client(brokenLogService);
        brokenLogClient.Join(Code); brokenLogTransport.SucceedMembership(Lobby);
        Assert(brokenLogClient.JoinedCode == Code, "diagnostic failures cannot prevent joining a room");
    }

    private static void CheckPeerVerification()
    {
        foreach (bool leaveAfterFirstReply in new[] { false, true })
        {
            var host = new SteamRoomChecks.FakeTransport { LocalSteamId = 77 };
            var guest = new SteamRoomChecks.FakeTransport { LocalSteamId = 88 };
            using var hostService = Service(host);
            using var guestService = Service(guest);
            using var hostClient = Client(hostService);
            using var guestClient = Client(guestService);
            host.Seed(Lobby, new SteamRoomMemberData(88, "guest", "1:10:0:1001"));
            // Verification must also work when player text chat is disabled.
            var both = host.Rooms[Lobby] with { Game = "work", GameState = SteamRoomProtocol.EncodeGameState(new(1, "work")) };
            host.Rooms[Lobby] = guest.Rooms[Lobby] = both;
            var alone = both with { Count = 1, Members = both.Members.Where(m => m.SteamId == 77).ToArray() };
            hostClient.Join(Code); host.SucceedMembership(Lobby);
            guestClient.Join(Code); guest.SucceedMembership(Lobby);
            byte[] lastReply = null;
            guest.ChatSent = (lobby, message) => host.DeliverChat(lobby, 88, message);
            host.ChatSent = (lobby, message) => { lastReply = message; guest.DeliverChat(lobby, 77, message); };
            guest.IsAvailable = false; guestService.Tick();
            host.Rooms[Lobby] = leaveAfterFirstReply ? both : alone;
            guest.IsAvailable = true;
            Assert(guestService.TryReconnect(guestClient), "two-peer fixture begins recovery");
            StartRecoveryJoin(guest, guestService);
            guest.SucceedMembership(Lobby);
            Assert(guestClient.View == null && guestService.ReconnectContext.IsPending,
                "successful Join plus a cached two-person roster is not sufficient");
            guest.Now += SteamRoomService.RecoverySettleSeconds; host.Now = guest.Now;
            guestService.Tick();
            if (leaveAfterFirstReply)
            {
                Assert(lastReply != null && guestClient.View == null, "first acknowledgment cannot immediately restore the UI");
                // Reproduce A's late Left notification after briefly seeing B.
                host.Rooms[Lobby] = alone;
                guest.DeliverChat(Lobby, 77, lastReply);
                guest.DeliverChat(Lobby, 99, lastReply);
                Assert(guestClient.View == null, "replayed or wrong-sender replies cannot pass the second check");
            }
            else Assert(lastReply == null, "host refuses to acknowledge an absent guest even if it receives a probe");
            guest.Now += SteamRoomService.RecoverySettleSeconds; host.Now = guest.Now;
            guestService.Tick();
            Assert(guestClient.View == null && guest.Rooms[Lobby].Members.Length == 2,
                "stale B cache remains hidden after A removes B");
            guest.Now = host.Now = RoomRules.RequestTimeout;
            guestService.Tick(); guestClient.AdvanceTo(guest.Now); hostService.Tick();
            Assert(guestClient.View == null && !guestClient.IsBusy && guestService.ReconnectContext.IsPending
                && !guestService.ReconnectContext.IsAttempting && hostClient.View.Members.Length == 1,
                "unconfirmed native success times out into bounded retry without restarting Steam");
            Assert(guest.Left.Count == 3, "failed verification cleans its native membership exactly once");

            guest.Now = host.Now = RoomRules.RequestTimeout + 5; guestClient.AdvanceTo(guest.Now);
            Assert(guestService.TryReconnect(guestClient), "peer disagreement honors the existing five-second retry backoff");
            StartRecoveryJoin(guest, guestService);
            host.Rooms[Lobby] = guest.Rooms[Lobby] = both;
            guest.SucceedMembership(Lobby);
            if (lastReply != null) guest.DeliverChat(Lobby, 77, lastReply);
            Assert(guestClient.View == null, "a previous attempt's reply cannot restore a new visit");
            guest.Now += SteamRoomService.RecoverySettleSeconds; host.Now = guest.Now;
            guestService.Tick();
            var firstReply = lastReply;
            Assert(firstReply != null && guestClient.View == null, "retry waits between its two independent probes");
            guest.DeliverChat(Lobby, 77, firstReply);
            guest.Now += SteamRoomService.RecoverySettleSeconds; host.Now = guest.Now;
            guestService.Tick(); hostService.Tick();
            Assert(guestClient.View?.Members.Length == 2 && hostClient.View?.Members.Length == 2
                && !guestService.ReconnectContext.IsPending && !guestService.RestartRequired,
                "both endpoints recover after a genuinely new membership and two fresh round trips");
            Assert(hostClient.Bubbles.Count == 0 && guestClient.Bubbles.Count == 0
                && host.FilterCalls.Count == 0 && guest.FilterCalls.Count == 0,
                "membership control traffic never creates bubbles, invokes word filtering or requires enabled chat");
            var malformed = firstReply[..^1];
            Assert(!SteamRoomProtocol.TryDecodeMembershipProbe(malformed, out _, out _, out _, out _), "truncated controls are rejected");
        }
    }

    private static void CheckVerificationCancellation()
    {
        foreach (string action in new[] { "cleanup", "cancel", "invite", "dispose", "ban", "owner-change" })
        {
            var transport = new SteamRoomChecks.FakeTransport();
            using var service = Service(transport);
            using var client = Client(service);
            transport.Seed(Lobby, new SteamRoomMemberData(88, "owner", "1:10:0:1001"), new SteamRoomMemberData(99, "peer", "1:10:0:1001"));
            transport.Rooms[Lobby] = transport.Rooms[Lobby] with { OwnerId = 88 };
            client.Join(Code); transport.SucceedMembership(Lobby); transport.Disconnect();
            service.TryReconnect(client);
            if (action == "cleanup")
            {
                client.Cancel(); StartRecoveryJoin(transport, service);
                Assert(transport.MembershipCalls == 1 && !client.IsBusy && !service.ReconnectContext.IsPending,
                    "cancelling during cleanup never launches JoinLobby later");
                continue;
            }
            StartRecoveryJoin(transport, service);
            transport.SucceedMembership(Lobby);
            transport.Now += SteamRoomService.RecoverySettleSeconds; service.Tick();
            Assert(SteamRoomProtocol.TryDecodeMembershipProbe(transport.Chats.Last(), out var requester,
                out var peer, out var nonce, out var reply) && !reply && peer == 88, "candidate requests confirmation from its current owner");
            var ack = SteamRoomProtocol.EncodeMembershipProbe(requester, peer, nonce, true);
            if (action == "owner-change")
            {
                transport.Rooms[Lobby] = transport.Rooms[Lobby] with { OwnerId = 99 };
                transport.DeliverChat(Lobby, 88, ack);
                Assert(client.View == null, "former owner's late reply cannot confirm a new owner");
                service.Tick(); ConfirmRecovery(transport, service);
                Assert(client.View != null && !service.ReconnectContext.IsPending, "verification restarts against the new owner");
                continue;
            }
            if (action == "cancel") client.Cancel();
            if (action == "invite") transport.DeliverInvitation(Lobby + 1);
            if (action == "dispose") service.Dispose();
            if (action == "ban") transport.SetBans(Lobby, SteamRoomProtocol.EncodeBannedMembers(new[] { 77UL }), notify: false);
            if (action != "dispose") service.Tick();
            transport.DeliverChat(Lobby, 88, ack);
            Assert(client.View == null && !service.ReconnectContext.IsAttempting && transport.Left.Count == 3,
                action + " cleans an already-returned native join without waiting for a nonexistent second callback");
            if (action == "ban") Assert(client.Failure == RoomFailure.Banned && !service.ReconnectContext.IsPending,
                "fresh ban data terminates recovery during verification");
        }
    }

    private static void CheckPostJoinMembership()
    {
        foreach (bool afterRecovery in new[] { false, true })
        {
            var host = new SteamRoomChecks.FakeTransport { LocalSteamId = 77 };
            var guest = new SteamRoomChecks.FakeTransport { LocalSteamId = 88 };
            using var hostService = Service(host);
            using var guestService = Service(guest);
            using var hostClient = Client(hostService);
            using var guestClient = Client(guestService);
            host.Seed(Lobby, new SteamRoomMemberData(88, "guest", "1:10:0:1001"));
            var both = host.Rooms[Lobby] with { Game = "work", GameState = SteamRoomProtocol.EncodeGameState(new(1, "work")) };
            host.Rooms[Lobby] = guest.Rooms[Lobby] = both;
            hostClient.Join(Code); host.SucceedMembership(Lobby);
            guestClient.Join(Code); guest.SucceedMembership(Lobby);
            byte[] lastReply = null;
            bool drop = false;
            guest.ChatSent = (lobby, message) => { if (!drop) host.DeliverChat(lobby, 88, message); };
            host.ChatSent = (lobby, message) => { lastReply = message; guest.DeliverChat(lobby, 77, message); };
            void Tick(double delta)
            {
                guest.Now += delta; host.Now = guest.Now;
                hostService.Tick(); guestService.Tick(); guestClient.AdvanceTo(guest.Now);
            }
            if (afterRecovery)
            {
                guest.Disconnect();
                Assert(guestService.TryReconnect(guestClient), "recovery starts before post-commit late departure");
                Tick(SteamRoomService.RecoverySettleSeconds); guest.SucceedMembership(Lobby);
                Tick(SteamRoomService.RecoverySettleSeconds); Tick(SteamRoomService.RecoverySettleSeconds);
                Assert(guestClient.View?.Members.Length == 2, "both initial peer checks passed before delayed departure");
            }
            // A removes B only after B has restored the UI. B keeps a valid but
            // stale native snapshot indefinitely, reproducing the Nov 21 logs.
            host.Rooms[Lobby] = both with { Count = 1, Members = both.Members.Where(m => m.SteamId == 77).ToArray() };
            Tick(afterRecovery ? SteamRoomService.RecoveryProbeIntervalSeconds : SteamRoomService.MembershipProbeIntervalSeconds);
            if (lastReply != null) guest.DeliverChat(Lobby, 77, lastReply);
            Assert(SteamRoomProtocol.TryDecodeMembershipProbe(guest.Chats.Last(), out var requester, out var peer, out var nonce, out _),
                "committed guest probes its current owner");
            guest.DeliverChat(Lobby, 99, SteamRoomProtocol.EncodeMembershipProbe(requester, peer, nonce, true));
            Tick(5); Tick(5);
            Assert(guestClient.View != null, "one dropped reply does not immediately tear down the room");
            Tick(5);
            Assert(guestClient.View == null && guestService.ReconnectContext.IsPending
                && hostClient.View?.Members.Length == 1 && guest.Rooms[Lobby].Members.Length == 2,
                "post-commit disagreement clears B's stale room without evicting the healthy host");
            if (afterRecovery)
            {
                Assert(!guestService.TryReconnect(guestClient) && guestService.ReconnectContext.Attempts == 1,
                    "brief restoration does not reset the retry budget or bypass backoff");
                Tick(5);
            }
            Assert(guestService.TryReconnect(guestClient), "persistent split enters a real membership retry");
            guest.BeforeJoin = _ =>
            {
                host.Rooms[Lobby] = guest.Rooms[Lobby] = both;
                host.SetRemote(Lobby, both.Members.Single(m => m.SteamId == 88));
            };
            Tick(SteamRoomService.RecoverySettleSeconds); guest.SucceedMembership(Lobby);
            Tick(SteamRoomService.RecoverySettleSeconds); Tick(SteamRoomService.RecoverySettleSeconds);
            Assert(guestClient.View?.Members.Length == 2 && hostClient.View?.Members.Length == 2,
                "new membership and two peer confirmations restore both views");
            int joins = guest.MembershipCalls;
            drop = true; Tick(2); Tick(5); drop = false; Tick(5);
            Assert(guestClient.View != null && guest.MembershipCalls == joins, "a lost probe is retried without unnecessary rejoining");
            for (int i = 0; i < 4; i++) Tick(10);
            Assert(guestClient.View?.Members.Length == 2 && guest.MembershipCalls == joins
                && hostClient.Bubbles.Count == 0 && guestClient.Bubbles.Count == 0
                && guest.FilterCalls.Count == 0 && host.FilterCalls.Count == 0,
                "healthy checks remain connected and never enter text chat or filtering, even in work mode");
            guest.Disconnect();
            Assert(guestService.TryReconnect(guestClient) && guestService.ReconnectContext.Attempts == 1,
                "a later independent outage gets a fresh budget after sustained acknowledged membership");
        }
    }

    private static void CheckMembershipProbeLifecycle()
    {
        foreach (string action in new[] { "leave", "invite", "ban", "dispose", "owner-change", "become-owner" })
        {
            var transport = new SteamRoomChecks.FakeTransport();
            using var service = Service(transport);
            using var client = Client(service);
            transport.Seed(Lobby, new SteamRoomMemberData(88, "owner", "1:10:0:1001"), new SteamRoomMemberData(99, "next", "1:10:0:1001"));
            transport.Rooms[Lobby] = transport.Rooms[Lobby] with { OwnerId = 88 };
            client.Join(Code); transport.SucceedMembership(Lobby);
            transport.Now = SteamRoomService.MembershipProbeIntervalSeconds; service.Tick();
            Assert(SteamRoomProtocol.TryDecodeMembershipProbe(transport.Chats.Last(), out var requester, out var peer, out var nonce, out _),
                "lifecycle fixture has an outstanding health probe");
            var reply = SteamRoomProtocol.EncodeMembershipProbe(requester, peer, nonce, true);
            if (action == "leave") client.Leave();
            if (action == "invite") transport.DeliverInvitation(Lobby + 1);
            if (action == "ban") transport.SetBans(Lobby, SteamRoomProtocol.EncodeBannedMembers(new[] { 77UL }), notify: true);
            if (action == "dispose") service.Dispose();
            if (action is "owner-change" or "become-owner")
            {
                transport.Rooms[Lobby] = transport.Rooms[Lobby] with { OwnerId = action == "owner-change" ? 99UL : 77UL };
                transport.Now += 5; service.Tick();
                transport.DeliverChat(Lobby, 88, reply);
                if (action == "owner-change")
                {
                    transport.Now += 1; service.Tick();
                    Assert(SteamRoomProtocol.TryDecodeMembershipProbe(transport.Chats.Last(), out requester, out peer, out nonce, out _)
                        && peer == 99, "new owner receives a fresh challenge");
                    transport.DeliverChat(Lobby, peer, SteamRoomProtocol.EncodeMembershipProbe(requester, peer, nonce, true));
                    transport.Now += 10; service.Tick();
                }
                else { transport.Now += 60; service.Tick(); }
                Assert(client.View != null && !service.ReconnectContext.IsPending,
                    "old owner's outstanding deadline does not evict a room after ownership changes");
                continue;
            }
            transport.DeliverChat(Lobby, 88, reply);
            transport.Now += 60; service.Tick();
            Assert(!service.ReconnectContext.IsPending && transport.MembershipCalls == 1,
                action + " prevents delayed health checks from reviving cancelled recovery");
        }
    }

    private static void CheckUnstableRecoveryBudget()
    {
        double now = 0;
        var context = new RoomReconnectContext(() => now);
        long source = context.Bind(77, App);
        context.Interrupt(source, Lobby);
        for (int attempt = 1; attempt <= RoomReconnectContext.MaximumAttempts; attempt++)
        {
            Assert(context.TryBeginAttempt(source, out _, out var generation) && context.Attempts == attempt,
                "flapping recovery consumes the original attempt budget");
            context.CompleteAttempt(source, generation, RoomFailure.None);
            now += 1;
            context.ConfirmStableMembership(source, Lobby); // Too soon.
            context.Interrupt(source, Lobby);
            if (attempt < RoomReconnectContext.MaximumAttempts)
            {
                Assert(!context.TryBeginAttempt(source, out _, out _), "post-success loss respects backoff");
                now += attempt == 1 ? 5 : 15;
            }
        }
        Assert(!context.IsPending && context.StatusKey == "Rooms_ReconnectStopped",
            "repeated short successes cannot create an infinite reconnect loop");
        context.Interrupt(source, Lobby);
        context.TryBeginAttempt(source, out _, out var last);
        context.CompleteAttempt(source, last, RoomFailure.None);
        now += RoomReconnectContext.LifetimeSeconds;
        context.Interrupt(source, Lobby);
        Assert(!context.IsPending, "unconfirmed restoration cannot extend the original overall deadline");
    }

    private static void StartRecoveryJoin(SteamRoomChecks.FakeTransport transport, SteamRoomService service)
    {
        transport.Now += SteamRoomService.RecoverySettleSeconds;
        service.Tick();
    }

    // Existing tests isolate request/cancellation policy. Dedicated two-service
    // checks below exercise the responder with separate native membership caches.
    private static void ConfirmRecovery(SteamRoomChecks.FakeTransport transport, SteamRoomService service)
    {
        for (int round = 0; round < 2 && service.ReconnectContext.IsPending; round++)
        {
            transport.Now += SteamRoomService.RecoverySettleSeconds;
            service.Tick();
            if (!service.ReconnectContext.IsPending) break; // Lone owner.
            Assert(SteamRoomProtocol.TryDecodeMembershipProbe(transport.Chats.LastOrDefault(),
                out var requester, out var peer, out var nonce, out var reply) && !reply, "recovery sends a fresh control probe");
            transport.DeliverChat(Lobby, peer, SteamRoomProtocol.EncodeMembershipProbe(requester, peer, nonce, true));
        }
    }

    private static string Code => SteamRoomProtocol.Encode(Lobby);
    private static SteamRoomService Service(SteamRoomChecks.FakeTransport transport, RoomReconnectContext context = null,
        Action<string, object> diagnostics = null)
        => new(transport, 10, skin => skin is 10 or 20, hat => hat is 30 or 40,
            reaction => reaction is 1001 or 1002, () => transport.Now, reconnect: context, appId: App, diagnostics: diagnostics);
    private static RoomClient Client(SteamRoomService service) => new(service, 1, "local", 10);
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Room reconnect check failed: " + message);
    }
}
#endif
