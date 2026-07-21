using System.Linq;
using System.Text;
using Godot;

namespace LuckyDogRise;

public partial class SteamworksSmokeController : Control
{
    [Export] private Label _statusLabel = null!;
    [Export] private Label _configurationLabel = null!;
    [Export] private Label _detailsLabel = null!;
    [Export] private Label _achievementStatesLabel = null!;
    [Export] private Button _retryButton = null!;
    [Export] private Button _overlayButton = null!;
    [Export] private Button _quitButton = null!;

    private IGamePlatformService _platformService = null!;
    private uint _requestedAppId;

    public override void _Ready()
    {
        _retryButton.Pressed += InitializeSteamworks;
        _overlayButton.Pressed += OpenOverlay;
        _quitButton.Pressed += () => GetTree().Quit();
        InitializeSteamworks();
    }

    public override void _Process(double delta)
    {
        _platformService?.RunCallbacks();
    }

    public override void _ExitTree()
    {
        _platformService?.Dispose();
        _platformService = null;
    }

    private void InitializeSteamworks()
    {
        _platformService?.Dispose();
        var runtime = new SteamworksRuntime();
        var initialized = runtime.TryInitialize();
        _requestedAppId = runtime.RequestedAppId;
        if (initialized)
        {
            _platformService = new SteamGamePlatformService(runtime);
            _platformService.UserStatsReady += RefreshAchievementStates;
        }
        else
        {
            var failureReason = runtime.StatusMessage;
            runtime.Dispose();
            _platformService = new OfflineGamePlatformService(failureReason);
        }

        _statusLabel.Text = _platformService.StatusMessage;
        _configurationLabel.Text = _requestedAppId == 0
            ? "本地配置 AppID：未找到（Steam 正式启动时可忽略）"
            : $"本地配置 AppID：{_requestedAppId}";
        _detailsLabel.Text = initialized
            ? $"AppID: {_platformService.AppId}\n玩家：{_platformService.PersonaName}\n回调：每帧运行"
            : "AppID: -\n玩家：-\n回调：未启动";
        _overlayButton.Disabled = !initialized;
        RefreshAchievementStates();

        if (initialized)
            GD.Print($"[Steamworks] Initialized. AppID={_platformService.AppId}, Persona={_platformService.PersonaName}");
        else
            GD.Print($"[Steamworks] Not initialized. {_platformService.StatusMessage}");
    }

    private void OpenOverlay()
    {
        if (_platformService?.OpenFriendsOverlay() == true)
            _statusLabel.Text = "已请求打开 Steam 好友 Overlay。";
    }

    private void RefreshAchievementStates()
    {
        if (!_platformService.IsAvailable)
        {
            _achievementStatesLabel.Text = "成就只读检查：Steam 不可用";
            return;
        }

        var tableApiNames = LubanData.Tables.TbAchievement.DataList.Select(achievement => achievement.ApiName).ToArray();
        var result = _platformService.ReadAchievementStates(tableApiNames);
        if (!result.Succeeded)
        {
            _achievementStatesLabel.Text = result.Message;
            return;
        }

        var configured = result.States.Where(state => state.IsConfigured).ToArray();
        var builder = new StringBuilder();
        builder.AppendLine($"Steam 后台成就：{configured.Length} / 表内 {result.States.Count}（只读）");
        foreach (var state in configured)
        {
            var status = !state.ReadSucceeded ? "读取失败" : state.IsUnlocked ? "已解锁" : "未解锁";
            builder.AppendLine($"{state.ApiName}：{status}");
        }
        builder.Append($"阶段性未配置：{result.States.Count - configured.Length}");
        _achievementStatesLabel.Text = builder.ToString();
        GD.Print($"[Steamworks] {result.Message} MatchedTableRows={configured.Length}");
    }
}
