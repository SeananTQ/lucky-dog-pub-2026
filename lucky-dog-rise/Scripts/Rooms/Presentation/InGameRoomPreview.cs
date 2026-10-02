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
    [Export] private LineEdit _roomTitle = null!;
    [Export] private Label _roomCount = null!;
    [Export] private LinkButton _code = null!;
    [Export] private Button _codeVisibility = null!;
    [Export] private Button _invite = null!;
    [Export] private OptionButton _access = null!;
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
    private ConfirmOverlayController _kickConfirm;
    private Button _kickCancel;
    private string _kickName = "";
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
    private readonly Dictionary<(int Id, long Presence), HBoxContainer> _memberRows = new();
    private RoomClient _membersClient;
    private long _membersSession;
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
    private long _accessEpoch;
    private RoomClient _nameEditClient;
    private long _nameEditSession;
    private long _nameEditEpoch;
    private bool _nameImeAtKey;
    private bool _nameCommitQueued;
    private bool _exitingTree;
    public event Action<RoomClient> ClientChanged;
    public event Action PageChanged;
    public RoomClient CurrentClient => _client;
    public PopupMenu AccessPopup => _access?.GetPopup();

    // The panel owns this sibling overlay so scrolling cannot move or clip it.
    public void BindKickConfirmation(ConfirmOverlayController confirmation)
    {
        CancelKick();
        if (GodotObject.IsInstanceValid(_kickConfirm))
        {
            _kickConfirm.Confirmed -= ConfirmKick;
            _kickConfirm.Canceled -= CancelKick;
        }
        _kickConfirm = confirmation;
        if (_kickConfirm == null) { _kickCancel = null; return; }
        _kickConfirm.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        _kickConfirm.Confirmed += ConfirmKick;
        _kickConfirm.Canceled += CancelKick;
        var actions = _kickConfirm.GetNode<BoxContainer>("OverlayPanel/Margin/Content/ButtonRow");
        var accept = actions.GetNode<Button>("ConfirmButton");
        _kickCancel = actions.GetNode<Button>("CancelButton");
        // Only this dialog uses stacked actions; other shared-overlay users
        // retain their current layout until they are migrated individually.
        actions.Vertical = true;
        actions.MoveChild(accept, 0);
        _kickConfirm.FocusMode = FocusModeEnum.All;
        _kickConfirm.FocusNext = _kickConfirm.FocusNeighborBottom = _kickConfirm.GetPathTo(accept);
        _kickConfirm.FocusPrevious = _kickConfirm.FocusNeighborTop = _kickConfirm.GetPathTo(_kickCancel);
        _kickConfirm.FocusNeighborLeft = _kickConfirm.FocusPrevious;
        _kickConfirm.FocusNeighborRight = _kickConfirm.FocusNext;
        foreach (var (button, other) in new[] { (accept, _kickCancel), (_kickCancel, accept) })
        {
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            var path = button.GetPathTo(other);
            button.FocusNext = button.FocusPrevious = path;
            button.FocusNeighborLeft = button.FocusNeighborRight = path;
            button.FocusNeighborTop = button.FocusNeighborBottom = path;
        }
    }

    public void CancelKickConfirmation() => CancelKick();

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
        _return.Pressed += () => { _browsing = false; _dirty = true; NotifyPageChanged(); };
        _codeVisibility.Pressed += ToggleCodeVisibility;
        _invite.Pressed += InviteFriends;
        foreach (var access in Enum.GetValues<RoomAccess>()) _access.AddItem(L10n.Tr(AccessKey(access)), (int)access);
        AccessPopup.AboutToPopup += RememberAccessContext;
        AccessPopup.PopupHide += () =>
        {
            long epoch = _accessEpoch;
            // OptionButton emits selection during the same popup-close dispatch.
            // Retain that captured context until the signal has been processed.
            Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(this) && IsInsideTree()
                    && epoch == _accessEpoch && !AccessPopup.Visible) _accessClient = null;
            }).CallDeferred();
        };
        _access.FocusEntered += RememberAccessContext;
        _access.ItemSelected += index => SelectAccess((RoomAccess)_access.GetItemId((int)index));
        _roomTitle.FocusEntered += BeginNameEdit;
        _roomTitle.FocusExited += QueueNameCommit;
        _roomTitle.TextSubmitted += _ => { if (!_nameImeAtKey && !HasNameIme) CommitNameEdit(); };
        _code.Pressed += OnCopyCode;
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
        if (!IsVisibleInTree())
        {
            CancelKick();
            CloseAccessChoices();
            if (_nameEditClient != null && !_nameCommitQueued) QueueNameCommit();
        }
        if (_dirty) { _dirty = false; Render(); }
    }
    public override void _ExitTree()
    {
        _exitingTree = true;
        L10n.Changed -= OnLanguageChanged;
        if (_gameData != null) _gameData.EquipmentChanged -= SyncLocalAppearance;
        ReplaceClient(null);
        BindKickConfirmation(null);
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
        CancelNameEdit();
        if (_client != null)
        {
            _client.Changed -= OnClientChanged;
            _client.Dispose();
        }
        _client = client;
        _lastSession = 0;
        _lastListings = null;
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
        if (_nameEditClient != null && NameEditError(_nameEditClient, _nameEditSession).Length > 0)
            CancelNameEdit();
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
            NotifyPageChanged();
        }
        _dirty = true;
    }
    private void OnLanguageChanged()
    {
        _lastListings = null;
        _dirty = true;
        if (_kickClient != null) ShowKickConfirmation();
    }
    private void JoinCode() { _noticeKey = ""; _client?.Join(_codeInput.Text); }
    public void RefreshRooms()
    {
        _noticeKey = "";
        RefreshService();
        if (_client == null) (_platform as IRecoverablePlatformService)?.RequestReconnect();
        else _client.Search();
        _dirty = true;
    }
    private void NotifyPageChanged() => Callable.From(() =>
    {
        if (GodotObject.IsInstanceValid(this) && IsInsideTree()) PageChanged?.Invoke();
    }).CallDeferred();
    public void OnBrowse() { CancelKick(); CommitNameEdit(); CloseAccessChoices(); _browsing = true; RefreshRooms(); _dirty = true; NotifyPageChanged(); }
    public void OnLeave() { CancelKick(); CancelNameEdit(); CloseAccessChoices(); _client?.Leave(); _browsing = true; RefreshRooms(); NotifyPageChanged(); }
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

    private void RememberAccessContext()
    {
        if (AccessError(_client, _client?.Session ?? 0).Length > 0) { CloseAccessChoices(); return; }
        _accessEpoch++;
        _accessClient = _client;
        _accessSession = _client.Session;
    }

    private void CloseAccessChoices()
    {
        _accessClient = null;
        _accessEpoch++;
        AccessPopup?.Hide();
    }

    private void SelectAccess(RoomAccess access)
    {
        // The selected room or host may have changed since the menu opened.
        // A stale button signal must never modify a newly joined room.
        if (_accessClient == null) { RenderAccess(); return; }
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
        foreach (var value in Enum.GetValues<RoomAccess>())
            _access.SetItemText(_access.GetItemIndex((int)value), L10n.Tr(AccessKey(value)));
        _access.Select(_access.GetItemIndex((int)access));
        _access.Disabled = AccessError(_client, _client?.Session ?? 0).Length > 0;
        _access.TooltipText = _client?.View != null && _client.View.OwnerId != _client.Id
            ? L10n.Tr("Rooms_AccessNotOwner") : "";
        if (_access.Disabled) CloseAccessChoices();
    }

    private static string AccessKey(RoomAccess access) => access switch
    {
        RoomAccess.FriendsOnly => "Rooms_AccessFriendsOnly",
        RoomAccess.InviteOnly => "Rooms_AccessInviteOnly",
        _ => "Rooms_AccessPublic"
    };

    private string NameEditError(RoomClient client, long session)
    {
        if (!ReferenceEquals(client, _client) || client == null || client.Session != session
            || client.JoinedCode.Length == 0 || client.View == null || client.IsBusy)
            return "Rooms_NameUnavailable";
        return client.View.OwnerId == client.Id ? "" : "Rooms_NameNotOwner";
    }

    private void BeginNameEdit()
    {
        if (!CanUseNameEditor || NameEditError(_client, _client?.Session ?? 0).Length > 0) return;
        _nameEditEpoch++;
        _nameEditClient = _client;
        _nameEditSession = _client.Session;
        _nameCommitQueued = false;
        _nameImeAtKey = false;
    }

    private void QueueNameCommit()
    {
        if (_nameEditClient == null || _nameCommitQueued) return;
        if (!CanUseNameEditor) { CancelNameEdit(); return; }
        // Losing focus while composition is unfinished must not publish the
        // partial pre-composition text as a new room name.
        if (HasNameIme || _nameImeAtKey) { CancelNameEdit(); return; }
        _nameCommitQueued = true;
        long epoch = _nameEditEpoch;
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(this) && epoch == _nameEditEpoch) CommitNameEdit();
        }).CallDeferred();
    }

    private void CommitNameEdit()
    {
        if (_nameEditClient == null) return;
        if (!CanUseNameEditor) { CancelNameEdit(); return; }
        if (HasNameIme) { CancelNameEdit(); return; }
        var client = _nameEditClient;
        string error = NameEditError(client, _nameEditSession);
        string draft = _roomTitle.Text;
        _nameEditClient = null;
        _nameEditEpoch++;
        _nameCommitQueued = false;
        if (error.Length == 0)
        {
            if (!RoomRules.TryNormalizeName(draft, out string name)) error = "Rooms_InvalidName";
            else if (!string.Equals(name, client.View.Name, StringComparison.Ordinal))
            {
                string result = client.SetName(name);
                if (result != "ok") error = result;
            }
        }
        _noticeKey = error;
        // SetName may synchronously notify listeners which remove this page.
        if (!CanUseNameEditor) return;
        RenderName();
        if (_roomTitle.HasFocus()) _roomTitle.ReleaseFocus();
        _dirty = true;
    }

    private void CancelNameEdit()
    {
        _nameEditClient = null;
        _nameEditEpoch++;
        _nameCommitQueued = false;
        _nameImeAtKey = false;
        // Children leave the tree before the page's _ExitTree. Invalidate the
        // draft even then, but never ask a detached LineEdit to release focus.
        if (!CanUseNameEditor) return;
        if (_roomTitle.HasFocus()) _roomTitle.ReleaseFocus();
        RenderName();
    }

    private bool CanUseNameEditor => GodotObject.IsInstanceValid(this) && !_exitingTree
        && !IsQueuedForDeletion() && IsInsideTree() && GodotObject.IsInstanceValid(_roomTitle)
        && !_roomTitle.IsQueuedForDeletion() && _roomTitle.IsInsideTree();

    private void RenderName()
    {
        string error = NameEditError(_client, _client?.Session ?? 0);
        _roomTitle.Editable = error.Length == 0;
        _roomTitle.TooltipText = _client?.View != null && error.Length > 0 ? L10n.Tr(error) : "";
        _roomTitle.PlaceholderText = _nameInput.PlaceholderText = L10n.Tr("Rooms_Name");
        if (_nameEditClient == null) _roomTitle.Text = _client?.View?.Name ?? "";
        _roomCount.Text = _client?.View == null ? "" : $"{_client.View.Members.Length}/{RoomRules.Capacity}";
    }

    public override void _Input(InputEvent @event)
    {
        if (_kickClient != null)
        {
            if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
            { CancelKick(); GetViewport().SetInputAsHandled(); }
            return;
        }
        if (_access.HasFocus() && @event is InputEventKey { Pressed: true }) RememberAccessContext();
        if (!_roomTitle.HasFocus() || _nameEditClient == null) return;
        if (@event is InputEventKey { Pressed: true } key)
        {
            _nameImeAtKey = HasNameIme;
            if (key.Keycode == Key.Escape && !_nameImeAtKey)
            { CancelNameEdit(); GetViewport().SetInputAsHandled(); }
        }
        else if (@event is InputEventMouseButton { Pressed: true }) _nameImeAtKey = HasNameIme;
    }

    private bool HasNameIme =>
