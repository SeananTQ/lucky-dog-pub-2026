#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace LuckyDogRise;

// Pure, read-only validation; can also be called by a future packaging check.
public static class LootConfigurationValidator
{
    public sealed class Report
    {
        public List<string> Errors { get; } = new();
        public List<string> Notes { get; } = new();
        public bool Passed => Errors.Count == 0;
        public override string ToString() => (Passed ? "校验通过" : $"校验失败：{Errors.Count} 项")
            + "\n\n" + string.Join("\n", Notes.Concat(Errors.Select(error => "[失败] " + error)));
    }

    public static Report Run(string dataDirectory, string snapshotPath)
    {
        var report = new Report();
        report.Notes.Add($"游戏数据：{dataDirectory}");
        report.Notes.Add($"导出快照：{snapshotPath}");
        try
        {
            var items = ReadRows(Path.Combine(dataDirectory, "tbitem.json"));
            var weights = ReadRows(Path.Combine(dataDirectory, "tbblindboxitemweight.json"));
            var rates = ReadRows(Path.Combine(dataDirectory, "tbblindboxrarityrate.json"));
            var boxes = ReadRows(Path.Combine(dataDirectory, "tbblindbox.json"));
            CheckDefinitions(items, weights, rates, boxes, report);
            if (!File.Exists(snapshotPath))
                report.Errors.Add("缺少导出快照，无法确认 CSV 回填是否正确。请用新版掉率编辑器重新导出 CSV，并回填、生成游戏数据。");
            else
            {
                var snapshot = JsonNode.Parse(File.ReadAllText(snapshotPath))!.AsObject();
                if (Number(snapshot, "Version") != 1)
                    throw new InvalidDataException("不支持的导出快照版本。");
                report.Notes.Add($"快照导出时间：{snapshot["ExportedAtUtc"]}");
                Compare(Rows(snapshot, "Items"), items, "物品", Id, ["ItemRarity", "AcquisitionType"], report);
                Compare(Rows(snapshot, "ItemWeights"), weights, "奖池", Id,
                    ["BlindBoxId", "ItemId", "Weight", "IsEnabled"], report);
                Compare(Rows(snapshot, "RarityRates"), rates, "稀有度概率", RateKey,
                    ["Weight", "IsEnabled"], report);
                Compare(Rows(snapshot, "BlindBoxes"), boxes, "盲盒", Id, ["BoxType", "IsEnabled"], report);
                foreach (var hash in snapshot["CsvSha256"]!.AsObject())
                {
                    if (Path.GetFileName(hash.Key) != hash.Key)
                        throw new InvalidDataException("快照中的 CSV 文件名无效。");
                    var path = Path.Combine(Path.GetDirectoryName(snapshotPath)!, hash.Key);
                    if (!File.Exists(path) || !string.Equals(
                        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                        hash.Value!.GetValue<string>(), StringComparison.OrdinalIgnoreCase))
                        report.Errors.Add($"CSV {hash.Key} 缺失或已被修改，与本次导出快照不匹配。");
                }
            }
            report.Notes.Add($"已检查 {items.Count} 个物品、{boxes.Count} 个盲盒、{weights.Count} 条奖池关系。");
        }
        catch (Exception exception)
        {
            report.Errors.Add($"数据读取/校验异常：{exception.Message}");
        }
        return report;
    }

