using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using DataTables;

namespace LuckyDogRise;

/// <summary>
/// 全局音效入口。
/// 调用方只传不含变体号的逻辑名（例如 "Chip_BetStackLanding"）。
/// 本类会在 _1、_2… 变体中等权随机选择，并优先查找 .ogg、.wav、.mp3；
/// 同名 .xxx.txt 仅作为开发占位并打印文件名。
/// </summary>
public partial class AudioManager : Node
{
    public enum BlindBoxUpgradePitchMode
    {
        Scale,
        Arpeggio,
    }

    private enum AudioKind
    {
        Sfx,
        Bgm,
    }

    private const int SfxPlayerCount = 12;
    private const int MaxAudioVariants = 64;
    private const string PitchShiftSfxBus = "PitchShiftSFX";
    private const string RequiredSfxPath = "res://Audio/SFX/Card_PokerHandDeal_1.ogg";
    private static readonly string[] SupportedExtensions = [".ogg", ".wav", ".mp3"];
    private readonly Dictionary<string, AudioStream> _streamCache = new(StringComparer.Ordinal);
    private readonly List<AudioStreamPlayer> _sfxPlayers = new();
    private AudioStreamPlayer _bgmPlayer = null!;
    private AudioStreamPlayer _pitchShiftSfxPlayer = null!;
    private AudioStreamPlayer _loopSfxPlayer = null!;
    private Tween _loopSfxFadeTween = null!;
    private bool _loopSfxActive;
    private int _lastBgmId = -1;
    private bool _bgmPausedForDesktop;
    private bool _bgmPausedForVolume;
#if DEBUG
    // TEMP DEBUG: 盲盒升品音效响度确定后可连同 Debug 页输入框一起删除。
    private float _debugBlindBoxUpgradeGainDb;
#endif

    // 保持与原设置页兼容：0 表示静音，1 表示原始音量。
    public float SfxVolume { get; private set; } = 0.5f;
    public float BgmVolume { get; private set; } = 0.5f;
    public BlindBoxUpgradePitchMode BlindBoxPitchMode { get; set; } = BlindBoxUpgradePitchMode.Scale;

    public static AudioManager Instance { get; private set; } = null!;

    public override void _Ready()
    {
        Instance = this;

        if (!DirAccess.DirExistsAbsolute("res://Audio/SFX"))
            GD.PushError("[Audio] Exported SFX directory is missing.");
        if (!ResourceLoader.Exists(RequiredSfxPath))
            GD.PushError("[Audio] Required SFX resource is missing.");
        if (!TryResolve(AudioKind.Sfx, "Card_PokerHandDeal", null, out _))
            GD.PushError("[Audio] Required SFX cue cannot be resolved.");

        _bgmPlayer = CreatePlayer(AudioKind.Bgm);
        _bgmPlayer.Finished += PlayRandomBgm;
        AddChild(_bgmPlayer);

        for (var index = 0; index < SfxPlayerCount; index++)
        {
            var player = CreatePlayer(AudioKind.Sfx);
            AddChild(player);
            _sfxPlayers.Add(player);
        }

        _pitchShiftSfxPlayer = new AudioStreamPlayer { Bus = PitchShiftSfxBus };
        AddChild(_pitchShiftSfxPlayer);

        _loopSfxPlayer = CreatePlayer(AudioKind.Sfx);
        _loopSfxPlayer.Finished += OnLoopSfxFinished;
        AddChild(_loopSfxPlayer);

        ApplyBusVolume(AudioKind.Sfx, SfxVolume);
        ApplyBusVolume(PitchShiftSfxBus, SfxVolume);
        ApplyBusVolume(AudioKind.Bgm, BgmVolume);
    }

    /// <summary>
    /// 播放短音效。cue 不含变体号；持续音效的状态单独传入，
    /// 例如 PlaySfx("Tool_ElectricDrill", "Loop") 会匹配 Tool_ElectricDrill_1_Loop。
    /// </summary>
    public void PlaySfx(string cue, string state = null, float pitchVariation = 0f)
    {
        PlaySfxInternal(cue, state, 1f, pitchVariation);
    }

    /// <summary>播放带随机变调的短音效，例如 PlaySfx("Card_PokerHandDeal", 0.06f)。</summary>
    public void PlaySfx(string cue, float pitchVariation)
    {
        PlaySfxInternal(cue, null, 1f, pitchVariation);
    }

    /// <summary>以指定基准音高播放，并在该基准附近加入随机变调。</summary>
    public void PlaySfx(string cue, float pitchCenter, float pitchVariation)
    {
        PlaySfxInternal(cue, null, pitchCenter, pitchVariation);
    }

