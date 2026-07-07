using Godot;
using System.Collections.Generic;
using System.Linq;
using DataTables;

namespace LuckyDogRise;

public partial class DogVisual : Node2D
{
    [Signal]
    public delegate void DogClickedEventHandler();

    private Sprite2D _head = null!;
    private Sprite2D _eyes = null!;
    private Sprite2D _ears = null!;
    private Sprite2D _tongue = null!;
    private Node2D _clawLeft = null!;
    private Node2D _clawRight = null!;
    private Sprite2D _eyewear = null!;
    private Sprite2D _headwear = null!;
    private Button _hitButton = null!;

    private GameData _gameData = null!;
    private DogSkin _dogSkin = null!;
    private EDogReactionTrigger _currentReaction = EDogReactionTrigger.Default;
    private Tween _tongueTween = null!;
    private Vector2 _desktopTongueBasePosition;
    private float _desktopTongueTimer;
    private float _desktopTongueActiveTimer;
    private float _desktopTongueBeatTimer;
    private float _desktopTongueBurstTimer;
    private int _desktopTongueBurstCount;
    private bool _desktopTongueExtended;

    [Export] private float _tonguePantOffset = 10f;
    [Export] private float _tonguePantStepDuration = 0.06f;
    [Export] private int _tonguePantLoops = 2;
    [Export] private bool _desktopTongueImmediateMode;
    [Export] private float _desktopTongueTapOffset = 10f;
    [Export] private float _desktopTongueTapHoldSeconds = 0.08f;
    [Export] private float _desktopTongueActiveSeconds = 0.7f;
    [Export] private float _desktopTongueBeatSeconds = 0.12f;
    [Export] private float _desktopTongueBurstWindowSeconds = 0.25f;
    [Export] private int _desktopTongueBurstThreshold = 2;
    [Export] public bool ShowEquippedEyewearByDefault { get; set; }

    public GameData GameData
    {
        get => _gameData;
        set
        {
            _gameData = value;
            RefreshEquippedVisuals();
        }
    }

    // PSD → DogArea 本地坐标转换参数
    // Headwear 需要独立 Y 偏移（683 vs 677），因为帽子在头顶位置较高
    private const float OffsetX = 586f;
    private const float OffsetY = 677f;
    private const float OffsetXHeadwear = 585f;
    private const float OffsetYHeadwear = 683f;

    private Dictionary<string, Vector2> _positionCache = null!;

    public override void _Ready()
    {
        _head = GetNode<Sprite2D>("HeadRoot/Head");
        _eyes = GetNode<Sprite2D>("HeadRoot/Eyes");
        _ears = GetNode<Sprite2D>("HeadRoot/Ears");
        _tongue = GetNodeOrNull<Sprite2D>("HeadRoot/Tonghe");
        _clawLeft = GetNode<Node2D>("ClawLeft");
        _clawRight = GetNode<Node2D>("ClawRight");
        _eyewear = GetNode<Sprite2D>("HeadRoot/Eyewear");
        _headwear = GetNode<Sprite2D>("HeadRoot/Headwear");
        _hitButton = GetNode<Button>("HitButton");
        _hitButton.Pressed += () => EmitSignal(SignalName.DogClicked);

        EnsurePositionCache();
        RefreshEquippedVisuals();
    }

    public override void _Process(double delta)
    {
        if (_tongue == null) return;

        if (IsDesktopTongueImmediateMode())
        {
            if (_desktopTongueTimer <= 0f) return;

            _desktopTongueTimer -= (float)delta;
            if (_desktopTongueTimer <= 0f)
                _tongue.Position = _desktopTongueBasePosition;
            return;
        }

        if (_desktopTongueBurstTimer > 0f)
        {
            _desktopTongueBurstTimer -= (float)delta;
            if (_desktopTongueBurstTimer <= 0f)
                _desktopTongueBurstCount = 0;
        }

        if (_desktopTongueTimer > 0f)
        {
            _desktopTongueTimer -= (float)delta;
            if (_desktopTongueTimer <= 0f && _desktopTongueActiveTimer <= 0f)
                _tongue.Position = _desktopTongueBasePosition;
        }

        if (_desktopTongueActiveTimer <= 0f) return;

        _desktopTongueActiveTimer -= (float)delta;
        _desktopTongueBeatTimer -= (float)delta;

        if (_desktopTongueBeatTimer <= 0f)
        {
            _desktopTongueExtended = !_desktopTongueExtended;
            _desktopTongueBeatTimer = _desktopTongueBeatSeconds;
            _tongue.Position = _desktopTongueBasePosition
                + (_desktopTongueExtended ? new Vector2(0, _desktopTongueTapOffset) : Vector2.Zero);
        }

        if (_desktopTongueActiveTimer <= 0f)
        {
            _desktopTongueExtended = false;
            _tongue.Position = _desktopTongueBasePosition;
        }
    }

