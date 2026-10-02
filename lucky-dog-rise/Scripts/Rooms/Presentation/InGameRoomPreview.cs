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
    [Export] private Button _codeVisibility = null!;
    [Export] private Button _invite = null!;
    [Export] private Button _access = null!;
    [Export] private VBoxContainer _accessChoices = null!;
    [Export] private Button _accessPublic = null!;
    [Export] private Button _accessFriends = null!;
    [Export] private Button _accessInvite = null!;
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
    [Export] private VBoxContainer _kickConfirm = null!;
    [Export] private Label _kickName = null!;
    [Export] private Button _kickAccept = null!;
    [Export] private Button _kickCancel = null!;
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
    private bool _lastCompanionEntry;
    private RoomMember[] _lastMembers;
    private bool _browsing = true;
    private bool _dirty;
    private long _lastSession;
    private string _noticeKey = "";
    private bool _hideRoomCode;
    private bool _privacyLoaded;
    private RoomClient _kickClient;
    private long _kickSession;
    private int _kickMemberId;
    private long _kickPresence;
    private RoomClient _accessClient;
    private long _accessSession;
    public event Action<RoomClient> ClientChanged;
    public RoomClient CurrentClient => _client;

    // The standalone lab also uses these strings, without creating the room page.
    public static string ChatText(string key)
    {
        var locale = TranslationServer.GetLocale();
        locale = locale.StartsWith("zh") ? (locale.Contains("TW") || locale.Contains("Hant") ? "zh_TW" : "zh_CN") : "en";
        var value = GD.Load<Translation>($"res://Scenes/Rooms/InGameRoomText.{locale}.translation").GetMessage(key).ToString();
        return value.Length > 0 ? value : key;
    }

    public void Configure(IGamePlatformService platform, GameData gameData)
    {
        _platform = platform;
        if (_gameData != null) _gameData.EquipmentChanged -= SyncLocalAppearance;
        _gameData = gameData;
        if (_gameData != null) _gameData.EquipmentChanged += SyncLocalAppearance;
        // UI privacy is a local preference, independent of the Steam account.
        // Standalone fake-provider checks have no GameData and never write it.
        if (!_privacyLoaded && _gameData != null)
        {
            _hideRoomCode = SettingsManager.LoadRoomCodeHidden();
            _privacyLoaded = true;
        }
        if (IsNodeReady()) RefreshService();
        _dirty = true;
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
        _kickAccept.Pressed += ConfirmKick;
        _kickCancel.Pressed += CancelKick;
        _codeVisibility.Pressed += ToggleCodeVisibility;
        _invite.Pressed += InviteFriends;
        _access.Pressed += ToggleAccessChoices;
        _accessPublic.Pressed += () => SelectAccess(RoomAccess.Public);
        _accessFriends.Pressed += () => SelectAccess(RoomAccess.FriendsOnly);
        _accessInvite.Pressed += () => SelectAccess(RoomAccess.InviteOnly);
        L10n.Changed += OnLanguageChanged;
        InitializeMock();
        RefreshService();
        _dirty = true;
    }

    public override void _Process(double delta)
    {
        RefreshService();
        TickMock(delta);
        // Steam stamps chat expiry with its service clock. Godot's uptime has a
        // different origin, so mixing them leaves otherwise valid bubbles stuck.
        if (!_isMock && _client != null) _client.AdvanceTo(_service.Now);
        ConsumeAcceptedInvite();
        if (!IsVisibleInTree()) CloseAccessChoices();
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
        client.AdvanceTo(current.Now);
        ReplaceClient(client);
        // A known invite already supplies the target. Do not put an unnecessary
        // directory search in front of the cold-start join on Steam's callback queue.
        if (current is not IRoomInviteService { HasPendingJoinRequest: true }) client.Search();
    }

    private void ReplaceClient(RoomClient client)
    {
        CancelKick();
        CloseAccessChoices();
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
            _client.SetAppearance(skin, headwear, _client.Reaction);
    }
    private void OnClientChanged()
    {
        if (_accessClient != null && AccessError(_accessClient, _accessSession).Length > 0)
            CloseAccessChoices();
        if (_client.Failure is RoomFailure.Removed or RoomFailure.Banned)
            _noticeKey = _client.Failure == RoomFailure.Removed ? "Rooms_Removed" : "Rooms_Banned";
        if (_kickClient != null && KickTargetError(_kickClient, _kickSession, _kickMemberId, _kickPresence).Length > 0)
            CancelKick();
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
    public void OnBrowse() { CloseAccessChoices(); _browsing = true; RefreshRooms(); _dirty = true; }
    public void OnLeave() { CloseAccessChoices(); _client?.Leave(); _browsing = true; RefreshRooms(); }
    public void OnCopyCode()
    {
        if (_client == null || _client.JoinedCode.Length == 0) return;
        DisplayServer.ClipboardSet(_client.JoinedCode);
        _noticeKey = "Rooms_CodeCopied";
        _dirty = true;
    }

    private void ToggleCodeVisibility()
    {
        _hideRoomCode = !_hideRoomCode;
        if (_gameData != null) SettingsManager.SaveRoomCodeHidden(_hideRoomCode);
        // Apply immediately: do not leave a frame of unmasked text after a click.
        RenderCodePrivacy();
    }

    private void RenderCodePrivacy()
    {
        string code = _client?.JoinedCode ?? "";
        _code.Text = _hideRoomCode && code.Length > 0 ? "••••••••••••" : code;
        _code.TooltipText = _hideRoomCode ? "" : code;
        _codeVisibility.Text = _hideRoomCode ? "Rooms_ShowCode" : "Rooms_HideCode";
        _codeInput.Secret = _hideRoomCode;
    }

    private void InviteFriends()
    {
        if (_client == null || _client.JoinedCode.Length == 0 || _client.IsBusy
            || _isMock || _service is not IRoomInviteService invitations) return;
        string result = invitations.InviteFriends(_client);
        // Opening the picker does not mean the player has sent an invitation.
        _noticeKey = result == "ok" ? "" : result;
        _dirty = true;
    }

    private string AccessError(RoomClient client, long session)
    {
        if (!ReferenceEquals(client, _client) || client == null || client.Session != session
            || client.JoinedCode.Length == 0 || client.IsBusy || client.View == null)
            return "Rooms_AccessUnavailable";
        return client.View.OwnerId == client.Id ? "" : "Rooms_AccessNotOwner";
    }

    private void ToggleAccessChoices()
    {
        if (_accessChoices.Visible) { CloseAccessChoices(); return; }
        if (AccessError(_client, _client?.Session ?? 0).Length > 0) return;
        _accessClient = _client;
        _accessSession = _client.Session;
        _accessChoices.Show();
    }

    private void CloseAccessChoices()
    {
        _accessClient = null;
        _accessChoices?.Hide();
    }

    private void SelectAccess(RoomAccess access)
    {
        // The selected room or host may have changed since the menu opened.
        // A stale button signal must never modify a newly joined room.
        if (_accessClient == null || !_accessChoices.Visible) return;
        var error = AccessError(_accessClient, _accessSession);
        if (error.Length == 0)
        {
            string result = _accessClient.SetAccess(access);
            error = result == "ok" ? "" : result;
        }
        CloseAccessChoices();
        _noticeKey = error;
        RenderAccess();
        _dirty = true;
    }

    private void RenderAccess()
    {
        var access = _client?.View?.Access ?? RoomAccess.Public;
        _access.Text = L10n.Tr(AccessKey(access)) + "  ▾";
        _access.Disabled = AccessError(_client, _client?.Session ?? 0).Length > 0;
        _access.TooltipText = _client?.View != null && _client.View.OwnerId != _client.Id
            ? L10n.Tr("Rooms_AccessNotOwner") : "";
        foreach (var (button, value) in new[] { (_accessPublic, RoomAccess.Public),
                     (_accessFriends, RoomAccess.FriendsOnly), (_accessInvite, RoomAccess.InviteOnly) })
        {
            button.SetPressedNoSignal(access == value);
            button.Disabled = _access.Disabled || access == value;
        }
        if (_access.Disabled) CloseAccessChoices();
    }

    private static string AccessKey(RoomAccess access) => access switch
    {
        RoomAccess.FriendsOnly => "Rooms_AccessFriendsOnly",
        RoomAccess.InviteOnly => "Rooms_AccessInviteOnly",
        _ => "Rooms_AccessPublic"
    };

    private void ConsumeAcceptedInvite()
    {
        if (_isMock || _client == null || _service is not IRoomInviteService invitations
            || !invitations.TryTakeJoinRequest(out string code)) return;
        CancelKick();
        _noticeKey = "";
        _client.Cancel();
        // Reuse the ordinary join path so errors retain the previous membership,
        // and Steam callback cleanup remains serialized by the room service.
        if (!string.Equals(_client.JoinedCode, code, StringComparison.OrdinalIgnoreCase))
            _client.Join(code);
        _dirty = true;
    }

    private string KickTargetError(RoomClient client, long session, int memberId, long presence)
    {
        if (!ReferenceEquals(client, _client) || client == null || client.Session != session
            || client.JoinedCode.Length == 0 || client.IsBusy) return "Rooms_KickUnavailable";
        if (client.View?.OwnerId != client.Id) return "Rooms_KickNotOwner";
        return memberId == client.Id || !client.View.Members.Any(m => m.Id == memberId
                && m.Presence == presence && !m.IsCompanion)
            ? "Rooms_KickInvalidTarget" : "";
    }

    private void RequestKick(RoomClient client, long session, RoomMember member)
    {
        var error = KickTargetError(client, session, member.Id, member.Presence);
        if (error.Length > 0) { _noticeKey = error; _dirty = true; return; }
        _kickClient = client;
        _kickSession = session;
        _kickMemberId = member.Id;
        _kickPresence = member.Presence;
        _kickName.Text = member.Name;
        _kickConfirm.Show();
        _noticeKey = "";
        _dirty = true;
    }

    private void ConfirmKick()
    {
        // Validate the captured membership again: the host, room or target can
        // change while the confirmation is visible, or before this signal runs.
        var error = KickTargetError(_kickClient, _kickSession, _kickMemberId, _kickPresence);
        if (error.Length == 0) error = _kickClient.Kick(_kickMemberId, _kickPresence);
        CancelKick();
        _noticeKey = error.Length == 0 ? "Rooms_KickSent" : error;
        _dirty = true;
    }

    private void CancelKick()
    {
        _kickClient = null;
        _kickConfirm?.Hide();
    }

    private bool HasEmptySearchResult => _client != null && !_client.IsBusy
        && _client.Operation == RoomOperation.Search && _client.RequestState == RoomRequestState.Succeeded
        && _client.Listings.Length == 0;

    private void JoinCompanionEntry(InGameRoomDirectoryRow row, RoomClient client, long session,
        RoomListing[] listings, string name)
    {
        // This is a local invitation to create a room, never a made-up Steam ID.
        // Capture the rendered result/session so refresh, reconnect, room changes
        // and queued signals from removed rows cannot create a different room.
        if (!ReferenceEquals(client, _client) || client.Session != session
            || !ReferenceEquals(client.Listings, listings) || !HasEmptySearchResult
            || !_lobby.IsVisibleInTree() || !GodotObject.IsInstanceValid(row)
            || row.GetParent() != _rooms) return;
        _noticeKey = "";
        client.Create(name, joiningFromDirectory: true);
        _dirty = true;
    }

    private void Render()
    {
        RenderCodePrivacy();
        RenderAccess();
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
        _invite.Disabled = !joined || _client.IsBusy || _isMock || _service is not IRoomInviteService;
        _invite.TooltipText = _isMock || _service is not IRoomInviteService
            ? L10n.Tr("Rooms_InviteUnavailable") : "";
        _status.Text = L10n.Tr(_noticeKey.Length > 0 ? _noticeKey : StatusKey());
        _status.Visible = _status.Text.Length > 0;
        bool companionEntry = HasEmptySearchResult;
        // Empty successful searches offer the local companion entry. Pending or
        // failed requests must not imply that the actual Steam directory is empty.
        _empty.Hide();
        if (!ReferenceEquals(_lastListings, _client.Listings) || _lastCompanionEntry != companionEntry)
        {
            _lastListings = _client.Listings;
            _lastCompanionEntry = companionEntry;
            Clear(_rooms);
            foreach (var room in _client.Listings)
            {
                var row = _directoryRow.Instantiate<InGameRoomDirectoryRow>();
                _rooms.AddChild(row);
                row.Bind(room, _client.IsBusy, () => { _noticeKey = ""; _client.Join(room.Code); });
            }
            if (companionEntry)
            {
                var row = _directoryRow.Instantiate<InGameRoomDirectoryRow>();
                _rooms.AddChild(row);
                var client = _client;
                var session = client.Session;
                var listings = client.Listings;
                string name = L10n.Tr("Rooms_Social");
                row.Bind(new RoomListing("", name, "social", RoomCompanionPlan.Count, RoomRules.Capacity),
                    false, () => JoinCompanionEntry(row, client, session, listings, name));
            }
        }
        else
            foreach (var row in _rooms.GetChildren().OfType<InGameRoomDirectoryRow>()) row.SetBusy(_client.IsBusy);

        var view = _client.View;
        _game.Text = GameName(view?.GameId);
        _roomTitle.Text = view == null ? L10n.Tr("Rooms_WaitMembers") : $"{view.Name}  {view.Members.Length}/{RoomRules.Capacity}";
        var members = view?.Members;
        if (ReferenceEquals(_lastMembers, members))
        {
            foreach (var row in _members.GetChildren())
            {
                var kick = row.GetNode<Button>("Kick");
                kick.Disabled = _client.IsBusy || !kick.Visible;
            }
            return;
        }
        _lastMembers = members;
        Clear(_members);
        if (members == null) return;
        foreach (var member in members)
        {
            var row = _memberRow.Instantiate<HBoxContainer>();
            _members.AddChild(row);
            var name = member.Id == _client.Id ? L10n.Tr("Rooms_You") : member.Name;
            row.GetNode<Label>("Name").Text = (!member.IsCompanion && member.Id == view.OwnerId ? "♛ " : "") + name;
            var kick = row.GetNode<Button>("Kick");
            kick.Visible = view.OwnerId == _client.Id && member.Id != _client.Id && !member.IsCompanion;
            kick.Disabled = _client.IsBusy || member.IsCompanion;
            var client = _client;
            var session = client.Session;
            kick.Pressed += () => RequestKick(client, session, member);
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
        RoomRequestState.Failed => FailureKey(_client.Failure),
        _ => ""
    };
    public static string FailureKey(RoomFailure failure) => failure switch
        {
            RoomFailure.InvalidName => "Rooms_InvalidName", RoomFailure.NotFound => "Rooms_NotFound",
            RoomFailure.Full => "Rooms_Full", RoomFailure.NoMatchingRoom => "Rooms_NoMatch",
            RoomFailure.AccessDenied => "Rooms_AccessDenied",
            RoomFailure.Removed => "Rooms_Removed", RoomFailure.Banned => "Rooms_Banned", _ => "Rooms_Unavailable"
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
