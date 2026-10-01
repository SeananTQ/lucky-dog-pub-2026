#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Steamworks;

namespace LuckyDogRise.Rooms;

// A deterministic transport seam exercises actual RoomClient/SteamRoomService state changes.
// These checks neither initialize Steam nor access inventory, settings or player saves.
public static class SteamRoomChecks
{
    public static void Run()
    {
        CheckCodes();
        CheckLateCreate();
        CheckDeadlineBeforeClientTick();
        CheckSerializedRetry();
        CheckFailedJoinKeepsOldRoom();
        CheckValidationAndPresence();
        CheckSearchAndRandomJoin();
        CheckOfflineAndSuspension();
        CheckMissingCallback();
        CheckDispose();
        CheckActivity();
        CheckChat();
        CheckChatFiltering();
        CheckSteamChatTextFilter();
        CheckKickProtocol();
        CheckKickAuthorityAndDelivery();
        CheckKickReception();
        CheckKickMetadataAndPendingRequests();
        CheckBanAdmissionAndLifetime();
        CheckBanMigrationAndCapacity();
        SteamRoomInviteInboxChecks.Run();
        CheckInvitingFriends();
        CheckIncomingInvitations();
        CheckInvitationAdmission();
        CheckInvitationLifetime();
        CheckInvitationRecovery();
    }

    private const ulong InviteLobby = 109775243348102719;

    private static void CheckInvitingFriends()
    {
        var transport = new FakeTransport();
        using var service = Service(transport);
        using var client = Client(service);
        Assert(service.InviteFriends(client) == "Rooms_InviteUnavailable" && transport.InviteDialogs.Count == 0,
            "an invitation dialog requires a joined room");
        client.Create("invite room"); transport.SucceedMembership(InviteLobby);
        Assert(service.InviteFriends(client) == "ok" && transport.InviteDialogs.SequenceEqual(new[] { InviteLobby }),
            "invitation dialog targets the currently joined lobby");

        using var staleClient = Client(service);
        Assert(service.InviteFriends(staleClient) == "Rooms_InviteUnavailable" && transport.InviteDialogs.Count == 1,
            "an unrelated client cannot invite through another client's membership");
        client.Search();
        Assert(service.InviteFriends(client) == "Rooms_InviteUnavailable" && transport.InviteDialogs.Count == 1,
            "busy clients cannot open an invitation dialog");
        client.Cancel(); transport.CompleteSearch();
        transport.InviteDialogSucceeds = false;
        Assert(service.InviteFriends(client) == "Rooms_InviteOverlayUnavailable"
            && client.JoinedCode == SteamRoomProtocol.Encode(InviteLobby),
            "an unavailable overlay reports a useful error without changing membership");
        transport.InviteDialogSucceeds = true;
        transport.SetRemote(InviteLobby, new SteamRoomMemberData(88, "new host", "1:10:0:1001"));
        transport.SetOwner(InviteLobby, 88);
        Assert(service.InviteFriends(client) == "ok", "a current non-host member can invite friends to the public room");
        var membership = transport.Rooms[InviteLobby];
        transport.Rooms[InviteLobby] = membership with { Members = Array.Empty<SteamRoomMemberData>(), Count = 0 };
        int opened = transport.InviteDialogs.Count;
        Assert(service.InviteFriends(client) == "Rooms_InviteUnavailable" && transport.InviteDialogs.Count == opened,
            "a stale displayed membership cannot open the overlay before its departure callback");
        transport.Rooms[InviteLobby] = membership;
        transport.IsAvailable = false;
        Assert(service.InviteFriends(client) == "Rooms_InviteUnavailable" && transport.InviteDialogs.Count == opened,
            "offline invitation attempts never call the overlay");
    }

    private static void CheckIncomingInvitations()
    {
        var transport = new FakeTransport();
        using var service = Service(transport);
        transport.DeliverInvitation(0);
        Assert(!service.TryTakeJoinRequest(out _), "invalid invite callbacks cannot become room requests");
        transport.DeliverInvitation(InviteLobby);
        transport.DeliverInvitation(InviteLobby);
        transport.DeliverInvitation(InviteLobby + 1);
        Assert(service.TryTakeJoinRequest(out var code) && code == SteamRoomProtocol.Encode(InviteLobby + 1)
            && !service.TryTakeJoinRequest(out _),
            "invites arriving before the client exists coalesce to one latest intent and consume once");

        using var client = Client(service);
        transport.Seed(InviteLobby + 1);
        client.Join(code);
        transport.DeliverInvitation(InviteLobby + 1);
        Assert(!service.TryTakeJoinRequest(out _), "duplicate invite during the same pending join does not restart it");
        transport.SucceedMembership(InviteLobby + 1);
        var session = client.Session;
        transport.DeliverInvitation(InviteLobby + 1);
        Assert(!service.TryTakeJoinRequest(out _) && client.Session == session,
            "accepting an invitation to the idle current room keeps its membership and transient state");

        transport.Seed(InviteLobby + 2);
        client.Join(SteamRoomProtocol.Encode(InviteLobby + 2));
        transport.DeliverInvitation(InviteLobby + 1);
        Assert(service.TryTakeJoinRequest(out code),
            "an invitation to the current room can cancel a different pending destination");
        client.Cancel(); client.Join(code);
        transport.SucceedMembership(InviteLobby + 2);
        Assert(client.JoinedCode == SteamRoomProtocol.Encode(InviteLobby + 1)
            && transport.Left.Contains(InviteLobby + 2) && !client.IsBusy,
            "cancelled native join is cleaned up without replacing the requested current room");

        client.FindAndJoin();
        transport.DeliverInvitation(InviteLobby + 2);
        Assert(service.TryTakeJoinRequest(out code), "an explicit invitation supersedes automatic room search");
        client.Cancel(); client.Join(code);
        transport.DeliverInvitation(InviteLobby + 2);
        Assert(!service.TryTakeJoinRequest(out _), "duplicate of a queued invitation join is ignored");
        transport.CompleteSearch(InviteLobby + 1);
        transport.SucceedMembership(InviteLobby + 2);
        Assert(client.JoinedCode == SteamRoomProtocol.Encode(InviteLobby + 2)
            && !service.TryTakeJoinRequest(out _), "the cancelled random search cannot override the invitation destination");
    }