    /// <summary>
    /// 从 v1 layer_index.json 读取指定资源的 PSD 坐标，返回 DogArea 本地坐标。
    /// </summary>
    public Vector2 GetScenePosition(string assetPath)
    {
        var key = NormalizeLayerPath(assetPath);
        if (_positionCache.TryGetValue(key, out var pos))
            return pos;

        var fileName = key.Split('/')[^1];
        if (_positionCache.TryGetValue(fileName, out pos))
            return pos;

        GD.PushWarning($"[DogVisual] Position not found for: {assetPath}");
        return Vector2.Zero;
    }

    private void EnsurePositionCache()
    {
        if (_positionCache != null) return;
        _positionCache = new Dictionary<string, Vector2>();

        using var file = FileAccess.Open("res://Assets/v1/layer_index.json", FileAccess.ModeFlags.Read);
        if (file == null) return;

        var json = new Json();
        if (json.Parse(file.GetAsText()) != Error.Ok) return;

        var layers = json.Data.AsGodotDictionary()["layers"].AsGodotArray();
        foreach (var layer in layers)
        {
            var d = layer.AsGodotDictionary();
            var path = NormalizeLayerPath(d["file"].AsString());
            var fileOnly = path.Split('/')[^1];
            var cx = ReadFloat(d, "doc_x", "x") + ReadFloat(d, "width", "w") / 2f;
            var cy = ReadFloat(d, "doc_y", "y") + ReadFloat(d, "height", "h") / 2f;

            var pos = path.StartsWith("Headwear/")
                ? new Vector2(cx - OffsetXHeadwear, cy - OffsetYHeadwear)
                : new Vector2(cx - OffsetX, cy - OffsetY);

            _positionCache[path] = pos;
            _positionCache.TryAdd(fileOnly, pos);
        }
    }

    public void ResetAppearance()
    {
        ApplyReaction(EDogReactionTrigger.Default);
    }

    public void ResetDisguiseAppearance()
    {
        ApplyReaction(EDogReactionTrigger.Default);
    }

    // TODO: 待清理。旧硬编码表情入口，主流程已改为 DogReaction 表驱动。
    public void ShowSignal(DogSignal signal)
    {
        ApplyReaction(signal switch
        {
            DogSignal.Bored => EDogReactionTrigger.Bored,
            DogSignal.Happy => EDogReactionTrigger.Excited,
            DogSignal.LuckyEye => EDogReactionTrigger.Starstruck,
            DogSignal.TopTier => EDogReactionTrigger.Starstruck,
            _ => EDogReactionTrigger.Default,
        });
    }

    // TODO: 待清理。旧硬编码表情入口，主流程已改为 DogReaction 表驱动。
    public void ShowSunglasses()
    {
        ApplyReaction(EDogReactionTrigger.Silent);
    }

    public void ResetAccessories()
    {
        _eyewear.Visible = false;
        _headwear.Visible = false;
    }

    public void PlayDesktopTongueTap(int inputCount = 1)
    {
        if (!IsNodeReady() || _tongue == null) return;

        if (_desktopTongueTimer <= 0f && _desktopTongueActiveTimer <= 0f)
            _desktopTongueBasePosition = _tongue.Position;

        StopTongueAnimation();
        if (!IsDesktopTongueImmediateMode())
        {
            _desktopTongueBurstTimer = _desktopTongueBurstWindowSeconds;
            _desktopTongueBurstCount += Mathf.Max(1, inputCount);

            if (_desktopTongueBurstCount < _desktopTongueBurstThreshold && _desktopTongueActiveTimer <= 0f)
            {
                _tongue.Position = _desktopTongueBasePosition + new Vector2(0, _desktopTongueTapOffset);
                _desktopTongueTimer = _desktopTongueTapHoldSeconds;
                return;
            }

            _desktopTongueActiveTimer = Mathf.Min(
                _desktopTongueActiveSeconds + Mathf.Max(0, inputCount - 1) * 0.04f,
                1.2f
            );

            if (_desktopTongueBeatTimer <= 0f)
            {
                _desktopTongueExtended = true;
                _desktopTongueBeatTimer = _desktopTongueBeatSeconds;
                _tongue.Position = _desktopTongueBasePosition + new Vector2(0, _desktopTongueTapOffset);
            }
            return;
        }

        _tongue.Position = _desktopTongueBasePosition + new Vector2(0, _desktopTongueTapOffset);
        _desktopTongueTimer = Mathf.Min(
            _desktopTongueTapHoldSeconds + Mathf.Max(0, inputCount - 1) * 0.015f,
            0.16f
        );
    }

