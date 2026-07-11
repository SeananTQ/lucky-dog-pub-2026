using Godot;

namespace LuckyDogRise;

public static class SettingsManager
{
    private const string Path = "user://settings.cfg";
    private const string SectionAudio = "audio";
    private const string SectionSystem = "system";
    private const string SectionDisplay = "display";
    private const string SectionLocalization = "localization";
    private const string KeyAudioEnabled = "enabled";
    private const string KeyAlwaysOnTop = "always_on_top";
    private const string KeyTaskbarIcon = "taskbar_icon";
    private const string KeyAutoHidePanel = "auto_hide_panel";
    private const string KeyDesktopTongueImmediateMode = "desktop_tongue_immediate_mode";
    private const string KeyShowOverFullscreenApps = "show_over_fullscreen_apps";
    private const string KeyEnhancedTopmostMode = "enhanced_topmost_mode";
    private const string KeyAlwaysShowBlindBoxBubble = "always_show_blind_box_bubble";
    private const string KeyAutoEquipNewOutfits = "auto_equip_new_outfits";
    private const string KeySnapToWindowsTaskbar = "snap_to_windows_taskbar";
    private const string KeyStreamerSafeMode = "streamer_safe_mode";
    private const string KeySaveDataMode = "save_data_mode";
    private const string KeyDisplayMode = "mode";
    private const string KeyCenterCounterOnTaskbar = "center_counter_on_taskbar";
    private const string KeyProactiveInteractionHints = "proactive_interaction_hints";
    private const string KeyLocale = "locale";

    public enum DisplayMode
    {
        Clock = 0,
        Chips = 1,
        Hidden = 2
    }

    public enum SaveDataMode
    {
        DebugAllItems = 0,
        LocalSave = 1
    }

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

    public static bool LoadAutoHidePanel()
    {
        var config = Load();
        return (bool)config.GetValue(SectionSystem, KeyAutoHidePanel, true);
    }

    public static void SaveAutoHidePanel(bool enabled)
    {
        var config = Load();
        config.SetValue(SectionSystem, KeyAutoHidePanel, enabled);
        config.Save(Path);
    }

    public static bool LoadDesktopTongueImmediateMode()
    {
        var config = Load();
        return (bool)config.GetValue(SectionSystem, KeyDesktopTongueImmediateMode, false);
    }

    public static void SaveDesktopTongueImmediateMode(bool enabled)
    {
        var config = Load();
        config.SetValue(SectionSystem, KeyDesktopTongueImmediateMode, enabled);
        config.Save(Path);
    }

    public static bool LoadAlwaysShowBlindBoxBubble()
    {
        var config = Load();
        return (bool)config.GetValue(SectionSystem, KeyAlwaysShowBlindBoxBubble, true);
    }

    public static void SaveAlwaysShowBlindBoxBubble(bool enabled)
    {
        var config = Load();
        config.SetValue(SectionSystem, KeyAlwaysShowBlindBoxBubble, enabled);
        config.Save(Path);
    }

    public static bool LoadShowOverFullscreenApps()
    {
        var config = Load();
        return (bool)config.GetValue(SectionSystem, KeyShowOverFullscreenApps, true);
    }

    public static void SaveShowOverFullscreenApps(bool enabled)
    {
        var config = Load();
        config.SetValue(SectionSystem, KeyShowOverFullscreenApps, enabled);
        config.Save(Path);
    }

    public static bool LoadEnhancedTopmostMode()
    {
        var config = Load();
        return (bool)config.GetValue(SectionSystem, KeyEnhancedTopmostMode, false);
    }

    public static void SaveEnhancedTopmostMode(bool enabled)
    {
        var config = Load();
        config.SetValue(SectionSystem, KeyEnhancedTopmostMode, enabled);
        config.Save(Path);
    }

    public static bool LoadAutoEquipNewOutfits()
    {
        var config = Load();
        return (bool)config.GetValue(SectionSystem, KeyAutoEquipNewOutfits, true);
    }