    /// <summary>播放持续状态音效；资源自身未启用循环时，播放结束后也会自动重播。</summary>
    public void PlayLoopingSfx(string cue, string state = "Loop")
    {
        if (SfxVolume <= 0f || !TryResolve(AudioKind.Sfx, cue, state, out var stream))
            return;

        _loopSfxFadeTween?.Kill();
        _loopSfxActive = true;
        _loopSfxPlayer.Stream = stream;
        _loopSfxPlayer.VolumeDb = 0f;
        _loopSfxPlayer.Play();
    }

    public void StopLoopingSfx(float fadeOutSeconds = 0f)
    {
        _loopSfxActive = false;
        _loopSfxFadeTween?.Kill();

        if (!_loopSfxPlayer.Playing)
        {
            _loopSfxPlayer.VolumeDb = 0f;
            return;
        }

        if (fadeOutSeconds <= 0f)
        {
            _loopSfxPlayer.Stop();
            _loopSfxPlayer.VolumeDb = 0f;
            return;
        }

        _loopSfxFadeTween = CreateTween();
        _loopSfxFadeTween.TweenProperty(_loopSfxPlayer, "volume_db", -80f, fadeOutSeconds);
        _loopSfxFadeTween.TweenCallback(Callable.From(() =>
        {
            _loopSfxPlayer.Stop();
            _loopSfxPlayer.VolumeDb = 0f;
        }));
    }

    private void OnLoopSfxFinished()
    {
        if (_loopSfxActive && _loopSfxPlayer.Stream != null)
            _loopSfxPlayer.Play();
    }

    /// <summary>
    /// 通过独立总线的 AudioEffectPitchShift 改变音高，保持播放速度。
    /// </summary>
    public void PlayPitchShiftedSfx(string cue, float pitchScale, float volumeDb = 0f)
    {
        if (SfxVolume <= 0f || !TryResolve(AudioKind.Sfx, cue, null, out var stream))
            return;

        var busIndex = AudioServer.GetBusIndex(PitchShiftSfxBus);
        if (busIndex < 0 || AudioServer.GetBusEffect(busIndex, 0) is not AudioEffectPitchShift effect)
        {
            GD.PushWarning("[Audio] PitchShiftSFX bus or AudioEffectPitchShift is unavailable.");
            return;
        }

        effect.PitchScale = Mathf.Clamp(pitchScale, 0.01f, 4f);
        _pitchShiftSfxPlayer.Stream = stream;
        _pitchShiftSfxPlayer.PitchScale = 1f;
        _pitchShiftSfxPlayer.VolumeDb = volumeDb;
        _pitchShiftSfxPlayer.Play();
    }

    public void PlayBlindBoxUpgradeSfx(ERarity rarity)
    {
        var gainDb = 0f;
#if DEBUG
        gainDb = _debugBlindBoxUpgradeGainDb;
#endif
        PlayPitchShiftedSfx("BlindBox/BlindBox_RarityUpgrade", GetBlindBoxPitchScale(rarity), gainDb);
    }

#if DEBUG
    public void SetDebugBlindBoxUpgradeGainDb(float gainDb)
    {
        _debugBlindBoxUpgradeGainDb = Mathf.Clamp(gainDb, -24f, 24f);
    }
#endif

    public void PlayBlindBoxRewardRevealSfx(ERarity rarity)
    {
        PlayPitchShiftedSfx("BlindBox/BlindBox_RewardReveal", GetBlindBoxPitchScale(rarity));
    }

    private float GetBlindBoxPitchScale(ERarity rarity)
    {
        var semitones = BlindBoxPitchMode switch
        {
            BlindBoxUpgradePitchMode.Arpeggio => rarity switch
            {
                ERarity.Common => 0,
                ERarity.Uncommon => 4,
                ERarity.Rare => 7,
                ERarity.Epic => 12,
                ERarity.Legendary => 16,
                ERarity.Mythic => 19,
                _ => 0,
            },
            _ => rarity switch
            {
                ERarity.Common => 0,
                ERarity.Uncommon => 2,
                ERarity.Rare => 4,
                ERarity.Epic => 5,
                ERarity.Legendary => 7,
                ERarity.Mythic => 9,
                _ => 0,
            },
        };

        return Mathf.Pow(2f, semitones / 12f);
    }

    private void PlaySfxInternal(string cue, string state, float pitchCenter, float pitchVariation)
    {
        if (SfxVolume <= 0f)
            return;

        if (!TryResolve(AudioKind.Sfx, cue, state, out var stream))
            return;

        var player = GetAvailableSfxPlayer();
        player.Stream = stream;
        player.PitchScale = GetRandomPitchScale(pitchCenter, pitchVariation);
        player.Play();
    }

