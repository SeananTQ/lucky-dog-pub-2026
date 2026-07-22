#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace LuckyDogRise;

/// <summary>
/// 将账号级本地成就与平台成就按并集合并。平台不可用时，本地进度照常工作。
/// </summary>
public sealed class PlatformAchievementSynchronizer
{
    private const double SyncIntervalSeconds = 2.0;
    private const double RetryIntervalSeconds = 10.0;

    private readonly IGamePlatformService _platformService;
    private readonly IPlatformAchievementSyncOperations? _writeOperations;
    private readonly PlayerProgress _playerProgress;
    private readonly string[] _knownAchievementApiNames;
    private double _secondsUntilSync;
    private string _lastEligibleSnapshot = string.Empty;
    private bool _initialSyncCompleted;
    private bool _reportedMissingPlatformDefinitions;

    public PlatformAchievementSynchronizer(IGamePlatformService platformService, PlayerProgress playerProgress)
    {
        _platformService = platformService;
        _writeOperations = platformService as IPlatformAchievementSyncOperations;
        _playerProgress = playerProgress;
        _knownAchievementApiNames = LubanData.Tables.TbAchievement.DataList
            .Select(achievement => achievement.ApiName)
            .Where(apiName => !string.IsNullOrWhiteSpace(apiName))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public void Tick(double delta)
    {
        if (!_platformService.IsAvailable || _writeOperations == null || !_playerProgress.IsPlatformSyncAllowed)
            return;

        _secondsUntilSync -= delta;
        var eligibleSnapshot = BuildEligibleSnapshot();
        var localProgressChanged = !string.Equals(eligibleSnapshot, _lastEligibleSnapshot, StringComparison.Ordinal);
        if (_secondsUntilSync > 0.0 && _initialSyncCompleted && !localProgressChanged)
            return;

        Synchronize();
    }

    private void Synchronize()
    {
        var writeOperations = _writeOperations;
        if (writeOperations == null)
            return;

        var readResult = _platformService.ReadAchievementStates(_knownAchievementApiNames);
        if (!readResult.Succeeded)
        {
            _secondsUntilSync = RetryIntervalSeconds;
            return;
        }

        if (!_reportedMissingPlatformDefinitions)
        {
            var missingCount = readResult.States.Count(state => !state.IsConfigured);
            if (missingCount > 0)
                GD.Print($"[AchievementSync] {missingCount} local achievement(s) are not configured by {_platformService.ProviderName}; keeping them local only.");
            _reportedMissingPlatformDefinitions = true;
        }

        var remoteUnlocked = readResult.States
            .Where(state => state.IsConfigured && state.ReadSucceeded && state.IsUnlocked)
            .Select(state => state.ApiName)
            .ToArray();
        var importedCount = _playerProgress.ImportPlatformAchievements(remoteUnlocked);

        var localEligible = new HashSet<string>(
            _playerProgress.GetPlatformSyncEligibleAchievementApiNames(),
            StringComparer.Ordinal);
        var pendingUploads = readResult.States
            .Where(state => state.IsConfigured && state.ReadSucceeded && !state.IsUnlocked && localEligible.Contains(state.ApiName))
            .Select(state => state.ApiName)
            .ToArray();

        var writeResult = writeOperations.UnlockAchievements(pendingUploads);
        if (!writeResult.Succeeded)
        {
            GD.PushWarning($"[AchievementSync] {writeResult.Message}");
            _secondsUntilSync = RetryIntervalSeconds;
            return;
        }

        if (writeResult.SubmittedApiNames.Count > 0)
            GD.Print($"[AchievementSync] Submitted {writeResult.SubmittedApiNames.Count} achievement(s) to {_platformService.ProviderName}.");

        if (importedCount > 0)
        {
            _playerProgress.SaveIfDirty();
            GD.Print($"[AchievementSync] Imported {importedCount} achievement(s) from {_platformService.ProviderName} into local progress.");
        }

        _lastEligibleSnapshot = BuildEligibleSnapshot();
        _initialSyncCompleted = true;
        _secondsUntilSync = SyncIntervalSeconds;
    }

    private string BuildEligibleSnapshot() => string.Join(
        "\n",
        _playerProgress.GetPlatformSyncEligibleAchievementApiNames().OrderBy(apiName => apiName, StringComparer.Ordinal));
}
