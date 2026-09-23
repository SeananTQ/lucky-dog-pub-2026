using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DataTables;
using Godot;

namespace LuckyDogRise.Tools;

/// <summary>A development-only, OBS-ready stage. It never opens a player profile or Steam session.</summary>
public partial class SteamStoreCaptureController : Control
{
    private const string SettingsPath = "user://steam_store_capture_settings.json";
    private const string EditorScenePath = "res://Tools/DogSkinEditor/DogSkinEditor.tscn";
    private static readonly Vector2I CaptureSize = new(2560, 1440);
    private static readonly PackedScene DogScene = GD.Load<PackedScene>("res://Scenes/Shared/DogArea.tscn");
    private static readonly Color GreenScreen = new("00ff00");
    private readonly RandomNumberGenerator _random = new();
    private readonly Random _layoutRandom = new();
    private readonly List<Tile> _tiles = new();
    private readonly List<Tween> _flightTweens = new();
    private readonly List<string> _backgrounds = new();
    private readonly List<Item> _hats = new();
    private readonly List<DogSkin> _skins = new();
    private readonly List<EDogReactionTrigger> _reactions = new();

    private CaptureSettings _settings = new();
    private Control _stage = null!;
    private Control _tileLayer = null!;
    private ColorRect _stageBackground = null!;
    private PanelContainer _settingsPanel = null!;
    private Button _exitButton = null!;
    private VBoxContainer _actionRows = null!;
    private Label _countdown = null!;
    private Label _message = null!;
    private PlayState _state;
    private double _countdownRemaining;
    private double _phaseRemaining;
    private int _actionIndex;
    private float _offsetX;
    private float _offsetY;
    private Tile _lastArrived = null!;
    private int _arrivalCount;

    private enum PlayState { Configure, Countdown, Playing, Outro, Finished }

    private sealed class CaptureSettings
    {
        public CaptureSettings() { }

        public int Columns { get; set; } = 4;
        public int Rows { get; set; } = 3;
        public List<int> Actions { get; set; } = new() { 1009, 1005, 1004, 1003, 1006 };
        public double ReactionSeconds { get; set; } = 1.2;
        public double IntervalSeconds { get; set; } = 0.8;
        public bool RandomBackgrounds { get; set; }
        public int Direction { get; set; } = 5;
        public double ScrollSpeed { get; set; } = 90;
        public bool GreenEnding { get; set; }
        public double FlightSeconds { get; set; } = 1.15;
        public double StaggerSeconds { get; set; } = 0.055;
    }

    private sealed class Tile
    {
        public int Column;
        public int Row;
        public int DogId;
        public string BackgroundPath = "";
        public Control Root = null!;
        public TextureRect Background = null!;
        public DogVisual Dog = null!;
    }