    private bool IsDesktopTongueImmediateMode()
    {
        return _desktopTongueImmediateMode || SettingsManager.LoadDesktopTongueImmediateMode();
    }

    public void RefreshEquippedVisuals()
    {
        if (!IsNodeReady()) return;

        var dogItem = _gameData?.Inventory.GetEquipped(EItemType.Dog);
        _dogSkin = dogItem != null
            ? LubanData.Tables.TbDogSkin.GetOrDefault(dogItem.SkinId)
            : null;

        ReapplyCurrentReaction();
    }

    public void RefreshEquippedDisguiseVisuals()
    {
        if (!IsNodeReady()) return;

        var dogItem = _gameData?.Inventory.GetEquipped(EItemType.Dog);
        _dogSkin = dogItem != null
            ? LubanData.Tables.TbDogSkin.GetOrDefault(dogItem.SkinId)
            : null;

        ReapplyCurrentReaction();
    }

    public void ApplyReaction(EDogReactionTrigger trigger)
    {
        if (!IsNodeReady()) return;

        _currentReaction = trigger;
        ReapplyCurrentReaction();
    }

    public void SetHitButtonEnabled(bool enabled)
    {
        if (!IsNodeReady()) return;

        _hitButton.Visible = enabled;
        _hitButton.Disabled = !enabled;
        _hitButton.MouseFilter = enabled
            ? Control.MouseFilterEnum.Stop
            : Control.MouseFilterEnum.Ignore;
    }

    public void SetIntroPartVisibility(bool showHeadParts, bool showTongue, bool showClaws)
    {
        if (!IsNodeReady()) return;

        if (showHeadParts)
            ReapplyCurrentReaction();

        _ears.Visible = showHeadParts;
        _head.Visible = showHeadParts;
        _eyes.Visible = showHeadParts;
        if (!showHeadParts)
        {
            _eyewear.Visible = false;
            _headwear.Visible = false;
        }

        if (_tongue != null)
            _tongue.Visible = showTongue;

        _clawLeft.Visible = showClaws;
        _clawRight.Visible = showClaws;
    }

    public void RefreshEquippedHeadwear()
    {
        if (!IsNodeReady()) return;

        var item = _gameData?.Inventory.GetEquipped(EItemType.Headwear);
        if (item == null || item.AssetPathList.Count == 0)
        {
            _headwear.Visible = false;
            return;
        }

        var path = PlayerInventory.ToResPath(item.AssetPathList[0]);
        var texture = GD.Load<Texture2D>(path);
        if (texture == null)
        {
            _headwear.Visible = false;
            return;
        }

        _headwear.Texture = texture;
        _headwear.Position = GetScenePosition(item.AssetPathList[0]);
        _headwear.Visible = true;
    }

    public void RefreshEquippedEyewear(bool showIfEquipped = false, bool allowOwnedFallback = false)
    {
        if (!IsNodeReady()) return;

        var item = _gameData?.Inventory.GetEquipped(EItemType.Eyewear);
        if ((item == null || item.AssetPathList.Count == 0) && allowOwnedFallback)
            item = _gameData?.Inventory.GetOwnedOfType(EItemType.Eyewear).FirstOrDefault();

        if (item == null || item.AssetPathList.Count == 0)
        {
            _eyewear.Visible = false;
            return;
        }

        var path = PlayerInventory.ToResPath(item.AssetPathList[0]);
        var texture = GD.Load<Texture2D>(path);
        if (texture == null)
        {
            _eyewear.Visible = false;
            return;
        }

        _eyewear.Texture = texture;
        _eyewear.Position = GetScenePosition(item.AssetPathList[0]);
        if (showIfEquipped)
            _eyewear.Visible = true;
    }