    private static void CheckInvitationAdmission()
    {
        var transport = new FakeTransport();
        using var service = Service(transport);
        using var client = Client(service);
        client.Create("current room"); transport.SucceedMembership(InviteLobby);
        foreach (var failure in new[] { RoomFailure.Full, RoomFailure.NotFound })
        {
            transport.DeliverInvitation(InviteLobby + 1);
            Assert(service.TryTakeJoinRequest(out var code), "fresh explicit invitation can retry after an earlier failure");
            client.Cancel(); client.Join(code); transport.FailMembership(failure);
            Assert(client.Failure == failure && client.JoinedCode == SteamRoomProtocol.Encode(InviteLobby)
                && !transport.Left.Contains(InviteLobby) && !service.TryTakeJoinRequest(out _),
                "full or closed invited room keeps the existing room and does not repeat automatically");
        }
        transport.Seed(InviteLobby + 2);
        transport.Rooms[InviteLobby + 2] = transport.Rooms[InviteLobby + 2] with { Protocol = "incompatible-game" };
        transport.DeliverInvitation(InviteLobby + 2);
        Assert(service.TryTakeJoinRequest(out var incompatible), "incompatible invite enters normal admission checking");
        client.Cancel(); client.Join(incompatible); transport.SucceedMembership(InviteLobby + 2);
        Assert(client.Failure != RoomFailure.None && client.JoinedCode == SteamRoomProtocol.Encode(InviteLobby)
            && transport.Left.Contains(InviteLobby + 2), "invitation cannot bypass room protocol validation");

        transport.Seed(InviteLobby + 3);
        transport.SetBans(InviteLobby + 3, SteamRoomProtocol.EncodeBannedMembers(new ulong[] { 77 }), notify: false);
        transport.DeliverInvitation(InviteLobby + 3);
        Assert(service.TryTakeJoinRequest(out var banned), "banned destination still uses shared admission logic");
        client.Cancel(); client.Join(banned); transport.SucceedMembership(InviteLobby + 3);
        Assert(client.Failure == RoomFailure.Banned && client.JoinedCode == SteamRoomProtocol.Encode(InviteLobby)
            && transport.Left.Contains(InviteLobby + 3), "Steam invite is not an exemption from the room ban list");
        int calls = transport.MembershipCalls;
        transport.DeliverInvitation(InviteLobby + 3);
        Assert(service.TryTakeJoinRequest(out banned), "a new explicit banned-room invitation can report the failure");
        client.Cancel(); client.Join(banned);
        Assert(client.Failure == RoomFailure.Banned && transport.MembershipCalls == calls,
            "known banned room fails without another native join or rejoin loop");
    }

    private static void CheckInvitationLifetime()
    {
        var transport = new FakeTransport();
        using var service = Service(transport);
        using var client = Client(service);
        client.Create("leaving room"); transport.SucceedMembership(InviteLobby);
        transport.DeliverInvitation(InviteLobby + 1);
        client.Leave();
        Assert(!service.TryTakeJoinRequest(out _), "explicit leave cancels pending invitation intent");

        transport.Seed(InviteLobby + 2, new SteamRoomMemberData(88, "host", "1:10:0:1001"));
        transport.SetOwner(InviteLobby + 2, 88, notify: false);
        transport.DeliverInvitation(InviteLobby + 2);
        Assert(service.TryTakeJoinRequest(out var code), "fresh invitation after leaving can join another room");
        client.Join(code); transport.SucceedMembership(InviteLobby + 2);
        transport.DeliverInvitation(InviteLobby + 3);
        transport.SetBans(InviteLobby + 2, SteamRoomProtocol.EncodeBannedMembers(new ulong[] { 77 }));
        Assert(client.Failure == RoomFailure.Removed && client.JoinedCode == "" && !service.TryTakeJoinRequest(out _),
            "host removal clears both consumed and pending invitation paths instead of auto-joining elsewhere");
        service.Tick();
        Assert(!service.TryTakeJoinRequest(out _), "later callback pumps cannot revive a consumed invitation after removal");

        transport.DeliverInvitation(InviteLobby + 4);
        service.SetSuspended(true);
        transport.DeliverInvitation(InviteLobby + 5);
        service.SetSuspended(false);
        Assert(!service.TryTakeJoinRequest(out _), "Mock suspension clears old invitations and ignores new real Steam callbacks");
    }

    private static void CheckInvitationRecovery()
    {
        foreach (bool disposeBeforeClient in new[] { false, true })
        {
            var inbox = new SteamRoomInviteInbox();
            var transport = new FakeTransport();
            using var service = Service(transport, inbox);
            var client = Client(service);
            client.Create("room before reconnect"); transport.SucceedMembership(InviteLobby);
            transport.DeliverInvitation(InviteLobby + 1);
            if (disposeBeforeClient) service.Dispose();
            else { transport.IsAvailable = false; service.Tick(); }
            client.Dispose(); // The page disposes its old client after the platform becomes unavailable.
            Assert(!service.TryTakeJoinRequest(out _), "an unavailable adapter does not consume pending invitation intent");
            var recoveredTransport = new FakeTransport();
            using var recovered = Service(recoveredTransport, inbox);
            Assert(recovered.TryTakeJoinRequest(out var code) && code == SteamRoomProtocol.Encode(InviteLobby + 1)
                && !recovered.TryTakeJoinRequest(out _),
                "same-account recovery preserves an unconsumed invite through old client and service teardown");
            recovered.Dispose();
            using var nextRecovery = Service(new FakeTransport(), inbox);
            Assert(!nextRecovery.TryTakeJoinRequest(out _), "later recovery cannot replay an already consumed startup or callback invite");
        }
    }

    private static void CheckKickProtocol()
    {
        var ids = Enumerable.Range(1, SteamRoomProtocol.MaxBannedMembers).Select(id => (ulong)id).ToArray();
        Assert(SteamRoomProtocol.TryDecodeBannedMembers(SteamRoomProtocol.EncodeBannedMembers(ids), out var decoded)
            && decoded.SetEquals(ids), "ban metadata supports the documented maximum without losing identities");
        Assert(SteamRoomProtocol.TryDecodeBannedMembers("1:", out decoded) && decoded.Count == 0,
            "explicit empty ban metadata is valid");
        foreach (string invalid in new[] { null, "", "2:", "1:0", "1:-1", "1:0000000000000000",
            "1:0000000000000001,0000000000000001", "1:18446744073709551616", new string('1', 9000) })
            Assert(!SteamRoomProtocol.TryDecodeBannedMembers(invalid, out _), "malformed ban metadata is not an empty list");

        var membership = Guid.NewGuid();
        var packet = SteamRoomProtocol.EncodeKick(88, membership);
        Assert(packet.Length == 28 && SteamRoomProtocol.TryDecodeKick(packet, out var target, out var token)
            && target == 88 && token == membership, "management packet preserves target and membership token");
        Assert(!SteamRoomProtocol.TryDecodeChat(packet, out _, out _, out _, out _),
            "management packet never decodes as displayed chat");
        var invalidMagic = (byte[])packet.Clone(); invalidMagic[0] = (byte)'X';
        foreach (var invalid in new[] { null, Array.Empty<byte>(), packet[..^1], packet.Concat(new byte[] { 0 }).ToArray(), invalidMagic })
            Assert(!SteamRoomProtocol.TryDecodeKick(invalid, out _, out _), "malformed management packet rejected");
        var invalidTarget = (byte[])packet.Clone(); Array.Clear(invalidTarget, 4, 8);
        var invalidToken = (byte[])packet.Clone(); Array.Clear(invalidToken, 12, 16);
        Assert(!SteamRoomProtocol.TryDecodeKick(invalidTarget, out _, out _)
            && !SteamRoomProtocol.TryDecodeKick(invalidToken, out _, out _), "zero target and empty membership token rejected");
    }

