using Godot;

namespace LuckyDogRise;

public static class SettingsManager
{
    private const string Path = "user://settings.cfg";
    private const string Section = "audio";
    private const string KeyEnabled = "enabled";

    public static bool LoadAudioEnabled()
    {
        var config = new ConfigFile();
        var err = config.Load(Path);
        if (err != Error.Ok) return true; // 默认开启
        return (bool)config.GetValue(Section, KeyEnabled, true);
    }

    public static void SaveAudioEnabled(bool enabled)
    {
        var config = new ConfigFile();
        config.SetValue(Section, KeyEnabled, enabled);
        config.Save(Path);
    }
}