    public static void SaveAutoEquipNewOutfits(bool enabled)
    {
        var config = Load();
        config.SetValue(SectionSystem, KeyAutoEquipNewOutfits, enabled);
        config.Save(Path);
    }

    public static bool LoadSnapToWindowsTaskbar()
    {
        var config = Load();
        return (bool)config.GetValue(SectionSystem, KeySnapToWindowsTaskbar, true);
    }

    public static void SaveSnapToWindowsTaskbar(bool enabled)
    {
        var config = Load();
        config.SetValue(SectionSystem, KeySnapToWindowsTaskbar, enabled);
        config.Save(Path);
    }

    public static bool LoadStreamerSafeMode()
    {
        var config = Load();
        return (bool)config.GetValue(SectionSystem, KeyStreamerSafeMode, false);
    }

    public static void SaveStreamerSafeMode(bool enabled)
    {
        var config = Load();
        config.SetValue(SectionSystem, KeyStreamerSafeMode, enabled);
        config.Save(Path);
    }

    public static SaveDataMode LoadSaveDataMode()
    {
#if !DEBUG
        return SaveDataMode.LocalSave;
#else
        var config = Load();
        return (SaveDataMode)(int)config.GetValue(SectionSystem, KeySaveDataMode, (int)SaveDataMode.DebugAllItems);
#endif
    }

    public static void SaveSaveDataMode(SaveDataMode mode)
    {
#if !DEBUG
        mode = SaveDataMode.LocalSave;
#endif
        var config = Load();
        config.SetValue(SectionSystem, KeySaveDataMode, (int)mode);
        config.Save(Path);
    }

    // === 显示模式 ===
    public static DisplayMode CurrentDisplayMode { get; private set; } = DisplayMode.Clock;

    public static DisplayMode LoadDisplayMode()
    {
        var config = Load();
        CurrentDisplayMode = (DisplayMode)(int)config.GetValue(SectionDisplay, KeyDisplayMode, (int)DisplayMode.Clock);
        return CurrentDisplayMode;
    }

    public static void SaveDisplayMode(DisplayMode mode)
    {
        CurrentDisplayMode = mode;
        var config = Load();
        config.SetValue(SectionDisplay, KeyDisplayMode, (int)mode);
        config.Save(Path);
    }

    public static bool LoadCenterCounterOnTaskbar()
    {
        var config = Load();
        return (bool)config.GetValue(SectionDisplay, KeyCenterCounterOnTaskbar, true);
    }

    // === 扑克 ===
    public static bool LoadProactiveInteractionHints()
    {
        var config = Load();
        return (bool)config.GetValue(SectionSystem, KeyProactiveInteractionHints, true);
    }

    public static void SaveProactiveInteractionHints(bool enabled)
    {
        var config = Load();
        config.SetValue(SectionSystem, KeyProactiveInteractionHints, enabled);
        config.Save(Path);
        ProactiveInteractionHintsChanged?.Invoke(enabled);
    }

    public static event System.Action<bool> ProactiveInteractionHintsChanged;

    public static void SaveCenterCounterOnTaskbar(bool enabled)
    {
        var config = Load();
        config.SetValue(SectionDisplay, KeyCenterCounterOnTaskbar, enabled);
        config.Save(Path);
    }

    public static string LoadLocale()
    {
        var config = Load();
        return (string)config.GetValue(SectionLocalization, KeyLocale, L10n.SystemLocale);
    }

    public static void SaveLocale(string locale)
    {
        var config = Load();
        config.SetValue(SectionLocalization, KeyLocale, locale);
        config.Save(Path);
    }

    public static void ResetToDefaults()
    {
        CurrentDisplayMode = DisplayMode.Clock;
        var config = new ConfigFile();
        config.Save(Path);
        ProactiveInteractionHintsChanged?.Invoke(true);
    }

    private static ConfigFile Load()
    {
        var config = new ConfigFile();
        config.Load(Path);
        return config;
    }
}