    private static void CheckKickAuthorityAndDelivery()
    {
        var transport = new FakeTransport();
        using var service = Service(transport);
        using var client = Client(service);
        var token = Guid.NewGuid();
        transport.Seed(120, new SteamRoomMemberData(88, "guest", "1:10:0:1001", ChatSession: token.ToString("N")));
        client.Join(SteamRoomProtocol.Encode(120)); transport.SucceedMembership(120);
        var guest = client.View.Members.Single(member => member.Id != 1);
        var self = client.View.Members.Single(member => member.Id == 1);
        Assert(client.Kick(self.Id, self.Presence) == "Rooms_KickInvalidTarget", "owner cannot remove itself");
        Assert(client.Kick(guest.Id, guest.Presence + 1) == "Rooms_KickInvalidTarget"
            && client.Kick(999, guest.Presence) == "Rooms_KickInvalidTarget", "unknown and stale confirmation targets rejected");
        var original = transport.Rooms[120];
        transport.Rooms[120] = original with
        {
            Members = original.Members.Select(member => member.SteamId == 88
                ? member with { ChatSession = Guid.NewGuid().ToString("N") } : member).ToArray()
        };
        Assert(client.Kick(guest.Id, guest.Presence) == "Rooms_KickInvalidTarget" && transport.BanWrites.Count == 0,
            "same Steam user rejoining before the UI callback cannot be kicked through an old confirmation");
        transport.Rooms[120] = original with { Members = original.Members.Where(member => member.SteamId != 88).ToArray(), Count = 1 };
        Assert(client.Kick(guest.Id, guest.Presence) == "Rooms_KickInvalidTarget" && transport.BanWrites.Count == 0,
            "target departing before the UI callback cannot create a new ban from an obsolete row");
        transport.Rooms[120] = original;

        transport.SetOwner(120, 88, notify: false);
        Assert(client.View.OwnerId == 1 && client.Kick(guest.Id, guest.Presence) == "Rooms_KickNotOwner"
            && transport.BanWrites.Count == 0, "authority comes from current Steam owner, not stale displayed owner");
        transport.SetOwner(120, 77, notify: false);
        transport.BanWriteSucceeds = false;
        Assert(client.Kick(guest.Id, guest.Presence) == "Rooms_KickUnavailable" && transport.Chats.Count == 0
            && transport.Rooms[120].BannedMembers == "1:", "failed ban write cannot announce a completed kick");
        transport.BanWriteSucceeds = true;
        Assert(client.SendChat("owner is chatting") == "", "chat starts ordinary text cooldown");
        int filterCalls = transport.FilterCalls.Count;
        transport.ChatFilter = (_, _) => throw new InvalidOperationException("management must not use text filtering");
        transport.Events.Clear();
        Assert(client.Kick(guest.Id, guest.Presence) == "", "kick bypasses text cooldown and unavailable text filter");
        Assert(transport.FilterCalls.Count == filterCalls && transport.Events.SequenceEqual(new[] { "ban", "chat" }),
            "ban persistence precedes its independent management prompt");
        Assert(SteamRoomProtocol.TryDecodeBannedMembers(transport.Rooms[120].BannedMembers, out var banned)
            && banned.SetEquals(new ulong[] { 88 }), "ban uses Steam identity rather than local display slot");
        Assert(SteamRoomProtocol.TryDecodeKick(transport.Chats.Last(), out var target, out var membership)
            && target == 88 && membership == token, "kick prompt addresses the confirmed membership");

        // A failed/lost prompt must not undo a successful metadata update.
        transport.SetRemote(120, new SteamRoomMemberData(99, "second guest", "1:10:0:1001", ChatSession: Guid.NewGuid().ToString("N")));
        var second = client.View.Members.Single(member => member.Name == "second guest");
        transport.ChatSucceeds = false;
        Assert(client.Kick(second.Id, second.Presence) == ""
            && SteamRoomProtocol.TryDecodeBannedMembers(transport.Rooms[120].BannedMembers, out banned) && banned.Contains(99),
            "accepted ban survives best-effort prompt delivery failure");
    }

    private static void CheckKickReception()
    {
        var transport = new FakeTransport();
        using var service = Service(transport);
        using var client = Client(service);
        var ownerToken = Guid.NewGuid();
        transport.Seed(121,
            new SteamRoomMemberData(88, "first owner", "1:10:0:1001", ChatSession: ownerToken.ToString("N")),
            new SteamRoomMemberData(99, "next owner", "1:10:0:1001", ChatSession: Guid.NewGuid().ToString("N")));
        transport.SetOwner(121, 88, notify: false);
        client.Join(SteamRoomProtocol.Encode(121)); transport.SucceedMembership(121);
        var localToken = Guid.ParseExact(transport.ChatSessions[121], "N");
        var kick = SteamRoomProtocol.EncodeKick(77, localToken);
        transport.DeliverChat(121, 88, kick);
        Assert(client.JoinedCode.Length > 0, "a prompt alone cannot invent a ban missing from owner metadata");
        transport.DeliverChat(121, 88, SteamRoomProtocol.EncodeChat(ownerToken, 1, transport.ServerTime, "hello"));
        int filtered = transport.FilterCalls.Count;
        transport.ChatFilter = (_, _) => throw new InvalidOperationException("management is not text");
        transport.SetBans(121, SteamRoomProtocol.EncodeBannedMembers(new ulong[] { 77 }), notify: false);
        transport.DeliverChat(999, 88, kick);
        transport.DeliverChat(121, 88, SteamRoomProtocol.EncodeKick(99, localToken));
        transport.DeliverChat(121, 88, SteamRoomProtocol.EncodeKick(77, Guid.NewGuid()));
        transport.DeliverChat(121, 99, kick);
        Assert(client.JoinedCode.Length > 0 && transport.FilterCalls.Count == filtered,
            "wrong room, target, membership and non-owner commands cannot remove local player or call text filter");

        transport.SetOwner(121, 99, notify: false);
        transport.DeliverChat(121, 88, kick);
        Assert(client.JoinedCode.Length > 0, "old host's delayed packet loses authority immediately on migration");
        transport.DeliverChat(121, 99, kick);
        Assert(client.JoinedCode == "" && client.View == null && client.Bubbles.Count == 0
            && client.Failure == RoomFailure.Removed && transport.Left.SequenceEqual(new ulong[] { 121 }),
            "new current owner prompt removes membership even during text cooldown and filter outage");
        transport.DeliverChat(121, 99, kick);
        Assert(transport.Left.Count == 1 && transport.FilterCalls.Count == filtered,
            "duplicate removal is harmless and never displayed as chat");
    }

    private static void CheckKickMetadataAndPendingRequests()
    {
        foreach (bool pendingSearch in new[] { false, true })
        {
            var transport = new FakeTransport();
            using var service = Service(transport);
            using var client = Client(service);
            transport.Seed(122, new SteamRoomMemberData(88, "owner", "1:10:0:1001"));
            transport.SetOwner(122, 88, notify: false);
            transport.Seed(123);
            client.Join(SteamRoomProtocol.Encode(122)); transport.SucceedMembership(122);
            var oldToken = Guid.ParseExact(transport.ChatSessions[122], "N");
            if (pendingSearch) client.FindAndJoin();
            else client.Join(SteamRoomProtocol.Encode(123));
            transport.SetBans(122, SteamRoomProtocol.EncodeBannedMembers(new ulong[] { 77 }));
            Assert(client.JoinedCode == "" && !client.IsBusy && client.Failure == RoomFailure.Removed,
                "metadata callback alone removes player and cancels an in-flight room operation");
            transport.DeliverChat(122, 88, SteamRoomProtocol.EncodeKick(77, oldToken));
            if (pendingSearch)
            {
                transport.CompleteSearch(123);
                Assert(transport.MembershipCalls == 1, "late random-search result cannot auto-join after removal");
            }
            else
            {
                transport.SucceedMembership(123);
                Assert(transport.Left.Contains(123), "late successful join is cleaned after removal");
            }
            Assert(client.JoinedCode == "" && client.Failure == RoomFailure.Removed,
                "reordered prompt and late operation callbacks cannot resurrect a removed membership");

            client.Create("fresh room"); transport.SucceedMembership(124);
            Assert(client.JoinedCode == SteamRoomProtocol.Encode(124) && client.Failure == RoomFailure.None,
                "old room ban does not become a global ban on new rooms");
            transport.DeliverChat(122, 88, SteamRoomProtocol.EncodeKick(77, oldToken));
            Assert(client.JoinedCode == SteamRoomProtocol.Encode(124), "old lobby packet cannot remove new room membership");
        }
    }

