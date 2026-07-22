using System;
using System.Collections.Generic;
using System.Linq;
using Steamworks;

namespace LuckyDogRise;

public sealed class SteamGamePlatformService : IGamePlatformService, IPlatformAchievementTestOperations, IPlatformAchievementSyncOperations
{
    private readonly SteamworksRuntime _runtime;
    private readonly Callback<UserStatsReceived_t> _userStatsReceivedCallback;
    private readonly Callback<UserStatsStored_t> _userStatsStoredCallback;
    private readonly Callback<UserAchievementStored_t> _userAchievementStoredCallback;
    private bool _hasPendingAchievementStore;

    public SteamGamePlatformService(SteamworksRuntime runtime)
    {
        _runtime = runtime;
        _userStatsReceivedCallback = Callback<UserStatsReceived_t>.Create(OnUserStatsReceived);
        _userStatsStoredCallback = Callback<UserStatsStored_t>.Create(OnUserStatsStored);
        _userAchievementStoredCallback = Callback<UserAchievementStored_t>.Create(OnUserAchievementStored);
    }

    public event Action UserStatsReady = delegate { };
    public event Action<string> StoreStatusChanged = delegate { };

    public string ProviderName => "Steam";
    public string StatusMessage => _runtime.StatusMessage;
    public bool IsAvailable => _runtime.IsInitialized;
    public uint AppId => _runtime.AppId;
    public string PersonaName => _runtime.PersonaName;
    public bool IsReadyForWrites { get; private set; }

    public void RunCallbacks() => _runtime.RunCallbacks();
    public bool OpenFriendsOverlay() => _runtime.OpenFriendsOverlay();

    public PlatformAchievementReadResult ReadAchievementStates(IEnumerable<string> achievementApiNames)
    {
        if (!IsAvailable)
            return new(false, StatusMessage, Array.Empty<PlatformAchievementState>());

        try
        {
            var configuredNames = new HashSet<string>(StringComparer.Ordinal);
            var achievementCount = SteamUserStats.GetNumAchievements();
            for (uint index = 0; index < achievementCount; index++)
            {
                var apiName = SteamUserStats.GetAchievementName(index);
                if (!string.IsNullOrWhiteSpace(apiName))
                    configuredNames.Add(apiName);
            }

            var states = achievementApiNames
                .Where(apiName => !string.IsNullOrWhiteSpace(apiName))
                .Distinct(StringComparer.Ordinal)
                .Select(apiName => ReadAchievementState(apiName, configuredNames))
                .ToArray();
            if (states.Any(state => state.IsConfigured && state.ReadSucceeded))
                IsReadyForWrites = true;
            return new(true, $"Steam 返回 {achievementCount} 项成就定义。", states);
        }
        catch (Exception exception)
        {
            return new(false, $"读取 Steam 成就失败：{exception.GetType().Name}: {exception.Message}", Array.Empty<PlatformAchievementState>());
        }
    }

    public bool TrySetAchievementForTesting(string apiName, bool unlocked, out string message)
    {
        if (!IsAvailable || !IsReadyForWrites)
        {
            message = "Steam 用户统计尚未就绪，拒绝写入。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(apiName) || !SteamUserStats.GetAchievement(apiName, out _))
        {
            message = $"Steam 后台不存在成就：{apiName}";
            return false;
        }

        var changed = unlocked
            ? SteamUserStats.SetAchievement(apiName)
            : SteamUserStats.ClearAchievement(apiName);
        if (!changed)
        {
            message = $"Steam 拒绝{(unlocked ? "解锁" : "清除")}成就：{apiName}";
            return false;
        }

        if (!SteamUserStats.StoreStats())
        {
            message = $"已修改内存状态，但 StoreStats 请求失败：{apiName}";
            return false;
        }

        message = $"已提交{(unlocked ? "解锁" : "清除")}请求：{apiName}，等待 Steam 回调。";
        return true;
    }

    public PlatformAchievementUnlockResult UnlockAchievements(IEnumerable<string> achievementApiNames)
    {
        if (!IsAvailable || !IsReadyForWrites)
            return new(false, "Steam 用户统计尚未就绪，拒绝写入。", Array.Empty<string>());

        var submittedApiNames = new List<string>();
        foreach (var apiName in achievementApiNames
                     .Where(apiName => !string.IsNullOrWhiteSpace(apiName))
                     .Distinct(StringComparer.Ordinal))
        {
            if (!SteamUserStats.GetAchievement(apiName, out var isUnlocked))
                continue;
            if (isUnlocked)
                continue;
            if (!SteamUserStats.SetAchievement(apiName))
                return new(false, $"Steam 拒绝解锁成就：{apiName}", submittedApiNames);
            submittedApiNames.Add(apiName);
            _hasPendingAchievementStore = true;
        }

        if (!_hasPendingAchievementStore)
            return new(true, "没有需要提交的 Steam 成就。", submittedApiNames);
        if (!SteamUserStats.StoreStats())
            return new(false, "成就已写入 Steam 内存状态，但 StoreStats 请求失败。", submittedApiNames);

        _hasPendingAchievementStore = false;
        return new(true, $"已向 Steam 提交 {submittedApiNames.Count} 项成就。", submittedApiNames);
    }

    public void Dispose()
    {
        _userAchievementStoredCallback.Dispose();
        _userStatsStoredCallback.Dispose();
        _userStatsReceivedCallback.Dispose();
        _runtime.Dispose();
    }

    private static PlatformAchievementState ReadAchievementState(string apiName, HashSet<string> configuredNames)
    {
        if (!configuredNames.Contains(apiName))
            return new(apiName, IsConfigured: false, ReadSucceeded: false, IsUnlocked: false);

        var readSucceeded = SteamUserStats.GetAchievement(apiName, out var isUnlocked);
        return new(apiName, IsConfigured: true, readSucceeded, isUnlocked);
    }

    private void OnUserStatsReceived(UserStatsReceived_t callback)
    {
        if (callback.m_nGameID != AppId)
            return;

        if (callback.m_eResult != EResult.k_EResultOK)
        {
            Godot.GD.PushWarning($"[Steamworks] UserStatsReceived failed: {callback.m_eResult}");
            return;
        }

        Godot.GD.Print($"[Steamworks] User stats ready for AppID {AppId}.");
        IsReadyForWrites = true;
        UserStatsReady();
    }

    private void OnUserStatsStored(UserStatsStored_t callback)
    {
        if (callback.m_nGameID != AppId)
            return;

        var message = callback.m_eResult == EResult.k_EResultOK
            ? "Steam 已持久化成就/统计状态。"
            : $"Steam StoreStats 失败：{callback.m_eResult}";
        if (callback.m_eResult != EResult.k_EResultOK)
            _hasPendingAchievementStore = true;
        Godot.GD.Print($"[Steamworks] {message}");
        StoreStatusChanged(message);
    }

    private void OnUserAchievementStored(UserAchievementStored_t callback)
    {
        if (callback.m_nGameID != AppId)
            return;

        var message = callback.m_nMaxProgress == 0
            ? "Steam 已处理成就状态变更。"
            : $"Steam 已处理成就进度：{callback.m_nCurProgress}/{callback.m_nMaxProgress}";
        Godot.GD.Print($"[Steamworks] {message}");
        StoreStatusChanged(message);
    }
}
