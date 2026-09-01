#if DEBUG
using System;
using System.Linq;
using DataTables;
using Godot;

namespace LuckyDogRise;

internal static class PlayerStatisticRegressionSmoke
{
    public static void Run()
    {
        VerifyRetentionThresholds();
        VerifyBlindBoxSuccessRules();
        VerifyPlatformMergeRules();
        VerifyInitialRewardSequenceProgressDefinition();
        VerifyChipLedgerRules();
        GD.Print("[PlayerStatisticRegressionSmoke] Passed retention, blind-box, sequence-progress, Steam merge, and chip-ledger checks.");
    }

    private static void VerifyInitialRewardSequenceProgressDefinition()
    {
        var definition = LubanData.Tables.TbPlayerStatistic.DataList.FirstOrDefault(statistic =>
            statistic.StatisticKey == PlayerProgress.InitialRewardSequenceProgressStatisticKey);
        Assert(definition is
            {
                IsEnabled: true,
                StatisticType: EPlayerStatisticType.Maximum,
                PlatformApiName: "STAT_INITIAL_REWARD_SEQUENCE_PROGRESS",
                SyncToPlatform: true,
            }, "Initial reward sequence progress is missing or has the wrong Steam Maximum definition.");
        Assert(Calculate(EPlayerStatisticType.Maximum, 5_000, false, 0, 3_000) == 5_000,
            "A newer local sequence checkpoint did not win the Steam merge.");
        Assert(Calculate(EPlayerStatisticType.Maximum, 3_000, false, 0, 5_000) == 5_000,
            "A newer Steam sequence checkpoint did not win the local merge.");
    }

    private static void VerifyRetentionThresholds()
    {
        Assert(PlayerProgress.GetReturnStatisticKeys(TimeSpan.FromHours(23.99)).Count == 0,
            "Retention was recorded before 24 hours.");
        Assert(PlayerProgress.GetReturnStatisticKeys(TimeSpan.FromHours(24)).Count == 1,
            "The 24-hour threshold was not inclusive.");
        Assert(PlayerProgress.GetReturnStatisticKeys(TimeSpan.FromHours(72)).Count == 2,
            "The 72-hour launch did not include both reached thresholds.");
        Assert(PlayerProgress.GetReturnStatisticKeys(TimeSpan.FromHours(168)).Count == 3,
            "The 168-hour launch did not include all reached thresholds.");
    }

    private static void VerifyBlindBoxSuccessRules()
    {
        Assert(PlayerProgress.GetSteamBlindBoxClaimStatisticKey(1001, true, true).Length > 0,
            "Schedule 1001 Steam success was not recognized.");
        Assert(PlayerProgress.GetSteamBlindBoxClaimStatisticKey(1004, true, true).Length > 0,
            "Schedule 1004 Steam success was not recognized.");
        Assert(PlayerProgress.GetSteamBlindBoxClaimStatisticKey(2001, true, true).Length > 0,
            "Schedule 2001 Steam success was not recognized.");
        Assert(PlayerProgress.GetSteamBlindBoxClaimStatisticKey(1001, false, true).Length == 0,
            "Fallback incorrectly counted as Steam success.");
        Assert(PlayerProgress.GetSteamBlindBoxClaimStatisticKey(1001, true, false).Length == 0,
            "LateSteam incorrectly counted after the Schedule ended.");
    }

    private static void VerifyPlatformMergeRules()
    {
        Assert(Calculate(EPlayerStatisticType.Flag, 1, false, 0, 0) == 1,
            "Local Flag did not upload as 1.");
        Assert(Calculate(EPlayerStatisticType.Flag, 0, false, 0, 1) == 1,
            "Remote Flag did not merge into local state.");
        Assert(Calculate(EPlayerStatisticType.Counter, 120, false, 0, 100) == 120,
            "First counter merge double-counted existing Steam state.");
        Assert(Calculate(EPlayerStatisticType.Counter, 130, true, 100, 100) == 130,
            "Local counter delta was not preserved.");
        Assert(Calculate(EPlayerStatisticType.Counter, 130, true, 100, 120) == 150,
            "Remote progress and new local delta were not merged.");
        Assert(Calculate(EPlayerStatisticType.Counter, 30, true, 0, 1_000) == 1_030,
            "Fresh-device activity before the first Steam read was lost.");
        Assert(Calculate(EPlayerStatisticType.Counter, 1_030, true, 1_000, 1_200) == 1_230,
            "Existing local history was double-counted or its new pre-sync delta was lost.");
        Assert(Calculate(EPlayerStatisticType.Maximum, 80, true, 70, 90) == 90,
            "Maximum statistic did not retain the higher remote value.");
    }

    private static void VerifyChipLedgerRules()
    {
        Assert(PlayerProgress.CalculateChipLedgerBalance(1_000, 400) == 600,
            "Chip ledger did not subtract cumulative debits from credits.");
        Assert(PlayerProgress.CalculateChipLedgerBalance(400, 1_000) == 0,
            "Chip ledger allowed a negative player balance.");

        var mergedCredits = Calculate(EPlayerStatisticType.Counter, 130, true, 100, 120);
        var mergedDebits = Calculate(EPlayerStatisticType.Counter, 50, true, 40, 45);
        Assert(PlayerProgress.CalculateChipLedgerBalance(mergedCredits, mergedDebits) == 95,
            "Independent cross-device credit/debit deltas did not restore the expected balance.");

        var freshDevice = PlayerProgress.CalculateInitialChipLedger(
            remoteCredits: 1_200,
            remoteDebits: 300,
            migrationBalance: GameData.StartingChips,
            mayPreserveLocalBalance: false,
            pendingCredits: 0,
            pendingDebits: 0);
        Assert(freshDevice == (1_200, 300),
            "A fresh device granted its starting chips to an existing remote ledger.");

        var existingSave = PlayerProgress.CalculateInitialChipLedger(
            remoteCredits: 1_200,
            remoteDebits: 300,
            migrationBalance: 1_100,
            mayPreserveLocalBalance: true,
            pendingCredits: 25,
            pendingDebits: 10);
        Assert(existingSave == (1_425, 310),
            "Existing local balance or pre-sync chip changes were lost during migration.");
    }

    private static long Calculate(
        EPlayerStatisticType type,
        long local,
        bool hasBaseline,
        long baseline,
        long remote) => PlatformStatisticSynchronizer.CalculateTarget(
        new PlatformStatisticSyncState("Test", "STAT_TEST", type, local, hasBaseline, baseline),
        remote);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
#endif