    private static void CheckBanAdmissionAndLifetime()
    {
        var transport = new FakeTransport();
        using var service = Service(transport);
        using var client = Client(service);
        foreach (ulong lobby in new ulong[] { 125, 126, 127, 128, 129 }) transport.Seed(lobby);
        transport.SetBans(126, SteamRoomProtocol.EncodeBannedMembers(new ulong[] { 77 }), notify: false);
        transport.SetBans(127, "broken", notify: false);
        transport.Rooms[128] = transport.Rooms[128] with { Protocol = "lucky-dog-room-1" };
        transport.SetBans(129, "1:" + string.Join(",", Enumerable.Range(1, SteamRoomProtocol.MaxBannedMembers + 1)
            .Select(id => id.ToString("X16", System.Globalization.CultureInfo.InvariantCulture))), notify: false);
        client.Join(SteamRoomProtocol.Encode(125)); transport.SucceedMembership(125);
        long previous = client.Session;
        client.Search(); transport.CompleteSearch(125, 126, 127, 128, 129);
        Assert(client.Listings.Select(listing => listing.Code).SequenceEqual(new[] { SteamRoomProtocol.Encode(125) }),
            "search excludes locally banned, malformed, oversized and old-protocol rooms");

        client.Join(SteamRoomProtocol.Encode(126)); transport.SucceedMembership(126);
        Assert(client.Failure == RoomFailure.Banned && client.JoinedCode == SteamRoomProtocol.Encode(125)
            && client.Session == previous && transport.Left.Contains(126) && !transport.Left.Contains(125),
            "direct banned-room join cleans new membership and preserves previous room");
        int membershipCalls = transport.MembershipCalls;
        transport.SetBans(126, "1:", notify: false);
        client.Join(SteamRoomProtocol.Encode(126));
        Assert(client.Failure == RoomFailure.Banned && !client.IsBusy && transport.MembershipCalls == membershipCalls,
            "locally remembered removal rejects a direct retry before another Steam join, even with stale metadata");
        client.FindAndJoin(); transport.CompleteSearch(126);
        Assert(client.Failure == RoomFailure.NoMatchingRoom && !client.IsBusy
            && transport.MembershipCalls == membershipCalls && client.JoinedCode == SteamRoomProtocol.Encode(125),
            "stale search results cannot start a banned-room random-join loop");
        foreach (ulong invalidRoom in new ulong[] { 127, 128, 129 })
        {
            client.Join(SteamRoomProtocol.Encode(invalidRoom)); transport.SucceedMembership(invalidRoom);
            Assert(client.Failure != RoomFailure.None && client.JoinedCode == SteamRoomProtocol.Encode(125)
                && transport.Left.Contains(invalidRoom), "invalid room admission fails closed and cleans membership");
        }
        transport.SetBans(125, "malformed live metadata");
        Assert(client.JoinedCode == "", "live malformed ban state cannot silently disable moderation");
    }

    private static void CheckBanMigrationAndCapacity()
    {
        var transport = new FakeTransport();
        using var service = Service(transport);
        using var client = Client(service);
        transport.Seed(130,
            new SteamRoomMemberData(88, "old host", "1:10:0:1001", ChatSession: Guid.NewGuid().ToString("N")),
            new SteamRoomMemberData(99, "new target", "1:10:0:1001", ChatSession: Guid.NewGuid().ToString("N")));
        transport.SetOwner(130, 88, notify: false);
        transport.SetBans(130, SteamRoomProtocol.EncodeBannedMembers(new ulong[] { 55 }), notify: false);
        client.Join(SteamRoomProtocol.Encode(130)); transport.SucceedMembership(130);
        transport.SetOwner(130, 77);
        var guest = client.View.Members.Single(member => member.Name == "new target");
        Assert(client.Kick(guest.Id, guest.Presence) == ""
            && SteamRoomProtocol.TryDecodeBannedMembers(transport.Rooms[130].BannedMembers, out var banned)
            && banned.SetEquals(new ulong[] { 55, 99 }), "new owner retains previous owner's room bans");

        transport.SetBans(130, "1:");
        var oldHost = client.View.Members.Single(member => member.Name == "old host");
        Assert(client.Kick(oldHost.Id, oldHost.Presence) == ""
            && SteamRoomProtocol.TryDecodeBannedMembers(transport.Rooms[130].BannedMembers, out banned)
            && banned.SetEquals(new ulong[] { 55, 88, 99 }), "delayed metadata cannot erase bans already observed in this room");

        client.Leave();
        transport.Seed(131, new SteamRoomMemberData(88, "target in new room", "1:10:0:1001", ChatSession: Guid.NewGuid().ToString("N")));
        client.Join(SteamRoomProtocol.Encode(131)); transport.SucceedMembership(131);
        guest = client.View.Members.Single(member => member.Id != 1);
        transport.SetBans(131, SteamRoomProtocol.EncodeBannedMembers(
            Enumerable.Range(1000, SteamRoomProtocol.MaxBannedMembers).Select(id => (ulong)id)));
        int writes = transport.BanWrites.Count, prompts = transport.Chats.Count;
        Assert(client.Kick(guest.Id, guest.Presence) == "Rooms_KickListFull"
            && transport.BanWrites.Count == writes && transport.Chats.Count == prompts,
            "full room ban list fails visibly without evicting older entries or pretending removal succeeded");
    }

