using Godot;
using System.Collections.Generic;

namespace LuckyDogRise;

public partial class AudioManager : Node
{
    public static AudioManager Instance { get; private set; } = null!;

    private readonly Dictionary<string, AudioStream> _cache = new();
    private readonly List<AudioStreamPlayer> _pool = new();
    private const int PoolSize = 8;

    public override void _Ready()
    {
        Instance = this;

        // 创建音频播放器池
        for (int i = 0; i < PoolSize; i++)
        {
            var player = new AudioStreamPlayer();
            AddChild(player);
            _pool.Add(player);
        }

        // 预加载所有音效
        PreloadSfx("res://Audio/SFX/Knock.wav");
        PreloadSfx("res://Audio/SFX/ChipCollect.wav");
    }

    private void PreloadSfx(string path)
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

        // 从池中找一个空闲的播放器
        var player = GetFreePlayer();
        player.Stream = stream;
        player.Play();
    }

    public void PlaySfxByName(string fileName)
    {
        PlaySfx($"res://Audio/SFX/{fileName}");
    }

    private AudioStreamPlayer GetFreePlayer()
    {
        foreach (var p in _pool)
        {
            if (!p.Playing)
                return p;
        }
        // 全部在用，返回第一个（会中断当前播放）
        return _pool[0];
    }
}
