using System.Globalization;

namespace LuckyStats.Core;

public sealed class AnalysisEngine
{
    private const string Cohort = "STAT_PT1_COHORT_MEMBER";
    private static readonly (int Hours, string ApiName, string Label)[] RetentionStats =
    [
        (24, "STAT_PT1_RETURNED_AFTER_24H", "24小时成熟样本回访率"),
        (72, "STAT_PT1_RETURNED_AFTER_72H", "72小时成熟样本回访率"),
        (168, "STAT_PT1_RETURNED_AFTER_168H", "168小时成熟样本回访率")
    ];

    public AnalysisReport Analyze(AnalysisInput input)
    {
        var unavailableAccounts = input.ExcludedAccounts.Where(x => !x.Available).ToArray();
        var facts = input.Definitions.Select(definition =>
        {
            var global = Get(input.GlobalValues, definition.ApiName);
            var excluded = input.ExcludedAccounts
                .Where(x => x.Available)
                .Sum(x => Get(x.Values, definition.ApiName));
            return new FactRow(
                definition.ApiName,
                definition.DisplayName,
                UnitLabel(definition.Unit),
                global,
                excluded,
                global - excluded);
        }).ToArray();

        var analyzed = facts.ToDictionary(x => x.ApiName, x => x.AnalyzedValue, StringComparer.Ordinal);
        var metrics = new List<MetricResult>();
        var checks = new List<ValidationResult>();
        var cohort = Get(analyzed, Cohort);

        metrics.Add(new MetricResult("样本", "cohort", "有效 Playtest 样本玩家", FormatInteger(cohort),
            "全局样本玩家 - 已排除开发帐号", unavailableAccounts.Length == 0 ? "可用" : "部分排除失败", cohort));

        foreach (var retention in RetentionStats)
        {
            var returned = Get(analyzed, retention.ApiName);
            if (!input.RetentionBaselines.TryGetValue(retention.Hours, out var baseline))
            {
                metrics.Add(new MetricResult("留存", $"retention_{retention.Hours}h", retention.Label, "数据不足",
                    $"{retention.Hours}小时回访人数 / 查询时点前{retention.Hours}小时的样本人数",
                    "缺少足够早的历史快照"));
                continue;
            }

            var rate = baseline.CohortCount > 0 ? returned * 100d / baseline.CohortCount : (double?)null;
            metrics.Add(new MetricResult("留存", $"retention_{retention.Hours}h", retention.Label,
                rate.HasValue ? $"{rate.Value:0.0}%" : "数据不足",
                $"{returned} / {baseline.CohortCount}；分母快照 {baseline.CapturedAtUtc.LocalDateTime:g}",
                baseline.DistanceFromCutoff.Duration() <= TimeSpan.FromHours(12) ? "可用" : "分母快照离截止点较远",
                rate));

            if (returned > baseline.CohortCount)
                checks.Add(new ValidationResult(CheckSeverity.Error, $"{retention.Hours}小时留存分子不超过成熟分母",
                    $"回访人数 {returned} 大于成熟样本 {baseline.CohortCount}，应检查快照覆盖或 Stat 数据。"));
        }

        AddRate(metrics, "行为", "guidance_completion_rate", "扑克基础引导完成率",
            Get(analyzed, "STAT_PT1_POKER_GUIDANCE_COMPLETED"), cohort);
        AddAverage(metrics, "行为", "guidance_average_seconds", "完成引导平均用时",
            Get(analyzed, "STAT_PT1_POKER_GUIDANCE_SECONDS"),
            Get(analyzed, "STAT_PT1_POKER_GUIDANCE_COMPLETED"), "秒");
        AddAverage(metrics, "行为", "guidance_average_hands", "完成引导平均局数",
            Get(analyzed, "STAT_PT1_POKER_GUIDANCE_HANDS"),
            Get(analyzed, "STAT_PT1_POKER_GUIDANCE_COMPLETED"), "局");
        AddRate(metrics, "奖励", "box_1_success_rate", "新手第1盲盒 Steam 成功率",
            Get(analyzed, "STAT_PT1_NEWCOMER_BOX_1_STEAM_CLAIMED"), cohort);
        AddRate(metrics, "奖励", "box_4_success_rate", "新手第4盲盒 Steam 成功率",
            Get(analyzed, "STAT_PT1_NEWCOMER_BOX_4_STEAM_CLAIMED"), cohort);
        AddRate(metrics, "奖励", "first_loop_box_success_rate", "首个循环盲盒 Steam 成功率",
            Get(analyzed, "STAT_PT1_FIRST_LOOP_BOX_STEAM_CLAIMED"), cohort);
        AddRate(metrics, "奖励", "linktree_claim_rate", "任意 LinkTree 奖励领取率",
            Get(analyzed, "STAT_PT1_ANY_LINKTREE_REWARD_CLAIMED"), cohort);
        AddAverage(metrics, "使用", "desktop_average_seconds", "每名样本玩家平均桌宠时长",
            Get(analyzed, "STAT_DESKTOP_MODE_SECONDS"), cohort, "秒");

        ValidateNonNegative(facts, checks);
        ValidateFlagsDoNotExceedCohort(facts, cohort, checks);
        ValidateRetentionOrder(analyzed, checks);
        ValidateGuidanceTotals(analyzed, checks);

        foreach (var unavailable in unavailableAccounts)
            checks.Add(new ValidationResult(CheckSeverity.Warning, "开发帐号扣除完整性",
                $"未能读取 {unavailable.Label}（{unavailable.SteamId}）：{unavailable.Error}；当前净值未扣除此帐号。"));

        if (checks.All(x => x.Severity == CheckSeverity.Info))
            checks.Add(new ValidationResult(CheckSeverity.Info, "数据关系检查", "当前快照未发现违反已知统计语义的关系。"));

        return new AnalysisReport(
            input.CapturedAtUtc,
            facts,
            metrics,
            checks,
            input.ExcludedAccounts.Where(x => x.Available).Select(x => $"{x.Label} ({x.SteamId})").ToArray());
    }

