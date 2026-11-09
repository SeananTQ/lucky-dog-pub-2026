#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace LuckyDogRise.Rooms;

// Standalone visual sandbox. No GameData, room session, Steam or player saves.
// The scene owns all UI; this timeline is deliberately not wired into the live game yet.
public partial class PokerCardAnimationLab : Control
{
    [Export] private Godot.Collections.Array<TextureButton> _cards = new();
    [Export] private Godot.Collections.Array<Label> _slots = new();
    [Export] private Godot.Collections.Array<Button> _presets = new();
    [Export] private Texture2D _back;
    [Export] private Control _stageArea, _board;
    [Export] private OptionButton _axis, _speed;
    [Export] private SpinBox _tilt, _offset, _flipSeconds, _straightenTilt, _stagger;
    [Export] private CheckButton _sound, _loop;
    [Export] private Button _finish, _straighten, _replay, _reset, _defaults, _copy, _capture, _fit, _minimize, _close;
    [Export] private Label _title, _phaseLabel, _detail, _history;
    [Export] private int _feedbackSpace = 200;

    private const string StraightenCue = "Card_PokerBoardStraighten";
    private enum Stage { Idle, Replacing, Straightening, Complete }
    private Stage _stage;
    private readonly Random _random = new();
    private readonly List<int> _deck = new();
    private readonly Dictionary<int, Texture2D> _faces = new();
    private readonly List<string> _changes = new();
    private readonly List<string> _stageTrace = new();
    private Sample[] _samples;
    private Snapshot _last;
    private int _deckIndex, _topZ, _replacementCount, _straightenSounds, _finishCount;
    private double _elapsed, _replayWait;
    private Vector2[] _straightenPositions;
    private float[] _straightenAngles;
    private double _finishFlipDuration, _finishStagger;
    private float _finishTilt;

    private sealed class Sample
    {
        public TextureButton Node;
        public Vector2 Home;
        public int Value;
        public bool FaceUp;
        public Flip Motion;
    }
    private sealed class Flip
    {
        public double Time, Duration;
        public int Value;
        public bool FaceUp, Vertical, Swapped;
        public Vector2 From, To;
        public float FromAngle, ToAngle;
    }
    private sealed record Pose(int Value, bool FaceUp, Vector2 Position, float Angle, int Z);
    private sealed record Snapshot(Pose[] Cards, int[] Deck, int Cursor);
    private bool Flipping => _samples?.Any(s => s.Motion != null) == true;
    private bool Busy => Flipping || _stage is Stage.Replacing or Stage.Straightening;
    private float PlaybackRate => _speed.Selected switch { 1 => 0.5f, 2 => 0.25f, _ => 1f };
    private string OutputDirectory => ProjectSettings.GlobalizePath("res://../.local-build/poker-card-animation-lab");

