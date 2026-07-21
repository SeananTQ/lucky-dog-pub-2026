using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Godot;
using Steamworks;

namespace LuckyDogRise;

/// <summary>
/// Owns the low-level Steamworks.NET lifecycle. Game systems access it through
/// IGamePlatformService; the smoke scene may use it directly for diagnostics.
/// </summary>
public sealed class SteamworksRuntime : IDisposable
{
    private bool _initialized;
    private bool _disposed;

    public bool IsInitialized => _initialized;
    public string StatusMessage { get; private set; } = "尚未初始化";
    public uint AppId { get; private set; }
    public string PersonaName { get; private set; } = string.Empty;
    public ulong SteamId { get; private set; }
    public uint RequestedAppId { get; private set; }

    public bool TryInitialize()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SteamworksRuntime));

        if (_initialized)
            return true;

        try
        {
            SteamNativeLibraryResolver.Install();
            RequestedAppId = ApplyDevelopmentAppId();

            if (!SteamAPI.IsSteamRunning())
            {
                StatusMessage = "Steam 客户端未运行，未执行 SteamAPI.Init()。";
                return false;
            }

            if (!SteamAPI.Init())
            {
                StatusMessage = "SteamAPI.Init() 返回 false。Steam 已运行，但当前 AppID 尚未被客户端接受。";
                return false;
            }

            _initialized = true;
            AppId = SteamUtils.GetAppID().m_AppId;
            PersonaName = SteamFriends.GetPersonaName();
            SteamId = SteamUser.GetSteamID().m_SteamID;
            StatusMessage = "Steamworks 初始化成功。";
            return true;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Steamworks 初始化异常：{exception.GetType().Name}: {exception.Message}";
            GD.PushWarning($"[Steamworks] {exception}");
            Shutdown();
            return false;
        }
    }

    public void RunCallbacks()
    {
        if (!_initialized || _disposed)
            return;

        try
        {
            SteamAPI.RunCallbacks();
        }
        catch (Exception exception)
        {
            GD.PushWarning($"[Steamworks] Callback pump failed: {exception}");
            StatusMessage = $"Steam 回调异常：{exception.GetType().Name}: {exception.Message}";
            Shutdown();
        }
    }

    public bool OpenFriendsOverlay()
    {
        if (!_initialized || _disposed)
            return false;

        SteamFriends.ActivateGameOverlay("friends");
        return true;
    }

    public void Shutdown()
    {
        if (!_initialized)
            return;

        SteamAPI.Shutdown();
        _initialized = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Shutdown();
        _disposed = true;
    }

    private static uint ApplyDevelopmentAppId()
    {
        var appIdPath = ProjectSettings.GlobalizePath("res://steam_appid.txt");
        if (!File.Exists(appIdPath))
            return 0;

        var appIdText = File.ReadAllText(appIdPath).Trim();
        if (!uint.TryParse(appIdText, out var appId))
        {
            GD.PushWarning($"[Steamworks] Invalid development AppID in {appIdPath}.");
            return 0;
        }

        // Godot games launched from the editor may inherit the editor executable's
        // working directory, so Steam cannot reliably discover res://steam_appid.txt.
        // Steam-launched builds do not ship this file and receive these variables
        // from the Steam client instead.
        System.Environment.SetEnvironmentVariable("SteamAppId", appId.ToString());
        System.Environment.SetEnvironmentVariable("SteamGameId", appId.ToString());
        GD.Print($"[Steamworks] Applied local development AppID: {appId}");
        return appId;
    }
}

internal static class SteamNativeLibraryResolver
{
    private const string NativeLibraryName = "steam_api64";
    private static bool _installed;
    private static IntPtr _loadedHandle;
    private static readonly object LoadLock = new();

    public static void Install()
    {
        if (_installed)
            return;

        NativeLibrary.SetDllImportResolver(typeof(SteamAPI).Assembly, Resolve);
        _installed = true;
    }

    private static IntPtr Resolve(string libraryName, Assembly _, DllImportSearchPath? __)
    {
        if (!libraryName.Equals(NativeLibraryName, StringComparison.OrdinalIgnoreCase) &&
            !libraryName.Equals($"{NativeLibraryName}.dll", StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero;

        lock (LoadLock)
        {
            if (_loadedHandle != IntPtr.Zero)
                return _loadedHandle;

            foreach (var candidate in GetCandidatePaths())
            {
                if (!File.Exists(candidate) || !NativeLibrary.TryLoad(candidate, out _loadedHandle))
                    continue;

                GD.Print($"[Steamworks] Loaded native library: {candidate}");
                return _loadedHandle;
            }
        }

        GD.PushWarning("[Steamworks] steam_api64.dll was not found in the development SDK or beside the executable.");
        return IntPtr.Zero;
    }

    private static IEnumerable<string> GetCandidatePaths()
    {
        var executableDirectory = Path.GetDirectoryName(System.Environment.ProcessPath);
        if (!string.IsNullOrEmpty(executableDirectory))
            yield return Path.Combine(executableDirectory, "steam_api64.dll");

        yield return Path.Combine(AppContext.BaseDirectory, "steam_api64.dll");

        var projectRoot = ProjectSettings.GlobalizePath("res://");
        yield return Path.GetFullPath(Path.Combine(
            projectRoot,
            "..",
            ".local-build",
            "steamworks",
            "Steamworks.NET-Standalone_2025.163.0",
            "Windows-x64",
            "steam_api64.dll"));
    }
}
