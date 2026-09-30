#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using DataTables;

namespace LuckyDogRise.Rooms;

// The page borrows the room service from the active platform session. It never
// owns a Steam runtime or pumps platform callbacks independently.
public partial class InGameRoomPreview : VBoxContainer
{
    [Export] private VBoxContainer _lobby = null!;
    [Export] private VBoxContainer _room = null!;
    [Export] private VBoxContainer _rooms = null!;
    [Export] private VBoxContainer _members = null!;
    [Export] private LineEdit _nameInput = null!;
    [Export] private LineEdit _codeInput = null!;
    [Export] private Label _roomTitle = null!;
    [Export] private Label _code = null!;
    [Export] private Label _status = null!;
    [Export] private Label _empty = null!;
    [Export] private Label _connection = null!;
    [Export] private Label _game = null!;
    [Export] private Button _create = null!;
    [Export] private Button _join = null!;
    [Export] private Button _random = null!;
    [Export] private Button _refresh = null!;
    [Export] private Button _return = null!;
    [Export] private Button _cancel = null!;
    [Export] private PackedScene _directoryRow = null!;
    [Export] private PackedScene _memberRow = null!;
    private RoomClient _client;
    private IGamePlatformService _platform;
    private IRoomService _service;
    private GameData _gameData;
    private bool _isMock;
    private bool _restartRequired;
    private static readonly List<Translation> Translations = new();
    private static int _translationUsers;
    private bool _registeredTranslations;
    private RoomListing[] _lastListings;
    private RoomMember[] _lastMembers;
    private bool _browsing = true;
    private bool _dirty;
    private long _lastSession;
    private string _noticeKey = "";
    public event Action<RoomClient> ClientChanged;
    public RoomClient CurrentClient => _client;

    public void Configure(IGamePlatformService platform, GameData gameData)
    {
        _platform = platform;
        if (_gameData != null) _gameData.EquipmentChanged -= SyncLocalAppearance;
        _gameData = gameData;
        if (_gameData != null) _gameData.EquipmentChanged += SyncLocalAppearance;
        if (IsNodeReady()) RefreshService();
    }

    public override void _Ready()
    {
        if (!BuildCapabilities.SteamRooms) { QueueFree(); return; }
        // All other supported locales currently carry an explicit English
        // fallback; register each resource once for this page's lifetime.
        _registeredTranslations = true;
        if (_translationUsers++ == 0)
        foreach (var locale in new[] { "en", "zh_CN", "zh_TW", "ja", "es_ES", "es", "pt_BR", "pt_PT",
                     "fr", "de", "da", "id", "nb", "sv", "nl", "vi", "ms", "ko", "ru", "uk" })
        {
            var translation = GD.Load<Translation>($"res://Scenes/Rooms/InGameRoomText.{locale}.translation");
            TranslationServer.AddTranslation(translation);
            Translations.Add(translation);
        }
        // Children cache their translated text before this parent's _Ready.
        // Registering a resource alone does not refresh those caches; notify
        // only this preview, without changing the player's selected locale.
        PropagateNotification((int)NotificationTranslationChanged);
        _nameInput.Text = L10n.Tr("Rooms_DefaultName");
        _create.Pressed += () => { _noticeKey = ""; _client?.Create(_nameInput.Text); };
        _join.Pressed += JoinCode;
        _codeInput.TextSubmitted += _ => JoinCode();
        _random.Pressed += () => { _noticeKey = ""; _client?.FindAndJoin(); };
        _refresh.Pressed += RefreshRooms;
        _cancel.Pressed += () => _client?.Cancel();
        _return.Pressed += () => { _browsing = false; _dirty = true; };
        L10n.Changed += OnLanguageChanged;
        InitializeMock();
        RefreshService();
        _dirty = true;
    }

    public override void _Process(double delta)
    {
        RefreshService();
        TickMock(delta);
        if (!_isMock) _client?.AdvanceTo(Time.GetTicksMsec() / 1000d);
        if (_dirty) { _dirty = false; Render(); }
    }
    public override void _ExitTree()
    {
        L10n.Changed -= OnLanguageChanged;
        if (_gameData != null) _gameData.EquipmentChanged -= SyncLocalAppearance;
        ReplaceClient(null);
        DisposeMock();
        if (_registeredTranslations && --_translationUsers == 0)
        {
            foreach (var translation in Translations) TranslationServer.RemoveTranslation(translation);
            Translations.Clear();
        }
    }
    partial void InitializeMock();
    partial void TickMock(double delta);
    partial void DisposeMock();

    private void RefreshService()
    {
        if (_isMock) return;
        var provider = _platform as IPlatformRoomServiceProvider;
        bool restartRequired = provider?.RoomRestartRequired == true;
        if (_restartRequired != restartRequired)
        {
            _restartRequired = restartRequired;
            _dirty = true;
        }
        var current = provider?.RoomService;
        if (ReferenceEquals(current, _service)) return;
        ReplaceClient(null);
        _service = current;
        if (current == null) return;
        var name = string.IsNullOrWhiteSpace(_platform.PersonaName) ? L10n.Tr("Rooms_You") : _platform.PersonaName;
        var client = new RoomClient(current, 1, name, LubanData.Tables.TbDogSkin.DataList[0].Id);
        client.AdvanceTo(Time.GetTicksMsec() / 1000d);
        ReplaceClient(client);
        client.Search();
    }