    private static void CheckDefinitions(List<JsonObject> items, List<JsonObject> weights,
        List<JsonObject> rates, List<JsonObject> boxes, Report report)
    {
        CheckKeys(items, Id, "物品", report);
        CheckKeys(boxes, Id, "盲盒", report);
        CheckKeys(weights, Id, "奖池编号", report);
        CheckKeys(weights, row => $"{Number(row, "BlindBoxId")}/{Number(row, "ItemId")}", "奖池关系", report);
        CheckKeys(rates, Id, "稀有度行编号", report);
        CheckKeys(rates, RateKey, "盲盒/稀有度", report);
        var itemsById = items.GroupBy(Id).ToDictionary(group => group.Key, group => group.First());
        var boxesById = boxes.GroupBy(Id).ToDictionary(group => group.Key, group => group.First());
        foreach (var item in items)
        {
            if (!new[] { 1, 2, 3, 4, 5, 6, 21, 22 }.Contains(Number(item, "ItemRarity")))
                report.Errors.Add($"物品 {Id(item)} 的 ItemRarity 无效。");
            if (Number(item, "AcquisitionType") is < 1 or > 6)
                report.Errors.Add($"物品 {Id(item)} 的 AcquisitionType 无效。");
        }
        foreach (var row in weights)
        {
            var label = $"奖池 {Id(row)}（盲盒 {Number(row, "BlindBoxId")} / 物品 {Number(row, "ItemId")}）";
            if (Number(row, "Weight") < 0)
                report.Errors.Add($"{label} 权重不能为负数。");
            if (!itemsById.TryGetValue(Number(row, "ItemId").ToString(), out var item))
                report.Errors.Add($"{label} 引用了不存在的物品。");
            if (!boxesById.TryGetValue(Number(row, "BlindBoxId").ToString(), out var box))
                report.Errors.Add($"{label} 引用了不存在的盲盒。");
            if (item == null || box == null || !Enabled(row) || Number(row, "Weight") <= 0 || !Enabled(box))
                continue;
            if (Number(item, "AcquisitionType") != ExpectedAcquisition(box))
                report.Errors.Add($"{label} 的物品获取类型不符，会被游戏奖池过滤。");
            if (box["IsPlatformInventoryRequired"]!.GetValue<bool>() && Number(item, "SteamItemDefId") <= 0)
                report.Errors.Add($"{label} 为 Steam 奖励但缺少 SteamItemDefId。");
            if (!rates.Any(rate => Enabled(rate) && Number(rate, "Weight") > 0
                && Number(rate, "BlindBoxId") == Number(box, "Id")
                && Number(rate, "Rarity") == Number(item, "ItemRarity")))
                report.Notes.Add($"[提示] {label} 所属稀有度概率为零或禁用，该物品当前不会中奖。");
        }
        foreach (var rate in rates)
        {
            if (Number(rate, "Weight") < 0)
                report.Errors.Add($"稀有度 {RateKey(rate)} 权重不能为负数。");
            if (!boxesById.ContainsKey(Number(rate, "BlindBoxId").ToString()))
                report.Errors.Add($"稀有度 {RateKey(rate)} 引用了不存在的盲盒。");
            if (!new[] { 1, 2, 3, 4, 5, 6, 21, 22 }.Contains(Number(rate, "Rarity")))
                report.Errors.Add($"稀有度 {RateKey(rate)} 的 Rarity 无效。");
        }
        foreach (var box in boxes.Where(Enabled))
        {
            if (Number(box, "BoxType") is < 1 or > 4)
                report.Errors.Add($"盲盒 {Id(box)} 的 BoxType 无效。");
            var activeRates = rates.Where(rate => Enabled(rate) && Number(rate, "Weight") > 0
                && Number(rate, "BlindBoxId") == Number(box, "Id")).ToList();
            if (activeRates.Count == 0)
                report.Errors.Add($"启用盲盒 {Id(box)} 没有正权重稀有度。");
            CheckTotal(activeRates, $"盲盒 {Id(box)} 稀有度", report);
            foreach (var rate in activeRates)
            {
                var candidates = weights.Where(row => Enabled(row) && Number(row, "Weight") > 0
                    && Number(row, "BlindBoxId") == Number(box, "Id")
                    && itemsById.TryGetValue(Number(row, "ItemId").ToString(), out var item)
                    && Number(item, "ItemRarity") == Number(rate, "Rarity")
                    && Number(item, "AcquisitionType") == ExpectedAcquisition(box)).ToList();
                if (candidates.Count == 0)
                    report.Errors.Add($"盲盒/稀有度 {RateKey(rate)} 有正概率，但没有合法正权重候选物品。");
                CheckTotal(candidates, $"盲盒/稀有度 {RateKey(rate)} 物品", report);
            }
        }
    }

    private static int ExpectedAcquisition(JsonObject box) => Number(box, "BoxType") switch
    {
        1 or 2 => 2, 3 => 3, 4 => 4, _ => -1,
    };
    private static void CheckTotal(List<JsonObject> rows, string label, Report report)
    {
        if (rows.Sum(row => (long)Number(row, "Weight")) > int.MaxValue)
            report.Errors.Add($"{label} 权重总和超过游戏抽样支持的 int 范围。");
    }
    private static void Compare(List<JsonObject> expected, List<JsonObject> actual, string label,
        Func<JsonObject, string> key, string[] fields, Report report)
    {
        CheckKeys(expected, key, $"快照{label}", report);
        var actualByKey = actual.GroupBy(key).ToDictionary(group => group.Key, group => group.First());
        var expectedKeys = expected.Select(key).ToHashSet();
        foreach (var row in expected)
        {
            var rowKey = key(row);
            if (!actualByKey.TryGetValue(rowKey, out var other))
            {
                report.Errors.Add($"{label} {rowKey}：最终游戏数据缺少该行。");
                continue;
            }
            foreach (var field in fields)
                if (row[field] == null || other[field] == null || !JsonNode.DeepEquals(row[field], other[field]))
                    report.Errors.Add($"{label} {rowKey} / {field}：预期 {row[field]}，实际 {other[field] ?? "<缺失>"}。");
        }
        foreach (var extra in actualByKey.Keys.Where(value => !expectedKeys.Contains(value)))
            report.Errors.Add($"{label} {extra}：最终游戏数据存在快照之外的行，请确认本次回填版本。");
    }
    private static void CheckKeys(List<JsonObject> rows, Func<JsonObject, string> key, string label, Report report)
    {
        foreach (var duplicate in rows.GroupBy(key).Where(group => group.Count() > 1))
            report.Errors.Add($"{label} {duplicate.Key} 重复，共 {duplicate.Count()} 行。");
    }
    private static List<JsonObject> ReadRows(string path) => JsonNode.Parse(File.ReadAllText(path))!
        .AsArray().Select(node => node!.AsObject()).ToList();
    private static List<JsonObject> Rows(JsonObject obj, string field) => obj[field]!
        .AsArray().Select(node => node!.AsObject()).ToList();
    private static int Number(JsonObject row, string field) => row[field]?.GetValue<int>()
        ?? throw new InvalidDataException($"字段 {field} 缺失。");
    private static bool Enabled(JsonObject row) => row["IsEnabled"]!.GetValue<bool>();
    private static string Id(JsonObject row) => Number(row, "Id").ToString();
    private static string RateKey(JsonObject row) => $"{Number(row, "BlindBoxId")}/{Number(row, "Rarity")}";
}
#endif
