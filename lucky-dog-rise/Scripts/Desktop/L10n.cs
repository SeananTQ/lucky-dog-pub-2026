#nullable enable

using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LuckyDogRise;

public static class L10n
{
    public const string SystemLocale = "system";
    public const string EnglishLocale = "en";
    public const string SimplifiedChineseLocale = "zh_CN";

    private const string CsvPath = "res://Data/Localization/LocalizationText.csv";
    private static bool _loaded;
    private static bool _safeMode;

    public static event Action? Changed;

    public static string CurrentLocale { get; private set; } = EnglishLocale;
    public static bool SafeMode => _safeMode;

    public static void ApplySavedOrSystemLocale()
    {
        EnsureLoaded();
        SetSafeMode(SettingsManager.LoadStreamerSafeMode(), notify: false);
        SetLocale(SettingsManager.LoadLocale(), save: false);
    }

    public static void SetLocale(string locale, bool save = true)
    {
        EnsureLoaded();
        var resolved = ResolveLocale(locale);
        CurrentLocale = resolved;
        TranslationServer.SetLocale(resolved);
        if (save)
            SettingsManager.SaveLocale(locale);
        Changed?.Invoke();
    }

    public static void SetSafeMode(bool enabled, bool notify = true)
    {
        _safeMode = enabled;
        if (notify)
            Changed?.Invoke();
    }

    public static string Tr(string key)
    {
        EnsureLoaded();
        var resolvedKey = ResolveSafeKey(key);
        return TranslationServer.Translate(resolvedKey);
    }

    public static string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.InvariantCulture, Tr(key), args);
    }

    public static string GetDisplayName(string locale)
    {
        return locale switch
        {
            SystemLocale => Tr(L10nKey.Settings_Language_System),
            EnglishLocale => Tr(L10nKey.Settings_Language_English),
            SimplifiedChineseLocale => Tr(L10nKey.Settings_Language_SimplifiedChinese),
            _ => locale,
        };
    }

    public static string GetHandRankKey(DataTables.EHandRank rank)
    {
        return rank switch
        {
            DataTables.EHandRank.JacksOrBetter => L10nKey.Paytable_JacksOrBetter,
            DataTables.EHandRank.TwoPair => L10nKey.Paytable_TwoPair,
            DataTables.EHandRank.ThreeOfAKind => L10nKey.Paytable_ThreeOfAKind,
            DataTables.EHandRank.Straight => L10nKey.Paytable_Straight,
            DataTables.EHandRank.Flush => L10nKey.Paytable_Flush,
            DataTables.EHandRank.FullHouse => L10nKey.Paytable_FullHouse,
            DataTables.EHandRank.FourOfAKind => L10nKey.Paytable_FourOfAKind,
            DataTables.EHandRank.StraightFlush => L10nKey.Paytable_StraightFlush,
            DataTables.EHandRank.RoyalFlush => L10nKey.Paytable_RoyalFlush,
            DataTables.EHandRank.Nothing => L10nKey.InfoPanel_Nothing,
            _ => rank.ToString(),
        };
    }

    private static string ResolveLocale(string locale)
    {
        if (locale == SystemLocale)
            locale = OS.GetLocale();

        if (locale.StartsWith("zh_CN", StringComparison.OrdinalIgnoreCase)
            || locale.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase)
            || locale.StartsWith("zh_Hans", StringComparison.OrdinalIgnoreCase))
            return SimplifiedChineseLocale;

        if (locale.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            return EnglishLocale;

        return EnglishLocale;
    }

    private static string ResolveSafeKey(string key)
    {
        if (!_safeMode || key.EndsWith("_Safe", StringComparison.Ordinal))
            return key;

        var safeKey = $"{key}_Safe";
        return HasTranslation(safeKey) ? safeKey : key;
    }

    private static bool HasTranslation(string key)
    {
        return TranslationServer.Translate(key) != key;
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;

        LoadCsvTranslations(CsvPath);
        _loaded = true;
    }

    private static void LoadCsvTranslations(string path)
    {
        if (!FileAccess.FileExists(path))
        {
            GD.PushWarning($"[L10n] Missing localization CSV: {path}");
            return;
        }

        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        var rows = new List<string[]>();
        while (!file.EofReached())
        {
            var line = file.GetLine();
            if (string.IsNullOrWhiteSpace(line))
                continue;
            rows.Add(ParseCsvLine(line));
        }

        if (rows.Count == 0 || rows[0].Length < 2)
            return;

        var locales = rows[0];
        var translations = new Dictionary<string, Translation>();
        for (int col = 1; col < locales.Length; col++)
        {
            var locale = locales[col];
            if (string.IsNullOrWhiteSpace(locale) || locale.StartsWith("_", StringComparison.Ordinal))
                continue;

            var translation = new Translation();
            translation.Locale = locale;
            translations[locale] = translation;
            TranslationServer.AddTranslation(translation);
        }

        for (int row = 1; row < rows.Count; row++)
        {
            var fields = rows[row];
            if (fields.Length == 0 || string.IsNullOrWhiteSpace(fields[0]))
                continue;

            var key = fields[0];
            for (int col = 1; col < locales.Length && col < fields.Length; col++)
            {
                if (translations.TryGetValue(locales[col], out var translation))
                    translation.AddMessage(key, fields[col]);
            }
        }
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
                continue;
            }

            if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