    private static void CheckChatFiltering()
    {
        // This is a display-adapter spy, not a substitute dictionary or a room Mock feature.
        var transport = new FakeTransport { ChatFilter = (_, _) => "display result" };
        using var service = Service(transport);
        using var client = Client(service);
        var token = Guid.NewGuid();
        transport.Seed(110, new SteamRoomMemberData(88, "guest", "1:10:0:1001",
            ChatSession: token.ToString("N")));
        client.Join(SteamRoomProtocol.Encode(110));
        transport.SucceedMembership(110);
        int guest = client.View.Members.Single(member => member.Id != 1).Id;
        byte[] Packet(long sequence, string text, long age = 0, Guid? session = null) =>
            SteamRoomProtocol.EncodeChat(session ?? token, sequence, transport.ServerTime - age, text);
        void Advance(double seconds) { transport.Now += seconds; client.AdvanceTo(transport.Now); }

        Assert(client.SendChat("wire original 🐶") == "", "filtered local chat sends");
        Assert(transport.FilterCalls.SequenceEqual(new[] { (77UL, "wire original 🐶") }),
            "local display uses actual local Steam identity and original text");
        Assert(client.Bubbles[1].Text == "display result", "local bubble uses returned display text");
        Assert(SteamRoomProtocol.TryDecodeChat(transport.Chats.Single(), out _, out _, out _, out var wire)
            && wire == "wire original 🐶", "wire retains original text for each recipient's filter preferences");

        var valid = Packet(1, "remote original");
        transport.DeliverChat(111, 88, valid);
        transport.DeliverChat(110, 999, valid);
        transport.DeliverChat(110, 77, transport.Chats[0]);
        transport.DeliverChat(110, 88, Packet(1, "wrong token", session: Guid.NewGuid()));
        transport.DeliverChat(110, 88, Packet(1, "expired", age: 7));
        transport.DeliverChat(110, 88, Packet(1, "future", age: -10));
        transport.DeliverChat(110, 88, new byte[4096]);
        var malformed = (byte[])valid.Clone(); malformed[^1] = 0xff;
        transport.DeliverChat(110, 88, malformed);
        Assert(transport.FilterCalls.Count == 1, "untrusted packets and self echoes rejected before filtering");

        transport.ChatFilter = (_, _) => "remote display";
        transport.DeliverChat(110, 88, valid);
        Assert(transport.FilterCalls.Last() == (88UL, "remote original")
            && client.Bubbles[guest].Text == "remote display", "remote bubble uses sender identity and filtered result");
        int callCount = transport.FilterCalls.Count;
        transport.DeliverChat(110, 88, Packet(2, "too fast"));
        Assert(transport.FilterCalls.Count == callCount, "receive rate limit runs before filtering");
        Advance(2);
        transport.ChatFilter = (_, _) => "duplicate must not replace bubble";
        transport.DeliverChat(110, 88, valid);
        Assert(client.Bubbles[guest].Text == "remote display", "duplicate cannot replace an existing bubble");
        Advance(7);

        transport.ChatFilter = (_, _) => throw new InvalidOperationException("injected filter failure");
        int sent = transport.Chats.Count;
        Assert(client.SendChat("do not leak local original") == "Rooms_ChatUnavailable"
            && transport.Chats.Count == sent && client.Bubbles.Count == 0,
            "local filter failure sends nothing and does not show unfiltered text");
        transport.DeliverChat(110, 88, Packet(3, "do not leak remote original"));
        Assert(client.Bubbles.Count == 0, "remote filter failure is contained and does not show unfiltered text");

        long nextSequence = 4;
        foreach (string invalid in new[] { null, "\ninvalid display" })
        {
            Advance(2);
            transport.ChatFilter = (_, _) => invalid;
            Assert(client.SendChat("invalid display must not leak local original") == "Rooms_ChatUnavailable"
                && transport.Chats.Count == sent && client.Bubbles.Count == 0,
                "invalid display results prevent sending and local echo");
            transport.DeliverChat(110, 88, Packet(nextSequence++, "invalid display must not leak remote original"));
            Assert(client.Bubbles.Count == 0, "invalid remote display results do not show unfiltered text");
        }

        transport.ChatFilter = (_, text) => text;
        Advance(2);
        Assert(client.SendChat("local recovered") == "" && client.Bubbles[1].Text == "local recovered",
            "local filtering can recover after adapter failure");
        transport.DeliverChat(110, 88, Packet(nextSequence++, "remote recovered"));
        Assert(client.Bubbles[guest].Text == "remote recovered", "receive filtering can recover after adapter failure");
        client.Leave();
        callCount = transport.FilterCalls.Count;
        transport.DeliverChat(110, 88, Packet(nextSequence, "after leaving"));
        Assert(transport.FilterCalls.Count == callCount, "departed-room packets do not call the filter");
    }

    private static void CheckSteamChatTextFilter()
    {
        const ulong senderId = 76561198000000077UL;
        int initialized = 0, filtered = 0;
        uint options = uint.MaxValue, capacity = 0;
        ETextFilteringContext context = ETextFilteringContext.k_ETextFilteringContextUnknown;
        ulong actualSender = 0;
        string actualInput = "";
        int PassThrough(ETextFilteringContext value, CSteamID sender, string input, out string output, uint bytes)
        {
            filtered++;
            context = value; actualSender = sender.m_SteamID; actualInput = input; capacity = bytes;
            output = input;
            return 0;
        }
        var filter = new SteamChatTextFilter(value => { initialized++; options = value; return true; }, PassThrough);
        const string unicode = "中文 🐶 hello";
        Assert(filter.Filter(senderId, unicode) == unicode && filtered == 1,
            "zero filtered characters is a successful pass-through result");
        Assert(options == 0 && context == ETextFilteringContext.k_ETextFilteringContextChat
            && actualSender == senderId && actualInput == unicode,
            "SDK seam receives reserved zero, Chat context, original text and actual Steam identity");
        Assert(capacity >= Encoding.UTF8.GetByteCount(unicode) + 1,
            "UTF-8 buffer includes multibyte characters and terminating zero");
        filter.Filter(senderId, new string('a', RoomRules.MaxChatCharacters));
        Assert(initialized == 1 && capacity >= RoomRules.MaxChatCharacters * 3 + 1,
            "initialization is once per instance and ASCII replacement buffer permits three-byte masking characters");

        int unavailableInitializations = 0;
        int beforeUnavailable = filtered;
        var unavailable = new SteamChatTextFilter(_ => { unavailableInitializations++; return false; }, PassThrough);
        Assert(unavailable.Filter(senderId, unicode) == unicode && unavailable.Filter(senderId, "hello") == "hello"
            && unavailableInitializations == 1 && filtered == beforeUnavailable + 2,
            "unavailable dictionaries still call Steam pass-through without init loops");

        int retryInitializations = 0;
        bool RetryInitialization(uint _)
        {
            retryInitializations++;
            if (retryInitializations == 1) throw new InvalidOperationException("injected initialization failure");
            return true;
        }
        var retry = new SteamChatTextFilter(RetryInitialization, PassThrough);
        AssertFilterThrows(() => retry.Filter(senderId, "first"), "initialization exceptions are surfaced");
        Assert(retry.Filter(senderId, "retry") == "retry" && retryInitializations == 2,
            "a failed initialization can retry on the next message");
        var replacementSession = new SteamChatTextFilter(RetryInitialization, PassThrough);
        replacementSession.Filter(senderId, "new session");
        Assert(retryInitializations == 3, "a replacement Steam session initializes its own filter");

        string result = new string('♥', RoomRules.MaxChatCharacters);
        int resultCount = RoomRules.MaxChatCharacters;
        var outputCheck = new SteamChatTextFilter(_ => true,
            (ETextFilteringContext _, CSteamID _, string _, out string output, uint _) =>
            { output = result; return resultCount; });
        Assert(outputCheck.Filter(senderId, new string('a', RoomRules.MaxChatCharacters)) == result,
            "full-length multibyte masking output remains valid chat");
        result = "valid"; resultCount = -1;
        AssertFilterThrows(() => outputCheck.Filter(senderId, "hello"), "negative SDK result rejected");
        resultCount = 0;
        foreach (string invalid in new[] { null, "", "\nhello", new string('字', RoomRules.MaxChatCharacters + 1), "\uD800" })
        {
            result = invalid;
            AssertFilterThrows(() => outputCheck.Filter(senderId, "hello"), "invalid SDK output rejected");
        }
    }

    private static void AssertFilterThrows(Action action, string message)
    {
        bool threw = false;
        try { action(); }
        catch (InvalidOperationException) { threw = true; }
        Assert(threw, message);
    }

