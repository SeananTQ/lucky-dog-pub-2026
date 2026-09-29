#if DEBUG && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LuckyDogRise.Rooms;

// In-game entry for stage A. One local client, with two simulated remote members.
// This owns only room memory: no GameData, platform service, rewards or saves.
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
    [Export] private Button _create = null!;
    [Export] private Button _join = null!;
    [Export] private Button _random = null!;
    [Export] private Button _refresh = null!;
    [Export] private Button _return = null!;
    [Export] private Button _cancel = null!;
    [Export] private PackedScene _directoryRow = null!;
    [Export] private PackedScene _memberRow = null!;
    private readonly RoomSandbox _server = new();
    private RoomClient _client;
    private readonly List<RoomClient> _clients = new();
    private readonly List<Translation> _translations = new();
    private RoomListing[] _lastListings;
    private RoomMember[] _lastMembers;
    private bool _browsing = true;
    private bool _dirty;
    private long _lastSession;
    private string _noticeKey = "";

    public override void _Ready()
    {
        if (BuildInfo.IsDebugDemo || BuildInfo.IsRecording) { QueueFree(); return; }
        // Preview-only text is exported with Dev resources, never the release CSV.
        foreach (var locale in new[] { "en", "zh_CN", "zh_TW" })
        {
            var translation = GD.Load<Translation>($"res://Scenes/Dev/Rooms/InGameRoomText.{locale}.translation");
            TranslationServer.AddTranslation(translation);
            _translations.Add(translation);
        }
        // Children cache their translated text before this parent's _Ready.
        // Registering a resource alone does not refresh those caches; notify
        // only this preview, without changing the player's selected locale.
        PropagateNotification((int)NotificationTranslationChanged);
        var skins = LubanData.Tables.TbDogSkin.DataList;
        _client = _server.AddClient(1, L10n.Tr("Rooms_You"), skins[0].Id);
        var host = _server.AddClient(2, L10n.Tr("Rooms_MockPlayer2"), skins[0].Id);
        var guest = _server.AddClient(3, L10n.Tr("Rooms_MockPlayer3"), skins[0].Id);
        _clients.AddRange(new[] { _client, host, guest });
        _server.Create(host, L10n.Tr("Rooms_MockRoom"));
        _server.Join(guest, host.JoinedCode);
        _server.Settings(_client).RequestDelay = 0.35;
        _client.Changed += OnClientChanged;
        _nameInput.Text = L10n.Tr("Rooms_DefaultName");
        _create.Pressed += () => { _noticeKey = ""; _client.Create(_nameInput.Text); };
        _join.Pressed += JoinCode;
        _codeInput.TextSubmitted += _ => JoinCode();
        _random.Pressed += () => { _noticeKey = ""; _client.FindAndJoin(); };
        _refresh.Pressed += RefreshRooms;
        _cancel.Pressed += () => _client.Cancel();
        _return.Pressed += () => { _browsing = false; _dirty = true; };
        L10n.Changed += OnLanguageChanged;
        RefreshRooms();
    }

    public override void _Process(double delta)
    {
        if (_client == null) return;
        _server.Tick(delta);
        if (_dirty) { _dirty = false; Render(); }
    }
    public override void _ExitTree()
    {
        L10n.Changed -= OnLanguageChanged;
        if (_client != null) _client.Changed -= OnClientChanged;
        foreach (var client in _clients) client.Dispose();
        foreach (var translation in _translations) TranslationServer.RemoveTranslation(translation);
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
    private void JoinCode() { _noticeKey = ""; _client.Join(_codeInput.Text); }
    public void RefreshRooms() { _noticeKey = ""; _client.Search(); }
    public void OnBrowse() { _browsing = true; RefreshRooms(); _dirty = true; }
    public void OnLeave() { _client.Leave(); _browsing = true; RefreshRooms(); }
    public void OnCopyCode()
    {
        if (_client.JoinedCode.Length == 0) return;
        DisplayServer.ClipboardSet(_client.JoinedCode);
        _noticeKey = "Rooms_CodeCopied";
        _dirty = true;
    }

    private void Render()
    {
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

    // Development-only access for main-game UI regression, never a Steam test.
    public RoomClient PreviewClient => _client;
    public void AdvancePreview(double seconds) => _server.Tick(seconds);
}
#endif