    public override void _Ready()
    {
        if (BuildInfo.Channel != BuildChannel.Dev || BuildInfo.IsDebugDemo || OS.HasFeature("lucky_demo"))
        {
            GetTree().Quit(1);
            return;
        }
        _samples = _cards.Select(n => new Sample { Node = n, Home = n.Position }).ToArray();
        for (int i = 0; i < _samples.Length; i++)
        {
            int slot = i;
            _cards[i].PivotOffset = _cards[i].Size / 2;
            _cards[i].Pressed += () => FlipCard(slot);
        }
        for (int i = 0; i < _presets.Count; i++)
        {
            int preset = i;
            _presets[i].Pressed += () => LoadPreset(preset);
        }
        _finish.Pressed += PlayFinish;
        _straighten.Pressed += PlayStraightenOnly;
        _replay.Pressed += ReplayFinish;
        _reset.Pressed += ResetBoard;
        _defaults.Pressed += RestoreDefaults;
        _copy.Pressed += () => { DisplayServer.ClipboardSet(DescribeParameters()); _history.Text = "参数已复制，可粘贴到反馈中。"; };
        _capture.Pressed += () => CaptureFrame("manual");
        _fit.Pressed += FitWindow;
        _minimize.Pressed += () => GetWindow().Mode = Window.ModeEnum.Minimized;
        _close.Pressed += () => GetTree().Quit();
        _title.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse)
            {
                if (mouse.DoubleClick) FitWindow(); else DisplayServer.WindowStartDrag();
            }
        };
        _stageArea.Resized += LayoutBoard;
        GetViewport().TransparentBg = false;
        GetWindow().Title = "小狗来啦 · 捣乱翻牌与收尾动画";
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        GetWindow().ContentScaleSize = Vector2I.Zero;
        GetWindow().MinSize = new Vector2I(1000, 620);
        FitWindow();
        ResetBoard();
        Callable.From(LayoutBoard).CallDeferred();
        if (OS.GetCmdlineUserArgs().Contains("--poker-card-animation-lab-smoke"))
            Callable.From(RunValidation).CallDeferred();
    }

    private void FitWindow()
    {
        var area = DisplayServer.ScreenGetUsableRect(GetWindow().CurrentScreen);
        if (area.Size.X <= 0 || area.Size.Y <= 0) return;
        GetWindow().Mode = Window.ModeEnum.Windowed;
        GetWindow().Position = area.Position;
        GetWindow().Size = new Vector2I(area.Size.X, Math.Max(620, area.Size.Y - _feedbackSpace));
    }

    private void LayoutBoard()
    {
        // Scale the whole authored stage, including its overflow margins; individual poses do not jump.
        float scale = Mathf.Min(_stageArea.Size.X / _board.Size.X, _stageArea.Size.Y / _board.Size.Y);
        _board.Scale = Vector2.One * Math.Max(0.1f, scale);
        _board.Position = (_stageArea.Size - _board.Size * _board.Scale) / 2;
    }

    public override void _Process(double delta) => Advance(delta * PlaybackRate);

    private void Advance(double delta)
    {
        if (_samples == null) return;
        foreach (var sample in _samples) AdvanceFlip(sample, delta);
        if (_stage == Stage.Replacing)
        {
            _elapsed += delta;
            if (_elapsed + 0.000001 >= _finishFlipDuration && !Flipping) BeginStraighten();
        }
        else if (_stage == Stage.Straightening)
        {
            _elapsed += delta;
            for (int i = 0; i < _samples.Length; i++)
            {
                var sample = _samples[i];
                double time = _elapsed - i * _finishStagger;
                if (time < 0) continue;
                float targetAngle = Mathf.DegToRad(_finishTilt * (0.62f + 0.095f * i));
                Vector2 raised = sample.Home + Vector2.Up * (8 + i);
                if (time < 0.14)
                {
                    float t = EaseOut((float)(time / 0.14));
                    sample.Node.Position = _straightenPositions[i].Lerp(raised, t);
                    sample.Node.Rotation = Mathf.Lerp(_straightenAngles[i], targetAngle, t);
                }
                else
                {
                    float t = Mathf.Clamp((float)((time - 0.14) / 0.19), 0, 1);
                    sample.Node.Position = raised.Lerp(sample.Home, BounceOut(t));
                    sample.Node.Rotation = Mathf.Lerp(targetAngle, 0, EaseOut(t));
                }
            }
            if (_elapsed + 0.000001 >= 0.33 + 4 * _finishStagger)
            {
                foreach (var sample in _samples) { sample.Node.Position = sample.Home; sample.Node.Rotation = 0; sample.Node.ZIndex = 0; }
                _topZ = 0;
                _finishCount++;
                SetStage(Stage.Complete);
                _replayWait = 0;
            }
        }
        else if (_stage == Stage.Complete && _loop.ButtonPressed)
        {
            _replayWait += delta;
            if (_replayWait >= 1.2) ReplayFinish();
        }
        RefreshUi();
    }

    private static float EaseOut(float t) => 1 - (1 - t) * (1 - t);
    private static float BounceOut(float t)
    {
        if (t < 1 / 2.75f) return 7.5625f * t * t;
        if (t < 2 / 2.75f) { t -= 1.5f / 2.75f; return 7.5625f * t * t + 0.75f; }
        if (t < 2.5f / 2.75f) { t -= 2.25f / 2.75f; return 7.5625f * t * t + 0.9375f; }
        t -= 2.625f / 2.75f; return 7.5625f * t * t + 0.984375f;
    }

    private Texture2D Face(int value)
    {
        if (!_faces.TryGetValue(value, out var texture)) _faces[value] = texture = GD.Load<Texture2D>(DeckManager.CardToAssetPath(value));
        return texture;
    }
    private void Show(Sample sample) => sample.Node.TextureNormal = sample.FaceUp ? Face(sample.Value) : _back;

    public void FlipCard(int slot)
    {
        if (slot < 0 || slot >= _samples.Length || _stage is Stage.Replacing or Stage.Straightening || _samples[slot].Motion != null) return;
        var sample = _samples[slot];
        _loop.ButtonPressed = false;
        SetStage(Stage.Idle);
        // Only accepted player operations alter card order. The latest one draws above other board cards.
        if (_topZ >= 1000)
        {
            int z = 0;
            foreach (var s in _samples.OrderBy(s => s.Node.ZIndex)) s.Node.ZIndex = ++z;
            _topZ = z;
        }
        sample.Node.ZIndex = ++_topZ;
        // Control input order follows tree order, not ZIndex. Keep the visible top card clickable.
        sample.Node.GetParent().MoveChild(sample.Node, -1);
        float angle = Mathf.DegToRad((float)_tilt.Value) * (float)(_random.NextDouble() * 2 - 1);
        Vector2 shift = new((float)(_random.NextDouble() * 2 - 1), (float)(_random.NextDouble() * 2 - 1));
        BeginFlip(sample, sample.Value, !sample.FaceUp, _axis.Selected == 1,
            (double)_flipSeconds.Value, sample.Home + shift * (float)_offset.Value, angle);
        PlaySound(sample.FaceUp ? "Card_PokerHandFlipToBack" : "Card_PokerHandFlipToFront");
        RefreshUi();
    }

    private void BeginFlip(Sample sample, int value, bool faceUp, bool vertical, double duration, Vector2 position, float angle)
    {
        sample.Motion = new Flip { Duration = duration, Value = value, FaceUp = faceUp, Vertical = vertical,
            From = sample.Node.Position, To = position, FromAngle = sample.Node.Rotation, ToAngle = angle };
    }

    private void AdvanceFlip(Sample sample, double delta)
    {
        var motion = sample.Motion;
        if (motion == null) return;
        motion.Time += delta;
        float t = Mathf.Clamp((float)(motion.Time / motion.Duration), 0, 1);
        if (t >= 0.5f && !motion.Swapped)
        {
            sample.Value = motion.Value; sample.FaceUp = motion.FaceUp; Show(sample); motion.Swapped = true;
        }
        float squeezed = t < 0.5f ? 1 - EaseOut(t * 2) : EaseOut(t * 2 - 1);
        sample.Node.Scale = motion.Vertical ? new Vector2(1, squeezed) : new Vector2(squeezed, 1);
        sample.Node.Position = motion.From.Lerp(motion.To, EaseOut(t));
        sample.Node.Rotation = Mathf.Lerp(motion.FromAngle, motion.ToAngle, EaseOut(t));
        if (t >= 1) { sample.Motion = null; sample.Node.Scale = Vector2.One; }
    }

    public void PlayFinish()
    {
        if (Busy) return;
        StartFinish(CaptureSnapshot());
    }
    private void StartFinish(Snapshot snapshot)
    {
        int count = _samples.Count(s => !s.FaceUp);
        if (_deckIndex + count > _deck.Count)
        {
            _loop.ButtonPressed = false;
            _history.Text = "本副牌已经用完，请重新发牌；不会让弃掉的旧牌重复回到牌桌。";
            return;
        }
        _last = snapshot;
        _changes.Clear();
        _stageTrace.Clear();
        _replacementCount = count;
        _straightenSounds = 0;
        CaptureFinishParameters();
        SetStage(Stage.Replacing);
        for (int i = 0; i < _samples.Length; i++)
        {
            var s = _samples[i];
            if (s.FaceUp) continue;
            int replacement = _deck[_deckIndex++];
            _changes.Add($"{i + 1} 号：{CardName(s.Value)} → {CardName(replacement)}");
            // System replacement always flips horizontally, preserving the disturbed pose until the sweep.
            BeginFlip(s, replacement, true, false, _finishFlipDuration, s.Node.Position, s.Node.Rotation);
        }
        if (count > 0) PlaySound("Card_PokerHandFlipToFront");
        _history.Text = count == 0 ? "本次无需补牌；等时停顿后仍完整整理五张牌。" : string.Join("　", _changes);
        RefreshUi();
    }
    public void PlayStraightenOnly()
    {
        if (Busy) return;
        _loop.ButtonPressed = false;
        _last = null;
        _stageTrace.Clear();
        _straightenSounds = 0;
        _replacementCount = 0;
        CaptureFinishParameters();
        _history.Text = "仅比较整组整理：保留当前牌面及正反状态，不补牌。";
        BeginStraighten();
    }
    private void CaptureFinishParameters()
    {
        _finishFlipDuration = _flipSeconds.Value;
        _finishStagger = _stagger.Value;
        _finishTilt = (float)_straightenTilt.Value;
    }
    private void BeginStraighten()
    {
        SetStage(Stage.Straightening);
        _straightenPositions = _samples.Select(s => s.Node.Position).ToArray();
        _straightenAngles = _samples.Select(s => s.Node.Rotation).ToArray();
        if (_sound.ButtonPressed) { PlaySound(StraightenCue); _straightenSounds++; }
    }
    private void PlaySound(string cue)
    {
        if (!_sound.ButtonPressed) return;
        if (cue == StraightenCue) AudioManager.Instance?.PlaySfx(cue, PlaybackRate, 0f);
        else AudioManager.Instance?.PlaySfxOncePerFrame(cue, "card-lab-flip", PlaybackRate);
    }
    private void SetStage(Stage stage)
    {
        _stage = stage;
        _elapsed = 0;
        _stageTrace.Add(stage.ToString());
        if (_stageTrace.Count > 20) _stageTrace.RemoveAt(0);
    }

    private Snapshot CaptureSnapshot() => new(_samples.Select(s => new Pose(s.Value, s.FaceUp,
        s.Node.Position, s.Node.Rotation, s.Node.ZIndex)).ToArray(), _deck.ToArray(), _deckIndex);
    private void RestoreSnapshot(Snapshot snapshot)
    {
        _deck.Clear(); _deck.AddRange(snapshot.Deck); _deckIndex = snapshot.Cursor;
        for (int i = 0; i < _samples.Length; i++)
        {
            var s = _samples[i]; var p = snapshot.Cards[i];
            s.Motion = null; s.Value = p.Value; s.FaceUp = p.FaceUp;
            s.Node.Position = p.Position; s.Node.Rotation = p.Angle; s.Node.ZIndex = p.Z;
            s.Node.Scale = Vector2.One; Show(s);
        }
        _topZ = _samples.Max(s => s.Node.ZIndex);
        SetStage(Stage.Idle);
    }
    public void ReplayFinish()
    {
        if (Busy || _last == null) return;
        RestoreSnapshot(_last);
        StartFinish(_last);
    }

    public void LoadPreset(int preset)
    {
        ResetBoard();
        for (int i = 0; i < _samples.Length; i++)
        {
            var s = _samples[i];
            s.FaceUp = preset == 0 || (preset == 1 && i != 1 && i != 3);
            if (preset != 0)
            {
                s.Node.Rotation = Mathf.DegToRad((i % 2 == 0 ? 1 : -1) * (float)_tilt.Value);
                s.Node.Position = s.Home + new Vector2((i % 2 == 0 ? 1 : -1) * (float)_offset.Value, 0);
                s.Node.ZIndex = i;
            }
            Show(s);
        }
        _topZ = 5;
        RefreshUi();
    }
    public void ResetBoard()
    {
        _loop.ButtonPressed = false;
        _last = null; _topZ = 0; _replacementCount = 0; _straightenSounds = 0;
        _stageTrace.Clear(); _changes.Clear();
        _deck.Clear(); _deck.AddRange(Enumerable.Range(0, 52));
        for (int i = 51; i > 0; i--) { int j = _random.Next(i + 1); (_deck[i], _deck[j]) = (_deck[j], _deck[i]); }
        _deckIndex = 5;
        for (int i = 0; i < _samples.Length; i++)
        {
            var s = _samples[i]; s.Motion = null; s.Value = _deck[i]; s.FaceUp = true;
            s.Node.Position = s.Home; s.Node.Rotation = 0; s.Node.Scale = Vector2.One; s.Node.ZIndex = 0; Show(s);
        }
        SetStage(Stage.Idle);
        _history.Text = "新牌桌：点击卡牌可反复翻同一张原牌，完整收尾才替换背面的牌。";
        RefreshUi();
    }
    private void RestoreDefaults()
    {
        if (Busy) return;
        _axis.Select(0); _speed.Select(0); _tilt.Value = 14; _offset.Value = 10;
        _flipSeconds.Value = 0.16; _straightenTilt.Value = 2.4; _stagger.Value = 0.05;
    }

    private void RefreshUi()
    {
        _phaseLabel.Text = _stage switch
        {
            Stage.Replacing => _replacementCount > 0 ? "补牌 · 背面换成新牌并翻正" : "无需补牌 · 等时短停顿",
            Stage.Straightening => "整理 · 五张牌倾斜后回正",
            Stage.Complete => "收尾完成 · 可继续翻牌或重播",
            _ => Flipping ? "玩家捣乱 · 原牌翻面，不换牌" : "自由试翻 · 点击卡牌或数字 1–5"
        };
        _detail.Text = $"背面 {_samples.Count(s => !s.FaceUp)}/5　最近补牌 {_replacementCount} 张　整组音效 {_straightenSounds} 次　收尾 {_finishCount} 次";
        for (int i = 0; i < _samples.Length; i++)
        {
            _slots[i].Text = $"{i + 1} · {(_samples[i].Motion != null ? "翻动中" : _samples[i].FaceUp ? "正面" : "待补牌")}";
            _cards[i].MouseFilter = _stage is Stage.Replacing or Stage.Straightening ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        }
        _finish.Disabled = _straighten.Disabled = Busy;
        _replay.Disabled = Busy || _last == null;
        _defaults.Disabled = Busy;
        _axis.Disabled = _speed.Disabled = Busy;
        foreach (var spin in new[] { _tilt, _offset, _flipSeconds, _straightenTilt, _stagger }) spin.Editable = !Busy;
    }

    private static string CardName(int card) => new[] { "梅花", "方片", "红桃", "黑桃" }[card / 13]
        + new[] { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" }[card % 13];
    private string DescribeParameters() => $"翻牌={(_axis.Selected == 0 ? "水平" : "垂直")}, 捣乱倾角=±{_tilt.Value:0.#}°, 位移=±{_offset.Value:0.#}, 翻面={_flipSeconds.Value:0.00}s, 整理最大倾角={_straightenTilt.Value:0.#}°, 错拍={_stagger.Value:0.00}s, 速度={PlaybackRate:0.##}x";

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key || GetViewport().GuiGetFocusOwner() is LineEdit) return;
        if (key.Keycode >= Key.Key1 && key.Keycode <= Key.Key5) FlipCard((int)key.Keycode - (int)Key.Key1);
        else if (key.Keycode == Key.Space) PlayFinish();
        else if (key.Keycode == Key.R) ReplayFinish();
        else if (key.Keycode == Key.Escape) ResetBoard();
        else return;
        GetViewport().SetInputAsHandled();
    }

    public string CaptureFrame(string name)
    {
        System.IO.Directory.CreateDirectory(OutputDirectory);
        string path = System.IO.Path.Combine(OutputDirectory, System.IO.Path.GetFileName(name) + ".png");
        GetViewport().GetTexture().GetImage().SavePng(path);
        _history.Text = "截图已保存：" + path;
        return path;
    }
    public string InspectState() => JsonSerializer.Serialize(new { Stage = _stage.ToString(), Busy, _deckIndex,
        _replacementCount, _straightenSounds, _finishCount, Trace = _stageTrace,
        Cards = _samples.Select(s => new { s.Value, s.FaceUp, Angle = s.Node.RotationDegrees, X = s.Node.Position.X, Y = s.Node.Position.Y, Z = s.Node.ZIndex, ScaleX = s.Node.Scale.X, ScaleY = s.Node.Scale.Y }) });

    // Targeted animation contracts, runnable without Steam, rendering capture or real-time waits.
    private void RunValidation()
    {
        var checks = new List<string>();
        var sounds = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        void Step(double seconds)
        {
            while (seconds > 0.000001) { double step = Math.Min(seconds, 1.0 / 120); Advance(step); seconds -= step; }
        }
        bool AtHome() => _samples.All(s => s.Motion == null && s.Node.Position.IsEqualApprox(s.Home)
            && Mathf.IsZeroApprox(s.Node.Rotation) && s.Node.Scale.IsEqualApprox(Vector2.One));
        int soundBus = AudioServer.GetBusIndex("SFX");
        bool wasMuted = soundBus >= 0 && AudioServer.IsBusMute(soundBus);
        SetProcess(false);
        if (soundBus >= 0) AudioServer.SetBusMute(soundBus, true);
        AudioManager.Instance.SfxStartedForSmoke += sounds.Add;
        try
        {
            Check(_samples.Length == 5 && _slots.Count == 5 && _presets.Count == 3, "scene-bindings");
            Check(_back != null && Enumerable.Range(0, 52).All(v => Face(v) != null), "card-textures");
            Check(ResourceLoader.Exists("res://Audio/SFX/Card_PokerBoardStraighten_1.ogg"), "separate-straighten-audio");
            for (int axis = 0; axis < 2; axis++)
            {
                ResetBoard(); _axis.Select(axis);
                int original = _samples[0].Value;
                FlipCard(0); FlipCard(0); Step(0.04);
                var scale = _samples[0].Node.Scale;
                Check(axis == 0 ? scale.X < 1 && scale.Y == 1 : scale.Y < 1 && scale.X == 1, $"flip-axis-{axis}");
                Step(0.2);
                Check(!_samples[0].FaceUp && _samples[0].Value == original, $"rapid-click-no-double-toggle-{axis}");
                FlipCard(1); Step(0.2); FlipCard(0); Step(0.2);
                Check(_samples[0].FaceUp && _samples[0].Value == original && _deckIndex == 5, $"flip-keeps-original-{axis}");
                Check(_samples[0].Node.ZIndex > _samples[1].Node.ZIndex
                    && _samples[0].Node.GetIndex() > _samples[1].Node.GetIndex(), $"latest-card-top-and-input-{axis}");
            }
            for (int preset = 0; preset < 3; preset++)
            {
                LoadPreset(preset); sounds.Clear();
                var before = CaptureSnapshot(); int expected = preset == 0 ? 0 : preset == 1 ? 2 : 5;
                PlayFinish(); FlipCard(0); PlayFinish(); Step(0.04);
                Check(_stage == Stage.Replacing && _replacementCount == expected, $"replacement-stage-and-input-lock-{preset}");
                Check(_samples.Select((s, i) => s.Node.Position.IsEqualApprox(before.Cards[i].Position)
                    && Mathf.IsEqualApprox(s.Node.Rotation, before.Cards[i].Angle)).All(v => v), $"no-snap-before-straighten-{preset}");
                Step(1.4);
                Check(_stage == Stage.Complete && AtHome() && _samples.All(s => s.FaceUp), $"complete-upright-{preset}");
                Check(_samples.Select((s, i) => before.Cards[i].FaceUp ? s.Value == before.Cards[i].Value
                    : !before.Cards.Any(p => p.Value == s.Value)).All(v => v)
                    && _samples.Select(s => s.Value).Distinct().Count() == 5 && _deckIndex == 5 + expected, $"only-back-cards-replaced-{preset}");
                Check(sounds.Count(s => s == StraightenCue) == 1 && _straightenSounds == 1, $"one-group-sound-even-with-no-replacement-{preset}");
                int[] result = _samples.Select(s => s.Value).ToArray();
                ReplayFinish(); Step(1.4);
                Check(_samples.Select(s => s.Value).SequenceEqual(result) && AtHome(), $"replay-same-cards-{preset}");
            }
            for (int phase = 0; phase < 3; phase++)
            {
                LoadPreset(1);
                if (phase == 0) FlipCard(0); else PlayFinish();
                Step(phase == 2 ? 0.25 : 0.04);
                ResetBoard(); int[] resetCards = _samples.Select(s => s.Value).ToArray(); Step(2);
                Check(_stage == Stage.Idle && AtHome() && _samples.All(s => s.FaceUp)
                    && _samples.Select(s => s.Value).SequenceEqual(resetCards), $"reset-cancels-old-animation-{phase}");
            }
            LoadPreset(1);
            PlayFinish(); Step(1.4); _loop.ButtonPressed = true; Step(1.21);
            Check(_stage == Stage.Replacing, "loop-restarts-same-finish");
            Step(0.8); _loop.ButtonPressed = false;
            PlayStraightenOnly(); Step(1.4);
            Check(_last == null && _replay.Disabled, "straighten-only-clears-stale-replay");
            ResetBoard();
            for (int i = 0; i < 20; i++) { FlipCard(i % 5); Step(0.2); PlayFinish(); Step(1.4); }
            Check(AtHome() && _deckIndex == 25, "twenty-rounds-no-pose-or-deck-drift");
            // Exhausting a deck does not silently recycle already discarded cards.
            for (int i = 0; i < 27; i++) { FlipCard(i % 5); Step(0.2); PlayFinish(); Step(1.4); }
            FlipCard(0); Step(0.2); PlayFinish();
            Check(_deckIndex == 52 && _stage == Stage.Idle && !_samples[0].FaceUp, "deck-exhaustion-stays-safe");
            ResetBoard(); RestoreDefaults();
            GD.Print("[PokerCardAnimationLab] PASS " + JsonSerializer.Serialize(checks));
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError("[PokerCardAnimationLab] FAIL " + error);
            GetTree().Quit(1);
        }
        finally
        {
            AudioManager.Instance.SfxStartedForSmoke -= sounds.Add;
            if (soundBus >= 0) AudioServer.SetBusMute(soundBus, wasMuted);
        }
    }
}
#endif
