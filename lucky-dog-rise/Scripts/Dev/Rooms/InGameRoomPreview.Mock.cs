#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LuckyDogRise.Rooms;

// Development-only local clients. An unavailable real Steam session never
// selects this path; it requires the explicit launcher sandbox or offline flag.
public partial class InGameRoomPreview
{
    private RoomSandbox _sandbox;
    private readonly List<RoomClient> _mockClients = new();
    private readonly List<Translation> _mockTranslations = new();
    private int _pendingPokerMockHost;

    partial void InitializeMock()
    {
        if (_gameData?.IsSteamMockSimulationActive != true
            && !OS.GetCmdlineUserArgs().Contains(GamePlatformServiceFactory.DisableSteamArgument)) return;
        foreach (var locale in new[] { "en", "zh_CN", "zh_TW" })
        {
            var translation = GD.Load<Translation>($"res://Scenes/Dev/Rooms/InGameRoomMockText.{locale}.translation");
            TranslationServer.AddTranslation(translation);
            _mockTranslations.Add(translation);
        }
        _isMock = true;
        _sandbox = new RoomSandbox { PokerSimulationLocalId = 1 };
        var skins = LubanData.Tables.TbDogSkin;
        var skin = skins.DataList[0].Id;
        var client = _sandbox.AddClient(1, L10n.Tr("Rooms_You"), skin,
            () => BuildCapabilities.CanInitiateMischiefPoker);
        // Distinct table-defined coats make the two test companions easier to follow.
        var host = _sandbox.AddClient(2, L10n.Tr("Rooms_MockPlayer2"), skins.GetOrDefault(1002)?.Id ?? skin); // 黑柴
        var guest = _sandbox.AddClient(3, L10n.Tr("Rooms_MockPlayer3"), skins.GetOrDefault(1003)?.Id ?? skin); // 白柴
        _mockClients.AddRange(new[] { client, host, guest });
        _sandbox.Create(host, L10n.Tr("Rooms_MockRoom"));
        _sandbox.Join(guest, host.JoinedCode);
        // Ordinary creation stays owner-only, matching Steam. Companion rooms
        // are entered through the shared local arrival flow.
        _sandbox.Settings(client).RequestDelay = 0.35;
        ReplaceClient(client);
        client.Search();
    }

    partial void TickMock(double delta)
    {
        _sandbox?.Tick(delta);
        if (_pendingPokerMockHost == 0) return;
        if (!BuildCapabilities.CanInitiateMischiefPoker) { _pendingPokerMockHost = 0; return; }
        var owner = MockClientForSmoke(_pendingPokerMockHost);
        if (owner.View?.GameId == RoomRules.MischiefPoker) { _pendingPokerMockHost = 0; return; }
        if (owner.View?.Members.Length != 3) return;
        if (owner.View.GameChange == null) owner.ProposeGameChange(RoomRules.MischiefPoker);
        if (owner.View.GameChange is not { } proposal) return;
        foreach (var simulated in _mockClients.Where(c => c.Id != _client.Id && c.Id != owner.Id))
            if (simulated.View?.GameChange?.Id == proposal.Id) simulated.RespondGameChange(proposal.Id, true);
        if (owner.CanConfirmGameChange) owner.ConfirmGameChange(proposal.Id);
    }
    public void PrepareNetworkPokerMock(bool localHost)
    {
        if (_sandbox == null || !BuildCapabilities.CanInitiateMischiefPoker) return;
        _sandbox.SetPokerOffline(2, false);
        foreach (var client in _mockClients) client.Leave();
        var owner = localHost ? _client : MockClientForSmoke(2);
        _sandbox.Create(owner, "捣乱德州 · 联机模拟");
        foreach (var client in new[] { _client, MockClientForSmoke(2), MockClientForSmoke(3) }.Where(c => c != owner))
            _sandbox.Join(client, owner.JoinedCode);
        _pendingPokerMockHost = owner.Id;
    }
    public void SetNetworkPokerMockChat(bool enabled)
    { if (_sandbox != null) _sandbox.PokerSettlementChatEnabled = enabled; }
    public void TestNetworkPokerRecovery(int action)
    {
        if (_sandbox == null) return;
        var other = MockClientForSmoke(2);
        switch (action)
        {
            case 0: _sandbox.SetPokerOffline(2, true); break;
            case 1: _sandbox.SetPokerOffline(2, false); break;
            case 2: other.Leave(); break;
            case 3:
                if (_client.View != null && other.View == null)
                {
                    _sandbox.GrantInvitation(_client.JoinedCode, other);
                    other.Join(_client.JoinedCode);
                }
                break;
            case 4: _sandbox.PokerIdlePlayerId = 2; break;
            case 5: _sandbox.PokerIdlePlayerId = 0; break;
        }
    }
    partial void RenderMockNotice()
    {
        if (_isMock) _connection.Text = L10n.Tr("Rooms_MockNotice");
    }
    partial void DisposeMock()
    {
        _sandbox?.DisposePokerSimulation();
        foreach (var client in _mockClients) client.Dispose();
        foreach (var translation in _mockTranslations) TranslationServer.RemoveTranslation(translation);
    }
    public void AdvancePreview(double seconds) => _sandbox?.Tick(seconds);
    // Seed a pre-existing legacy roster only for broad migration/interaction
    // checks. The real Create button still exercises an owner-only request.
    public void SeedLegacyCompanionRoomForSmoke(string name, RoomClient owner = null)
    {
        owner ??= _client;
        _sandbox.Create(owner, name, RoomCompanionPlan.Create(
            new[] { owner.SkinId }, new[] { owner.HeadwearId }, owner.CompanionTime));
    }
    public RoomClient MockClientForSmoke(int id) => _mockClients.Single(client => client.Id == id);
    public void InviteMockClientForSmoke(RoomClient client) => _sandbox.GrantInvitation(_client.JoinedCode, client);
    public RoomClient AddMockClientForSmoke(int id)
    {
        var client = _sandbox.AddClient(id, $"测试玩家 {id}", LubanData.Tables.TbDogSkin.DataList[0].Id);
        _mockClients.Add(client);
        return client;
    }
}
#endif