    public override void _EnterTree()
    {
        if (DisplayServer.GetName() == "headless") return;
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Transparent, false);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, false);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.ResizeDisabled, true);
        GetWindow().TransparentBg = false;
    }

    public override void _Ready()
    {
        _random.Randomize();
        Theme = GD.Load<Theme>("res://Tools/DogSkinEditor/EditorFiles/DogSkinEditorTheme.tres");
        if (DisplayServer.GetName() != "headless")
        {
            GetWindow().Title = "Lucky Dog Rise - Steam 商店页录屏演出";
            DisplayServer.WindowSetSize(CaptureSize);
            var screen = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
            DisplayServer.WindowSetPosition(screen.Position);
        }

        LoadResources();
        LoadSettings();
        BuildStage();
        BuildSettingsPanel();
        try
        {
            BuildTiles();
        }
        catch (Exception exception)
        {
            ShowError("无法生成狗狗矩阵：" + exception.Message);
        }
    }

    public override void _Process(double delta)
    {
        if (_state == PlayState.Countdown)
        {
            _countdownRemaining -= delta;
            if (_countdownRemaining <= 0)
            {
                _countdown.Hide();
                _state = PlayState.Playing;
                _actionIndex = 0;
                StartAction();
            }
            else
            {
                _countdown.Text = Mathf.CeilToInt((float)_countdownRemaining).ToString();
            }
            return;
        }
        if (_state != PlayState.Playing) return;

        AdvanceScroll((float)delta);
        _phaseRemaining -= delta;
        if (_phaseRemaining > 0) return;
        _actionIndex = (_actionIndex + 1) % _settings.Actions.Count;
        StartAction();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (key.Keycode == Key.F10 && _state == PlayState.Playing)
            BeginOutro();
        else if (key.Keycode == Key.Escape)
        {
            if (_state == PlayState.Configure) ReturnToEditor();
            else ReturnToConfiguration();
        }
        else if (key.Keycode == Key.F5 && _state == PlayState.Configure)
            BeginCountdown();
        else if (key.Keycode == Key.R && _state == PlayState.Configure)
            Reshuffle();
        else
            return;
        GetViewport().SetInputAsHandled();
    }

    private void LoadResources()
    {
        _skins.AddRange(LubanData.Tables.TbDogSkin.DataList.OrderBy(skin => skin.Id)
            .GroupBy(VisualKey).Select(group => group.First()));
        _hats.AddRange(LubanData.Tables.TbItem.DataList
            .Where(item => item.ItemType == EItemType.Headwear && item.AssetPathList.Count > 0)
            .Where(item => ResourceLoader.Exists(PlayerInventory.ToResPath(item.AssetPathList[0])))
            .GroupBy(item => item.AssetPathList[0], StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()));
        _reactions.AddRange(LubanData.Tables.TbDogReaction.DataList
            .Select(row => row.DogReactionTrigger)
            .Where(trigger => (int)trigger >= 1001)
            .Distinct()
            .OrderBy(trigger => (int)trigger));
        var directory = ProjectSettings.GlobalizePath("res://Assets/v1/Background");
        if (Directory.Exists(directory))
            _backgrounds.AddRange(Directory.EnumerateFiles(directory, "*.png")
                .Select(path => "res://Assets/v1/Background/" + Path.GetFileName(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
    }

    private void LoadSettings()
    {
        try
        {
            var path = ProjectSettings.GlobalizePath(SettingsPath);
            if (File.Exists(path))
                _settings = JsonSerializer.Deserialize<CaptureSettings>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception exception)
        {
            GD.PushWarning("[商店页录屏演出] 配置载入失败：" + exception.Message);
        }
        _settings.Columns = Math.Clamp(_settings.Columns, 2, 12);
        _settings.Rows = Math.Clamp(_settings.Rows, 2, 8);
        _settings.ReactionSeconds = Math.Clamp(_settings.ReactionSeconds, 0.1, 30);
        _settings.IntervalSeconds = Math.Clamp(_settings.IntervalSeconds, 0, 30);
        _settings.ScrollSpeed = Math.Clamp(_settings.ScrollSpeed, 0, 1000);
        _settings.FlightSeconds = Math.Clamp(_settings.FlightSeconds, 0.2, 8);
        _settings.StaggerSeconds = Math.Clamp(_settings.StaggerSeconds, 0, 0.5);
        if (_settings.Direction < 1 || _settings.Direction > 9) _settings.Direction = 5;
        _settings.Actions = (_settings.Actions ?? new List<int>())
            .Where(value => _reactions.Contains((EDogReactionTrigger)value)).ToList();
        if (_settings.Actions.Count == 0)
            _settings.Actions.Add((int)EDogReactionTrigger.Hello);
    }

    private static string VisualKey(DogSkin skin) => string.Join('|', new[]
    {
        skin.FolderPath, skin.Head, skin.DefaultEars, skin.DefaultEyes,
        skin.DefaultTongue, skin.FixedEyewear, skin.ClawLeftBack,
        skin.ClawRightPalms, skin.TongueRegular, skin.DefaultNose,
        skin.DefaultMouse, skin.MouseSilent, skin.EarsNormal,
        skin.EarsDrooped, skin.EyesBored, skin.EyesNormalMood,
        skin.EyesHappy, skin.EyesAdmiring, skin.EyesStunned,
        skin.EyesSignaling,
    });

    private void SaveSettings()
    {
        try
        {
            var path = ProjectSettings.GlobalizePath(SettingsPath);
            File.WriteAllText(path, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            GD.PushWarning("[商店页录屏演出] 配置保存失败：" + exception.Message);
            ShowError("录屏配置保存失败，请检查用户数据目录的写入权限。");
        }
    }

    private void BuildStage()
    {
        _stage = new Control { ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
        FullRect(_stage);
        AddChild(_stage);
        _stageBackground = new ColorRect { Color = Colors.Black, MouseFilter = MouseFilterEnum.Ignore };
        FullRect(_stageBackground);
        _stage.AddChild(_stageBackground);
        _tileLayer = new Control { MouseFilter = MouseFilterEnum.Ignore };
        FullRect(_tileLayer);
        _stage.AddChild(_tileLayer);
        _countdown = new Label
        {
            Visible = false,
            ZIndex = 3000,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _countdown.AddThemeFontSizeOverride("font_size", 140);
        _countdown.AddThemeColorOverride("font_color", Colors.White);
        _countdown.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _countdown.AddThemeConstantOverride("shadow_offset_x", 4);
        _countdown.AddThemeConstantOverride("shadow_offset_y", 4);
        FullRect(_countdown);
        AddChild(_countdown);
    }

    private void BuildSettingsPanel()
    {
        _settingsPanel = new PanelContainer
        {
            Position = new Vector2(28, 28),
            CustomMinimumSize = new Vector2(540, 0),
            Size = new Vector2(540, CaptureSize.Y - 56),
            ZIndex = 3000,
        };
        AddChild(_settingsPanel);
        _exitButton = Button("返回 DogSkin 编辑器", ReturnToEditor);
        _exitButton.Position = new Vector2(CaptureSize.X - 248, 28);
        _exitButton.Size = new Vector2(220, 44);
        _exitButton.ZIndex = 3001;
        AddChild(_exitButton);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(540, CaptureSize.Y - 56) };
        _settingsPanel.AddChild(scroll);
        var content = new VBoxContainer { CustomMinimumSize = new Vector2(500, 0) };
        content.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(content);
        content.AddChild(Title("Steam 商店页录屏演出", 24));
        content.AddChild(Title("OBS 录制画面：2560 × 1440", 17));
        content.AddChild(Title("F5 播放 · F10 汇聚结束 · Esc 设置/退出 · R 重新随机", 15));
        content.AddChild(new HSeparator());

        AddSpin(content, "矩阵列数", _settings.Columns, 2, 12, 1, value =>
        {
            _settings.Columns = (int)value;
            Reshuffle();
        });
        AddSpin(content, "矩阵行数", _settings.Rows, 2, 8, 1, value =>
        {
            _settings.Rows = (int)value;
            Reshuffle();
        });
        content.AddChild(Title("表情与动作顺序", 18));
        _actionRows = new VBoxContainer();
        content.AddChild(_actionRows);
        RefreshActionRows();
        content.AddChild(Button("添加动作", () =>
        {
            _settings.Actions.Add((int)EDogReactionTrigger.Hello);
            RefreshActionRows();
        }));
        AddSpin(content, "每个动作播放（秒）", _settings.ReactionSeconds, 0.1, 30, 0.1,
            value => _settings.ReactionSeconds = value);
        AddSpin(content, "动作结束后保持表情（秒）", _settings.IntervalSeconds, 0, 30, 0.1,
            value => _settings.IntervalSeconds = value);
        content.AddChild(new HSeparator());

        var backgroundToggle = new CheckButton { Text = "每次切换动作时重新随机背景", ButtonPressed = _settings.RandomBackgrounds };
        backgroundToggle.Toggled += value => _settings.RandomBackgrounds = value;
        content.AddChild(backgroundToggle);
        content.AddChild(Title("背景始终保证周围八格不同；关闭开关时保持当前排布。", 14));
        content.AddChild(Title("帽子在随机排布后保持固定；点击重新随机才更换。", 14));
        content.AddChild(new HSeparator());

        var direction = new OptionButton { CustomMinimumSize = new Vector2(470, 36) };
        foreach (var (value, label) in new[]
        {
            (7, "7 ↖ 左上"), (8, "8 ↑ 上"), (9, "9 ↗ 右上"),
            (4, "4 ← 左"), (5, "5 · 停留"), (6, "6 → 右"),
            (1, "1 ↙ 左下"), (2, "2 ↓ 下"), (3, "3 ↘ 右下"),
        })
        {
            direction.AddItem(label, value);
            if (value == _settings.Direction) direction.Select(direction.ItemCount - 1);
        }
        direction.ItemSelected += index => _settings.Direction = direction.GetItemId((int)index);
        content.AddChild(direction);
        AddSpin(content, "卷动速度（像素/秒）", _settings.ScrollSpeed, 0, 1000, 5,
            value => _settings.ScrollSpeed = value);
        content.AddChild(new HSeparator());

        var endBackground = new OptionButton { CustomMinimumSize = new Vector2(470, 36) };
        endBackground.AddItem("结束背景：黑色", 0);
        endBackground.AddItem("结束背景：绿色抠像", 1);
        endBackground.Select(_settings.GreenEnding ? 1 : 0);
        endBackground.ItemSelected += index => _settings.GreenEnding = index == 1;
        content.AddChild(endBackground);
        AddSpin(content, "矩形飞行时长（秒）", _settings.FlightSeconds, 0.2, 8, 0.05,
            value => _settings.FlightSeconds = value);
        AddSpin(content, "起飞错峰（秒/只）", _settings.StaggerSeconds, 0, 0.5, 0.005,
            value => _settings.StaggerSeconds = value);
        content.AddChild(new HSeparator());
        content.AddChild(Button("重新随机狗狗、帽子与背景", Reshuffle));
        content.AddChild(Button("播放 · 3 秒倒计时", BeginCountdown));
        content.AddChild(Button("返回 DogSkin 编辑器", ReturnToEditor));
        _message = Title("使用正式 DogSkin 和 Item 配置；不会修改玩家存档。", 14);
        content.AddChild(_message);
    }

    private void RefreshActionRows()
    {
        foreach (var child in _actionRows.GetChildren())
        {
            _actionRows.RemoveChild(child);
            child.QueueFree();
        }
        for (var i = 0; i < _settings.Actions.Count; i++)
        {
            var index = i;
            var row = new HBoxContainer();
            _actionRows.AddChild(row);
            var option = new OptionButton { CustomMinimumSize = new Vector2(260, 36) };
            foreach (var reaction in _reactions)
            {
                option.AddItem($"{(int)reaction}  {reaction}", (int)reaction);
                if ((int)reaction == _settings.Actions[index]) option.Select(option.ItemCount - 1);
            }
            option.ItemSelected += selected => _settings.Actions[index] = option.GetItemId((int)selected);
            row.AddChild(option);
            row.AddChild(Button("↑", () => MoveAction(index, -1)));
            row.AddChild(Button("↓", () => MoveAction(index, 1)));
            row.AddChild(Button("×", () =>
            {
                if (_settings.Actions.Count <= 1) return;
                _settings.Actions.RemoveAt(index);
                RefreshActionRows();
            }));
        }
    }

    private void MoveAction(int index, int delta)
    {
        var other = index + delta;
        if (other < 0 || other >= _settings.Actions.Count) return;
        (_settings.Actions[index], _settings.Actions[other]) = (_settings.Actions[other], _settings.Actions[index]);
        RefreshActionRows();
    }

    private void BuildTiles()
    {
        foreach (var tile in _tiles)
        {
            _tileLayer.RemoveChild(tile.Root);
            tile.Root.QueueFree();
        }
        _tiles.Clear();
        _offsetX = 0;
        _offsetY = 0;
        if (_skins.Count(skin => skin.Id == 1001) != 1)
            throw new InvalidOperationException("正式 DogSkin 表必须恰好包含一条 1001。");
        if (_skins.Count < 9 || _hats.Count < 9 || _backgrounds.Count < 9)
            throw new InvalidOperationException("狗狗造型、帽子和背景各至少需要 9 种，才能保证周围八格不同。");

        var width = _settings.Columns + 2;
        var height = _settings.Rows + 2;
        var otherSkins = _skins.Where(skin => skin.Id != 1001).ToArray();
        var dogValues = SteamStoreCaptureLayout.Assign(width, height,
            otherSkins.Select(skin => skin.Id).ToArray(), _layoutRandom, uniqueId: 1001);
        var hatValues = SteamStoreCaptureLayout.Assign(width, height,
            Enumerable.Range(1, _hats.Count).ToArray(), _layoutRandom);

        var dogById = _skins.ToDictionary(skin => skin.Id);
        var cellSize = CellSize();
        for (var row = 0; row < height; row++)
        for (var column = 0; column < width; column++)
        {
            var index = row * width + column;
            var dogId = dogValues[index];
            var tile = CreateTile(column - 1, row - 1, dogById[dogId], _hats[hatValues[index] - 1], cellSize);
            _tiles.Add(tile);
        }
        ApplyBackgroundLayout();
        PositionTiles();
    }

    private Tile CreateTile(int column, int row, DogSkin skin, Item hat, Vector2 cellSize)
    {
        var root = new Control
        {
            Size = cellSize,
            ClipContents = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _tileLayer.AddChild(root);
        var background = new TextureRect
        {
            Position = Vector2.Zero,
            Size = cellSize,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        root.AddChild(background);

        var sourceFrame = new Rect2(180, 40, 840, 760);
        var scale = Mathf.Min(cellSize.X / sourceFrame.Size.X, cellSize.Y / sourceFrame.Size.Y);
        var fitted = sourceFrame.Size * scale;
        var letterbox = (cellSize - fitted) / 2;
        var dogOrigin = letterbox + (new Vector2(610, 677) - sourceFrame.Position) * scale;
        var dog = DogScene.Instantiate<DogVisual>();
        dog.SetPreviewAppearance(DogAppearanceSpec.FromDogSkin(skin));
        dog.SetPreviewHeadwear(hat);
        dog.Scale = Vector2.One * scale;
        // Let the paws cross the cell seam and rest on the next row's background.
        dog.Position = new Vector2(dogOrigin.X, cellSize.Y);
        dog.ZIndex = 2;
        root.AddChild(dog);
        dog.SetHitButtonEnabled(false);
        dog.ApplyReaction(EDogReactionTrigger.Default);
        dog.ShowInspectionClaws();
        ApplyCapturePawDepths(dog);
        return new Tile
        {
            Column = column, Row = row, DogId = skin.Id,
            Root = root, Background = background, Dog = dog,
        };
    }

    private void SetTileBackground(TextureRect background, string path)
    {
        var texture = string.IsNullOrEmpty(path) ? null : GD.Load<Texture2D>(path);
        if (texture == null)
        {
            background.Texture = null;
            return;
        }
        // Crop from the image's top edge; KeepAspectCovered centers its vertical crop.
        var source = texture.GetSize();
        var target = background.Size;
        var sourceRatio = source.X / source.Y;
        var targetRatio = target.X / target.Y;
        var region = targetRatio >= sourceRatio
            ? new Rect2(0, 0, source.X, source.X / targetRatio)
            : new Rect2((source.X - source.Y * targetRatio) / 2, 0, source.Y * targetRatio, source.Y);
        background.Texture = new AtlasTexture { Atlas = texture, Region = region };
        background.Modulate = Colors.White;
    }

    private static void ApplyCapturePawDepths(DogVisual dog)
    {
        // Each row occupies ten Z levels. Its dog is at +2; the next row's
        // background is at +10 and its dog at +12. A back paw at +9 relative
        // to this dog lands between those two; a palm at +1 stays underneath.
        foreach (var name in new[] { "ClawLeft", "ClawRight" })
        {
            var claw = dog.GetNode<Node2D>(name);
            claw.ZAsRelative = true;
            claw.ZIndex = claw.GetNode<Sprite2D>("Claw_Back_Left").Visible ? 9 : 1;
        }
        var tongue = dog.GetNode<Sprite2D>("HeadRoot/Tonghe");
        tongue.ZAsRelative = true;
        tongue.ZIndex = 9;
    }

    private void ApplyBackgroundLayout()
    {
        var width = _settings.Columns + 2;
        var height = _settings.Rows + 2;
        var values = SteamStoreCaptureLayout.Assign(width, height,
            Enumerable.Range(1, _backgrounds.Count).ToArray(), _layoutRandom);
        foreach (var tile in _tiles)
        {
            tile.BackgroundPath = _backgrounds[values[(tile.Row + 1) * width + tile.Column + 1] - 1];
            SetTileBackground(tile.Background, tile.BackgroundPath);
        }
    }

    private void StartAction()
    {
        var trigger = (EDogReactionTrigger)_settings.Actions[_actionIndex];
        foreach (var tile in _tiles)
        {
            tile.Dog.ApplyReaction(trigger);
            ApplyCapturePawDepths(tile.Dog);
        }
        if (_settings.RandomBackgrounds) ApplyBackgroundLayout();
        _phaseRemaining = _settings.ReactionSeconds + _settings.IntervalSeconds;
    }

    private void AdvanceScroll(float delta)
    {
        var direction = _settings.Direction switch
        {
            7 => new Vector2(-1, -1), 8 => Vector2.Up, 9 => new Vector2(1, -1),
            4 => Vector2.Left, 6 => Vector2.Right,
            1 => new Vector2(-1, 1), 2 => Vector2.Down, 3 => new Vector2(1, 1),
            _ => Vector2.Zero,
        };
        if (direction == Vector2.Zero || _settings.ScrollSpeed <= 0) return;
        var shift = direction.Normalized() * (float)_settings.ScrollSpeed * delta;
        _offsetX += shift.X;
        _offsetY += shift.Y;
        var cell = CellSize();
        while (_offsetX >= cell.X)
        {
            _offsetX -= cell.X;
            foreach (var tile in _tiles)
            {
                tile.Column = SteamStoreCaptureLayout.WrapCoordinate(tile.Column, _settings.Columns, 1);
            }
        }
        while (_offsetX <= -cell.X)
        {
            _offsetX += cell.X;
            foreach (var tile in _tiles)
            {
                tile.Column = SteamStoreCaptureLayout.WrapCoordinate(tile.Column, _settings.Columns, -1);
            }
        }
        while (_offsetY >= cell.Y)
        {
            _offsetY -= cell.Y;
            foreach (var tile in _tiles)
            {
                tile.Row = SteamStoreCaptureLayout.WrapCoordinate(tile.Row, _settings.Rows, 1);
            }
        }
        while (_offsetY <= -cell.Y)
        {
            _offsetY += cell.Y;
            foreach (var tile in _tiles)
            {
                tile.Row = SteamStoreCaptureLayout.WrapCoordinate(tile.Row, _settings.Rows, -1);
            }
        }
        PositionTiles();
    }

    private void PositionTiles()
    {
        var cell = CellSize();
        foreach (var tile in _tiles)
        {
            tile.Root.Position = new Vector2(tile.Column * cell.X + _offsetX, tile.Row * cell.Y + _offsetY);
            tile.Root.ZIndex = 100 + (tile.Row + 1) * 10;
        }
    }

    private Vector2 CellSize() => new((float)CaptureSize.X / _settings.Columns, (float)CaptureSize.Y / _settings.Rows);

    private void BeginCountdown()
    {
        if (_state != PlayState.Configure || _tiles.Count == 0) return;
        SaveSettings();
        _settingsPanel.Hide();
        _exitButton.Hide();
        _countdownRemaining = 3;
        _countdown.Text = "3";
        _countdown.Show();
        _state = PlayState.Countdown;
    }

    private void BeginOutro()
    {
        _state = PlayState.Outro;
        _stageBackground.Color = _settings.GreenEnding ? GreenScreen : Colors.Black;
        var target = _tiles.Single(tile => tile.DogId == 1001);
        var destination = (new Vector2(CaptureSize.X, CaptureSize.Y) - CellSize()) / 2;
        var others = _tiles.Where(tile => tile != target).ToArray();
        Shuffle(others);
        _lastArrived = null;
        _arrivalCount = 0;
        var longest = 0.0;
        for (var i = 0; i < others.Length; i++)
        {
            var delay = i * _settings.StaggerSeconds + _random.RandfRange(0, (float)_settings.StaggerSeconds);
            var duration = _settings.FlightSeconds * _random.RandfRange(0.8f, 1.2f);
            longest = Math.Max(longest, delay + duration);
            FlyTile(others[i], destination, delay, duration, false);
        }
        FlyTile(target, destination, longest + 0.12, _settings.FlightSeconds, true);
    }

    private void FlyTile(Tile tile, Vector2 destination, double delay, double duration, bool isFinal)
    {
        tile.Root.ZIndex = isFinal ? 2000 : 10 + _flightTweens.Count;
        var tween = CreateTween();
        _flightTweens.Add(tween);
        tween.TweenInterval(delay);
        tween.TweenProperty(tile.Root, "position", destination, duration)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
        tween.TweenCallback(Callable.From(() =>
        {
            if (_state != PlayState.Outro) return;
            if (_lastArrived != null && _lastArrived != tile)
                _lastArrived.Root.Hide();
            tile.Root.ZIndex = isFinal ? 2000 : 1000 + _arrivalCount++;
            _lastArrived = tile;
            if (isFinal)
            {
                tile.Background.Hide();
                _state = PlayState.Finished;
            }
        }));
    }

    private void ReturnToConfiguration()
    {
        foreach (var tween in _flightTweens) tween.Kill();
        _flightTweens.Clear();
        _state = PlayState.Configure;
        _stageBackground.Color = Colors.Black;
        _countdown.Hide();
        _settingsPanel.Show();
        _exitButton.Show();
        _lastArrived = null;
        _offsetX = 0;
        _offsetY = 0;
        foreach (var tile in _tiles)
        {
            tile.Root.Show();
            tile.Root.ZIndex = 0;
            tile.Background.Show();
            tile.Dog.ApplyReaction(EDogReactionTrigger.Default);
            tile.Dog.ShowInspectionClaws();
            ApplyCapturePawDepths(tile.Dog);
        }
        PositionTiles();
    }

    private void Reshuffle()
    {
        if (_state != PlayState.Configure) return;
        try
        {
            BuildTiles();
            _message.Text = "已重新随机造型、帽子和背景；1001 只出现一次。";
        }
        catch (Exception exception)
        {
            ShowError("重新随机失败：" + exception.Message);
        }
    }

    private void ReturnToEditor()
    {
        SaveSettings();
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.ResizeDisabled, false);
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            var workArea = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
            var size = new Vector2I(Math.Min(1480, workArea.Size.X), Math.Min(900, workArea.Size.Y));
            DisplayServer.WindowSetSize(size);
            DisplayServer.WindowSetPosition(workArea.Position + (workArea.Size - size) / 2);
        }
        GetTree().ChangeSceneToFile(EditorScenePath);
    }

    private void ShowError(string message)
    {
        GD.PushError("[商店页录屏演出] " + message);
        if (_message != null) _message.Text = message;
    }

    private void Shuffle<T>(T[] values)
    {
        for (var i = values.Length - 1; i > 0; i--)
        {
            var j = _random.RandiRange(0, i);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }

    private static Button Button(string caption, Action action)
    {
        var button = new Button { Text = caption, CustomMinimumSize = new Vector2(0, 36) };
        button.Pressed += action;
        return button;
    }

    private static Label Title(string text, int size)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    private static void AddSpin(Control parent, string caption, double initial,
        double minimum, double maximum, double step, Action<double> changed)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);
        var label = new Label { Text = caption, CustomMinimumSize = new Vector2(260, 0) };
        row.AddChild(label);
        var spin = new SpinBox
        {
            MinValue = minimum, MaxValue = maximum, Step = step, Value = initial,
            CustomMinimumSize = new Vector2(150, 36),
        };
        spin.ValueChanged += value => changed(value);
        row.AddChild(spin);
    }

    private static void FullRect(Control control)
    {
        control.AnchorLeft = 0;
        control.AnchorTop = 0;
        control.AnchorRight = 1;
        control.AnchorBottom = 1;
        control.OffsetLeft = 0;
        control.OffsetTop = 0;
        control.OffsetRight = 0;
        control.OffsetBottom = 0;
    }
}
