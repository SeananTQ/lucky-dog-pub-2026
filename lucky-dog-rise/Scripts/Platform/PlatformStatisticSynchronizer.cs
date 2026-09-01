#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using DataTables;
using Godot;

namespace LuckyDogRise;

/// <summary>
/// Merges monotonic local player facts with Steam INT stats. Flags and maxima use max;
/// counters add only the local delta recorded since the last merge baseline. A persisted
/// migration baseline separates pre-existing local history from activity before the first
/// successful Steam read, including after an offline restart.
/// </summary>
public sealed class PlatformStatisticSynchronizer
{
    private const double MonotonicFactUploadDelaySeconds = 10.0;
    private const double CounterUploadIntervalSeconds = 60.0;
    private const double RetryIntervalSeconds = 10.0;

    private readonly IGamePlatformService _platformService;
    private readonly IPlatformStatisticSyncOperations? _operations;
    private readonly PlayerProgress _playerProgress;
    private readonly Action? _synchronizationCompleted;
    private string _lastLocalSnapshot = string.Empty;
    private double _secondsUntilSync;
    private bool _initialSyncCompleted;
    private bool _reportedMissingDefinitions;

    public PlatformStatisticSynchronizer(
        IGamePlatformService platformService,
        PlayerProgress playerProgress,
        Action? synchronizationCompleted = null)
    {
        _platformService = platformService;
        _operations = platformService as IPlatformStatisticSyncOperations;
        _playerProgress = playerProgress;
        _synchronizationCompleted = synchronizationCompleted;
    }

    public void Tick(double delta)
    {
        if (!_platformService.IsAvailable || _operations == null || !_playerProgress.IsPlatformSyncAllowed)
            return;

        var states = _playerProgress.GetPlatformSyncStatisticStates();
        var snapshot = BuildSnapshot(states);
        if (!_initialSyncCompleted)
        {
            _secondsUntilSync -= Math.Max(0.0, delta);
            if (_secondsUntilSync > 0.0)
                return;
            Synchronize(states);
            return;
        }

        if (!string.Equals(snapshot, _lastLocalSnapshot, StringComparison.Ordinal))
        {
            var previousValues = ParseSnapshot(_lastLocalSnapshot);
            var monotonicFactChanged = states.Any(state =>
                state.StatisticType != EPlayerStatisticType.Counter
                && previousValues.GetValueOrDefault(state.StatisticKey) != state.LocalValue);
            var delay = monotonicFactChanged
                ? MonotonicFactUploadDelaySeconds
                : CounterUploadIntervalSeconds;
            if (_secondsUntilSync <= 0.0 || delay < _secondsUntilSync)
                _secondsUntilSync = delay;
            _lastLocalSnapshot = snapshot;
        }

        _secondsUntilSync -= Math.Max(0.0, delta);
        if (_secondsUntilSync > 0.0)
            return;

        Synchronize(states);
    }

    public void Flush()
    {
        if (_platformService.IsAvailable && _operations != null && _playerProgress.IsPlatformSyncAllowed)
            Synchronize(_playerProgress.GetPlatformSyncStatisticStates());
    }

    private void Synchronize(IReadOnlyList<PlatformStatisticSyncState> localStates)
    {
        var operations = _operations;
        if (operations == null)
            return;

        var readResult = operations.ReadStatistics(localStates.Select(state => state.ApiName));
        if (!readResult.Succeeded)
        {
            _secondsUntilSync = RetryIntervalSeconds;
            return;
        }

        var remoteByApiName = readResult.States.ToDictionary(state => state.ApiName, StringComparer.Ordinal);
        var pendingValues = new Dictionary<string, int>(StringComparer.Ordinal);
        var targetsByApiName = new Dictionary<string, (string StatisticKey, long Target)>(StringComparer.Ordinal);
        var missingApiNames = new List<string>();
        foreach (var local in localStates)
        {
            if (!remoteByApiName.TryGetValue(local.ApiName, out var remote)
                || !remote.IsConfigured
                || !remote.ReadSucceeded)
            {
                missingApiNames.Add(local.ApiName);
                continue;
            }

            var target = CalculateTarget(local, remote.Value);
            if (target > int.MaxValue)
            {
                GD.PushError($"[StatisticSync] Value exceeds Steam INT range: {local.ApiName}={target}.");
                continue;
            }

            targetsByApiName[local.ApiName] = (local.StatisticKey, target);
            if (target != remote.Value)
                pendingValues[local.ApiName] = checked((int)target);
        }

        if (missingApiNames.Count > 0 && !_reportedMissingDefinitions)
        {
            _reportedMissingDefinitions = true;
            GD.PushWarning(
                $"[StatisticSync] Steam backend is missing or has the wrong type for: {string.Join(", ", missingApiNames)}.");
        }

        var writeResult = operations.SubmitStatistics(pendingValues);
        var accepted = writeResult.AcceptedApiNames.ToHashSet(StringComparer.Ordinal);
        foreach (var (apiName, targetState) in targetsByApiName)
        {
            if (!pendingValues.ContainsKey(apiName) || accepted.Contains(apiName))
                _playerProgress.CommitPlatformStatisticSync(targetState.StatisticKey, targetState.Target);
        }

        _initialSyncCompleted = true;
        _lastLocalSnapshot = BuildSnapshot(_playerProgress.GetPlatformSyncStatisticStates());
        _synchronizationCompleted?.Invoke();
        _secondsUntilSync = writeResult.Succeeded
            ? CounterUploadIntervalSeconds
            : RetryIntervalSeconds;
        if (pendingValues.Count > 0)
        {
            if (writeResult.Succeeded)
                GD.Print($"[StatisticSync] {writeResult.Message}");
            else
                GD.PushWarning($"[StatisticSync] {writeResult.Message}");
        }
    }

    internal static long CalculateTarget(PlatformStatisticSyncState local, long remoteValue)
    {
        remoteValue = Math.Max(0, remoteValue);
        return local.StatisticType switch
        {
            EPlayerStatisticType.Flag => local.LocalValue > 0 || remoteValue > 0 ? 1 : 0,
            EPlayerStatisticType.Maximum => Math.Max(local.LocalValue, remoteValue),
            EPlayerStatisticType.Counter when local.HasBaseline => checked(
                Math.Max(remoteValue, local.BaselineValue)
                + Math.Max(0, local.LocalValue - local.BaselineValue)),
            EPlayerStatisticType.Counter => Math.Max(local.LocalValue, remoteValue),
            _ => Math.Max(local.LocalValue, remoteValue),
        };
    }

    private static string BuildSnapshot(IEnumerable<PlatformStatisticSyncState> states) => string.Join(
        "|",
        states.Select(state => $"{state.StatisticKey}={state.LocalValue}"));

    private static Dictionary<string, long> ParseSnapshot(string snapshot)
    {
        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var part in snapshot.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0 || !long.TryParse(part[(separator + 1)..], out var value))
                continue;
            values[part[..separator]] = value;
        }
        return values;
    }
}