    /// <summary>兼容旧调用；可传 Knock.wav，也可直接传 Knock。</summary>
    public void PlaySfxByName(string fileName) => PlaySfx(fileName);

    /// <summary>播放 BGM。cue 不带扩展名，例如 PlayBgm("MainTheme")。</summary>
    public void PlayBgm(string cue)
    {
        if (!TryResolve(AudioKind.Bgm, cue, null, out var stream))
            return;

        _bgmPlayer.Stream = stream;
        _bgmPlayer.VolumeDb = 0f;
        _bgmPlayer.Play();
    }

    /// <summary>兼容旧调用；可传 MainTheme.ogg，也可直接传 MainTheme。</summary>
    public void PlayBgmByName(string fileName) => PlayBgm(fileName);

    /// <summary>
    /// 从 Luban BGMList 中随机播放启用曲目；存在多首时不会连续播放同一首。
    /// </summary>
    public void PlayRandomBgm()
    {
        var enabledTracks = LubanData.Tables.TbBGMList.DataList
            .Where(track => track.Enabled)
            .ToArray();
        if (enabledTracks.Length == 0)
        {
            GD.PushWarning("[BGM] No enabled tracks in BGMList.");
            return;
        }

        var candidates = enabledTracks.Length > 1
            ? enabledTracks.Where(track => track.Id != _lastBgmId).ToArray()
            : enabledTracks;
        foreach (var track in candidates.OrderBy(_ => GD.Randf()))
        {
            if (!TryResolve(AudioKind.Bgm, track.AudioPath, null, out var stream))
                continue;

            _bgmPlayer.Stream = stream;
            _bgmPlayer.VolumeDb = track.VolumeDb;
            _bgmPlayer.Play();
            _lastBgmId = track.Id;
            return;
        }

        GD.PushWarning("[BGM] No enabled BGMList track could be loaded.");
    }

    public void StopBgm()
    {
        _bgmPlayer?.Stop();
    }

    /// <summary>暂停或恢复当前 BGM，保留曲目与播放进度。</summary>
    public void SetBgmPaused(bool paused)
    {
        _bgmPausedForDesktop = paused;
        RefreshBgmPausedState();
    }

    public void SetSfxVolume(float linear)
    {
        SfxVolume = Mathf.Clamp(linear, 0f, 1f);
        if (SfxVolume <= 0f)
            StopAllSfx();
        ApplyBusVolume(AudioKind.Sfx, SfxVolume);
        ApplyBusVolume(PitchShiftSfxBus, SfxVolume);
    }

    public void SetBgmVolume(float linear)
    {
        BgmVolume = Mathf.Clamp(linear, 0f, 1f);
        _bgmPausedForVolume = BgmVolume <= 0f;
        RefreshBgmPausedState();
        ApplyBusVolume(AudioKind.Bgm, BgmVolume);
    }

    private static AudioStreamPlayer CreatePlayer(AudioKind kind)
    {
        return new AudioStreamPlayer { Bus = GetBusName(kind) };
    }

    private bool TryResolve(AudioKind kind, string inputCue, string state, out AudioStream stream)
    {
        stream = null!;
        var cue = NormalizeCue(kind, inputCue);
        if (string.IsNullOrEmpty(cue))
        {
            GD.PushWarning($"[Audio] Empty {kind} cue.");
            return false;
        }

        var folder = kind == AudioKind.Sfx ? "res://Audio/SFX" : "res://Audio/BGM";
        var variantPaths = FindVariantPaths(folder, cue, state, placeholders: false);
        if (variantPaths.Count > 0)
        {
            var selectedPath = variantPaths[GD.RandRange(0, variantPaths.Count - 1)];
            return TryLoadStream(selectedPath, out stream);
        }

        var placeholderPaths = FindVariantPaths(folder, cue, state, placeholders: true);
        if (placeholderPaths.Count > 0)
        {
            var selectedPath = placeholderPaths[GD.RandRange(0, placeholderPaths.Count - 1)];
            GD.Print($"[{(kind == AudioKind.Sfx ? "SFX" : "BGM")} Placeholder] {selectedPath.GetFile()}");
            return false;
        }

        // 兼容没有变体号的旧资源（例如当前 BGM MainTheme.ogg）。
        var basePath = $"{folder}/{cue}";
        foreach (var extension in SupportedExtensions)
        {
            var audioPath = basePath + extension;
            if (!ResourceLoader.Exists(audioPath))
                continue;

            return TryLoadStream(audioPath, out stream);
        }

        foreach (var extension in SupportedExtensions)
        {
            var placeholderPath = basePath + extension + ".txt";
            if (FileAccess.FileExists(placeholderPath))
            {
                GD.Print($"[{(kind == AudioKind.Sfx ? "SFX" : "BGM")} Placeholder] {placeholderPath.GetFile()}");
                return false;
            }
        }

        GD.PushWarning($"[{(kind == AudioKind.Sfx ? "SFX" : "BGM")} Missing] {cue}");
        return false;
    }