    private static void CheckChat()
    {
        var transport = new FakeTransport();
        using var service = Service(transport);
        using var client = Client(service);
        var token = Guid.NewGuid();
        var remote = new SteamRoomMemberData(88, "guest", "1:10:0:1001", ChatSession: token.ToString("N"));
        transport.Seed(100, remote);
        client.Join(SteamRoomProtocol.Encode(100));
        transport.SucceedMembership(100);
        var guest = client.View.Members.Single(m => m.Id != 1);
        byte[] Packet(long seq, string value, long age = 0, Guid? session = null) =>
            SteamRoomProtocol.EncodeChat(session ?? token, seq, transport.ServerTime - age, value);
        void Deliver(byte[] bytes, ulong lobby = 100, ulong sender = 88) => transport.DeliverChat(lobby, sender, bytes);
        void Advance(double seconds) { transport.Now += seconds; client.AdvanceTo(transport.Now); }

        Assert(client.SendChat(" 你好 🐶 : hello ") == "", "UTF-8 chat sends and echoes locally");
        Assert(client.Bubbles[1].Text == "你好 🐶 : hello", "local whitespace trimmed");
        Assert(SteamRoomProtocol.TryDecodeChat(transport.Chats[0], out _, out _, out _, out var wireText)
            && wireText == client.Bubbles[1].Text, "binary envelope round trip");
        Deliver(transport.Chats[0], sender: 77);
        Assert(client.Bubbles.Count == 1, "Steam self echo ignored");
        Assert(client.SendChat("flood") == "Rooms_ChatTooFast" && transport.Chats.Count == 1, "sender rate limit");
        foreach (var invalid in new[] { " ", "\nhello", new string('字', 121), "\uD800" })
            Assert(client.SendChat(invalid).Length > 0, "outgoing invalid text blocked");

        var valid = Packet(1, "来自另一只狗 🐕");
        Deliver(valid, lobby: 200); Deliver(valid, sender: 999);
        Deliver(Packet(1, "wrong incarnation", session: Guid.NewGuid()));
        Deliver(Packet(1, "expired", age: 7)); Deliver(Packet(1, "future", age: -10));
        Deliver(new byte[4096]);
        var malformed = (byte[])valid.Clone(); malformed[^1] = 0xff; Deliver(malformed);
        Assert(client.Bubbles.Count == 1, "wrong room/sender/token/time/encoding rejected");
        Deliver(valid);
        Assert(client.Bubbles[guest.Id].Text == "来自另一只狗 🐕", "remote message delivered");
        Deliver(Packet(2, "flood"));
        Assert(client.Bubbles[guest.Id].Id == 1, "receiver rate limit independent of sender");
        Advance(2); Deliver(valid);
        Assert(client.Bubbles[guest.Id].Id == 1, "duplicate ignored");
        Advance(2); Deliver(Packet(3, "latest"));
        Assert(client.Bubbles.Count == 2 && client.Bubbles[guest.Id].Text == "latest", "one bubble per sender");
        Advance(7);
        Assert(client.Bubbles.Count == 0, "all messages expire");
        transport.ChatSucceeds = false;
        Assert(client.SendChat("not sent") == "Rooms_ChatUnavailable" && client.Bubbles.Count == 0,
            "failed transport does not fake successful delivery");
        Advance(2); transport.ChatSucceeds = true;
        Assert(client.SendChat("retry") == "", "failure can retry");

        transport.RemoveRemote(100, 88);
        var oldToken = token; token = Guid.NewGuid();
        transport.SetRemote(100, remote with { ChatSession = token.ToString("N") });
        Deliver(Packet(99, "old member", session: oldToken));
        Assert(client.Bubbles.Count == 1, "departed incarnation cannot inject a delayed message");
        Deliver(Packet(1, "rejoined"));
        Assert(client.Bubbles.Count == 2, "new membership can start sequence at one");
        client.Leave();
        Deliver(Packet(2, "after leaving"));
        Assert(client.Bubbles.Count == 0, "leave clears messages and rejects callbacks");
        client.Join(SteamRoomProtocol.Encode(100)); transport.SucceedMembership(100);
        Deliver(Packet(3, "before rejoin", age: 2));
        Assert(client.Bubbles.Count == 0, "messages predating this join rejected");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Steam room check failed: " + message);
    }

    private static SteamRoomService Service(FakeTransport transport, SteamRoomInviteInbox invitations = null) => new(transport, 10,
        skin => skin is 10 or 20, hat => hat is 30 or 40, reaction => reaction is 1001 or 1002,
        () => transport.Now, invitations);
    private static RoomClient Client(SteamRoomService service) => new(service, 1, "local", 10);

    private static void CheckCodes()
    {
        foreach (var id in new ulong[] { 1, 31, 32, 109775242000000000, ulong.MaxValue })
        {
            var code = SteamRoomProtocol.Encode(id);
            Assert(SteamRoomProtocol.TryDecode(code.ToLowerInvariant(), out var decoded) && decoded == id,
                "room code must round-trip every 64-bit id");
        }
        Assert(!SteamRoomProtocol.TryDecode("LD-Z000000000000", out _), "overflow rejected");
        Assert(!SteamRoomProtocol.TryDecode("LD-0000000000000", out _), "zero rejected");
        Assert(!SteamRoomProtocol.TryDecode("LD-000000000000I", out _), "invalid alphabet rejected");
        Assert(!SteamRoomProtocol.TryDecodeAppearance("1:10:-30:1001", out _, out _, out _), "negative item rejected");
        Assert(!SteamRoomProtocol.TryDecodeAppearance(new string('1', 500), out _, out _, out _), "oversize metadata rejected");
    }

    private static void CheckLateCreate()
    {
        var transport = new FakeTransport();
        using var service = Service(transport);
        using var client = Client(service);
        client.Create("room");
        client.AdvanceTo(RoomRules.RequestTimeout);
        Assert(client.RequestState == RoomRequestState.TimedOut, "request timeout visible");
        transport.SucceedMembership(101);
        Assert(client.JoinedCode == "" && client.View == null, "late create cannot commit");
        Assert(transport.Left.SequenceEqual(new ulong[] { 101 }), "late create membership cleaned");
        Assert(!transport.RequestHandles[0].DisposedBeforeCallback, "cancel retains native callback");
    }

    private static void CheckSerializedRetry()
    {
        var transport = new FakeTransport();
        using var service = Service(transport);
        using var client = Client(service);
        transport.Seed(201);
        client.Join(SteamRoomProtocol.Encode(201));
        client.Cancel();
        client.Join(SteamRoomProtocol.Encode(201));
        Assert(transport.MembershipCalls == 1, "same-lobby retry must await stale operation");
        transport.SucceedMembership(201);
        Assert(transport.Left.SequenceEqual(new ulong[] { 201 }) && transport.MembershipCalls == 2,
            "cleanup precedes retry launch");
        transport.SucceedMembership(201);
        Assert(client.JoinedCode == SteamRoomProtocol.Encode(201) && client.View.Members.Length == 1,
            "new same-lobby membership survives stale cleanup");
        Assert(transport.Left.Count == 1, "new membership was not evicted");
    }

    private static void CheckDeadlineBeforeClientTick()
    {
        var transport = new FakeTransport();
        using var service = Service(transport);
        using var client = Client(service);
        client.Create("callback pump runs first");
        transport.Now = 15;
        transport.SucceedMembership(202);
        Assert(client.JoinedCode == "" && transport.Left.Contains(202),
            "expired result cannot commit before the UI client clock advances");
        client.AdvanceTo(15);
        Assert(client.RequestState == RoomRequestState.TimedOut, "normal UI timeout remains visible");
    }

