#if DEBUG
using System;
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
        GD.Print("[PlayerStatisticRegressionSmoke] Passed retention, blind-box, and Steam merge checks.");
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
        Assert(Calculate(EPlayerStatisticType.Maximum, 80, true, 70, 90) == 90,
            "Maximum statistic did not retain the higher remote value.");
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
