using System;
using System.Collections.Generic;
using System.Linq;
using DataTables;
using Godot;

namespace LuckyDogRise;

public partial class ModeManager : Control
{
    public enum Mode { BossKey, Play, Immersive }
    public Mode CurrentMode { get; private set; } = Mode.BossKey;

    private SystemPanelController _settingsPanel = null!;
    public SystemPanelController SettingsPanelObj => _settingsPanel;
    private Node2D _bossKeyContent = null!;
    private DogVisual _bossDogVisual = null!;
    private GameManager _gameManager = null!;
    private Label _mainText = null!;
    private Vector2 _windowBaseSize;
    private Vector2 _panelSize;
    private Vector2 _contentOffset;

    private bool _isDragging, _potentialDrag, _isClickThrough = true;
    private Vector2I _mouseScreenStart, _windowPosStart;
    private const float DragThreshold = 5f;

    private bool _taskbarSnapped;
    private const int SnapThreshold = 15;
    private const int BreakawayThreshold = 30;

    private Rect2 _dogHitRect;
    private Rect2 _btnHitRect;

    private GameData _gameData = null!;
    public GameData GameDataObj => _gameData;

    private static readonly EItemType[] DebugDogItemTypes =
    [
        EItemType.Dog,
        EItemType.Headwear,
        EItemType.Eyewear,
    ];

    private static readonly EItemType[] DebugSceneItemTypes = Enum.GetValues<EItemType>()
        .Where(type => !DebugDogItemTypes.Contains(type))
        .ToArray();

    private readonly Random _debugRandom = new();
    private readonly Dictionary<EItemType, ShuffleBag<int>> _debugItemBags = new();

    public override void _Ready()
    {
        _gameData = new GameData();
        _gameData.Name = "GameData";
        AddChild(_gameData);

        _bossKeyContent = GD.Load<PackedScene>("res://Scenes/BossKeyContent.tscn").Instantiate<Node2D>();
        _bossKeyContent.Name = "BossKeyContent";
        AddChild(_bossKeyContent);
        _bossDogVisual = _bossKeyContent.GetNode<DogVisual>("ContentA/DogArea");
        _bossDogVisual.GameData = _gameData;
        RefreshBossDogVisuals();
        _gameData.EquipmentChanged += RefreshBossDogVisuals;
        _mainText = _bossKeyContent.GetNode<Label>("CanvasLayer/Panel/HBoxContainer/MainText");
        var modeBtn = _bossKeyContent.GetNode<Button>("CanvasLayer/Panel/HBoxContainer/ModeSwitch");
        var sysBtn = _bossKeyContent.GetNode<Button>("CanvasLayer/Panel/HBoxContainer/SystemButton");
        modeBtn.Pressed += SwitchToPlay;
        sysBtn.Pressed += ToggleSettingsPanel;

        // 先实例化面板以读取实际尺寸
        _settingsPanel = GD.Load<PackedScene>("res://Scenes/SystemPanel.tscn").Instantiate<SystemPanelController>();
        _settingsPanel.Name = "SettingsPanel";
        _settingsPanel.Layer = 100;
        AddChild(_settingsPanel);
        _settingsPanel.GameData = _gameData;
        _settingsPanel.SwitchToPlayRequested += SwitchToPlay;
        _settingsPanel.SwitchToBossKeyRequested += SwitchToBossKey;
        _settingsPanel.RandomizeRequested += OnRandomizeScene;
        _settingsPanel.RandomizeDogRequested += OnRandomizeDog;
        _settingsPanel.DogReactionRequested += OnDogReactionRequested;

        _panelSize = _settingsPanel.PanelSize;
        _contentOffset = _panelSize;

        _dogHitRect = new Rect2(60 + _contentOffset.X, 90 + _contentOffset.Y, 180, 180);
        _btnHitRect = new Rect2(50 + _contentOffset.X, 265 + _contentOffset.Y, 240, 40);

        _windowBaseSize = _bossKeyContent.GetNode<Marker2D>("ContentA/WindowSize").Position;
        _bossKeyContent.GetNode<Node2D>("ContentA").Position = _contentOffset;
        SetupFatWindow();
        SetWindowAboveTaskbar();
        DisplayServer.WindowSetPosition(DisplayServer.WindowGetPosition());
        EnableLayeredWindow();

        _bossKeyContent.GetNode<CanvasLayer>("CanvasLayer").Offset = _contentOffset;
        _bossKeyContent.GetNode<CanvasLayer>("Bubble").Offset = _contentOffset;
        _bossKeyContent.GetNode<CanvasLayer>("Bubble").Visible = false;

        var tracker = new GlobalInputTracker();
        tracker.Name = "GlobalInputTracker";
        tracker.GameData = _gameData;
        tracker.TypingInputOccurred += OnTypingInputOccurred;
        AddChild(tracker);
    }

