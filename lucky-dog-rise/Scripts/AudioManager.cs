using Godot;
using System;
using System.Collections.Generic;

namespace LuckyDogRise;

/// <summary>
/// 全局音效入口。
/// 调用方只传逻辑名（例如 "ChipDrop" 或 "PlayCard/CardDeal"），
/// 本类会依次查找 .ogg、.wav、.mp3；同名 .xxx.txt 仅作为开发占位并打印文件名。
/// </summary>
public partial class AudioManager : Node
{
    private enum AudioKind
    {
        Sfx,
        Bgm,
    }

    private const int SfxPlayerCount = 12;
    private static readonly string[] SupportedExtensions = [".ogg", ".wav", ".mp3"];
    private readonly Dictionary<string, AudioStream> _streamCache = new(StringComparer.Ordinal);
    private readonly List<AudioStreamPlayer> _sfxPlayers = new();
    private AudioStreamPlayer _bgmPlayer = null!;

    // 保持与原设置页兼容：0 表示静音，1 表示原始音量。
    public float SfxVolume { get; private set; } = 1f;
    public float BgmVolume { get; private set; } = 0.7f;

    public static AudioManager Instance { get; private set; } = null!;

    public override void _Ready()
    {
        Instance = this;

        _bgmPlayer = CreatePlayer(AudioKind.Bgm);
        AddChild(_bgmPlayer);

        for (var index = 0; index < SfxPlayerCount; index++)
        {
            var player = CreatePlayer(AudioKind.Sfx);
            AddChild(player);
            _sfxPlayers.Add(player);
        }

        ApplyBusVolume(AudioKind.Sfx, SfxVolume);
        ApplyBusVolume(AudioKind.Bgm, BgmVolume);
    }

    /// <summary>播放短音效。cue 不带扩展名，例如 PlaySfx("Chip_BetStackLanding_1")。</summary>
    public void PlaySfx(string cue)
    {
        if (!TryResolve(AudioKind.Sfx, cue, out var stream))
            return;

        var player = GetAvailableSfxPlayer();
        player.Stream = stream;
        player.Play();
    }

    /// <summary>兼容旧调用；可传 Knock.wav，也可直接传 Knock。</summary>
    public void PlaySfxByName(string fileName) => PlaySfx(fileName);

    /// <summary>播放 BGM。cue 不带扩展名，例如 PlayBgm("MainTheme")。</summary>
    public void PlayBgm(string cue)
    {
        if (!TryResolve(AudioKind.Bgm, cue, out var stream))
            return;

        _bgmPlayer.Stream = stream;
        _bgmPlayer.Play();
    }

    /// <summary>兼容旧调用；可传 MainTheme.ogg，也可直接传 MainTheme。</summary>
    public void PlayBgmByName(string fileName) => PlayBgm(fileName);

    public void StopBgm()
    {
        _bgmPlayer?.Stop();
    }

    public void SetSfxVolume(float linear)
    {
        SfxVolume = Mathf.Clamp(linear, 0f, 1f);
        ApplyBusVolume(AudioKind.Sfx, SfxVolume);
    }

    public void SetBgmVolume(float linear)
    {
        BgmVolume = Mathf.Clamp(linear, 0f, 1f);
        ApplyBusVolume(AudioKind.Bgm, BgmVolume);
    }

    private static AudioStreamPlayer CreatePlayer(AudioKind kind)
    {
        return new AudioStreamPlayer { Bus = GetBusName(kind) };
    }

    private bool TryResolve(AudioKind kind, string inputCue, out AudioStream stream)
    {
        stream = null!;
        var cue = NormalizeCue(kind, inputCue);
        if (string.IsNullOrEmpty(cue))
        {
            GD.PushWarning($"[Audio] Empty {kind} cue.");
            return false;
        }

        var cacheKey = $"{kind}:{cue}";
        if (_streamCache.TryGetValue(cacheKey, out stream))
            return true;

        var folder = kind == AudioKind.Sfx ? "res://Audio/SFX" : "res://Audio/BGM";
        var basePath = $"{folder}/{cue}";
        foreach (var extension in SupportedExtensions)
        {
            var audioPath = basePath + extension;
            if (!ResourceLoader.Exists(audioPath))
                continue;

            stream = GD.Load<AudioStream>(audioPath);
            if (stream == null)
            {
                GD.PushWarning($"[Audio] Failed to load: {audioPath}");
                return false;
            }

            _streamCache[cacheKey] = stream;
            return true;
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

    private static string NormalizeCue(AudioKind kind, string cue)
    {
        if (string.IsNullOrWhiteSpace(cue))
            return string.Empty;

        cue = cue.Replace('\\', '/').Trim();
        var expectedPrefix = kind == AudioKind.Sfx ? "res://Audio/SFX/" : "res://Audio/BGM/";
        if (cue.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            cue = cue[expectedPrefix.Length..];

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

    private void ApplyBusVolume(AudioKind kind, float linear)
    {
        var busIndex = AudioServer.GetBusIndex(GetBusName(kind));
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
}