    private static void AddRate(List<MetricResult> metrics, string group, string key, string label, long numerator, long denominator)
    {
        var rate = denominator > 0 ? numerator * 100d / denominator : (double?)null;
        metrics.Add(new MetricResult(group, key, label, rate.HasValue ? $"{rate.Value:0.0}%" : "数据不足",
            $"{numerator} / {denominator}", denominator > 0 ? "可用" : "样本人数为0", rate));
    }

    private static void AddAverage(List<MetricResult> metrics, string group, string key, string label, long total, long count, string unit)
    {
        var average = count > 0 ? total / (double)count : (double?)null;
        metrics.Add(new MetricResult(group, key, label,
            average.HasValue ? $"{average.Value:0.0} {unit}" : "数据不足",
            $"{total} / {count}", count > 0 ? "可用" : "分母为0", average));
    }

    private static void ValidateNonNegative(IEnumerable<FactRow> facts, ICollection<ValidationResult> checks)
    {
        foreach (var fact in facts.Where(x => x.AnalyzedValue < 0))
            checks.Add(new ValidationResult(CheckSeverity.Error, "扣除后的统计不得为负数",
                $"{fact.DisplayName}：全局 {fact.GlobalValue} - 排除帐号 {fact.ExcludedValue} = {fact.AnalyzedValue}。"));
    }

    private static void ValidateFlagsDoNotExceedCohort(
        IEnumerable<FactRow> facts,
        long cohort,
        ICollection<ValidationResult> checks)
    {
        foreach (var fact in facts.Where(x => x.Unit == "Flag" && x.ApiName != Cohort && x.AnalyzedValue > cohort))
            checks.Add(new ValidationResult(CheckSeverity.Error, "Flag 总数不超过样本人数",
                $"{fact.DisplayName}={fact.AnalyzedValue}，大于样本人数 {cohort}。"));
    }

    private static void ValidateRetentionOrder(IReadOnlyDictionary<string, long> values, ICollection<ValidationResult> checks)
    {
        var h24 = Get(values, "STAT_PT1_RETURNED_AFTER_24H");
        var h72 = Get(values, "STAT_PT1_RETURNED_AFTER_72H");
        var h168 = Get(values, "STAT_PT1_RETURNED_AFTER_168H");
        if (h168 > h72 || h72 > h24)
            checks.Add(new ValidationResult(CheckSeverity.Error, "回访 Flag 应满足 168h ≤ 72h ≤ 24h",
                $"当前为 168h={h168}、72h={h72}、24h={h24}。游戏会在一次回访中补齐所有已达到阈值的 Flag。"));
    }

    private static void ValidateGuidanceTotals(IReadOnlyDictionary<string, long> values, ICollection<ValidationResult> checks)
    {
        var completed = Get(values, "STAT_PT1_POKER_GUIDANCE_COMPLETED");
        var seconds = Get(values, "STAT_PT1_POKER_GUIDANCE_SECONDS");
        var hands = Get(values, "STAT_PT1_POKER_GUIDANCE_HANDS");
        if (completed == 0 && (seconds > 0 || hands > 0))
            checks.Add(new ValidationResult(CheckSeverity.Error, "引导累计值必须有完成人数",
                $"完成人数为0，但累计秒数={seconds}、累计局数={hands}。"));
        if (completed > 0 && seconds == 0 && hands == 0)
            checks.Add(new ValidationResult(CheckSeverity.Warning, "引导完成应有耗时或局数",
                "已有引导完成人数，但秒数和局数累计值均为0；可能是玩家在首次观察前已接近完成。"));
    }

    private static long Get(IReadOnlyDictionary<string, long> values, string key) =>
        values.TryGetValue(key, out var value) ? value : 0;

    private static string UnitLabel(StatisticUnit unit) => unit switch
    {
        StatisticUnit.Seconds => "秒",
        StatisticUnit.Flag => "Flag",
        StatisticUnit.Chips => "筹码",
        _ => "次数"
    };

    private static string FormatInteger(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