    private double _displayTimer;
    private SettingsManager.DisplayMode _lastMode = (SettingsManager.DisplayMode)(-1);

    public override void _Process(double _)
    {
        var mode = SettingsManager.CurrentDisplayMode;
        if (mode != _lastMode)
        {
            _lastMode = mode;
            _mainText.Text = mode switch
            {
                SettingsManager.DisplayMode.Clock => DateTime.Now.ToString("HH:mm"),
                SettingsManager.DisplayMode.Hidden => "",
                _ => "0"
            };
        }
        if (mode == SettingsManager.DisplayMode.Clock)
            _mainText.Text = DateTime.Now.ToString("HH:mm");
        else if (mode == SettingsManager.DisplayMode.Chips)
            _mainText.Text = _gameData.Chips.ToString();

        var localPos = DisplayServer.MouseGetPosition() - DisplayServer.WindowGetPosition();
        bool over = _settingsPanel.ContainsPoint(localPos);

        if (CurrentMode == Mode.BossKey)
        {
            over |= _dogHitRect.HasPoint(localPos) || _btnHitRect.HasPoint(localPos);
        }
        else if (CurrentMode == Mode.Play)
        {
            if (_playViewport != null)
            {
                var gameRect = new Rect2(_playViewport.Position,
                    _playViewport.Size * _playViewport.Scale);
                over |= gameRect.HasPoint(localPos);
            }
            if (_infoPanel != null && _infoPanel.Visible)
            {
                int infoX = _infoPanelOnRight ? 240 + 600 : 0;
                over |= new Rect2(infoX, _contentOffset.Y, 240, 600).HasPoint(localPos);
            }
        }

        if (_isClickThrough && over) SetClickThrough(false);
        else if (!_isClickThrough && !over && !_isDragging) SetClickThrough(true);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut && _settingsPanel.IsOpen
            && SettingsManager.LoadAutoHidePanel())
        {
            var mouse = DisplayServer.MouseGetPosition();
            var wp = DisplayServer.WindowGetPosition();
            var ws = DisplayServer.WindowGetSize();
            if (mouse.X < wp.X || mouse.X > wp.X + ws.X ||
                mouse.Y < wp.Y || mouse.Y > wp.Y + ws.Y)
                _settingsPanel.CloseImmediate();
        }
    }

    // ===== 模式切换 =====

    private Node2D _playRoot = null!;
    private SubViewportContainer _playViewport = null!;
    private InfoPanelController _infoPanel = null!;
    // 游玩模式布局状态：false=信息面板在左(默认), true=信息面板在右
    private bool _infoPanelOnRight;

    private void SwitchToPlay()
    {
        if (CurrentMode == Mode.Play) return;
        if (_settingsPanel.IsOpen) _settingsPanel.CloseImmediate();

        HideBossKeyContent();

        if (_playRoot == null)
        {
            _playRoot = GD.Load<PackedScene>("res://Scenes/PlayContent.tscn").Instantiate<Node2D>();
            _playRoot.Name = "PlayRoot";
            AddChild(_playRoot);
            _playViewport = _playRoot.GetNode<SubViewportContainer>("SubViewportContainer");

            // 信息面板由 ModeManager 直接管理（需要动态定位+避让）
            _infoPanel = GD.Load<PackedScene>("res://Scenes/InfoPanel.tscn").Instantiate<InfoPanelController>();
            _infoPanel.Name = "InfoPanel";
            AddChild(_infoPanel);
            _infoPanel.SettingsRequested += ToggleSettingsPanel;

            // 连接 Main 中的 GameManager 信号
            _gameManager = _playRoot.GetNode<GameManager>("SubViewportContainer/SubViewport/Main");
            _gameManager.GameData = _gameData;
            _gameManager.SettingsPanel = _settingsPanel;

            // InfoPanel 绑定 GameData
            _infoPanel.Bind(_gameData);
        }

        // 切换游玩模式的胖窗口尺寸（840×600 内容 + 420 缓冲）
        SetupPlayFatWindow();
        SetClickThrough(false);
        UpdatePlayLayout();
        _playRoot.Visible = true;
        _infoPanel.Visible = true;
        CurrentMode = Mode.Play;
    }

    private void UpdatePlayLayout()
    {
        var scrSize = DisplayServer.ScreenGetSize();
        var winPos = DisplayServer.WindowGetPosition();
        int baseY = (int)_contentOffset.Y;
        const int gameW = 600;
        const int infoW = 240;
        const int pad = 5;

        // 信息面板在左侧（默认）：屏幕范围 winPos.X ~ winPos.X + 240
        bool leftOk = winPos.X >= -pad && winPos.X + infoW <= scrSize.X + pad;

        _infoPanelOnRight = !leftOk;

        // 游戏面板位置固定，信息面板自己绕到右侧
        _playViewport.Position = new Vector2(infoW, baseY);
        _infoPanel.SetPanelPosition(new Vector2(_infoPanelOnRight ? infoW + gameW : 0, baseY));
    }

    private void SwitchToBossKey()
    {
        if (CurrentMode == Mode.BossKey) return;
        if (_settingsPanel.IsOpen) _settingsPanel.CloseImmediate();

        if (_playRoot != null)
            _playRoot.Visible = false;
        if (_infoPanel != null)
            _infoPanel.Visible = false;

        ShowBossKeyContent();
        SetupFatWindow();
        SetClickThrough(true);
        CurrentMode = Mode.BossKey;
    }

    private void HideBossKeyContent()
    {
        _bossKeyContent.Visible = false;
        // CanvasLayer 不继承 Node2D 的 Visible，需单独隐藏
        _bossKeyContent.GetNode<CanvasLayer>("CanvasLayer").Visible = false;
        _bossKeyContent.GetNode<CanvasLayer>("Bubble").Visible = false;
    }

    private void ShowBossKeyContent()
    {
        _bossKeyContent.Visible = true;
        _bossKeyContent.GetNode<CanvasLayer>("CanvasLayer").Visible = true;
        RefreshBossDogVisuals();
    }

    private void RefreshBossDogVisuals()
    {
        _bossDogVisual.RefreshEquippedDisguiseVisuals();
        _bossDogVisual.RefreshEquippedEyewear(showIfEquipped: true);
    }

    private void OnRandomizeScene()
    {
        ApplyRandomEquipment(DebugSceneItemTypes);
    }

    private void OnRandomizeDog()
    {
        ApplyRandomEquipment(DebugDogItemTypes);
    }

    private void OnDogReactionRequested(int trigger)
    {
        if (CurrentMode == Mode.BossKey)
            _bossDogVisual.ApplyReaction((EDogReactionTrigger)trigger);
        else
            _gameManager?.OnPlayDogReaction(trigger);
    }

    private void OnTypingInputOccurred(int count)
    {
        if (CurrentMode == Mode.BossKey)
            _bossDogVisual.PlayDesktopTongueTap(count);
    }

    private void ApplyRandomEquipment(IEnumerable<EItemType> types)
    {
        foreach (var type in types)
        {
            var items = _gameData.Inventory.GetOwnedOfType(type)
                .OrderBy(item => item.Id)
                .ToList();
            if (items.Count == 0) continue;

            if (!_debugItemBags.TryGetValue(type, out var bag))
            {
                bag = new ShuffleBag<int>();
                _debugItemBags[type] = bag;
            }

            var equippedId = _gameData.Inventory.GetEquipped(type)?.Id ?? -1;
            var pickedId = bag.Pick(items.Select(item => item.Id).ToList(), _debugRandom, equippedId);
            _gameData.EquipItem(pickedId);
        }
    }

    // ===== 面板切换 =====

    private void ToggleSettingsPanel()
    {
        if (_settingsPanel.IsOpen)
        {
            _settingsPanel.Close();
            return;
        }
        PositionPanelInBestSlot();
        _settingsPanel.Open();
    }

    private void PositionPanelInBestSlot()
    {
        var winPos = DisplayServer.WindowGetPosition();
        var scrSize = DisplayServer.ScreenGetSize();
        int pw = (int)_panelSize.X;
        int ph = (int)_panelSize.Y;
        const int pad = 5;

        // 根据模式取 A 区位置和尺寸
        float aX, aY; int aw, ah;
        if (CurrentMode == Mode.Play && _playViewport != null)
        {
            aX = _playViewport.Position.X;
            aY = _playViewport.Position.Y;
            aw = (int)(_playViewport.Size.X * _playViewport.Scale.X);
            ah = (int)(_playViewport.Size.Y * _playViewport.Scale.Y);
        }
        else
        {
            aX = _contentOffset.X;
            aY = _contentOffset.Y;
            aw = (int)_windowBaseSize.X;
            ah = (int)_windowBaseSize.Y;
        }

        bool Fits(int sx, int sy)
        {
            if (sx < -pad || sx + pw > scrSize.X + pad ||
                sy < -pad || sy + ph > scrSize.Y + pad)
                return false;
            // 游玩模式下避开信息面板区域
            if (CurrentMode == Mode.Play && _infoPanel != null && _infoPanel.Visible)
            {
                int infoX = _infoPanelOnRight
                    ? (int)aX + aw : 0;
                int infoY = (int)aY;
                var setRect = new Rect2(sx, sy, pw, ph);
                // info 坐标是窗口空间，Fits 是屏幕空间，需加 winPos
                var infoRect = new Rect2(winPos.X + infoX, winPos.Y + infoY, 240, 600);
                if (setRect.Intersects(infoRect))
                    return false;
            }
            return true;
        }

        float bY = aY + ah - ph;
        float centerX = aX + aw / 2f - pw / 2f;

        // 改优先级就是改数组里那几行的顺序，不用动逻辑
        var slots = new (int slot, int wx, int wy)[]
        {
            (6, (int)aX + aw, (int)bY),  
            (8, (int)centerX, 0),
            (9, (int)aX + aw, 0),
            (7, 0, 0),          
            (4, 0, (int)bY),
            (2, (int)centerX, (int)aY + ah),
            (3, (int)aX + aw, (int)aY + ah),
            (1, 0, (int)aY + ah),
        };

        foreach (var (_, wx, wy) in slots)
        {
            if (Fits(winPos.X + wx, winPos.Y + wy))
            {
                _settingsPanel.SetPanelPosition(new Vector2(wx, wy));
                return;
            }
        }
        // 兜底：覆盖在 A 区中央
        _settingsPanel.SetPanelPosition(new Vector2(aX + aw / 2f - pw / 2f, aY + ah / 2f - ph / 2f));
    }

    // ===== 窗口管理 =====

    private void SetupFatWindow()
    {
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Transparent, true);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, true);

        int winW = (int)_windowBaseSize.X + (int)_panelSize.X * 2;
        int winH = (int)_windowBaseSize.Y + (int)_panelSize.Y * 2;
        var pos = DisplayServer.WindowGetPosition();
        DisplayServer.WindowSetSize(new Vector2I(winW, winH));
        DisplayServer.WindowSetPosition(pos);
        RenderingServer.SetDefaultClearColor(new Color(0, 0, 0, 0));
    }

    private void ApplyTaskbarSnap(ref Vector2I newPos)
    {
        var scrRect = DisplayServer.ScreenGetUsableRect();
        int taskbarTop = scrRect.Position.Y + scrRect.Size.Y;
        var anchor = _bossKeyContent.GetNode<Marker2D>("ContentA/TaskBar").Position;
        int anchorY = (int)(_contentOffset.Y + anchor.Y);
        int snappedY = taskbarTop - anchorY;

        int dist = Math.Abs(newPos.Y - snappedY);

        if (_taskbarSnapped)
        {
            if (newPos.Y < snappedY - BreakawayThreshold)
                _taskbarSnapped = false;
            else
                newPos.Y = snappedY;
        }
        else if (dist < SnapThreshold)
        {
            _taskbarSnapped = true;
            newPos.Y = snappedY;
        }
    }

    private void SetupPlayFatWindow()
    {
        int pw = (int)_panelSize.X;
        int ph = (int)_panelSize.Y;
        int contentW = 840;
        int contentH = 600;
        int winW = contentW + pw * 2;
        int winH = Math.Max(contentH, ph) + ph * 2;
        // 只 resize，保留窗口当前位置，不让内容跳位
        var pos = DisplayServer.WindowGetPosition();
        DisplayServer.WindowSetSize(new Vector2I(winW, winH));
        DisplayServer.WindowSetPosition(pos);
    }

    private void SetWindowAboveTaskbar()
    {
        var scrRect = DisplayServer.ScreenGetUsableRect();
        int taskbarTop = (int)(scrRect.Position.Y + scrRect.Size.Y);
        int winW = DisplayServer.WindowGetSize().X;
        var anchor = _bossKeyContent.GetNode<Marker2D>("ContentA/TaskBar").Position;
        int anchorY = (int)(_contentOffset.Y + anchor.Y);
        int x = (int)(scrRect.Position.X + (scrRect.Size.X - winW) / 2);
        int y = taskbarTop - anchorY;
        DisplayServer.WindowSetPosition(new Vector2I(x, y));
    }

    private void EnableLayeredWindow()
    {
        ApplyNativeWindowStyles(clickThrough: true);
    }

    private void SetClickThrough(bool enabled)
    {
        _isClickThrough = enabled;
        ApplyNativeWindowStyles(enabled);
    }

    private static void ApplyNativeWindowStyles(bool clickThrough)
    {
        var hWnd = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        if (hWnd == IntPtr.Zero) return;

        var style = WindowNative.GetWindowLong(hWnd, WindowNative.GWL_EXSTYLE);
        style |= WindowNative.WS_EX_LAYERED;
        if (clickThrough)
            style |= WindowNative.WS_EX_TRANSPARENT;
        else
            style &= ~WindowNative.WS_EX_TRANSPARENT;

        WindowNative.SetWindowLong(hWnd, WindowNative.GWL_EXSTYLE, style);
        WindowNative.SetWindowPos(hWnd, WindowNative.HWND_TOPMOST, 0, 0, 0, 0,
            WindowNative.SWP_NOMOVE | WindowNative.SWP_NOSIZE | WindowNative.SWP_SHOWWINDOW);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                var localPos = DisplayServer.MouseGetPosition() - DisplayServer.WindowGetPosition();
                if (_settingsPanel.ContainsPoint(localPos)) return;
                if (_infoPanel != null && _infoPanel.Visible)
                {
                    int infoX = _infoPanelOnRight ? 840 : 0;
                    if (new Rect2(infoX, _contentOffset.Y, 240, 600).HasPoint(localPos)) return;
                }

                _mouseScreenStart = DisplayServer.MouseGetPosition();
                _windowPosStart = DisplayServer.WindowGetPosition();
                _potentialDrag = true;
            }
            else
            {
                if (_isDragging) GetViewport().SetInputAsHandled();
                _isDragging = false; _potentialDrag = false;
                _taskbarSnapped = false;
            }
        }
        else if (@event is InputEventMouseMotion && _potentialDrag)
        {
            var d = DisplayServer.MouseGetPosition() - _mouseScreenStart;
            if (Mathf.Abs(d.X) > DragThreshold || Mathf.Abs(d.Y) > DragThreshold)
            { _isDragging = true; SetClickThrough(false); }
            if (_isDragging)
            {
                var newPos = _windowPosStart + d;
                ApplyTaskbarSnap(ref newPos);
                DisplayServer.WindowSetPosition(newPos);
                if (_settingsPanel.IsOpen)
                    PositionPanelInBestSlot();
                if (CurrentMode == Mode.Play)
                    UpdatePlayLayout();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private sealed class ShuffleBag<T>
    {
        private readonly Queue<T> _queue = new();
        private List<T> _lastCandidates = new();
        private T _lastPicked;
        private bool _hasLastPicked;

        public T Pick(IReadOnlyList<T> candidates, Random rng, T avoid)
        {
            if (candidates.Count == 0)
                throw new InvalidOperationException("ShuffleBag needs at least one candidate.");

            if (_queue.Count == 0 || !HasSameCandidates(candidates))
                Refill(candidates, rng, avoid);

            var picked = _queue.Dequeue();
            _lastPicked = picked;
            _hasLastPicked = true;
            return picked;
        }

        private void Refill(IReadOnlyList<T> candidates, Random rng, T avoid)
        {
            _lastCandidates = candidates.ToList();
            var shuffled = candidates.ToList();
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            MoveFirstRepeatBack(shuffled, avoid);
            if (_hasLastPicked)
                MoveFirstRepeatBack(shuffled, _lastPicked);

            _queue.Clear();
            foreach (var item in shuffled)
                _queue.Enqueue(item);
        }

        private static void MoveFirstRepeatBack(List<T> shuffled, T avoid)
        {
            if (shuffled.Count <= 1 || !EqualityComparer<T>.Default.Equals(shuffled[0], avoid))
                return;

            for (int i = 1; i < shuffled.Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(shuffled[i], avoid))
                {
                    (shuffled[0], shuffled[i]) = (shuffled[i], shuffled[0]);
                    return;
                }
            }
        }

        private bool HasSameCandidates(IReadOnlyList<T> candidates)
        {
            return _lastCandidates.Count == candidates.Count
                && !_lastCandidates.Except(candidates).Any()
                && !candidates.Except(_lastCandidates).Any();
        }
    }
}