    private static void CheckFailedJoinKeepsOldRoom()
    {
        var transport = new FakeTransport();
        using var service = Service(transport);
        using var client = Client(service);
        client.Create("old");
        transport.SucceedMembership(301);
        var session = client.Session;
        client.Join(SteamRoomProtocol.Encode(302));
        transport.FailMembership(RoomFailure.Full);
        Assert(client.Failure == RoomFailure.Full && client.JoinedCode == SteamRoomProtocol.Encode(301)
            && client.Session == session && transport.Left.Count == 0, "failed switch retains existing room");
        client.Create("bad metadata");
        transport.InitializeSucceeds = false;
        transport.SucceedMembership(303);
        Assert(client.JoinedCode == SteamRoomProtocol.Encode(301) && transport.Left.Contains(303),
            "failed metadata initialization cleans new room only");
    }

    private static void CheckValidationAndPresence()
    {
        var transport = new FakeTransport();
        using var service = Service(transport);
        using var client = Client(service);
        transport.Seed(401, new SteamRoomMemberData(88, "remote\nname", "1:999999:999999:999999"));
        client.Join(SteamRoomProtocol.Encode(401));
        transport.SucceedMembership(401);
        var remote = client.View.Members.Single(member => member.Id != 1);
        Assert(remote.SkinId == 10 && remote.HeadwearId == 0 && remote.Reaction == 1001,
            "untrusted metadata uses valid local defaults");
        Assert(remote.Name == "remotename", "control characters removed from nickname");
        transport.SetRemote(401, new SteamRoomMemberData(88, "new name", "1:20:30:1002"));
        var updated = client.View.Members.Single(member => member.Id != 1);
        Assert(updated.SkinId == 20 && updated.HeadwearId == 30 && updated.Reaction == 1002
            && updated.Presence == remote.Presence, "remote appearance updates retain identity");
        transport.RemoveRemote(401, 88);
        Assert(client.View.Members.Length == 1, "leave removes visible dog");
        transport.SetRemote(401, new SteamRoomMemberData(88, "back", "1:10:0:1001"));
        Assert(client.View.Members.Single(member => member.Id != 1).Presence != remote.Presence,
            "rejoined Steam identity has new presence");
        client.SetAppearance(20, 40, 1002);
        Assert(transport.AppearanceWrites.Last() == "1:20:40:1002"
            && client.View.Members.Single(member => member.Id == 1).SkinId == 20, "own appearance synchronized");
    }

    private static void CheckSearchAndRandomJoin()
    {
        var transport = new FakeTransport();
        using var service = Service(transport);
        using var client = Client(service);
        transport.Seed(501);
        transport.Seed(502);
        transport.Rooms[502] = transport.Rooms[502] with { Protocol = "future-incompatible" };
        client.FindAndJoin();
        transport.CompleteSearch(501, 502);
        Assert(client.Operation == RoomOperation.Join && transport.MembershipCalls == 1,
            "search can synchronously chain a compatible join");
        transport.SucceedMembership(501);
        Assert(client.JoinedCode == SteamRoomProtocol.Encode(501), "random join succeeds");
    }

    private static void CheckOfflineAndSuspension()
    {
        var transport = new FakeTransport();
        using var service = Service(transport);
        using var client = Client(service);
        client.Create("room");
        transport.SucceedMembership(601);
        client.Create("pending");
        transport.IsAvailable = false;
        service.Tick();
        Assert(client.JoinedCode == "" && client.Failure == RoomFailure.Unavailable && !client.IsBusy,
            "disconnect clears members and pending request");
        transport.IsAvailable = true;
        transport.SucceedMembership(602);
        Assert(transport.Left.Contains(602) && client.JoinedCode == "", "late offline success cleaned after reconnect");
        client.Create("before mock");
        service.SetSuspended(true);
        transport.SucceedMembership(603);
        Assert(!service.IsAvailable && transport.Left.Contains(603), "Mock switch blocks late membership");
        service.SetSuspended(false);
        Assert(service.IsAvailable, "real mode can resume without changing Steam runtime");
    }

    private static void CheckMissingCallback()
    {
        var transport = new FakeTransport();
        using var service = Service(transport);
        using var client = Client(service);
        client.Create("missing callback");
        client.AdvanceTo(15);
        client.Create("queued retry");
        transport.Now = 46;
        service.Tick();
        Assert(!service.IsAvailable && client.Failure == RoomFailure.Unavailable && !client.IsBusy,
            "missing native callback has bounded visible failure");
        transport.SucceedMembership(701);
        Assert(service.IsAvailable && transport.Left.Contains(701) && transport.MembershipCalls == 1,
            "late settlement cleans membership and permits recovery");
    }

    private static void CheckDispose()
    {
        var transport = new FakeTransport();
        var service = Service(transport);
        using var client = Client(service);
        client.Create("dispose");
        transport.SucceedMembership(801);
        service.Dispose();
        Assert(client.JoinedCode == "" && transport.Left.Contains(801) && transport.Disposed,
            "service teardown leaves room before transport disposal");
    }

    private static void CheckActivity()
    {
        Assert(SteamRoomProtocol.TryDecodeActivity("1:42:1", out var sequence, out var active)
            && sequence == 42 && active, "activity metadata round trip");
        foreach (var invalid in new[] { "", "1:-1:1", "1:2:5", "2:1:1", "1:9223372036854775808:1", new string('1', 100) })
            Assert(!SteamRoomProtocol.TryDecodeActivity(invalid, out _, out _), "invalid activity rejected");
        var transport = new FakeTransport();
        using var service = Service(transport);
        using var client = Client(service);
        transport.Seed(901, new SteamRoomMemberData(88, "remote", "1:10:0:1001"));
        client.Join(SteamRoomProtocol.Encode(901));
        transport.SucceedMembership(901);
        int remoteId = client.View.Members.Single(m => m.Id != 1).Id;
        Assert(!client.IsTongueActive(remoteId), "older clients without activity metadata remain idle");
        transport.SetRemote(901, new SteamRoomMemberData(88, "remote", "1:20:30:1002", "1:1:1"));
        Assert(client.IsTongueActive(remoteId), "Steam metadata activates remote tongue");
        client.AdvanceTo(4);
        transport.SetRemote(901, new SteamRoomMemberData(88, "renamed", "1:20:40:1002", "1:1:1"));
        Assert(!client.IsTongueActive(remoteId), "cached activity cannot renew on appearance callback");
        transport.SetRemote(901, new SteamRoomMemberData(88, "remote", "1:20:40:1002", "1:2:1"));
        Assert(client.IsTongueActive(remoteId), "new sequence resumes animation");
        transport.SetRemote(901, new SteamRoomMemberData(88, "remote", "1:20:40:1002", "1:3:0"));
        transport.SetRemote(901, new SteamRoomMemberData(88, "remote", "1:20:40:1002", "1:2:1"));
        Assert(!client.IsTongueActive(remoteId), "older active sequence cannot undo stop");

        var writes = transport.ActivityWrites.Count;
        var appearanceWrites = transport.AppearanceWrites.Count;
        client.NotifyInputActivity();
        for (int i = 0; i < 1000; i++) client.NotifyInputActivity();
        Assert(transport.ActivityWrites.Count == writes + 1, "rapid inputs publish one start, not 1001 events");
        for (int i = 1; i <= 30; i++)
        {
            client.NotifyInputActivity();
            client.AdvanceTo(4 + i * 0.1);
        }
        Assert(transport.ActivityWrites.Count <= writes + 4 && transport.AppearanceWrites.Count == appearanceWrites,
            "ongoing input has bounded renewals and does not resend hats");
        client.AdvanceTo(8);
        Assert(!client.TongueActive && transport.ActivityWrites.Last().EndsWith(":0"), "final idle is published without more input");
        client.SetReaction(1002);
        client.SetAppearance(20, 40, client.Reaction);
        Assert(transport.AppearanceWrites.Last() == "1:20:40:1002", "changing outfit retains activity reaction");
        client.NotifyInputActivity();
        transport.IsAvailable = false;
        service.Tick();
        Assert(!client.TongueActive && client.View == null && !client.IsTongueActive(remoteId), "disconnect clears transient state");
    }

