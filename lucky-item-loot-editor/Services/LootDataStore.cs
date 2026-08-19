using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;
using DataTables;
using LuckyItemLootEditor.Models;

namespace LuckyItemLootEditor.Services;

public sealed class LootDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public required string ProjectRoot { get; init; }
    public required string ItemPath { get; init; }
    public required JsonArray ItemJson { get; init; }
    public required IReadOnlyList<ItemRow> Items { get; init; }
    public required IReadOnlyList<BlindBoxOption> BlindBoxes { get; init; }
    public required IReadOnlyList<RarityRate> RarityRates { get; init; }
    public required string NewLine { get; init; }

    public static LootDataStore Load()
    {
        var projectRoot = ProjectPaths.RequireProjectRoot();
        var outputData = Path.Combine(projectRoot, "luban-excels", "output-data");
        var itemPath = Path.Combine(outputData, "tbitem.json");
        var blindBoxPath = Path.Combine(outputData, "tbblindbox.json");
        var rarityRatePath = Path.Combine(outputData, "tbblindboxrarityrate.json");

        var itemText = File.ReadAllText(itemPath, Encoding.UTF8);
        var itemJson = JsonNode.Parse(itemText)?.AsArray()
            ?? throw new InvalidDataException($"无法解析 {itemPath}");
        var blindBoxJson = JsonNode.Parse(File.ReadAllText(blindBoxPath, Encoding.UTF8))?.AsArray()
            ?? throw new InvalidDataException($"无法解析 {blindBoxPath}");
        var rarityRateJson = JsonNode.Parse(File.ReadAllText(rarityRatePath, Encoding.UTF8))?.AsArray()
            ?? throw new InvalidDataException($"无法解析 {rarityRatePath}");

        var items = itemJson
            .OfType<JsonObject>()
            .Select(ItemRow.FromJson)
            .ToList();

        var blindBoxes = blindBoxJson
            .OfType<JsonObject>()
            .Select(obj => new BlindBoxOption
            {
                Id = GetInt(obj, "Id"),
                Name = GetString(obj, "Name"),
                BoxType = (EBlindBoxType)GetInt(obj, "BoxType"),
                IsEnabled = GetBool(obj, "IsEnabled"),
            })
            .ToList();

        var rarityRates = rarityRateJson
            .OfType<JsonObject>()
            .Select(obj => new RarityRate(
                GetInt(obj, "BlindBoxId"),
                (ERarity)GetInt(obj, "Rarity"),
                GetInt(obj, "Weight"),
                GetBool(obj, "IsEnabled")))
            .ToList();

        return new LootDataStore
        {
            ProjectRoot = projectRoot,
            ItemPath = itemPath,
            ItemJson = itemJson,
            Items = items,
            BlindBoxes = blindBoxes,
            RarityRates = rarityRates,
            NewLine = itemText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n",
        };
    }

    public void Save()
    {
        foreach (var item in Items)
            item.ApplyToJson();

        // JsonNode uses the platform serializer's newline. Normalize first so
        // CRLF input does not become CRCRLF and turn the whole file into a diff.
        var json = ItemJson.ToJsonString(JsonOptions)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", NewLine, StringComparison.Ordinal);
        if (!json.EndsWith(NewLine, StringComparison.Ordinal))
            json += NewLine;

        var tempPath = ItemPath + ".loot-editor.tmp";
        File.WriteAllText(tempPath, json, new UTF8Encoding(false));
        File.Move(tempPath, ItemPath, true);
    }

    private static int GetInt(JsonObject obj, string key) => obj[key]?.GetValue<int>() ?? 0;
    private static string GetString(JsonObject obj, string key) => obj[key]?.GetValue<string>() ?? string.Empty;
    private static bool GetBool(JsonObject obj, string key) => obj[key]?.GetValue<bool>() ?? false;
}

public sealed record RarityRate(int BlindBoxId, ERarity Rarity, int Weight, bool IsEnabled);
