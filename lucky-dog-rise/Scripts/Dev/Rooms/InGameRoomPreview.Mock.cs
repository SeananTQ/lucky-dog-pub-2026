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
        var skin = LubanData.Tables.TbDogSkin.DataList[0].Id;
        var client = _sandbox.AddClient(1, L10n.Tr("Rooms_You"), skin);
        var host = _sandbox.AddClient(2, L10n.Tr("Rooms_MockPlayer2"), skin);
        var guest = _sandbox.AddClient(3, L10n.Tr("Rooms_MockPlayer3"), skin);
        _mockClients.AddRange(new[] { client, host, guest });
        _sandbox.Create(host, L10n.Tr("Rooms_MockRoom"));
        _sandbox.Join(guest, host.JoinedCode);
        // Keep the original three-human example; new player-created rooms exercise
        // the same C-stage companion plan as Steam, without touching real inventory.
        var companionSkins = LubanData.Tables.TbDogSkin.DataList
            .Where(s => BuildInfo.IncludesCurrentChannel(s.BuildChannelMask)).Select(s => s.Id).ToArray();
        var companionHats = LubanData.Tables.TbItem.DataList
            .Where(i => i.ItemType == DataTables.EItemType.Headwear && BuildInfo.IncludesCurrentChannel(i.BuildChannelMask))
            .Select(i => i.Id).Prepend(0).ToArray();
        if (companionSkins.Length > 0)
            _sandbox.CompanionFactory = now => RoomCompanionPlan.Create(companionSkins, companionHats, now);
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
    public RoomClient MockClientForSmoke(int id) => _mockClients.Single(client => client.Id == id);
}
#endif
