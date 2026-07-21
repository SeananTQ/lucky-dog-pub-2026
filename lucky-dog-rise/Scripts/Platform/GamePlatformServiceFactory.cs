using System;
using System.Linq;
using Godot;

namespace LuckyDogRise;

public static class GamePlatformServiceFactory
{
    public const string DisableSteamArgument = "--disable-steam";

    public static IGamePlatformService Create()
    {
        if (OS.GetCmdlineUserArgs().Any(argument =>
                string.Equals(argument, DisableSteamArgument, StringComparison.OrdinalIgnoreCase)))
        {
            return new OfflineGamePlatformService("Steam 已通过命令行参数禁用。游戏继续以离线平台模式运行。");
        }

        var runtime = new SteamworksRuntime();
        if (runtime.TryInitialize())
            return new SteamGamePlatformService(runtime);

        var failureReason = runtime.StatusMessage;
        runtime.Dispose();
        return new OfflineGamePlatformService(failureReason);
    }
}
