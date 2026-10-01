#if DEBUG && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Godot;
using DataTables;

namespace LuckyDogRise.Rooms;

public partial class RoomLab : Control
{
    [Export] private PanelContainer _panel = null!;
    [Export] private PanelContainer _tools = null!;
    [Export] private PanelContainer _titleBar = null!;
    [Export] private PanelContainer _toolbar = null!;
    [Export] private int _feedbackSpace = 200;
    [Export] private ColorRect _backdrop = null!;
    [Export] private Label _status = null!;
    [Export] private Label _metrics = null!;
    [Export] private Label _currentRoom = null!;
    [Export] private OptionButton _viewOption = null!;
    [Export] private OptionButton _targetOption = null!;
    [Export] private OptionButton _scaleOption = null!;
    [Export] private OptionButton _skinOption = null!;
    [Export] private OptionButton _hatOption = null!;
    [Export] private OptionButton _reactionOption = null!;
    [Export] private OptionButton _activityOption = null!;
    [Export] private CheckButton _continuousInput = null!;
    [Export] private CheckButton _sendPause = null!;
    [Export] private SpinBox _latency = null!;
    [Export] private SpinBox _requestDelay = null!;
    [Export] private OptionButton _requestFailure = null!;
    [Export] private Label _operation = null!;
    [Export] private Label _targetOperation = null!;
    [Export] private Button _cancel = null!;
    [Export] private Button _create = null!;
    [Export] private Button _join = null!;
    [Export] private Button _random = null!;
    [Export] private Button _refresh = null!;
    [Export] private CheckButton _pause = null!;
    [Export] private CheckButton _globalInput = null!;
    [Export] private CheckButton _showNames = null!;
    [Export] private CheckButton _desktop = null!;
    [Export] private LineEdit _roomName = null!;
    [Export] private LineEdit _roomCode = null!;
    [Export] private VBoxContainer _roomList = null!;
    [Export] private VBoxContainer _memberList = null!;
    [Export] private Node2D _dogRoot = null!;
    [Export] private PackedScene _dogScene = null!;
    [Export] private PackedScene _roomRowScene = null!;
    [Export] private PackedScene _memberRowScene = null!;
    private RoomSandbox _server = new();
    private readonly List<RoomClient> _clients = new();
    private readonly Dictionary<int, RoomDogView> _dogs = new();
    private RoomClient _listClient;
    private RoomListing[] _renderedListings;
    private readonly float[] _scales = { 0.5f, 1, 2, 3, 4 };
    private readonly bool[] _keys = new bool[256];
    private readonly Queue<double> _inputTimes = new();
    private readonly HashSet<int> _heldInput = new();
    private int _viewIndex;
    private int _targetIndex = 1;
    private bool _dirty;
    private bool _binding;
    private bool _passThrough;
    private IntPtr _windowHandle;
    private Vector2I _savedSize;
    private Vector2I _savedPosition;
    private bool _draggingTitle;
    private Vector2I _dragMouseStart;
    private Vector2I _dragWindowStart;
    private double _metricsTimer;
    private double _sampleTimer;
    private DesktopActivityState _candidate;
    private double _candidateTime;
    private double _cooldown;
    private RoomClient Current => _clients[_viewIndex];
    private RoomClient Target => _clients[_targetIndex];
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);

    public override void _Ready()
    {
        if (BuildInfo.Channel != BuildChannel.Dev || BuildInfo.IsDebugDemo || OS.HasFeature("lucky_demo"))
        {
            GD.PushError("RoomLab is development-only and cannot run in Demo.");
            GetTree().Quit(1);
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--rooms-smoke"))
        {
            RunSmokeAndQuit();
            return;
        }
        GetWindow().Title = "Lucky Dog · 房间实验室 A（本地模拟）";
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        GetWindow().ContentScaleSize = Vector2I.Zero;
        GetWindow().Borderless = true;
        OnFitScreen();
        GetViewport().TransparentBg = false;
        if (OS.GetName() == "Windows")
            _windowHandle = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);

        foreach (var scale in _scales) _scaleOption.AddItem($"{scale:0.0}x");
        foreach (var skin in LubanData.Tables.TbDogSkin.DataList)
            _skinOption.AddItem($"{skin.Id} · {skin.IconName}", skin.Id);
        _hatOption.AddItem("无头饰", 0);
        foreach (var item in LubanData.Tables.TbItem.DataList.Where(i => i.ItemType == EItemType.Headwear))
            _hatOption.AddItem($"{item.Id}", item.Id);
        foreach (var reaction in LubanData.Tables.TbDogReaction.DataList.Where(r => r.Id >= 1001 && r.Id <= 1009))
            _reactionOption.AddItem(((EDogReactionTrigger)reaction.Id).ToString(), reaction.Id);
        foreach (var state in LubanData.Tables.TbDesktopActivityState.DataList)
            _activityOption.AddItem($"{state.StateName} · {state.DogReactionTrigger}", state.Id);
        _continuousInput.Toggled += value =>
        {
            if (_binding) return;
            if (value) { _heldInput.Add(Target.Id); Target.NotifyInputActivity(); }
            else { _heldInput.Remove(Target.Id); Target.StopInputActivity(); }
        };
        _sendPause.Toggled += value => { if (!_binding) _server.SetSendingPaused(Target, value); };
        _requestFailure.AddItem("下一请求：正常", (int)MockRoomFailure.None);
        _requestFailure.AddItem("下一请求：服务不可用", (int)MockRoomFailure.Unavailable);
        _requestFailure.AddItem("下一请求：无回应（验证超时）", (int)MockRoomFailure.NoResponse);

        _viewOption.ItemSelected += index => SwitchView((int)index);
        _targetOption.ItemSelected += index => { _targetIndex = (int)index; BindTarget(); };
        _scaleOption.ItemSelected += index => { Current.LocalScale = _scales[index]; _dirty = true; };
        _latency.ValueChanged += value => { if (!_binding) _server.Settings(Target).Latency = value / 1000; };
        _requestDelay.ValueChanged += value => { if (!_binding) _server.Settings(Target).RequestDelay = value / 1000; };
        _requestFailure.ItemSelected += index =>
        {
            if (!_binding) _server.Settings(Target).NextFailure = (MockRoomFailure)_requestFailure.GetItemId((int)index);
        };
        _pause.Toggled += value => { if (!_binding) _server.SetPaused(Target, value); };
        _showNames.Toggled += value => { Current.ShowNames = value; _dirty = true; };
        _globalInput.Toggled += _ => ResetInputSampling();
        _desktop.Toggled += SetDesktop;
        Resized += () => _dirty = true;
        ResetClients(3);
        if (OS.GetCmdlineUserArgs().Contains("--rooms-ui-smoke")) RunUiSmoke();
        if (OS.GetCmdlineUserArgs().Contains("--rooms-kick-smoke")) RunKickSmokeAndQuit();
    }

    private void ResetClients(int count)
    {
        CloseEditors();
        foreach (var client in _clients) client.Dispose();
        _server = new RoomSandbox();
        _clients.Clear();
        _heldInput.Clear();
        _viewIndex = 0;
        _targetIndex = 1;
        _viewOption.Clear();
        _targetOption.Clear();
        var skins = LubanData.Tables.TbDogSkin.DataList.Select(s => s.Id).ToArray();
        for (var i = 0; i < count; i++)
        {
            var client = _server.AddClient(i + 1, $"模拟玩家 {i + 1}", skins[i % skins.Length]);
            client.Changed += () => _dirty = true;
            _clients.Add(client);
            _viewOption.AddItem(client.Name, client.Id);
            _targetOption.AddItem(client.Name, client.Id);
        }
        _server.Create(Current, "小狗一起待着");
        foreach (var client in _clients.Skip(1)) _server.Join(client, Current.JoinedCode);
        foreach (var client in _clients) client.Search();
        _server.Tick(0);
        _viewOption.Select(0);
        _targetOption.Select(1);
        BindTarget();
        BindLocal();
        ResetInputSampling();
        Status($"已启动 {count} 个独立模拟客户端；没有连接 Steam 或加载玩家存档。");
    }

    public void OnResetThree() => ResetClients(3);
    public void OnResetSix() => ResetClients(6);
    public void OnCreate() => StartRequest(Current.Create(_roomName.Text));
    public void OnJoinCode() => StartRequest(Current.Join(_roomCode.Text));
    public void OnRandom() => StartRequest(Current.FindAndJoin());
    private void StartRequest(bool started) => Status(started ? "" : "当前请求尚未结束，可等待或取消。");
    public void OnCancelRequest() => Current.Cancel();
    public void OnTargetCancelRequest() => Target.Cancel();
    public void OnLeave() { Current.Leave(); CloseEditors(); Status("已离开房间。"); }
    public void OnCopyCode()
    {
        if (Current.JoinedCode.Length > 0) DisplayServer.ClipboardSet(Current.JoinedCode);
        Status(Current.JoinedCode.Length > 0 ? "房间码已复制。" : "请先进入房间。");
    }
    public void OnTargetJoin() => StartRequest(Target.Join(Current.JoinedCode));
    public void OnTargetKick()
    {
        var presence = Current.View?.Members.FirstOrDefault(m => m.Id == Target.Id)?.Presence ?? 0;
        var error = Current.Kick(Target.Id, presence);
        if (error.Length == 0) _heldInput.Remove(Target.Id);
        BindTarget();
        Status(L10n.Tr(error.Length == 0 ? "Rooms_KickSent" : error));
    }
    public void OnTargetLeave()
    {
        _heldInput.Remove(Target.Id);
        Target.Leave();
        BindTarget();
        Status("模拟玩家已离开；房主离开时验证新房主。");
    }
    public void OnCloseTargetRoom()
    {
        _server.CloseRoom(Target.JoinedCode);
        Status("已模拟目标房间关闭；等待加入该房间的请求将在完成时检查房间是否仍存在。");
    }
    public void OnApplyAppearance()
    {
        Target.SetAppearance(_skinOption.GetSelectedId(), _hatOption.GetSelectedId(), _reactionOption.GetSelectedId());
        Status("变化已发送，按目标客户端的网络条件观察同步。");
    }
    public void OnMockChat()
    {
        var result = Target.SendChat($"你好，来自{Target.Name}！");
        Status(result.Length > 0 ? InGameRoomPreview.ChatText(result) : "", "模拟消息已发送。");
        if (result.Length > 0 && Target.JoinedCode.Length > 0 && Target.JoinedCode == Current.JoinedCode
            && _dogs.TryGetValue(Target.Id, out var dog) && dog.IsVisibleInTree())
            dog.Chat.ShakeError();
    }
    public void OnApplyActivity()
    {
        var state = LubanData.Tables.TbDesktopActivityState.Get(_activityOption.GetSelectedId());
        Target.SetReaction((int)state.DogReactionTrigger);
        if (!state.EnableTongueFeedback) OnStopActivity();
        BindTarget();
        Status("目标活动表情已发送；只切换表情，不生成键盘输入。");
    }
    public void OnPulseActivity() { Target.NotifyInputActivity(); Status("目标输入活动已触发，停止输入后自动收回舌头。"); }
    public void OnStopActivity()
    {
        _heldInput.Remove(Target.Id);
        Target.StopInputActivity();
        BindTarget();
    }
    public void OnChangeGame()
    {
        Status(Current.SetGame(Current.View?.GameId == "social" ? "test-mode" : "social"),
            "玩法标识已更新；刷新列表可观察图标提示变化。");
    }
    public void OnQuit() => GetTree().Quit();
    public void OnMinimize() => GetWindow().Mode = Window.ModeEnum.Minimized;
    private Rect2I TestArea()
    {
        var usable = DisplayServer.ScreenGetUsableRect(GetWindow().CurrentScreen);
        var reserve = Math.Clamp(_feedbackSpace, 0, Math.Max(0, usable.Size.Y - 400));
        return new Rect2I(usable.Position, new Vector2I(usable.Size.X, usable.Size.Y - reserve));
    }
    public void OnFitScreen()
    {
        GetWindow().Mode = Window.ModeEnum.Windowed;
        var area = TestArea();
        GetWindow().Position = area.Position;
        GetWindow().Size = area.Size;
        _dirty = true;
    }
    public void OnTitleInput(InputEvent input)
    {
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } button) return;
        if (button.DoubleClick) { _draggingTitle = false; OnFitScreen(); return; }
        _draggingTitle = button.Pressed;
        _dragMouseStart = DisplayServer.MouseGetPosition();
        _dragWindowStart = GetWindow().Position;
    }
    public override void _Input(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
            _draggingTitle = false;
    }

    public void OnRefresh() => StartRequest(Current.Search());
    private void RenderRoomList()
    {
        if (ReferenceEquals(_listClient, Current) && ReferenceEquals(_renderedListings, Current.Listings))
        {
            foreach (var row in _roomList.GetChildren().OfType<RoomListRow>()) row.SetRequestPending(Current.IsBusy);
            return;
        }
        _listClient = Current;
        _renderedListings = Current.Listings;
        ClearRows(_roomList);
        foreach (var room in Current.Listings)
        {
            var row = _roomRowScene.Instantiate<RoomListRow>();
            _roomList.AddChild(row);
            row.Bind(room, () => StartRequest(Current.Join(room.Code)), Current.IsBusy);
        }
    }

    private static void ClearRows(Node parent)
    {
        foreach (var child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); }
    }

    private void SwitchView(int index)
    {
        CloseEditors();
        _viewIndex = index;
        BindLocal();
        ResetInputSampling();
        _dirty = true;
    }
    private void BindLocal()
    {
        _scaleOption.Select(Array.IndexOf(_scales, Current.LocalScale));
        _showNames.SetPressedNoSignal(Current.ShowNames);
    }
    private void BindTarget()
    {
        _binding = true;
        _skinOption.Select(_skinOption.GetItemIndex(Target.SkinId));
        _hatOption.Select(_hatOption.GetItemIndex(Target.HeadwearId));
        _reactionOption.Select(_reactionOption.GetItemIndex(Target.Reaction));
        var state = LubanData.Tables.TbDesktopActivityState.DataList.FirstOrDefault(s => (int)s.DogReactionTrigger == Target.Reaction);
        if (state != null) _activityOption.Select(_activityOption.GetItemIndex(state.Id));
        _continuousInput.SetPressedNoSignal(_heldInput.Contains(Target.Id));
        var settings = _server.Settings(Target);
        _latency.Value = settings.Latency * 1000;
        _requestDelay.Value = settings.RequestDelay * 1000;
        _requestFailure.Select(_requestFailure.GetItemIndex((int)settings.NextFailure));
        _pause.SetPressedNoSignal(settings.Paused);
        _sendPause.SetPressedNoSignal(settings.SendingPaused);
        _binding = false;
    }
    private void Status(string text, string success = "") => _status.Text = text.Length > 0 ? text : success;
    private static string OperationText(RoomClient client)
    {
        var action = client.Operation switch
        {
            RoomOperation.Search => "搜索房间", RoomOperation.Create => "创建房间",
            RoomOperation.Join => "加入房间", _ => ""
        };
        return client.RequestState switch
        {
            RoomRequestState.Pending => $"正在{action}…可取消",
            RoomRequestState.Succeeded => $"{action}成功" +
                (client.Operation != RoomOperation.Search && client.View == null ? "，等待成员状态…" : ""),
            RoomRequestState.Cancelled => $"已取消{action}",
            RoomRequestState.TimedOut => $"{action}超时，可重试",
            RoomRequestState.Failed => client.Failure switch
            {
                RoomFailure.InvalidName => "房间名称需要 1–40 个字符。",
                RoomFailure.NotFound => "房间已关闭或不存在，请刷新列表。",
                RoomFailure.Full => "房间已满，请选择其他房间。",
                RoomFailure.NoMatchingRoom => "没有其他可加入房间，可以创建房间。",
                RoomFailure.Removed => L10n.Tr("Rooms_Removed"),
                RoomFailure.Banned => L10n.Tr("Rooms_Banned"),
                _ => "房间服务暂不可用，可稍后重试。"
            },
            _ => ""
        };
    }
    private void CloseEditors() { foreach (var dog in _dogs.Values) dog.CloseChat(); }

    public override void _Process(double delta)
    {
        if (_clients.Count == 0) return;
        if (_draggingTitle)
        {
            if (!Input.IsMouseButtonPressed(MouseButton.Left)) _draggingTitle = false;
            else GetWindow().Position = _dragWindowStart + DisplayServer.MouseGetPosition() - _dragMouseStart;
        }
        foreach (var client in _clients)
            if (_heldInput.Contains(client.Id))
            {
                if (client.JoinedCode.Length == 0) _heldInput.Remove(client.Id);
                else client.NotifyInputActivity();
            }
        _server.Tick(delta);
        ObserveInput(delta);
        if (_dirty) { _dirty = false; RenderCurrent(); }
        _targetOperation.Text = $"目标：{OperationText(Target)}";
        // A fault is consumed by Request(), not by a settings-panel refresh.
        _requestFailure.Select(_requestFailure.GetItemIndex((int)_server.Settings(Target).NextFailure));
        UpdatePassThrough();
        _metricsTimer += delta;
        if (_metricsTimer >= 1)
        {
            _metricsTimer = 0;
            _metrics.Text = $"{Engine.GetFramesPerSecond()} FPS · 待投递 {_server.PendingCount} · 请求 {_server.PendingRequestCount}\n视角 {Current.Id} · 状态版本 {Current.View?.Revision ?? 0}";
        }
    }

    private void RenderCurrent()
    {
        RenderRoomList();
        _operation.Text = OperationText(Current);
        _cancel.Disabled = !Current.IsBusy;
        _create.Disabled = _join.Disabled = _random.Disabled = _refresh.Disabled = Current.IsBusy;
        var view = Current.View;
        _currentRoom.Text = view == null ? (Current.JoinedCode.Length > 0 ? "等待房间状态…" : "尚未加入房间")
            : $"{view.Name} · {view.Members.Length}/6\n{view.Code}";
        var members = view?.Members ?? Array.Empty<RoomMember>();
        ClearRows(_memberList);
        foreach (var member in members)
        {
            var row = _memberRowScene.Instantiate<RoomMemberRow>();
            _memberList.AddChild(row);
            row.Bind(member, member.Id == view.OwnerId, member.Id == Current.Id,
                Current.HiddenMembers.Contains(member.Id), () =>
                {
                    if (!Current.HiddenMembers.Add(member.Id)) Current.HiddenMembers.Remove(member.Id);
                    _dirty = true;
                });
        }
        foreach (var id in _dogs.Keys.Except(members.Select(m => m.Id)).ToArray())
        {
            _dogs[id].QueueFree();
            _dogs.Remove(id);
        }
        var scale = Current.LocalScale;
        _titleBar.Size = new Vector2(Size.X, 44);
        _toolbar.Position = new Vector2(0, _titleBar.Size.Y);
        _toolbar.Size = new Vector2(Size.X, 48);
        var contentTop = _toolbar.Position.Y + _toolbar.Size.Y + 12;
        var panelHeight = Math.Max(200, Size.Y - contentTop - 16);
        _panel.Position = new Vector2(16, contentTop);
        _panel.Size = new Vector2(420, panelHeight);
        _tools.Size = new Vector2(400, panelHeight);
        _tools.Position = new Vector2(Math.Max(450, Size.X - _tools.Size.X - 16), contentTop);
        var left = _panel.Position.X + _panel.Size.X + 16;
        var width = Math.Max(240, _tools.Position.X - left - 16);
        var columns = Math.Clamp((int)(width / (240 * scale)), 1, 3);
        var rows = Math.Max(1, (int)Math.Ceiling(members.Length / (double)columns));
        var spacingY = Math.Max(220 * scale, (Size.Y - contentTop - 96) / rows);
        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            if (!_dogs.TryGetValue(member.Id, out var dog))
            {
                dog = _dogScene.Instantiate<RoomDogView>();
                dog.Name = $"Dog{member.Id}";
                _dogRoot.AddChild(dog);
                _dogs.Add(member.Id, dog);
                var target = dog;
                dog.SendRequested += text =>
                {
                    var result = Current.SendChat(text);
                    Status(result.Length > 0 ? InGameRoomPreview.ChatText(result) : "", "已发送。");
                    target.Chat.SetSendResult(result);
                };
            }
            dog.Scale = Vector2.One * scale;
            dog.Position = new Vector2(left + width / columns * (i % columns + 0.5f),
                Size.Y - 85 * scale - (rows - 1 - i / columns) * spacingY);
            dog.Visible = !Current.HiddenMembers.Contains(member.Id) || member.Id == Current.Id;
            dog.Display(member, member.Id == Current.Id, member.Id == view.OwnerId, Current.ShowNames,
                Current.Bubbles.TryGetValue(member.Id, out var bubble) ? bubble.Text : "",
                Current.IsTongueActive(member.Id));
        }
    }

    private void ResetInputSampling()
    {
        _inputTimes.Clear();
        _candidate = null;
        _candidateTime = _cooldown = 0;
        if (OS.GetName() == "Windows")
            for (var key = 1; key < 256; key++) _keys[key] = (GetAsyncKeyState(key) & 0x8000) != 0;
    }
    private void ObserveInput(double delta)
    {
        if (!_globalInput.ButtonPressed || OS.GetName() != "Windows") return;
        // A-only polling: records edge counts, never key contents or player statistics.
        _sampleTimer += delta;
        if (_sampleTimer >= 0.02)
        {
            _sampleTimer = 0;
            for (var key = 1; key < 256; key++)
            {
                var down = (GetAsyncKeyState(key) & 0x8000) != 0;
                if (down && !_keys[key])
                {
                    _inputTimes.Enqueue(_server.Now);
                    Current.NotifyInputActivity();
                }
                _keys[key] = down;
            }
        }
        while (_inputTimes.Count > 0 && _server.Now - _inputTimes.Peek() > 10) _inputTimes.Dequeue();
        var elapsed = _inputTimes.Count == 0 ? 10 : Math.Min(10, Math.Max(1, _server.Now - _inputTimes.Peek()));
        var rate = _inputTimes.Count / elapsed * 60;
        var state = LubanData.Tables.TbDesktopActivityState.DataList
            .Where(s => rate >= s.MinInputEventsPerMinute && (s.MaxInputEventsPerMinute == 0 || rate <= s.MaxInputEventsPerMinute))
            .OrderByDescending(s => s.Priority).FirstOrDefault();
        _cooldown = Math.Max(0, _cooldown - delta);
        if (state == null || (int)state.DogReactionTrigger == Current.Reaction || _cooldown > 0) return;
        if (_candidate?.Id != state.Id) { _candidate = state; _candidateTime = 0; }
        _candidateTime += delta;
        if (_candidateTime < state.MinDurationSeconds) return;
        Current.SetReaction((int)state.DogReactionTrigger);
        _cooldown = state.CooldownSeconds;
        _candidateTime = 0;
    }

    private void SetDesktop(bool enabled)
    {
        SetPassThrough(false);
        if (enabled)
        {
            _savedSize = GetWindow().Size;
            _savedPosition = GetWindow().Position;
            OnFitScreen();
        }
        else { GetWindow().Size = _savedSize; GetWindow().Position = _savedPosition; }
        GetViewport().TransparentBg = enabled;
        _backdrop.Visible = !enabled;
        _dirty = true;
    }
    private bool Over(Control control) => control.IsVisibleInTree()
        && new Rect2(Vector2.Zero, control.Size).HasPoint(control.GetLocalMousePosition());
    private void UpdatePassThrough()
    {
        var popup = _viewOption.GetPopup().Visible || _targetOption.GetPopup().Visible
            || _scaleOption.GetPopup().Visible || _skinOption.GetPopup().Visible
            || _hatOption.GetPopup().Visible || _reactionOption.GetPopup().Visible || _activityOption.GetPopup().Visible
            || _requestFailure.GetPopup().Visible;
        SetPassThrough(_desktop.ButtonPressed && !_draggingTitle && !popup
            && !Over(_titleBar) && !Over(_toolbar) && !Over(_panel) && !Over(_tools)
            && !_dogs.Values.Any(d => d.IsPointerOverContent()));
    }
    private void SetPassThrough(bool enabled)
    {
        if (_windowHandle == IntPtr.Zero || _passThrough == enabled) return;
        var style = WindowNative.GetWindowLong(_windowHandle, WindowNative.GWL_EXSTYLE);
        style = enabled ? style | WindowNative.WS_EX_TRANSPARENT | WindowNative.WS_EX_LAYERED
            : style & ~WindowNative.WS_EX_TRANSPARENT;
        WindowNative.SetWindowLong(_windowHandle, WindowNative.GWL_EXSTYLE, style);
        _passThrough = enabled;
    }
    public override void _ExitTree()
    {
        SetPassThrough(false);
        foreach (var client in _clients) client.Dispose();
    }

    // Development bridge: same commands as the visible controls, not a second UI implementation.
    public string DebugCommand(string command, int value = 0)
    {
        switch (command)
        {
            case "view": _viewOption.Select(value); SwitchView(value); break;
            case "target": _targetIndex = value; _targetOption.Select(value); BindTarget(); break;
            case "scale": Current.LocalScale = _scales[value]; BindLocal(); _dirty = true; break;
            case "six": OnResetSix(); break;
            case "three": OnResetThree(); break;
            case "chat": OnMockChat(); break;
            case "leave": OnTargetLeave(); break;
            case "join": OnTargetJoin(); break;
            case "kick": OnTargetKick(); break;
            case "pause": _server.SetPaused(Target, value != 0); BindTarget(); break;
            case "reaction": Target.SetAppearance(Target.SkinId, Target.HeadwearId, value); break;
            case "activity": _activityOption.Select(_activityOption.GetItemIndex(value)); OnApplyActivity(); break;
            case "pulse": OnPulseActivity(); break;
            case "stop-input": OnStopActivity(); break;
            case "hold-input": _continuousInput.ButtonPressed = value != 0; break;
            case "pause-send": _sendPause.ButtonPressed = value != 0; break;
            case "desktop": _desktop.ButtonPressed = value != 0; break;
            case "input": _globalInput.ButtonPressed = value != 0; break;
        }
        _server.Tick(0);
        return DebugState();
    }
    public string DebugState() => JsonSerializer.Serialize(new
    {
        view = Current.Id, scale = Current.LocalScale, room = Current.View,
        clients = _clients.Select(c => new { c.Id, c.LocalScale, c.JoinedCode, c.Reaction, c.TongueActive, c.ActivitySequence,
            sendingPaused = _server.Settings(c).SendingPaused,
            paused = _server.Settings(c).Paused, c.Operation, c.RequestState, c.Failure, revision = c.View?.Revision }),
        pending = _server.PendingCount, passThrough = _passThrough, status = _status.Text,
    });
    public async void SavePreview(string path)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(path);
    }
    private async void RunSmokeAndQuit()
    {
        try
        {
            GD.Print(RoomSandboxChecks.Run());
            SteamRoomChecks.Run();
            GD.Print("[SteamRoomChecks] PASS protocol, callbacks, membership, appearance, activity leases, chat, removal, invitation admission/queue/recovery and startup args (fake transport and filter API delegates; no native Steam).");
            await SteamRoomPageChecks.Run(this);
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); GetTree().Quit(1); }
    }

    private async void RunUiSmoke()
    {
        try
        {
            _globalInput.ButtonPressed = false;
            await UiFrame();
            await UiFrame();
            Verify(_dogs.Count == 3 && Current.View.Members.Length == 3, "three rendered dogs");
            await RunFirstChatFrameSmoke();
            var lobbyRow = _roomList.GetChild(0);
            OnApplyAppearance();
            _server.Tick(0);
            DebugCommand("reaction", 1006);
            await UiFrame();
            Verify(ReferenceEquals(lobbyRow, _roomList.GetChild(0)), "appearance sync preserves lobby buttons");
            Verify(Current.View.Members.Single(m => m.Id == Target.Id).Reaction == 1006, "button path updates remote dog");
            await RunActivityUiSmoke();
            DebugCommand("scale", 2);
            await UiFrame();
            Verify(_dogs.Values.All(d => d.Scale == Vector2.One * 2), "all visible dogs scale locally");
            SwitchView(1);
            await UiFrame();
            Verify(Current.LocalScale == 1 && _dogs.Values.All(d => d.Scale == Vector2.One), "second view stays 1x");
            SwitchView(0);
            DebugCommand("scale", 1);
            await UiFrame();
            _dogs[Current.Id].OpenChat();
            Verify(_dogs[Current.Id].Editing, "local bubble opens input");
            await UiFrame();
            await CaptureSmoke("chat-composer");
            _dogs[Current.Id].SubmitChatForSmoke("你好！本机消息测试。");
            _server.Tick(0);
            Verify(!_dogs[Current.Id].Editing && Current.Bubbles.ContainsKey(Current.Id), "send button delivers and closes input");
            OnMockChat();
            _server.Tick(0);
            await UiFrame();
            Verify(Current.Bubbles.ContainsKey(Target.Id), "remote bubble received");
            await CaptureSmoke("three");
            await RunChatUiSmoke();
            Current.HiddenMembers.Add(Target.Id);
            _dirty = true;
            await UiFrame();
            Verify(!_dogs[Target.Id].Visible, "per-member local hide");
            SwitchView(2);
            await UiFrame();
            Verify(_dogs[Target.Id].Visible, "hide does not leak to other client");
            OnResetSix();
            await UiFrame();
            Verify(_dogs.Count == 6, "six dog scene instances");
            await CaptureSmoke("six");
            var size = GetWindow().Size;
            var position = GetWindow().Position;
            _desktop.ButtonPressed = true;
            await UiFrame();
            Verify(GetWindow().Size == TestArea().Size, "desktop window preserves feedback space");
            _desktop.ButtonPressed = false;
            Verify(GetWindow().Size == size && GetWindow().Position == position, "window restore");
            OnTargetLeave();
            _server.Tick(0);
            await UiFrame();
            Verify(_dogs.Count == 5, "departure removes visual");
            OnTargetJoin();
            _server.Tick(0);
            await UiFrame();
            Verify(_dogs.Count == 6, "late join restores visual");
            await RunRequestUiSmoke();
            await RunKickUiSmoke();
            GD.Print("ROOM_UI_PASS: real dog scenes, appearance, local scale/hide, bubble input, six players, window restore, join/leave, async request controls, host kick controls.");
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); GetTree().Quit(1); }
    }
    private async System.Threading.Tasks.Task RunActivityUiSmoke()
    {
        var dog = _dogs[Target.Id].Dog;
        // Only override this replica; never change persisted player settings.
        dog.Set("_desktopTongueImmediateMode", true);
        foreach (int state in new[] { 3, 4, 1 })
        {
            _activityOption.Select(_activityOption.GetItemIndex(state));
            OnApplyActivity();
            _server.Tick(0);
            await UiFrame();
            Verify(dog.CurrentReaction == LubanData.Tables.TbDesktopActivityState.Get(state).DogReactionTrigger,
                "activity control reaches the real dog reaction");
        }
        _continuousInput.ButtonPressed = true;
        _server.Tick(0);
        await UiFrame();
        Verify(dog.RoomTongueActive && !_dogs[Current.Id].Dog.RoomTongueActive,
            "only selected dog becomes active");
        var tongue = dog.GetNode<Sprite2D>("HeadRoot/Tonghe");
        var first = tongue.Position;
        dog._Process(0.13);
        Verify(tongue.Position != first, "remote tongue animates despite instant setting");
        _sendPause.ButtonPressed = true;
        for (int i = 0; i < 8; i++) { Target.NotifyInputActivity(); _server.Tick(0.5); }
        await UiFrame();
        Verify(!dog.RoomTongueActive, "sending outage expires remote animation");
        _sendPause.ButtonPressed = false;
        _server.Tick(0);
        await UiFrame();
        Verify(dog.RoomTongueActive, "restored sender resumes animation");
        OnStopActivity();
        _server.Tick(0);
        await UiFrame();
        Verify(!dog.RoomTongueActive, "stop control stops remote animation");
        var stopped = tongue.Position;
        dog._Process(1);
        Verify(tongue.Position == stopped, "stopped tongue remains at rest");
        await CaptureSmoke("activity-idle");
        GD.Print("ROOM_ACTIVITY_UI_PASS: activity controls, reactions, independent tongue animation, forced smooth, outage timeout and recovery.");
    }

    private async System.Threading.Tasks.Task RunFirstChatFrameSmoke()
    {
        int previousTarget = _targetIndex;
        for (int target = 0; target < 3; target++)
        {
            DebugCommand("target", target);
            OnMockChat();
            _server.Tick(0);
            RenderCurrent();
            var panel = _dogs[Target.Id].Chat.GetNode<PanelContainer>("Message");
            Vector2? firstSize = null;
            // Check every rendered frame: waiting for two layouts hides the original flash.
            for (int frame = 0; frame < 4; frame++)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (!panel.Visible) continue;
                Verify(panel.Size.X <= 301 && panel.Size.Y <= 90,
                    $"first message has its final compact size (dog {Target.Id}, frame {frame}, {panel.Size})");
                firstSize ??= panel.Size;
                Verify(panel.Size.IsEqualApprox(firstSize.Value), "first bubble does not resize after becoming visible");
            }
            Verify(firstSize.HasValue, "first message is rendered");
            var chat = _dogs[Target.Id].Chat;
            var messageId = Current.Bubbles[Target.Id].Id;
            OnMockChat();
            await VerifyChatShake(chat, panel);
            Verify(Current.Bubbles[Target.Id].Id == messageId && !chat.Editing,
                "target rate-limit feedback neither sends a new message nor opens an input");
        }
        DebugCommand("target", previousTarget);
        _server.Tick(7);
        await UiFrame();
        GD.Print("ROOM_CHAT_FIRST_FRAME_PASS: all three first messages remain compact on every rendered frame.");
    }

    private async System.Threading.Tasks.Task RunChatUiSmoke()
    {
        var chat = _dogs[Current.Id].Chat;
        var tip = chat.GlobalPosition;
        var shortRect = chat.MessageRectForSmoke;
        chat.SubmitForSmoke("too soon");
        Verify(chat.Editing && chat.ErrorForSmoke == "Rooms_ChatTooFast", "failed send keeps editor open with feedback");
        await VerifyChatShake(chat, chat.GetNode<PanelContainer>("Composer"));
        Verify(chat.GetNode<LineEdit>("Composer/Content/Input").Text == "too soon",
            "shake preserves unsent text");
        chat._Input(new InputEventKey { Pressed = true, Keycode = Key.Escape });
        Verify(!chat.Editing, "Escape closes editor");
        _server.Tick(2);
        chat.SubmitForSmoke("大家好！这是一条较长的聊天消息，用来检查自动换行。气泡向右和向上延展，尾巴仍然指着同一只小狗。Hello from the room!");
        _server.Tick(0);
        await UiFrame(); await UiFrame();
        var longRect = chat.MessageRectForSmoke;
        Verify(chat.GlobalPosition == tip && longRect.Size.Y > shortRect.Size.Y
            && longRect.Position.Y < shortRect.Position.Y, "wrapped bubble grows upward with stationary tip");
        await CaptureSmoke("chat-long");
        chat.GlobalPosition = new Vector2(Size.X - 15, 20);
        chat.Present(true, true, "edge");
        await UiFrame();
        var trigger = chat.ButtonRectForSmoke;
        Verify(trigger.Position.X >= 0 && trigger.Position.Y >= 0 && trigger.End.X <= Size.X,
            "hover entry remains reachable near screen edges");
        chat.OpenChat();
        await UiFrame(); await UiFrame();
        var editor = chat.ComposerRectForSmoke;
        await CaptureSmoke("chat-edge");
        Verify(editor.Position.X >= 0 && editor.Position.Y >= 0 && editor.End.X <= Size.X
            && editor.End.Y <= Size.Y,  $"top-right editor stays within viewport: {editor}, size {Size}, tip {chat.GlobalPosition}");
        chat.GlobalPosition = tip;
        chat.CloseChat();
        _server.Tick(7);
        await UiFrame();
        Verify(Current.Bubbles.Count == 0, "UI messages expire without history");
        GD.Print("ROOM_CHAT_UI_PASS: shared composer, errors, Escape, wrapping, fixed tip, viewport edges and expiry.");
    }

    private async System.Threading.Tasks.Task VerifyChatShake(RoomChatView chat, PanelContainer panel)
    {
        var tip = chat.GlobalPosition;
        var samples = new List<float>();
        ulong started = Time.GetTicksMsec();
        while (Time.GetTicksMsec() - started < 500)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            samples.Add(panel.Position.X);
            Verify(chat.GlobalPosition.IsEqualApprox(tip), "error shake keeps the dog and tail anchor stationary");
        }
        float settled = panel.Position.X;
        Verify(samples.Max() > settled + 0.5f && samples.Min() < settled - 0.5f,
            "failed send visibly shakes the bubble in both directions");
        Verify(Mathf.IsEqualApprox(samples[^2], settled), "error shake settles without position drift");
    }

    private async System.Threading.Tasks.Task RunRequestUiSmoke()
    {
        OnResetThree();
        DebugCommand("target", 0);
        _requestDelay.Value = 3000;
        var previous = Current.JoinedCode;
        _roomName.Text = "异步创建测试";
        _create.EmitSignal(Button.SignalName.Pressed);
        await UiFrame();
        Verify(Current.IsBusy && _create.Disabled && _join.Disabled && !_cancel.Disabled,
            "pending request disables repeated actions and enables cancellation");
        Verify(Current.JoinedCode == previous, "old room remains while waiting");
        await CaptureSmoke("request-pending");
        _cancel.EmitSignal(Button.SignalName.Pressed);
        _server.Tick(4);
        await UiFrame();
        Verify(Current.RequestState == RoomRequestState.Cancelled && Current.JoinedCode == previous,
            "cancelled request cannot create a room later");
        Verify(!_create.Disabled && _cancel.Disabled, "cancellation restores controls");

        _requestDelay.Value = 0;
        SelectRequestFailure(MockRoomFailure.Unavailable);
        _create.EmitSignal(Button.SignalName.Pressed);
        await UiFrame();
        Verify(Current.RequestState == RoomRequestState.Failed && Current.JoinedCode == previous,
            "request failure keeps original room");
        Verify(_requestFailure.GetSelectedId() == (int)MockRoomFailure.None, "one-shot fault resets visible selector");
        _create.EmitSignal(Button.SignalName.Pressed);
        await UiFrame();
        Verify(Current.RequestState == RoomRequestState.Succeeded && Current.JoinedCode != previous
            && Current.View?.Members.Length == 1, "retry enters new room");

        SelectRequestFailure(MockRoomFailure.NoResponse);
        _refresh.EmitSignal(Button.SignalName.Pressed);
        await UiFrame();
        _server.Tick(RoomRules.RequestTimeout);
        await UiFrame();
        Verify(Current.RequestState == RoomRequestState.TimedOut && !_refresh.Disabled,
            "silent request times out and can retry");
        await CaptureSmoke("request-timeout");
        _refresh.EmitSignal(Button.SignalName.Pressed);
        await UiFrame();
        Verify(Current.RequestState == RoomRequestState.Succeeded && Current.Listings.Length == 2,
            "refresh recovers after timeout");
    }
    private void SelectRequestFailure(MockRoomFailure failure)
    {
        var index = _requestFailure.GetItemIndex((int)failure);
        _requestFailure.Select(index);
        _requestFailure.EmitSignal(OptionButton.SignalName.ItemSelected, index);
    }
    private async void RunKickSmokeAndQuit()
    {
        try
        {
            _globalInput.ButtonPressed = false;
            await RunKickUiSmoke();
            GD.Print("[RoomKickLabChecks] PASS target removal, replica cleanup, denied reentry and fresh room controls.");
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); GetTree().Quit(1); }
    }

    private async System.Threading.Tasks.Task RunKickUiSmoke()
    {
        OnResetThree();
        await UiFrame();
        var code = Current.JoinedCode;
        var target = Target;
        GetNode<Button>("Tools/Scroll/Content/Kick").EmitSignal(Button.SignalName.Pressed);
        await UiFrame();
        Verify(target.Failure == RoomFailure.Removed && target.View == null && _dogs.Count == 2,
            "target kick button removes the target and its visual");
        OnTargetJoin();
        await UiFrame();
        Verify(target.Failure == RoomFailure.Banned && target.JoinedCode == "",
            "target join button cannot bypass room ban");
        DebugCommand("view", 1);
        await UiFrame();
        Verify(Current.Failure == RoomFailure.Banned && _operation.Text == L10n.Tr("Rooms_Banned")
            && _dogs.Count == 0, "removed observer sees the ban reason and no stale room dogs");
        _roomName.Text = "被请出后创建新房间";
        OnCreate();
        await UiFrame();
        Verify(Current.JoinedCode != "" && Current.JoinedCode != code && _dogs.Count == 1,
            "removed player can explicitly create another room");
    }
    private async System.Threading.Tasks.Task UiFrame()
    {
        // process_frame fires before _Process; allow a full update between assertions.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private async System.Threading.Tasks.Task CaptureSmoke(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var folder = ProjectSettings.GlobalizePath("res://../.local-build/room-lab");
        System.IO.Directory.CreateDirectory(folder);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(folder, name + ".png"));
    }
    private static void Verify(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("Room UI check: " + label);
    }
}
#endif
