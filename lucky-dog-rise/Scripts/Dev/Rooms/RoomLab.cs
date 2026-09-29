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
    [Export] private SpinBox _latency = null!;
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
    private readonly float[] _scales = { 0.5f, 1, 2, 3, 4 };
    private readonly bool[] _keys = new bool[256];
    private readonly Queue<double> _inputTimes = new();
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

        _viewOption.ItemSelected += index => SwitchView((int)index);
        _targetOption.ItemSelected += index => { _targetIndex = (int)index; BindTarget(); };
        _scaleOption.ItemSelected += index => { Current.LocalScale = _scales[index]; _dirty = true; };
        _latency.ValueChanged += value => { if (!_binding) Target.Latency = value / 1000; };
        _pause.Toggled += value => { if (!_binding) _server.SetPaused(Target, value); };
        _showNames.Toggled += value => { Current.ShowNames = value; _dirty = true; };
        _globalInput.Toggled += _ => ResetInputSampling();
        _desktop.Toggled += SetDesktop;
        Resized += () => _dirty = true;
        ResetClients(3);
        if (OS.GetCmdlineUserArgs().Contains("--rooms-ui-smoke")) RunUiSmoke();
    }

    private void ResetClients(int count)
    {
        CloseEditors();
        _server = new RoomSandbox();
        _clients.Clear();
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
        _server.Tick(0);
        _viewOption.Select(0);
        _targetOption.Select(1);
        BindTarget();
        BindLocal();
        OnRefresh();
        ResetInputSampling();
        Status($"已启动 {count} 个独立模拟客户端；没有连接 Steam 或加载玩家存档。");
    }

    public void OnResetThree() => ResetClients(3);
    public void OnResetSix() => ResetClients(6);
    public void OnCreate() { Status(_server.Create(Current, _roomName.Text), "房间已创建。"); OnRefresh(); }
    public void OnJoinCode() { Status(_server.Join(Current, _roomCode.Text), "正在加入房间。"); OnRefresh(); }
    public void OnRandom()
    {
        var room = _server.Search().FirstOrDefault(r => r.Count < r.Capacity && r.Code != Current.JoinedCode);
        Status(room == null ? "没有其他可加入房间，可以创建房间。" : _server.Join(Current, room.Code), "正在加入房间。");
    }
    public void OnLeave() { _server.Leave(Current); CloseEditors(); OnRefresh(); Status("已离开房间。"); }
    public void OnCopyCode()
    {
        if (Current.JoinedCode.Length > 0) DisplayServer.ClipboardSet(Current.JoinedCode);
        Status(Current.JoinedCode.Length > 0 ? "房间码已复制。" : "请先进入房间。");
    }
    public void OnTargetJoin() { Status(_server.Join(Target, Current.JoinedCode), "模拟玩家已加入。"); OnRefresh(); }
    public void OnTargetLeave() { _server.Leave(Target); OnRefresh(); Status("模拟玩家已离开；房主离开时验证新房主。"); }
    public void OnApplyAppearance()
    {
        Target.SetAppearance(_skinOption.GetSelectedId(), _hatOption.GetSelectedId(), _reactionOption.GetSelectedId());
        Status("变化已发送，按目标客户端的网络条件观察同步。");
    }
    public void OnMockChat() => Status(_server.SendChat(Target, $"你好，来自{Target.Name}！"), "模拟消息已发送。");
    public void OnChangeGame()
    {
        Status(_server.SetGame(Current, Current.View?.GameId == "social" ? "test-mode" : "social"),
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

    public void OnRefresh()
    {
        ClearRows(_roomList);
        foreach (var room in _server.Search())
        {
            var row = _roomRowScene.Instantiate<RoomListRow>();
            _roomList.AddChild(row);
            row.Bind(room, () => { Status(_server.Join(Current, room.Code), "正在加入房间。"); });
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
        _latency.Value = Target.Latency * 1000;
        _pause.SetPressedNoSignal(Target.Paused);
        _binding = false;
    }
    private void Status(string text, string success = "") => _status.Text = text.Length > 0 ? text : success;
    private void CloseEditors() { foreach (var dog in _dogs.Values) dog.CloseChat(); }

    public override void _Process(double delta)
    {
        if (_clients.Count == 0) return;
        if (_draggingTitle)
        {
            if (!Input.IsMouseButtonPressed(MouseButton.Left)) _draggingTitle = false;
            else GetWindow().Position = _dragWindowStart + DisplayServer.MouseGetPosition() - _dragMouseStart;
        }
        _server.Tick(delta);
        ObserveInput(delta);
        if (_dirty) { _dirty = false; RenderCurrent(); }
        UpdatePassThrough();
        _metricsTimer += delta;
        if (_metricsTimer >= 1)
        {
            _metricsTimer = 0;
            _metrics.Text = $"{Engine.GetFramesPerSecond()} FPS · 待投递 {_server.PendingCount} · 视角 {Current.Id} · 状态版本 {Current.View?.Revision ?? 0}";
        }
    }

    private void RenderCurrent()
    {
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
                    var result = _server.SendChat(Current, text);
                    Status(result, "已发送。");
                    if (result.Length == 0) target.AcceptSend();
                };
            }
            dog.Scale = Vector2.One * scale;
            dog.Position = new Vector2(left + width / columns * (i % columns + 0.5f),
                Size.Y - 85 * scale - (rows - 1 - i / columns) * spacingY);
            dog.Visible = !Current.HiddenMembers.Contains(member.Id) || member.Id == Current.Id;
            dog.Display(member, member.Id == Current.Id, member.Id == view.OwnerId, Current.ShowNames,
                Current.Bubbles.TryGetValue(member.Id, out var bubble) ? bubble.Text : "");
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
                if (down && !_keys[key]) _inputTimes.Enqueue(_server.Now);
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
        Current.SetAppearance(Current.SkinId, Current.HeadwearId, (int)state.DogReactionTrigger);
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
            || _hatOption.GetPopup().Visible || _reactionOption.GetPopup().Visible;
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
    public override void _ExitTree() => SetPassThrough(false);

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
            case "pause": _server.SetPaused(Target, value != 0); BindTarget(); break;
            case "reaction": Target.SetAppearance(Target.SkinId, Target.HeadwearId, value); break;
            case "desktop": _desktop.ButtonPressed = value != 0; break;
            case "input": _globalInput.ButtonPressed = value != 0; break;
        }
        _server.Tick(0);
        return DebugState();
    }
    public string DebugState() => JsonSerializer.Serialize(new
    {
        view = Current.Id, scale = Current.LocalScale, room = Current.View,
        clients = _clients.Select(c => new { c.Id, c.LocalScale, c.JoinedCode, c.Reaction, c.Paused, revision = c.View?.Revision }),
        pending = _server.PendingCount, passThrough = _passThrough, status = _status.Text,
    });
    public async void SavePreview(string path)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(path);
    }
    private void RunSmokeAndQuit()
    {
        try { GD.Print(RoomSandboxChecks.Run()); GetTree().Quit(); }
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
            OnApplyAppearance();
            _server.Tick(0);
            DebugCommand("reaction", 1006);
            await UiFrame();
            Verify(Current.View.Members.Single(m => m.Id == Target.Id).Reaction == 1006, "button path updates remote dog");
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
            _dogs[Current.Id].SubmitChatForSmoke("你好！本机消息测试。");
            _server.Tick(0);
            Verify(!_dogs[Current.Id].Editing && Current.Bubbles.ContainsKey(Current.Id), "send button delivers and closes input");
            OnMockChat();
            _server.Tick(0);
            await UiFrame();
            Verify(Current.Bubbles.ContainsKey(Target.Id), "remote bubble received");
            await CaptureSmoke("three");
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
            GD.Print("ROOM_UI_PASS: real dog scenes, appearance, local scale/hide, bubble input, six players, window restore, join/leave.");
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); GetTree().Quit(1); }
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
