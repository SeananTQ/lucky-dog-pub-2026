using System;
using System.Linq;
using Godot;

namespace LuckyDogRise;

public static class GamePlatformServiceFactory
{
    public const string DisableSteamArgument = "--disable-steam";

    public static IGamePlatformService Create()
    {
#if DEBUG
        return Create(new DebugLaunchSelection(
            DebugRuntimeEnvironment.IntegratedDebug,
            DebugSteamScenario.NormalSuccess));
    }

    public static IGamePlatformService Create(DebugLaunchSelection selection)
    {
        if (selection.Environment == DebugRuntimeEnvironment.SteamMock)
        {
            var offline = new OfflineGamePlatformService(
                "Steam 模拟环境不会创建真实 Steam 会话。");
            return new DebugSteamMockPlatformService(
                offline,
                startInMock: true,
                canUseRealSteam: false,
                initialScenario: selection.SteamScenario);
        }
#endif
        if (OS.GetCmdlineUserArgs().Any(argument =>
                string.Equals(argument, DisableSteamArgument, StringComparison.OrdinalIgnoreCase)))
        {
            IGamePlatformService offline = new OfflineGamePlatformService(
                "Steam 已通过命令行参数禁用。游戏继续以离线平台模式运行。");
#if DEBUG
            offline = new DebugSteamMockPlatformService(offline);
#endif
            return offline;
        }

        IGamePlatformService service = new RecoveringSteamPlatformService();
#if DEBUG
        service = new DebugSteamMockPlatformService(service);
#endif
        return service;
    }
}