    // 手心（掌心朝上，被桌子挡住）
    // 层级：背景(0) < 狗头(1) < 爪子(1) < 桌子(2)
    public void ShowClawPalm()
    {
        _clawLeft.Visible = true;
        _clawRight.Visible = true;
        _clawLeft.ZAsRelative = false;
        _clawRight.ZAsRelative = false;
        _clawLeft.ZIndex = 1;
        _clawRight.ZIndex = 1;
        SetClawState(_clawLeft, "Palm");
        SetClawState(_clawRight, "Palm");
    }

    // 手背（手背朝上，挡住桌子）
    // 层级：背景(0) < 狗头(1) < 桌子(2) < 爪子(3)
    public void ShowClawBack()
    {
        _clawLeft.Visible = true;
        _clawRight.Visible = true;
        _clawLeft.ZAsRelative = false;
        _clawRight.ZAsRelative = false;
        _clawLeft.ZIndex = 3;
        _clawRight.ZIndex = 3;
        SetClawState(_clawLeft, "Back");
        SetClawState(_clawRight, "Back");
    }

    public void HideClaw()
    {
        _clawLeft.Visible = false;
        _clawRight.Visible = false;
    }

    // TODO: 待清理。旧动画入口，主流程应通过 DogReaction.Left/RightPawAnimation 驱动。
    // 摇手拒绝动画（双手旋转摆动）
    public void ShakePaw()
    {
        float angle = 0.25f; // ~15度
        var tween = CreateTween().SetParallel(true);
        tween.TweenProperty(_clawLeft, "rotation", angle, 0.06);
        tween.TweenProperty(_clawRight, "rotation", -angle, 0.06);
        tween.Chain().TweenProperty(_clawLeft, "rotation", -angle, 0.06);
        tween.TweenProperty(_clawRight, "rotation", angle, 0.06);
        tween.Chain().TweenProperty(_clawLeft, "rotation", angle * 0.6f, 0.05);
        tween.TweenProperty(_clawRight, "rotation", -angle * 0.6f, 0.05);
        tween.Chain().TweenProperty(_clawLeft, "rotation", 0f, 0.04);
        tween.TweenProperty(_clawRight, "rotation", 0f, 0.04);
    }

    private void SetClawState(Node2D claw, string state)
    {
        var back = claw.GetNode<Sprite2D>("Claw_Back_Left");
        var palm = claw.GetNode<Sprite2D>("Claw_Palm_Left");
        back.Visible = state == "Back";
        palm.Visible = state == "Palm";
    }

    private DogSkin CurrentDogSkin => _dogSkin ?? LubanData.Tables.TbDogSkin.Get(1001);

    private string DogResPath(string fileName)
    {
        return PlayerInventory.ToResPath($"{CurrentDogSkin.FolderPath}\\{fileName}");
    }

    private Vector2 GetDogScenePosition(string fileName)
    {
        return GetScenePosition($"{CurrentDogSkin.FolderPath}\\{fileName}");
    }

    private void SetDogTexture(Sprite2D sprite, string fileName)
    {
        var texture = GD.Load<Texture2D>(DogResPath(fileName));
        if (texture != null)
            sprite.Texture = texture;
    }

    private void ReapplyCurrentReaction()
    {
        ApplyReactionVisual(ResolveReaction(_currentReaction));
    }

    private DogReactionVisual ResolveReaction(EDogReactionTrigger trigger)
    {
        var reaction = LubanData.Tables.TbDogReaction.GetOrDefault((int)trigger);
        if (reaction == null)
            return DogReactionVisual.Default;

        if (reaction.AssetRef == EDogReactionTrigger.Bespoke)
            return ResolveBespokeReaction(reaction.Id);

        var result = DogReactionVisual.Default;
        if (reaction.AssetRef != EDogReactionTrigger.None && reaction.AssetRef != EDogReactionTrigger.Bespoke)
            result = ResolveReaction(reaction.AssetRef);

        if (!string.IsNullOrEmpty(reaction.EarAsset))
            result.EarAsset = reaction.EarAsset;
        if (!string.IsNullOrEmpty(reaction.EyeAsset))
            result.EyeAsset = reaction.EyeAsset;
        if (!string.IsNullOrEmpty(reaction.OverrideHeadwear))
            result.OverrideHeadwear = reaction.OverrideHeadwear;
        if (reaction.WearGlasses)
            result.WearGlasses = true;
        if (!string.IsNullOrEmpty(reaction.LeftPawAnimation))
            result.LeftPawAnimation = reaction.LeftPawAnimation;
        if (!string.IsNullOrEmpty(reaction.RightPawAnimation))
            result.RightPawAnimation = reaction.RightPawAnimation;
        if (!string.IsNullOrEmpty(reaction.TongueAnimation))
            result.TongueAnimation = reaction.TongueAnimation;

        return result;
    }

