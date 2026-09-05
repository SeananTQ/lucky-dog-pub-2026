using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using DataTables;
using LuckyItemLootEditor.Models;

namespace LuckyItemLootEditor.Services;

public sealed class LootDataStore
{
    public required string ProjectRoot { get; init; }
    public required string ItemPath { get; init; }
    public required string ItemWeightPath { get; init; }
    public required IReadOnlyList<ItemRow> Items { get; init; }
    public required IReadOnlyList<BlindBoxOption> BlindBoxes { get; init; }
    public required IReadOnlyList<RarityRate> RarityRates { get; init; }
    public required List<BlindBoxItemWeightRow> ItemWeights { get; init; }

    public static LootDataStore Load()
    {
        var projectRoot = ProjectPaths.RequireProjectRoot();
        var outputData = Path.Combine(projectRoot, "luban-excels", "output-data");
        var itemPath = Path.Combine(outputData, "tbitem.json");
        var blindBoxPath = Path.Combine(outputData, "tbblindbox.json");
        var rarityRatePath = Path.Combine(outputData, "tbblindboxrarityrate.json");
        var itemWeightPath = Path.Combine(outputData, "tbblindboxitemweight.json");

        var itemJson = ReadArray(itemPath);
        var blindBoxJson = ReadArray(blindBoxPath);
        var rarityRateJson = ReadArray(rarityRatePath);
        var itemWeightJson = ReadArray(itemWeightPath);

        return new LootDataStore
        {
            ProjectRoot = projectRoot,
            ItemPath = itemPath,
            ItemWeightPath = itemWeightPath,
            Items = itemJson.OfType<JsonObject>().Select(ItemRow.FromJson).ToList(),
            BlindBoxes = blindBoxJson.OfType<JsonObject>().Select(obj => new BlindBoxOption
            {
                Id = GetInt(obj, "Id"),
                Name = GetString(obj, "Name"),
                BoxType = (EBlindBoxType)GetInt(obj, "BoxType"),
                IsEnabled = GetBool(obj, "IsEnabled"),
            }).ToList(),
            RarityRates = rarityRateJson.OfType<JsonObject>().Select(obj => new RarityRate(
                GetInt(obj, "BlindBoxId"),
                (ERarity)GetInt(obj, "Rarity"),
                GetInt(obj, "Weight"),
                GetBool(obj, "IsEnabled"))).ToList(),
            ItemWeights = itemWeightJson.OfType<JsonObject>().Select(obj => new BlindBoxItemWeightRow
            {
                Id = GetInt(obj, "Id"),
                BlindBoxId = GetInt(obj, "BlindBoxId"),
                ItemId = GetInt(obj, "ItemId"),
                Weight = GetInt(obj, "Weight"),
                IsEnabled = GetBool(obj, "IsEnabled"),
            }).ToList(),
        };
    }

    public int GetWeight(int blindBoxId, int itemId) => ItemWeights
        .FirstOrDefault(row => row.BlindBoxId == blindBoxId && row.ItemId == itemId && row.IsEnabled)
        ?.Weight ?? 0;

    public void SetWeight(int blindBoxId, int itemId, int weight)
    {
        var row = ItemWeights.FirstOrDefault(candidate =>
            candidate.BlindBoxId == blindBoxId && candidate.ItemId == itemId);
        if (row == null)
        {
            if (weight <= 0)
                return;
            ItemWeights.Add(new BlindBoxItemWeightRow
            {
                Id = ItemWeights.Count == 0 ? 1 : ItemWeights.Max(candidate => candidate.Id) + 1,
                BlindBoxId = blindBoxId,
                ItemId = itemId,
                Weight = weight,
                IsEnabled = true,
            });
            return;
        }

        row.Weight = Math.Max(0, weight);
        row.IsEnabled = weight > 0;
    }

    public IReadOnlyList<string> ExportCsv()
    {
        var outputDirectory = Path.Combine(ProjectRoot, "lucky-item-loot-editor", "output");
        Directory.CreateDirectory(outputDirectory);
        var itemPatchPath = Path.Combine(outputDirectory, "ItemLootPatch.csv");
        var itemWeightPath = Path.Combine(outputDirectory, "BlindBoxItemWeight.csv");
        var encoding = new UTF8Encoding(true);

        var itemLines = new List<string> { "ItemId,ItemRarity,AcquisitionType" };
        itemLines.AddRange(Items.OrderBy(item => item.Id).Select(item => string.Join(",",
            item.Id,
            EscapeCsv(GetRarityLabel(item.Rarity)),
            EscapeCsv(GetAcquisitionLabel(item.AcquisitionType)))));
        File.WriteAllLines(itemPatchPath, itemLines, encoding);

        var weightLines = new List<string> { "Id,BlindBoxId,ItemId,Weight,IsEnabled" };
        weightLines.AddRange(ItemWeights.OrderBy(row => row.Id).Select(row => string.Join(",",
            row.Id, row.BlindBoxId, row.ItemId, row.Weight, row.IsEnabled ? "TRUE" : "FALSE")));
        File.WriteAllLines(itemWeightPath, weightLines, encoding);
        return new[] { itemPatchPath, itemWeightPath };
    }

    private static JsonArray ReadArray(string path) =>
        JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8))?.AsArray()
        ?? throw new InvalidDataException($"无法解析 {path}");

    private static string EscapeCsv(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    private static string GetRarityLabel(ERarity rarity) => rarity switch
    {
        (ERarity)0 => "",
        ERarity.Common => "普通",
        ERarity.Uncommon => "优秀",
        ERarity.Rare => "稀有",
        ERarity.Epic => "史诗",
        ERarity.Legendary => "传说",
        ERarity.Mythic => "神话",
        ERarity.Special1 => "特殊1",
        ERarity.Special2 => "特殊2",
        _ => rarity.ToString(),
    };

    private static string GetAcquisitionLabel(EAcquisitionType type) => type switch
    {
        EAcquisitionType.Initial => "初始拥有",
        EAcquisitionType.DecorationBlindBox => "装扮盲盒产出",
        EAcquisitionType.RefreshmentBlindBox => "消耗品盲盒产出",
        EAcquisitionType.EventReward => "活动产出",
        EAcquisitionType.Retired => "已下架",
        EAcquisitionType.DebugOnly => "仅调试",
        _ => type.ToString(),
    };

    private static int GetInt(JsonObject obj, string key) => obj[key]?.GetValue<int>() ?? 0;
    private static string GetString(JsonObject obj, string key) => obj[key]?.GetValue<string>() ?? string.Empty;
    private static bool GetBool(JsonObject obj, string key) => obj[key]?.GetValue<bool>() ?? false;
}

public sealed record RarityRate(int BlindBoxId, ERarity Rarity, int Weight, bool IsEnabled);
