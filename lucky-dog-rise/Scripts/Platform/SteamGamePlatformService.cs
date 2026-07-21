using System;
using System.Collections.Generic;
using System.Linq;
using Steamworks;

namespace LuckyDogRise;

public sealed class SteamGamePlatformService : IGamePlatformService
{
    private readonly SteamworksRuntime _runtime;
    private readonly Callback<UserStatsReceived_t> _userStatsReceivedCallback;

    public SteamGamePlatformService(SteamworksRuntime runtime)
    {
        _runtime = runtime;
        _userStatsReceivedCallback = Callback<UserStatsReceived_t>.Create(OnUserStatsReceived);
    }

    public event Action UserStatsReady = delegate { };

    public string ProviderName => "Steam";
    public string StatusMessage => _runtime.StatusMessage;
    public bool IsAvailable => _runtime.IsInitialized;
    public uint AppId => _runtime.AppId;
    public string PersonaName => _runtime.PersonaName;

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
            return new(true, $"Steam 返回 {achievementCount} 项成就定义。", states);
        }
        catch (Exception exception)
        {
            return new(false, $"读取 Steam 成就失败：{exception.GetType().Name}: {exception.Message}", Array.Empty<PlatformAchievementState>());
        }
    }

    public void Dispose()
    {
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
        UserStatsReady();
    }
}
