using Godot;
using System.Collections.Generic;

namespace LuckyDogRise;

public partial class AudioManager : Node
{
    public static AudioManager Instance { get; private set; } = null!;

    private readonly Dictionary<string, AudioStream> _cache = new();
    private readonly List<AudioStreamPlayer> _sfxPool = new();
    private AudioStreamPlayer _bgmPlayer = null!;
    private const int SfxPoolSize = 8;

    // 音量控制（线性 0.0-1.0）
    public float SfxVolume { get; set; } = 1.0f;
    public float BgmVolume { get; set; } = 0.7f;

    public override void _Ready()
    {
        Instance = this;

        // BGM 播放器
        _bgmPlayer = new AudioStreamPlayer();
        _bgmPlayer.Bus = "BGM";
        AddChild(_bgmPlayer);

        // SFX 播放器池
        for (int i = 0; i < SfxPoolSize; i++)
        {
            var player = new AudioStreamPlayer();
            player.Bus = "SFX";
            AddChild(player);
            _sfxPool.Add(player);
        }

        // 预加载音效
        PreloadSfx("res://Audio/SFX/Knock.wav");
        PreloadSfx("res://Audio/SFX/CardClick.wav");
        PreloadSfx("res://Audio/SFX/ChipCollect.wav");

        // 预加载BGM
        PreloadBgm("res://Audio/BGM/MainTheme.ogg");
    }

    public override void _Process(double delta)
    {
        // 实时同步音量
        _bgmPlayer.VolumeDb = LinearToDb(BgmVolume);
        foreach (var p in _sfxPool)
            p.VolumeDb = LinearToDb(SfxVolume);
    }

    private void PreloadSfx(string path)
    {
        if (ResourceLoader.Exists(path))
            _cache[path] = GD.Load<AudioStream>(path);
    }

    private void PreloadBgm(string path)
    {
        if (ResourceLoader.Exists(path))
            _cache[path] = GD.Load<AudioStream>(path);
    }

    public void PlaySfx(string path)
    {
        if (!_cache.TryGetValue(path, out var stream))
        {
            GD.Print($"[SFX] {path.GetFile().GetBaseName()}");
            return;
        }

        var player = GetFreeSfxPlayer();
        player.Stream = stream;
        player.VolumeDb = LinearToDb(SfxVolume);
        player.Play();
    }

    public void PlaySfxByName(string fileName)
    {
        PlaySfx($"res://Audio/SFX/{fileName}");
    }

    public void PlayBgm(string path)
    {
        if (!_cache.TryGetValue(path, out var stream))
        {
            GD.Print($"[BGM] {path.GetFile().GetBaseName()}");
            return;
        }

        _bgmPlayer.Stream = stream;
        _bgmPlayer.VolumeDb = LinearToDb(BgmVolume);
        _bgmPlayer.Play();
    }

    public void PlayBgmByName(string fileName)
    {
        PlayBgm($"res://Audio/BGM/{fileName}");
    }

    public void StopBgm()
    {
        _bgmPlayer.Stop();
    }

    public void SetSfxVolume(float linear)
    {
        SfxVolume = Mathf.Clamp(linear, 0f, 1f);
    }

    public void SetBgmVolume(float linear)
    {
        BgmVolume = Mathf.Clamp(linear, 0f, 1f);
    }

    private AudioStreamPlayer GetFreeSfxPlayer()
    {
        foreach (var p in _sfxPool)
        {
            if (!p.Playing)
                return p;
        }
        return _sfxPool[0];
    }

    private static float LinearToDb(float linear)
    {
        return linear <= 0.0001f ? -80f : 20f * Mathf.Log(linear) / Mathf.Log(10f);
    }
}