    private List<string> FindVariantPaths(string rootFolder, string cue, string state, bool placeholders)
    {
        var slashIndex = cue.LastIndexOf('/');
        var relativeFolder = slashIndex >= 0 ? cue[..slashIndex] : string.Empty;
        var cueName = slashIndex >= 0 ? cue[(slashIndex + 1)..] : cue;
        var folder = string.IsNullOrEmpty(relativeFolder) ? rootFolder : $"{rootFolder}/{relativeFolder}";
        var stateSuffix = string.IsNullOrEmpty(state) ? string.Empty : $"_{state}";
        var variants = new SortedDictionary<int, string>();

        // Exported encrypted PCKs do not expose imported source names through
        // DirAccess, so probe the stable variant paths through ResourceLoader.
        foreach (var extension in SupportedExtensions)
        {
            var suffix = stateSuffix + extension + (placeholders ? ".txt" : string.Empty);
            for (var variant = 1; variant <= MaxAudioVariants; variant++)
            {
                if (variants.ContainsKey(variant))
                    continue;

                var candidatePath = $"{folder}/{cueName}_{variant}{suffix}";
                if (placeholders ? FileAccess.FileExists(candidatePath) : ResourceLoader.Exists(candidatePath))
                    variants.Add(variant, candidatePath);
            }
        }

        return new List<string>(variants.Values);
    }

    private bool TryLoadStream(string audioPath, out AudioStream stream)
    {
        if (_streamCache.TryGetValue(audioPath, out stream))
            return true;

        stream = GD.Load<AudioStream>(audioPath);
        if (stream == null)
        {
            GD.PushWarning($"[Audio] Failed to load: {audioPath}");
            return false;
        }

        _streamCache[audioPath] = stream;
        return true;
    }

    private static string NormalizeCue(AudioKind kind, string cue)
    {
        if (string.IsNullOrWhiteSpace(cue))
            return string.Empty;

        cue = cue.Replace('\\', '/').Trim();
        var expectedPrefix = kind == AudioKind.Sfx ? "res://Audio/SFX/" : "res://Audio/BGM/";
        var projectRelativePrefix = kind == AudioKind.Sfx ? "Audio/SFX/" : "Audio/BGM/";
        if (cue.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            cue = cue[expectedPrefix.Length..];
        else if (cue.StartsWith(projectRelativePrefix, StringComparison.OrdinalIgnoreCase))
            cue = cue[projectRelativePrefix.Length..];

        foreach (var extension in SupportedExtensions)
        {
            if (cue.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return cue[..^extension.Length];
        }

        return cue.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? cue[..^4]
            : cue;
    }

    private AudioStreamPlayer GetAvailableSfxPlayer()
    {
        foreach (var player in _sfxPlayers)
        {
            if (!player.Playing)
                return player;
        }

        // 所有槽位繁忙时复用最早创建的槽位；普通 UI 音效不会因此阻塞主流程。
        return _sfxPlayers[0];
    }

    private void StopAllSfx()
    {
        foreach (var player in _sfxPlayers)
            player.Stop();
        _pitchShiftSfxPlayer?.Stop();
        _loopSfxActive = false;
        _loopSfxFadeTween?.Kill();
        _loopSfxPlayer?.Stop();
    }

    private void RefreshBgmPausedState()
    {
        if (_bgmPlayer != null)
            _bgmPlayer.StreamPaused = _bgmPausedForDesktop || _bgmPausedForVolume;
    }

    private void ApplyBusVolume(AudioKind kind, float linear)
    {
        ApplyBusVolume(GetBusName(kind), linear);
    }

    private static void ApplyBusVolume(string busName, float linear)
    {
        var busIndex = AudioServer.GetBusIndex(busName);
        if (busIndex < 0)
            return;

        AudioServer.SetBusVolumeDb(busIndex, LinearToDb(linear));
    }

    private static string GetBusName(AudioKind kind)
    {
        var desiredBus = kind == AudioKind.Sfx ? "SFX" : "BGM";
        return AudioServer.GetBusIndex(desiredBus) >= 0 ? desiredBus : "Master";
    }

    private static float LinearToDb(float linear)
    {
        return linear <= 0.0001f ? -80f : Mathf.LinearToDb(linear);
    }

    private static float GetRandomPitchScale(float center, float variation)
    {
        variation = Mathf.Max(variation, 0f);
        var offset = ((float)GD.Randf() * 2f - 1f) * variation;
        return Mathf.Clamp(center + offset, 0.01f, 4f);
    }
}