    private void ReplaceClient(RoomClient client)
    {
        if (_client != null)
        {
            _client.Changed -= OnClientChanged;
            _client.Dispose();
        }
        _client = client;
        _lastSession = 0;
        _lastListings = null;
        _lastMembers = null;
        _browsing = true;
        _noticeKey = "";
        if (_client != null)
        {
            _client.Changed += OnClientChanged;
            SyncLocalAppearance();
        }
        _dirty = true;
        ClientChanged?.Invoke(_client);
    }

    private void SyncLocalAppearance()
    {
        if (_client == null || _gameData == null) return;
        int skin = _gameData.Inventory.GetEquipped(EItemType.Dog)?.SkinId
            ?? LubanData.Tables.TbDogSkin.DataList[0].Id;
        int headwear = _gameData.Inventory.GetEquipped(EItemType.Headwear)?.Id ?? 0;
        if (_client.SkinId != skin || _client.HeadwearId != headwear)
            _client.SetAppearance(skin, headwear, 1001);
    }
    private void OnClientChanged()
    {
        if (_client.Session != _lastSession)
        {
            _lastSession = _client.Session;
            _browsing = _client.JoinedCode.Length == 0;
        }
        _dirty = true;
    }
    private void OnLanguageChanged() { _lastListings = null; _lastMembers = null; _dirty = true; }
    private void JoinCode() { _noticeKey = ""; _client?.Join(_codeInput.Text); }
    public void RefreshRooms()
    {
        _noticeKey = "";
        RefreshService();
        if (_client == null) (_platform as IRecoverablePlatformService)?.RequestReconnect();
        else _client.Search();
        _dirty = true;
    }
    public void OnBrowse() { _browsing = true; RefreshRooms(); _dirty = true; }
    public void OnLeave() { _client?.Leave(); _browsing = true; RefreshRooms(); }
    public void OnCopyCode()
    {
        if (_client == null || _client.JoinedCode.Length == 0) return;
        DisplayServer.ClipboardSet(_client.JoinedCode);
        _noticeKey = "Rooms_CodeCopied";
        _dirty = true;
    }

    private void Render()
    {
        _connection.Text = L10n.Tr(_restartRequired ? "Rooms_SteamRestartRequired"
            : _client == null ? "Rooms_SteamUnavailable" : "Rooms_SteamConnected");
        RenderMockNotice();
        if (_client == null)
        {
            _lobby.Show();
            _room.Hide();
            _return.Hide();
            _cancel.Hide();
            _create.Disabled = _join.Disabled = _random.Disabled = true;
            _refresh.Disabled = false;
            _empty.Hide();
            _status.Hide();
            Clear(_rooms);
            Clear(_members);
            return;
        }
        var joined = _client.JoinedCode.Length > 0;
        _lobby.Visible = _browsing || !joined;
        _room.Visible = !_lobby.Visible;
        _return.Visible = joined;
        _create.Disabled = _join.Disabled = _random.Disabled = _refresh.Disabled = _client.IsBusy;
        _cancel.Visible = _client.IsBusy;
        _status.Text = L10n.Tr(_noticeKey.Length > 0 ? _noticeKey : StatusKey());
        _status.Visible = _status.Text.Length > 0;
        _empty.Visible = _client.Listings.Length == 0 && !_client.IsBusy;
        if (!ReferenceEquals(_lastListings, _client.Listings))
        {
            _lastListings = _client.Listings;
            Clear(_rooms);
            foreach (var room in _client.Listings)
            {
                var row = _directoryRow.Instantiate<InGameRoomDirectoryRow>();
                _rooms.AddChild(row);
                row.Bind(room, _client.IsBusy, () => { _noticeKey = ""; _client.Join(room.Code); });
            }
        }
        else
            foreach (var row in _rooms.GetChildren().OfType<InGameRoomDirectoryRow>()) row.SetBusy(_client.IsBusy);

        var view = _client.View;
        _game.Text = GameName(view?.GameId);
        _roomTitle.Text = view == null ? L10n.Tr("Rooms_WaitMembers") : $"{view.Name}  {view.Members.Length}/{RoomRules.Capacity}";
        _code.Text = _client.JoinedCode;
        _code.TooltipText = _client.JoinedCode;
        var members = view?.Members;
        if (ReferenceEquals(_lastMembers, members)) return;
        _lastMembers = members;
        Clear(_members);
        if (members == null) return;
        foreach (var member in members)
        {
            var row = _memberRow.Instantiate<Label>();
            _members.AddChild(row);
            var name = member.Id == _client.Id ? L10n.Tr("Rooms_You") : member.Name;
            row.Text = (member.Id == view.OwnerId ? "♛ " : "") + name;
        }
    }
    partial void RenderMockNotice();
    public static string GameName(string gameId) => string.IsNullOrWhiteSpace(gameId) || gameId == "social"
        ? L10n.Tr("Rooms_Social") : gameId;
    private string StatusKey() => _client.RequestState switch
    {
        RoomRequestState.Pending => _client.Operation switch
        {
            RoomOperation.Create => "Rooms_Creating", RoomOperation.Join => "Rooms_Joining", _ => "Rooms_Searching"
        },
        RoomRequestState.Cancelled => "Rooms_Cancelled",
        RoomRequestState.TimedOut => "Rooms_TimedOut",
        RoomRequestState.Failed => _client.Failure switch
        {
            RoomFailure.InvalidName => "Rooms_InvalidName", RoomFailure.NotFound => "Rooms_NotFound",
            RoomFailure.Full => "Rooms_Full", RoomFailure.NoMatchingRoom => "Rooms_NoMatch", _ => "Rooms_Unavailable"
        },
        _ => ""
    };
    private static void Clear(Node parent)
    {
        foreach (var child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); }
    }

#if DEBUG
    public RoomClient PreviewClient => _client;
#endif
}
#endif