#if DEBUG
        NameImeForSmoke?.Invoke() ??
#endif
        _roomTitle.HasImeText();

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
                && m.Presence == presence)
            ? "Rooms_KickInvalidTarget" : "";
    }

    private void RequestKick(RoomClient client, long session, int memberId, long presence)
    {
        if (!GodotObject.IsInstanceValid(_kickConfirm) || !IsVisibleInTree()) return;
        CommitNameEdit();
        var error = KickTargetError(client, session, memberId, presence);
        if (error.Length > 0) { _noticeKey = error; _dirty = true; return; }
        CloseAccessChoices();
        _kickClient = client;
        _kickSession = session;
        _kickMemberId = memberId;
        _kickPresence = presence;
        _kickName = client.View.Members.First(member => member.Id == memberId && member.Presence == presence).Name;
        ShowKickConfirmation();
        // Take focus off the covered page without preselecting either action.
        _kickConfirm.GrabFocus();
        _noticeKey = "";
        _dirty = true;
    }

    private void ShowKickConfirmation() => _kickConfirm.ShowConfirm(
        L10n.Tr("Rooms_KickConfirm"), _kickName + "\n\n" + L10n.Tr("Rooms_KickConfirmMessage"),
        L10n.Tr("Rooms_KickConfirm"), L10n.Tr(L10nKey.Common_Cancel));

    private void ConfirmKick()
    {
        if (_kickClient == null) return;
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
        _kickName = "";
        if (GodotObject.IsInstanceValid(_kickConfirm)) _kickConfirm.Hide();
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
        RenderName();
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
            RenderMembers();
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
                string name = L10n.Tr("Rooms_DefaultName");
                row.Bind(new RoomListing("", name, "social", RoomCompanionPlan.Count, RoomRules.Capacity),
                    false, () => JoinCompanionEntry(row, client, session, listings, name));
            }
        }
        else
            foreach (var row in _rooms.GetChildren().OfType<InGameRoomDirectoryRow>()) row.SetBusy(_client.IsBusy);

        var view = _client.View;
        _game.Text = string.Format(L10n.Tr("Rooms_GameFormat"), GameName(view?.GameId));
        RenderMembers();
    }

    private void RenderMembers()
    {
        var session = _client?.Session ?? 0;
        if (!ReferenceEquals(_membersClient, _client) || _membersSession != session)
        {
            Clear(_members);
            _memberRows.Clear();
            _membersClient = _client;
            _membersSession = session;
        }
        var view = _client?.View;
        var members = view?.Members ?? Array.Empty<RoomMember>();
        var identities = members.Select(member => (member.Id, member.Presence)).ToHashSet();
        foreach (var identity in _memberRows.Keys.Where(key => !identities.Contains(key)).ToArray())
        {
            var removed = _memberRows[identity];
            _members.RemoveChild(removed);
            removed.QueueFree();
            _memberRows.Remove(identity);
        }
        for (int index = 0; index < members.Length; index++)
        {
            var member = members[index];
            var identity = (member.Id, member.Presence);
            if (!_memberRows.TryGetValue(identity, out var row))
            {
                row = _memberRow.Instantiate<HBoxContainer>();
                _members.AddChild(row);
                _memberRows.Add(identity, row);
                var client = _client;
                row.GetNode<Button>("Kick").Pressed += () => RequestKick(client, session, identity.Id, identity.Presence);
            }
            // Activity snapshots replace the model array frequently. Preserve
            // each live member's button across mouse-down/up and scroll gestures.
            if (row.GetIndex() != index) _members.MoveChild(row, index);
            var name = string.IsNullOrWhiteSpace(member.Name) && member.Id == _client.Id ? L10n.Tr("Rooms_You") : member.Name;
            row.GetNode<Label>("Name").Text = (!member.IsCompanion && member.Id == view.OwnerId ? "♛ " : "") + name;
            var kick = row.GetNode<Button>("Kick");
            kick.Visible = view.OwnerId == _client.Id && member.Id != _client.Id;
            kick.Disabled = _client.IsBusy || !kick.Visible;
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
    internal Func<bool> NameImeForSmoke { get; set; }
#endif
}
#endif
