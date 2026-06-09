using Godot;

namespace LuckyDogRise;

public static class SettingsManager
{
    private const string Path = "user://settings.cfg";
    private const string SectionAudio = "audio";
    private const string SectionSystem = "system";
    private const string KeyAudioEnabled = "enabled";
    private const string KeyAlwaysOnTop = "always_on_top";
    private const string KeyTaskbarIcon = "taskbar_icon";

    // === 音频 ===
    public static bool LoadAudioEnabled()
    {
        var config = Load();
        return (bool)config.GetValue(SectionAudio, KeyAudioEnabled, true);
    }

    public static void SaveAudioEnabled(bool enabled)
    {
        var config = Load();
        config.SetValue(SectionAudio, KeyAudioEnabled, enabled);
        config.Save(Path);
    }

    // === 系统 ===
    public static bool LoadAlwaysOnTop()
    {
        var config = Load();
        return (bool)config.GetValue(SectionSystem, KeyAlwaysOnTop, true);
    }

    public static void SaveAlwaysOnTop(bool on)
    {
        var config = Load();
        config.SetValue(SectionSystem, KeyAlwaysOnTop, on);
        config.Save(Path);
    }

    public static bool LoadTaskbarIcon()
    {
        var config = Load();
        return (bool)config.GetValue(SectionSystem, KeyTaskbarIcon, false);
    }

    public static void SaveTaskbarIcon(bool show)
    {
        var config = Load();
        config.SetValue(SectionSystem, KeyTaskbarIcon, show);
        config.Save(Path);
    }

    private static ConfigFile Load()
    {
        var config = new ConfigFile();
        config.Load(Path);
        return config;
    }
}
