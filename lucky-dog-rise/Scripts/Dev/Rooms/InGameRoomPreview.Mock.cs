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
        _sandbox = new RoomSandbox();
        var skins = LubanData.Tables.TbDogSkin;
        var skin = skins.DataList[0].Id;
        var client = _sandbox.AddClient(1, L10n.Tr("Rooms_You"), skin);
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

    partial void TickMock(double delta) => _sandbox?.Tick(delta);
    partial void RenderMockNotice()
    {
        if (_isMock) _connection.Text = L10n.Tr("Rooms_MockNotice");
    }
    partial void DisposeMock()
    {
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
    public RoomClient AddMockClientForSmoke(int id)
    {
        var client = _sandbox.AddClient(id, $"测试玩家 {id}", LubanData.Tables.TbDogSkin.DataList[0].Id);
        _mockClients.Add(client);
        return client;
    }
}
#endif
