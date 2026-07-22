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
    public const string TraditionalChineseLocale = "zh_TW";
    public const string JapaneseLocale = "ja";
    public const string SpanishSpainLocale = "es_ES";
    public const string SpanishLatinAmericaLocale = "es_419";
    public const string PortugueseBrazilLocale = "pt_BR";
    public const string PortuguesePortugalLocale = "pt_PT";
    public const string FrenchLocale = "fr";
    public const string GermanLocale = "de";
    public const string DanishLocale = "da";
    public const string IndonesianLocale = "id";
    public const string NorwegianLocale = "nb";
    public const string SwedishLocale = "sv";
    public const string DutchLocale = "nl";
    public const string VietnameseLocale = "vi";
    public const string MalayLocale = "ms";
    public const string KoreanLocale = "ko";

    private const string CsvPath = "res://Data/Localization/LocalizationText.csv";
    private const string ExtendedCsvPath = "res://Data/Localization/LocalizationText_Extended.csv";
    private const string ImportedTranslationPathFormat = "res://Data/Localization/LocalizationText.{0}.translation";
    private const string EmptyTextMarker = "@empty";
    private static bool _loaded;
    private static bool _safeMode;
    private static readonly HashSet<string> ExplicitEmptyTexts = new();

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
        if (IsExplicitEmptyText(CurrentLocale, resolvedKey))
            return string.Empty;

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
            EnglishLocale => "English",
            SimplifiedChineseLocale => "简体中文",
            TraditionalChineseLocale => "繁體中文",
            JapaneseLocale => "日本語",
            SpanishSpainLocale => "Español (España)",
            SpanishLatinAmericaLocale => "Español (Latinoamérica)",
            PortugueseBrazilLocale => "Português (Brasil)",
            PortuguesePortugalLocale => "Português (Portugal)",
            FrenchLocale => "Français",
            GermanLocale => "Deutsch",
            DanishLocale => "Dansk",
            IndonesianLocale => "Bahasa Indonesia",
            NorwegianLocale => "Norsk bokmål",
            SwedishLocale => "Svenska",
            DutchLocale => "Nederlands",
            VietnameseLocale => "Tiếng Việt",
            MalayLocale => "Bahasa Melayu",
            KoreanLocale => "한국어",
            _ => locale,
        };
    }

    public static string GetHandRankKey(DataTables.EHandRank rank)
    {
        return rank switch
        {
            DataTables.EHandRank.OnePair => L10nKey.Paytable_OnePair,
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

        if (locale.StartsWith("zh_TW", StringComparison.OrdinalIgnoreCase)
            || locale.StartsWith("zh_HK", StringComparison.OrdinalIgnoreCase)
            || locale.StartsWith("zh_MO", StringComparison.OrdinalIgnoreCase)
            || locale.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)
            || locale.StartsWith("zh_Hant", StringComparison.OrdinalIgnoreCase))
            return TraditionalChineseLocale;

        if (locale.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            return JapaneseLocale;

        if (locale.StartsWith("es_419", StringComparison.OrdinalIgnoreCase)
            || locale.StartsWith("es-419", StringComparison.OrdinalIgnoreCase)
            || locale.StartsWith("es_MX", StringComparison.OrdinalIgnoreCase)
            || locale.StartsWith("es-MX", StringComparison.OrdinalIgnoreCase))
            return SpanishLatinAmericaLocale;

        if (locale.StartsWith("es_ES", StringComparison.OrdinalIgnoreCase)
            || locale.StartsWith("es-ES", StringComparison.OrdinalIgnoreCase))
            return SpanishSpainLocale;

        if (locale.StartsWith("es", StringComparison.OrdinalIgnoreCase))
            return SpanishLatinAmericaLocale;

        if (locale.StartsWith("pt_PT", StringComparison.OrdinalIgnoreCase)
            || locale.StartsWith("pt-PT", StringComparison.OrdinalIgnoreCase))
            return PortuguesePortugalLocale;

        if (locale.StartsWith("pt_BR", StringComparison.OrdinalIgnoreCase)
            || locale.StartsWith("pt-BR", StringComparison.OrdinalIgnoreCase)
            || locale.StartsWith("pt", StringComparison.OrdinalIgnoreCase))
            return PortugueseBrazilLocale;

        if (locale.StartsWith("fr", StringComparison.OrdinalIgnoreCase))
            return FrenchLocale;

        if (locale.StartsWith("de", StringComparison.OrdinalIgnoreCase))
            return GermanLocale;

        if (locale.StartsWith("da", StringComparison.OrdinalIgnoreCase))
            return DanishLocale;

        if (locale.StartsWith("id", StringComparison.OrdinalIgnoreCase))
            return IndonesianLocale;

        if (locale.StartsWith("nb", StringComparison.OrdinalIgnoreCase)
            || locale.StartsWith("no", StringComparison.OrdinalIgnoreCase))
            return NorwegianLocale;

        if (locale.StartsWith("sv", StringComparison.OrdinalIgnoreCase))
            return SwedishLocale;

        if (locale.StartsWith("nl", StringComparison.OrdinalIgnoreCase))
            return DutchLocale;

        if (locale.StartsWith("vi", StringComparison.OrdinalIgnoreCase))
            return VietnameseLocale;

        if (locale.StartsWith("ms", StringComparison.OrdinalIgnoreCase))
            return MalayLocale;

        if (locale.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
            return KoreanLocale;

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
        return IsExplicitEmptyText(CurrentLocale, key) || TranslationServer.Translate(key) != key;
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;

        LoadCsvTranslations(CsvPath, clearExistingTranslations: true);
        LoadCsvTranslations(ExtendedCsvPath, clearExistingTranslations: false);
        _loaded = true;
    }

    private static void LoadCsvTranslations(string path, bool clearExistingTranslations)
    {
        if (!FileAccess.FileExists(path))
        {
#if DEBUG
            GD.PushWarning($"[L10n] Missing localization CSV: {path}. Falling back to imported Translation resources.");
#endif
            if (clearExistingTranslations)
                LoadImportedTranslations();
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
                {
                    var text = fields[col];
                    if (IsEmptyTextMarker(text))
                    {
                        ExplicitEmptyTexts.Add(GetTranslationId(locales[col], key));
                        continue;
                    }

                    translation.AddMessage(key, text);
                }
            }
        }
    }

    private static void LoadImportedTranslations()
    {
        string[] locales =
        [
            EnglishLocale,
            SimplifiedChineseLocale,
            TraditionalChineseLocale,
            JapaneseLocale,
            SpanishSpainLocale,
            SpanishLatinAmericaLocale,
            PortugueseBrazilLocale,
            PortuguesePortugalLocale,
            FrenchLocale,
            GermanLocale,
            DanishLocale,
            IndonesianLocale,
            NorwegianLocale,
            SwedishLocale,
            DutchLocale,
            VietnameseLocale,
            MalayLocale,
            KoreanLocale,
        ];

        foreach (var locale in locales)
        {
            var path = string.Format(
                CultureInfo.InvariantCulture,
                ImportedTranslationPathFormat,
                GetImportedTranslationLocale(locale));
            if (!ResourceLoader.Exists(path))
            {
                GD.PushWarning($"[L10n] Missing imported translation resource: {path}");
                continue;
            }

            var translation = ResourceLoader.Load<Translation>(path);
            if (translation == null)
            {
                GD.PushWarning($"[L10n] Failed to load imported translation resource: {path}");
                continue;
            }

            TranslationServer.AddTranslation(translation);
        }
    }

    // Godot 的 CSV 翻译导入器会将 es_419 规范化为通用的 es 资源名；
    // TranslationServer 仍会将它作为 es_419 的父语言回退使用。
    private static string GetImportedTranslationLocale(string locale)
    {
        return locale == SpanishLatinAmericaLocale ? "es" : locale;
    }

    private static bool IsEmptyTextMarker(string text)
    {
        return text.Trim() == EmptyTextMarker;
    }

    private static bool IsExplicitEmptyText(string locale, string key)
    {
        return ExplicitEmptyTexts.Contains(GetTranslationId(locale, key));
    }

    private static string GetTranslationId(string locale, string key)
    {
        return $"{locale}\u001f{key}";
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
