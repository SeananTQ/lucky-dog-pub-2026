#if DEBUG
using Godot;

namespace LuckyDogRise;

public enum DebugRuntimeEnvironment
{
    IntegratedDebug,
    SteamMock,
}

public readonly record struct DebugLaunchSelection(
    DebugRuntimeEnvironment Environment,
    DebugSteamScenario SteamScenario);

public partial class DeveloperLauncherController : CanvasLayer
{
    [Signal]
    public delegate void LaunchRequestedEventHandler(int environment, int scenario);

    [Export] private OptionButton _environmentOption = null!;
    [Export] private VBoxContainer _mockScenarioSection = null!;
    [Export] private OptionButton _scenarioOption = null!;
    [Export] private Label _environmentHint = null!;
    [Export] private Button _launchButton = null!;
    [Export] private Button _quitButton = null!;

    public override void _Ready()
    {
        _environmentOption.AddItem("综合调试环境", (int)DebugRuntimeEnvironment.IntegratedDebug);
        _environmentOption.AddItem("Steam 模拟环境", (int)DebugRuntimeEnvironment.SteamMock);

        _scenarioOption.AddItem("正常掉落规则：按游玩资格与窗口返回奖励或空结果", (int)DebugSteamScenario.NormalSuccess);
        _scenarioOption.AddItem("强制快速成功：每次请求都生成奖励", (int)DebugSteamScenario.ForcedSuccess);
        _scenarioOption.AddItem("请求前不可用：不提交 Steam 请求", (int)DebugSteamScenario.UnavailableBeforeOpen);
        _scenarioOption.AddItem("慢响应：提交后等待 3 秒成功", (int)DebugSteamScenario.SlowSuccess);
        _scenarioOption.AddItem("请求超时：等待 10 秒，复查 10 秒后确认成功", (int)DebugSteamScenario.TimeoutVerifiedSuccess);
        _scenarioOption.AddItem("请求无回执：复查库存后未发现新奖励", (int)DebugSteamScenario.TimeoutVerifiedFallback);
        _scenarioOption.AddItem("提交后持续断联：1 秒后断线，等待手动恢复", (int)DebugSteamScenario.DisconnectAfterSubmit);
        _scenarioOption.AddItem("断联后恢复：断线 10 秒，复查 10 秒后成功", (int)DebugSteamScenario.DisconnectRecoverSuccess);

        _environmentOption.ItemSelected += _ => RefreshEnvironmentControls();
        _launchButton.Pressed += OnLaunchPressed;
        _quitButton.Pressed += () => GetTree().Quit();
        RefreshEnvironmentControls();
    }

    private void RefreshEnvironmentControls()
    {
        var environment = (DebugRuntimeEnvironment)_environmentOption.GetSelectedId();
        _mockScenarioSection.Visible = environment == DebugRuntimeEnvironment.SteamMock;
        _environmentHint.Text = environment == DebugRuntimeEnvironment.SteamMock
            ? "Steam 模拟环境使用独立内存沙箱，不创建真实 Steam 会话，也不写入真实存档。"
            : "综合调试环境使用当前真实 Steam 与本地存档，并保留完整 Debug 工具。";
    }

    private void OnLaunchPressed()
    {
        _launchButton.Disabled = true;
        _environmentOption.Disabled = true;
        _scenarioOption.Disabled = true;
        EmitSignal(
            SignalName.LaunchRequested,
            _environmentOption.GetSelectedId(),
            _scenarioOption.GetSelectedId());
    }
}
#endif