    private DogReactionVisual ResolveBespokeReaction(int reactionId)
    {
        return reactionId switch
        {
            2004 => DogReactionVisual.Default,
            2008 => DogReactionVisual.Default,
            _ => DogReactionVisual.Default,
        };
    }

    private void ApplyReactionVisual(DogReactionVisual visual)
    {
        var skin = CurrentDogSkin;
        var eyeAsset = ResolveDogAsset(visual.EyeAsset, skin.DefaultEyes);
        var earAsset = ResolveDogAsset(visual.EarAsset, skin.DefaultEars);

        SetDogTexture(_head, skin.Head);
        _head.Position = GetDogScenePosition(skin.Head);

        SetDogTexture(_eyes, eyeAsset);
        _eyes.Position = GetDogScenePosition(eyeAsset);

        SetDogTexture(_ears, earAsset);
        _ears.Position = GetDogScenePosition(earAsset);

        if (_tongue != null)
        {
            StopTongueAnimation();
            _desktopTongueTimer = 0f;
            _desktopTongueActiveTimer = 0f;
            _desktopTongueBeatTimer = 0f;
            _desktopTongueBurstTimer = 0f;
            _desktopTongueBurstCount = 0;
            _desktopTongueExtended = false;
            SetDogTexture(_tongue, skin.TongueRegular);
            _tongue.Position = GetDogScenePosition(skin.TongueRegular);
        }

        // _head.ZIndex = 1;
        // _eyes.ZIndex = 1;
        // _ears.ZIndex = 1;
        // if (_tongue != null)
        //     _tongue.ZIndex = 1;
        // _eyewear.ZIndex = 1;
        // _headwear.ZIndex = 1;

        ApplyClawTextures();
        ApplyPawVisual(visual);
        RefreshEquippedHeadwear();

        if (!string.IsNullOrEmpty(visual.OverrideHeadwear))
            ApplyHeadwearOverride(visual.OverrideHeadwear);

        var shouldShowEyewear = visual.WearGlasses || ShowEquippedEyewearByDefault;
        RefreshEquippedEyewear(showIfEquipped: shouldShowEyewear, allowOwnedFallback: visual.WearGlasses);
        if (!shouldShowEyewear)
            _eyewear.Visible = false;

        ApplyTongueVisual(visual);
    }

    private void ApplyBaseAppearance(string eyesFileName, string earsFileName)
    {
        var skin = CurrentDogSkin;

        SetDogTexture(_head, skin.Head);
        _head.Position = GetDogScenePosition(skin.Head);

        SetDogTexture(_eyes, eyesFileName);
        _eyes.Position = GetDogScenePosition(eyesFileName);

        SetDogTexture(_ears, earsFileName);
        _ears.Position = GetDogScenePosition(earsFileName);

        if (_tongue != null)
        {
            SetDogTexture(_tongue, skin.TongueRegular);
            _tongue.Position = GetDogScenePosition(skin.TongueRegular);
        }

        // // 狗身体层级：背景(0) < 狗(1) < 桌子(2)
        // _head.ZIndex = 1;
        // _eyes.ZIndex = 1;
        // _ears.ZIndex = 1;
        // if (_tongue != null)
        //     _tongue.ZIndex = 1;
        // _eyewear.ZIndex = 1;
        // _headwear.ZIndex = 1;

        ApplyClawTextures();
        ShowClawBack();
        _eyewear.Visible = false;
        RefreshEquippedHeadwear();
    }