    private sealed class FakeHandle : IDisposable
    {
        public bool CallbackDelivered;
        public bool DisposedBeforeCallback;
        public void Dispose() { if (!CallbackDelivered) DisposedBeforeCallback = true; }
    }

    private sealed class FakeTransport : ISteamRoomTransport
    {
        private readonly Queue<(Action<SteamRoomJoinResult> Callback, FakeHandle Handle)> _membership = new();
        private Action<SteamRoomSearchResult> _search;
        private FakeHandle _searchHandle;
        public ulong LocalSteamId => 77;
        public bool IsAvailable { get; set; } = true;
        public bool InitializeSucceeds = true;
        public bool Disposed;
        public double Now;
        public long ServerTime => 100000 + (long)Now;
        public event Action<ulong, ulong, byte[]> ChatReceived = delegate { };
        public readonly List<byte[]> Chats = new();
        public readonly List<string> Events = new();
        public readonly Dictionary<ulong, string> ChatSessions = new();
        public readonly List<(ulong Lobby, string Members)> BanWrites = new();
        public bool BanWriteSucceeds = true;
        public bool ChatSucceeds = true;
        public bool InviteDialogSucceeds = true;
        public readonly List<ulong> InviteDialogs = new();
        public event Action<ulong> JoinRequested = delegate { };
        public void DeliverInvitation(ulong lobby) => JoinRequested(lobby);
        public bool OpenInviteDialog(ulong lobbyId)
        {
            InviteDialogs.Add(lobbyId);
            return InviteDialogSucceeds;
        }
        public Func<ulong, string, string> ChatFilter = (_, text) => text;
        public readonly List<(ulong Sender, string Text)> FilterCalls = new();
        public string FilterChatForDisplay(ulong senderSteamId, string text)
        {
            FilterCalls.Add((senderSteamId, text));
            return ChatFilter(senderSteamId, text);
        }
        public void SetChatSession(ulong lobbyId, string session) => ChatSessions[lobbyId] = session;
        public bool SendChat(ulong lobbyId, byte[] message)
        {
            Events.Add("chat");
            if (!ChatSucceeds) return false;
            Chats.Add(message);
            return true;
        }
        public bool SetBannedMembers(ulong lobbyId, string members)
        {
            if (!BanWriteSucceeds) return false;
            BanWrites.Add((lobbyId, members));
            Events.Add("ban");
            SetBans(lobbyId, members, notify: false);
            return true;
        }
        public void DeliverChat(ulong lobby, ulong sender, byte[] message) => ChatReceived(lobby, sender, message);
        public int MembershipCalls;
        public readonly Dictionary<ulong, SteamRoomData> Rooms = new();
        public readonly List<ulong> Left = new();
        public readonly List<string> AppearanceWrites = new();
        public readonly List<string> ActivityWrites = new();
        public readonly List<FakeHandle> RequestHandles = new();
        public event Action<ulong> LobbyChanged = delegate { };
        public event Action<ulong, ulong> MemberDeparted = delegate { };

        public void Seed(ulong lobby, params SteamRoomMemberData[] other)
        {
            var members = new[] { new SteamRoomMemberData(77, "local Steam name", "1:10:0:1001") }.Concat(other).ToArray();
            Rooms[lobby] = new SteamRoomData(lobby, SteamRoomProtocol.Version, "room", "social", 77,
                members.Length, 6, members);
        }
        public IDisposable Create(Action<SteamRoomJoinResult> completed) => Join(0, completed);
        public IDisposable Join(ulong lobbyId, Action<SteamRoomJoinResult> completed)
        {
            MembershipCalls++;
            var handle = new FakeHandle();
            RequestHandles.Add(handle);
            _membership.Enqueue((completed, handle));
            return handle;
        }
        public IDisposable Search(Action<SteamRoomSearchResult> completed)
        {
            _search = completed;
            _searchHandle = new FakeHandle();
            return _searchHandle;
        }
        public void CompleteSearch(params ulong[] ids)
        {
            _searchHandle.CallbackDelivered = true;
            _search(new SteamRoomSearchResult(RoomFailure.None, ids));
        }
        public void SucceedMembership(ulong lobby)
        {
            if (!Rooms.ContainsKey(lobby)) Seed(lobby);
            var request = _membership.Dequeue();
            request.Handle.CallbackDelivered = true;
            request.Callback(new SteamRoomJoinResult(RoomFailure.None, lobby));
        }
        public void FailMembership(RoomFailure failure)
        {
            var request = _membership.Dequeue();
            request.Handle.CallbackDelivered = true;
            request.Callback(new SteamRoomJoinResult(failure, 0));
        }
        public SteamRoomData ReadLobby(ulong lobbyId, bool includeMembers) => Rooms.GetValueOrDefault(lobbyId);
        public bool InitializeLobby(ulong lobbyId, string name)
        {
            if (!InitializeSucceeds) return false;
            Rooms[lobbyId] = Rooms[lobbyId] with { Name = name };
            return true;
        }
        public bool SetGame(ulong lobbyId, string game)
        {
            Rooms[lobbyId] = Rooms[lobbyId] with { Game = game };
            return true;
        }
        public void SetAppearance(ulong lobbyId, string appearance) => AppearanceWrites.Add(appearance);
        public void SetActivity(ulong lobbyId, string activity) => ActivityWrites.Add(activity);
        public void Leave(ulong lobbyId) => Left.Add(lobbyId);
        public void Dispose() => Disposed = true;
        public void SetBans(ulong lobbyId, string value, bool notify = true)
        {
            Rooms[lobbyId] = Rooms[lobbyId] with { BannedMembers = value };
            if (notify) LobbyChanged(lobbyId);
        }
        public void SetOwner(ulong lobbyId, ulong owner, bool notify = true)
        {
            Rooms[lobbyId] = Rooms[lobbyId] with { OwnerId = owner };
            if (notify) LobbyChanged(lobbyId);
        }
        public void SetRemote(ulong lobbyId, SteamRoomMemberData member)
        {
            var data = Rooms[lobbyId];
            var members = data.Members.Where(value => value.SteamId != member.SteamId).Append(member).ToArray();
            Rooms[lobbyId] = data with { Members = members, Count = members.Length };
            LobbyChanged(lobbyId);
        }
        public void RemoveRemote(ulong lobbyId, ulong member)
        {
            var data = Rooms[lobbyId];
            var members = data.Members.Where(value => value.SteamId != member).ToArray();
            Rooms[lobbyId] = data with { Members = members, Count = members.Length };
            MemberDeparted(lobbyId, member);
            LobbyChanged(lobbyId);
        }
    }
}
#endif
