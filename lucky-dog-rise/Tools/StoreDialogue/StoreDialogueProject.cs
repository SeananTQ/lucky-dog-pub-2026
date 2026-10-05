using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataTables;

namespace LuckyDogRise.Tools;

public sealed class StoreDialoguePose
{
    // Null retains a pre-action-selector draft until the author chooses an action.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ReactionId { get; set; }
    // Legacy fields are read for compatibility, never exposed as asset selectors.
    public string Eyes { get; set; } = "";
    public string Ears { get; set; } = "";
}

public sealed class StoreDialogueDog
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DogSkinDraft Appearance { get; set; } = new();
    public int HeadwearItemId { get; set; }
    public StoreDialoguePose Idle { get; set; } = new() { ReactionId = (int)EDogReactionTrigger.Default };
    public StoreDialoguePose Speaking { get; set; } = new() { ReactionId = (int)EDogReactionTrigger.Excited };
}

/// <summary>
/// Authoring-only JSON. Appearance drafts, translated names, and dialogue belong to this
/// document, and are never registered as game translations or written to player saves.
/// </summary>
public sealed class StoreDialogueProject
{
    public const int CurrentVersion = 1;
    private const int MaximumJsonBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        MaxDepth = 32,
    };

    public int Version { get; set; } = CurrentVersion;
    public int CanvasWidth { get; set; } = 1170;
    public int CanvasHeight { get; set; } = 430;
    public int Fps { get; set; } = 30;
    public double DogScale { get; set; } = 0.18;
    public double GapPixels { get; set; } = 12;
    public int NameFontSize { get; set; } = 18;
    public int BubbleFontSize { get; set; } = 24;
    public int BubbleWidth { get; set; } = 380;
    public double LeadInSeconds { get; set; } = 0.5;
    public double LineGapSeconds { get; set; } = 0.2;
    public double TailSeconds { get; set; } = 0.8;
    public List<StoreDialogueDog> Dogs { get; set; } = new();
    public Dictionary<string, StoreDialogueLocale> Locales { get; set; } = new(StringComparer.Ordinal);

    public StoreDialogueProject Clone()
    {
        // Serialization copies all nested drafts, poses, dictionaries, and lists.
        return JsonSerializer.Deserialize<StoreDialogueProject>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)
            ?? throw new InvalidDataException("无法复制对话方案。");
    }

    public StoreDialogueTimeline CreateTimeline(string localeCode)
    {
        if (Locales == null || !Locales.TryGetValue(localeCode, out var locale) || locale == null)
            throw new ArgumentException("找不到指定语言。", nameof(localeCode));
        return new StoreDialogueTimeline(locale, LeadInSeconds, LineGapSeconds, TailSeconds);
    }

    public IReadOnlyList<string> Validate(string localeCode, bool forExport = false)
    {
        var errors = ValidateStructure();
        if (errors.Count > 0) return errors;
        if (string.IsNullOrWhiteSpace(localeCode) || !Locales.TryGetValue(localeCode, out var locale))
        {
            errors.Add("请选择已有语言。");
            return errors;
        }

        if (forExport)
        {
            foreach (var dog in Dogs)
            {
                if (!locale.Names.TryGetValue(dog.Id, out var name) || string.IsNullOrWhiteSpace(name))
                    errors.Add($"语言 {localeCode} 缺少小狗 {Dogs.IndexOf(dog) + 1} 的名字。");
                if (string.IsNullOrWhiteSpace(dog.Appearance.FolderPath) || string.IsNullOrWhiteSpace(dog.Appearance.Head))
                    errors.Add($"小狗 {Dogs.IndexOf(dog) + 1} 尚未配置完整造型。");
            }
            if (locale.Lines.Count == 0) errors.Add("请至少添加一句对白。");
            for (var i = 0; i < locale.Lines.Count; i++)
                if (string.IsNullOrWhiteSpace(locale.Lines[i].Text)) errors.Add($"语言 {localeCode} 第 {i + 1} 句对白尚未填写。");
        }

        errors.AddRange(StoreDialogueExportLimits.Validate(CanvasWidth, CanvasHeight, Fps,
            CreateTimeline(localeCode).DurationSeconds));
        return errors;
    }

    /// <summary>Call after removing dogs. References are removed, never shifted to another dog.</summary>
    public void PruneRemovedDogs()
    {
        var ids = Dogs.Where(dog => dog != null).Select(dog => dog.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var locale in Locales.Values)
        {
            foreach (var id in locale.Names.Keys.Where(id => !ids.Contains(id)).ToArray()) locale.Names.Remove(id);
            locale.Lines.RemoveAll(line => line == null || !ids.Contains(line.DogId));
        }
    }

    public void Save(string path)
    {
        RequireJsonPath(path);
        var errors = ValidateStructure();
        if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
        var json = JsonSerializer.Serialize(this, JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes) throw new InvalidDataException("对话方案超过 4 MB。");
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static StoreDialogueProject Load(string path)
    {
        RequireJsonPath(path);
        using var stream = File.OpenRead(path);
        if (stream.Length > MaximumJsonBytes) throw new InvalidDataException("对话方案超过 4 MB。");
        StoreDialogueProject project;
        try
        {
            project = JsonSerializer.Deserialize<StoreDialogueProject>(stream, JsonOptions)
                ?? throw new InvalidDataException("对话方案为空。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("对话方案 JSON 格式错误。原文件未被修改。", exception);
        }
        var errors = project.ValidateStructure();
        if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
        return project;
    }

    private List<string> ValidateStructure()
    {
        var errors = new List<string>();
        if (Version != CurrentVersion) errors.Add($"不支持方案版本 {Version}，当前版本为 {CurrentVersion}。");
        if (!double.IsFinite(DogScale) || DogScale is < 0.05 or > 1) errors.Add("小狗倍率须为 0.05～1。");
        if (!double.IsFinite(GapPixels) || GapPixels is < 0 or > 500) errors.Add("间距须为 0～500 像素。");
        if (NameFontSize is < 8 or > 96 || BubbleFontSize is < 8 or > 96) errors.Add("文字字号须为 8～96。");
        if (BubbleWidth is < 64 or > 4096) errors.Add("气泡宽度须为 64～4096 像素。");
        if (!ValidPause(LeadInSeconds) || !ValidPause(LineGapSeconds) || !ValidPause(TailSeconds))
            errors.Add("开场、对白间隔与结尾停留须为 0～30 秒。");
        if (Dogs == null || Dogs.Count is < 1 or > 12)
        {
            errors.Add("方案须包含 1～12 只小狗。");
            return errors;
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dog in Dogs)
        {
            if (dog == null || string.IsNullOrWhiteSpace(dog.Id) || dog.Id.Length > 100 || !ids.Add(dog.Id))
            {
                errors.Add("小狗标识缺失、过长或重复。");
                continue;
            }
            if (dog.Appearance == null || dog.Idle == null || dog.Speaking == null)
                errors.Add("小狗造型或表情参数缺失。");
            else
            {
                if (dog.Idle.ReactionId is <= 0 || dog.Speaking.ReactionId is <= 0)
                    errors.Add("请选择有效的表情动作。");
                if (!ValidAssetString(dog.Idle.Eyes) || !ValidAssetString(dog.Idle.Ears)
                    || !ValidAssetString(dog.Speaking.Eyes) || !ValidAssetString(dog.Speaking.Ears))
                    errors.Add("表情素材名不能为 null 或过长；留空可使用默认素材。");
                if (!AppearanceStringsAreValid(dog.Appearance)) errors.Add("小狗造型包含空值或过长的素材名。");
            }
            if (dog.HeadwearItemId < 0) errors.Add("帽子编号不能为负数。");
        }
        if (Locales == null || Locales.Count is < 1 or > 64)
        {
            errors.Add("方案须包含 1～64 种语言。");
            return errors;
        }
        foreach (var entry in Locales)
        {
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Key.Length > 40) errors.Add("语言标识不能为空或超过 40 字符。");
            var locale = entry.Value;
            if (locale == null || locale.Names == null || locale.Lines == null)
            {
                errors.Add($"语言 {entry.Key} 的名字或对白列表缺失。");
                continue;
            }
            if (locale.Lines.Count > 100) errors.Add($"语言 {entry.Key} 对白超过 100 句。");
            foreach (var name in locale.Names)
            {
                if (!ids.Contains(name.Key)) errors.Add($"语言 {entry.Key} 的名字引用了已移除的小狗。");
                if (name.Value == null || name.Value.Length > 200) errors.Add($"语言 {entry.Key} 的名字为空值或超过 200 字符。");
            }
            foreach (var line in locale.Lines)
            {
                if (line == null)
                {
                    errors.Add($"语言 {entry.Key} 存在空对白记录。");
                    continue;
                }
                if (line.DogId == null || !ids.Contains(line.DogId)) errors.Add($"语言 {entry.Key} 的对白引用了已移除的小狗。");
                if (line.Text == null || line.Text.Length > 2000) errors.Add($"语言 {entry.Key} 的对白为空值或超过 2000 字符。");
                if (!double.IsFinite(line.DurationSeconds) || line.DurationSeconds is <= 0 or > 30)
                    errors.Add($"语言 {entry.Key} 的每句时长须大于 0 且不超过 30 秒。");
            }
        }
        // Validate dimensions even when saving an incomplete translation draft.
        errors.AddRange(StoreDialogueExportLimits.Validate(CanvasWidth, CanvasHeight, Fps, 1));
        return errors;
    }

    private static bool ValidPause(double value) => double.IsFinite(value) && value is >= 0 and <= 30;

    private static bool ValidAssetString(string value) => value != null && value.Length <= 1024;

    private static bool AppearanceStringsAreValid(DogSkinDraft appearance) => new[]
    {
        appearance.Alias, appearance.IconName, appearance.DefaultEars, appearance.DefaultEyes,
        appearance.DefaultTongue, appearance.FixedEyewear, appearance.FolderPath, appearance.DefaultNose,
        appearance.DefaultMouse, appearance.MouseSilent, appearance.Head, appearance.ClawLeftBack,
        appearance.ClawRightPalms, appearance.TongueRegular, appearance.EarsNormal, appearance.EarsDrooped,
        appearance.EyesBored, appearance.EyesNormalMood, appearance.EyesHappy, appearance.EyesAdmiring,
        appearance.EyesStunned, appearance.EyesSignaling,
    }.All(ValidAssetString);

    private static void RequireJsonPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("对话方案必须保存为 .json 文件。", nameof(path));
    }
}