    private void ApplyHeadwearOverride(string assetPath)
    {
        var texture = GD.Load<Texture2D>(PlayerInventory.ToResPath(assetPath));
        if (texture == null) return;

        _headwear.Texture = texture;
        _headwear.Position = GetScenePosition(assetPath);
        _headwear.Visible = true;
    }

    private void ApplyPawVisual(DogReactionVisual visual)
    {
        var left = visual.LeftPawAnimation;
        var right = visual.RightPawAnimation;
        if (left.Contains("手心") || right.Contains("手心"))
            ShowClawPalm();
        else
            ShowClawBack();

        if (left.Contains("摆手") || right.Contains("摆手"))
            ShakePaw();
    }

    private void ApplyTongueVisual(DogReactionVisual visual)
    {
        if (_tongue == null) return;

        if (visual.TongueAnimation.Contains("哈气"))
            PlayTonguePant();
    }

    private void PlayTonguePant()
    {
        if (_tongue == null) return;

        StopTongueAnimation();
        var basePosition = _tongue.Position;
        _tongueTween = CreateTween();
        _tongueTween.SetLoops(Mathf.Max(1, _tonguePantLoops));
        _tongueTween.TweenProperty(_tongue, "position", basePosition + new Vector2(0, _tonguePantOffset), _tonguePantStepDuration);
        _tongueTween.TweenProperty(_tongue, "position", basePosition, _tonguePantStepDuration);
        _tongueTween.TweenProperty(_tongue, "position", basePosition - new Vector2(0, _tonguePantOffset), _tonguePantStepDuration);
        _tongueTween.TweenProperty(_tongue, "position", basePosition, _tonguePantStepDuration);
    }

    private void StopTongueAnimation()
    {
        if (_tongueTween == null) return;

        _tongueTween.Kill();
        _tongueTween = null;
    }

    private string ResolveDogAsset(string asset, string defaultAsset)
    {
        if (string.IsNullOrEmpty(asset) || asset == "默认")
            return defaultAsset;

        return asset.EndsWith(".png") ? asset : $"{asset}.png";
    }

    private void ApplyClawTextures()
    {
        var skin = CurrentDogSkin;
        SetClawTextures(_clawLeft, skin.ClawLeftBack, skin.ClawRightPalms);
        SetClawTextures(_clawRight, skin.ClawLeftBack, skin.ClawRightPalms);
    }

    private void SetClawTextures(Node2D claw, string backFileName, string palmFileName)
    {
        var back = claw.GetNode<Sprite2D>("Claw_Back_Left");
        var palm = claw.GetNode<Sprite2D>("Claw_Palm_Left");
        SetDogTexture(back, backFileName);
        SetDogTexture(palm, palmFileName);
    }

    private (string eyes, string ears) GetSignalTextureNames(DogSignal signal)
    {
        var skin = CurrentDogSkin;
        return signal switch
        {
            DogSignal.Bored => (skin.EyesBored, skin.EarsPlane),
            DogSignal.Happy => (skin.EyesHappy, skin.EarsHappy),
            DogSignal.LuckyEye => (skin.EyesLucky, skin.EarsHappy),
            DogSignal.TopTier => (skin.EyesLucky, skin.EarsHappy),
            _ => (skin.EyesCute, skin.EarsHappy),
        };
    }

    private struct DogReactionVisual
    {
        public string EarAsset;
        public string EyeAsset;
        public string OverrideHeadwear;
        public bool WearGlasses;
        public string LeftPawAnimation;
        public string RightPawAnimation;
        public string TongueAnimation;

        public static DogReactionVisual Default => new()
        {
            EarAsset = "默认",
            EyeAsset = "默认",
            OverrideHeadwear = "",
            WearGlasses = false,
            LeftPawAnimation = "手背",
            RightPawAnimation = "手背",
            TongueAnimation = "正常",
        };
    }

    private static string NormalizeLayerPath(string path)
    {
        path = path.Replace('\\', '/');
        const string assetsPrefix = "res://Assets/";
        if (path.StartsWith(assetsPrefix))
            path = path[assetsPrefix.Length..];
        if (path.StartsWith("v1/"))
            path = path[3..];
        return path;
    }

    private static float ReadFloat(Godot.Collections.Dictionary d, string preferredKey, string fallbackKey)
    {
        return d.ContainsKey(preferredKey)
            ? (float)d[preferredKey].AsDouble()
            : (float)d[fallbackKey].AsDouble();
    }
}
