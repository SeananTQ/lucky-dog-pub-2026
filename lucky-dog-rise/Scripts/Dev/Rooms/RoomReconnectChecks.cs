#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Linq;

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
        CheckRetryFeedback();
        CheckServiceReplacement();
        CheckTerminalFailures();
        CheckUserIntent();
        CheckDepartureCauses();
        CheckMissingCallback();
        return "[RoomReconnectChecks] PASS 180-second budget, 3 attempts, 5/15-second backoff, source/account/App isolation, same/replaced service, late callbacks, explicit intent, departure causes and fresh chat membership (fake Steam).";
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
        transport.SucceedMembership(Lobby);
        Assert(client.JoinedCode == Code && !service.ReconnectContext.IsPending && transport.ChatSessions[Lobby] != oldChatSession,
            "successful rejoin is a fresh membership in the original lobby");
        transport.DeliverChat(Lobby, 88, SteamRoomProtocol.EncodeChat(remoteSession, 2, transport.ServerTime - 2, "late old message"));
        Assert(client.Bubbles.Count == 0 && transport.CreateCalls == 0 && transport.SearchCalls == 0
            && transport.JoinTargets.All(id => id == Lobby), "recovery does not replay old chat, create or search for a replacement room");
        transport.DeliverChat(Lobby, 88, SteamRoomProtocol.EncodeChat(remoteSession, 3, transport.ServerTime, "new message"));
        Assert(client.Bubbles.Count == 1, "fresh post-rejoin chat still works");
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
        oldService.Dispose();
        Assert(context.IsAttempting, "late disposal of replaced service does not cancel the new attempt");
        transport.SucceedMembership(Lobby);
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
        foreach (double at in new[] { 0d, 5d, 20d })
        {
            transport.Now = at; client.AdvanceTo(at); service.Tick();
            Assert(service.TryReconnect(client), "scheduled transient retry reaches the real service adapter");
            transport.FailMembership(RoomFailure.Unavailable);
            Assert(notices == (at == 20 ? 1 : 0), "transient retries stay quiet and only the terminal failure raises a notice");
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
        incompatible.Rooms[Lobby] = incompatible.Rooms[Lobby] with { Protocol = "different-client-version" };
        incompatible.SucceedMembership(Lobby);
        Assert(!incompatibleService.ReconnectContext.IsPending && incompatibleClient.View == null
            && incompatible.Left.Count(id => id == Lobby) == 2, "incompatible recovered lobby is left and terminates retries");
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
            if (invitation) transport.DeliverInvitation(Lobby + 1); else client.Cancel();
            transport.SucceedMembership(Lobby);
            Assert(client.View == null && !client.IsBusy && !service.ReconnectContext.IsPending
                && transport.Left.Count(id => id == Lobby) == 2, "late success after cancel/new invitation cannot pull player back");
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
        transport.Now = 15; client.AdvanceTo(15); service.Tick();
        transport.Now = 20; client.AdvanceTo(20); service.Tick();
        Assert(!service.TryReconnect(client) && transport.MembershipCalls == 2,
            "unsettled timed-out Steam operation prevents overlapping join calls");
        transport.Now = 45; client.AdvanceTo(45); service.Tick();
        Assert(service.RestartRequired && !service.ReconnectContext.IsPending && !service.TryReconnect(client),
            "unsettled native operation fails closed at forty-five seconds");
        transport.SucceedMembership(Lobby);
        Assert(client.View == null && transport.Left.Count(id => id == Lobby) == 2, "very late callback only cleans the native membership");
    }

    private static string Code => SteamRoomProtocol.Encode(Lobby);
    private static SteamRoomService Service(SteamRoomChecks.FakeTransport transport, RoomReconnectContext context = null)
        => new(transport, 10, skin => skin is 10 or 20, hat => hat is 30 or 40,
            reaction => reaction is 1001 or 1002, () => transport.Now, reconnect: context, appId: App);
    private static RoomClient Client(SteamRoomService service) => new(service, 1, "local", 10);
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Room reconnect check failed: " + message);
    }
}
#endif
