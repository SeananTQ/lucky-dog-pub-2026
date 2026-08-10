#if DEBUG
using System;
using System.Linq;
using Godot;

namespace LuckyDogRise;

public partial class SteamMockPanelController : CanvasLayer
{
    [Signal] public delegate void CloseRequestedEventHandler();
    [Signal] public delegate void SimulationResetEventHandler();

    [Export] private PanelContainer _panel = null!;
    [Export] private OptionButton _scenarioOption = null!;
    [Export] private Button _resetButton = null!;
    [Export] private Button _advanceButton = null!;
    [Export] private Button _closeButton = null!;
    [Export] private Label _connectionValue = null!;
    [Export] private Label _phaseValue = null!;
    [Export] private Label _phaseTimerValue = null!;
    [Export] private Label _voucherValue = null!;
    [Export] private Label _transactionValue = null!;
    [Export] private Label _lastEventValue = null!;
    [Export] private RichTextLabel _eventLog = null!;

    private IDebugSteamMockController _controller = null!;
    private GameData _gameData = null!;
    private bool _updatingSelection;
    private float _panelBottomY;

    public Rect2 PanelRect => _panel == null ? default : new Rect2(_panel.Position, _panel.Size);

    public override void _Ready()
    {
        _scenarioOption.AddItem("正常：使用真实 Steam，不进行模拟", (int)DebugSteamScenario.RealSteam);
        _scenarioOption.AddItem("开盒前不可用：点击时立即使用本地 Fallback", (int)DebugSteamScenario.UnavailableBeforeOpen);
        _scenarioOption.AddItem("慢响应：提交后等待 3 秒，随后成功发奖", (int)DebugSteamScenario.SlowSuccess);
        _scenarioOption.AddItem("请求超时：等待 10 秒，再复查 10 秒并确认成功", (int)DebugSteamScenario.TimeoutVerifiedSuccess);
        _scenarioOption.AddItem("请求超时：等待 10 秒，再复查 10 秒后安全 Fallback", (int)DebugSteamScenario.TimeoutVerifiedFallback);
        _scenarioOption.AddItem("提交后断联：1 秒后断线，结果保持未知", (int)DebugSteamScenario.DisconnectAfterSubmit);
        _scenarioOption.AddItem("断联后恢复：断线 10 秒，再复查 10 秒并成功", (int)DebugSteamScenario.DisconnectRecoverSuccess);
        _scenarioOption.ItemSelected += OnScenarioSelected;
        _resetButton.Pressed += ResetScenario;
        _advanceButton.Pressed += AdvancePhase;
        _closeButton.Pressed += () => EmitSignal(SignalName.CloseRequested);
        Visible = false;
        SetProcess(false);
        CallDeferred(MethodName.AlignPanelAboveContent);
    }

    public override void _Process(double delta)
    {
        if (_controller == null || !Visible)
            return;
        AlignPanelAboveContent();
        Refresh(_controller.Snapshot);
    }

    public void Bind(IGamePlatformService platformService, GameData gameData)
    {
        _controller = platformService as IDebugSteamMockController;
        _gameData = gameData;
        if (_controller == null)
        {
            GD.PushError("[Steam Mock] Debug controller is unavailable.");
            return;
        }
        _controller.SnapshotChanged += Refresh;
        Refresh(_controller.Snapshot);
    }

    public void SetPanelBottom(float bottomY)
    {
        _panelBottomY = bottomY;
        CallDeferred(MethodName.AlignPanelAboveContent);
    }

    public void SetPanelVisible(bool visible)
    {
        Visible = visible;
        SetProcess(visible);
        if (visible)
            CallDeferred(MethodName.AlignPanelAboveContent);
        if (visible && _controller != null)
            Refresh(_controller.Snapshot);
    }

    private void AlignPanelAboveContent()
    {
        if (_panel == null || _panelBottomY <= 0f)
            return;
        _panel.Position = new Vector2(0f, _panelBottomY - _panel.Size.Y);
    }

    public bool ContainsPoint(Vector2 windowLocalPoint) => Visible && PanelRect.HasPoint(windowLocalPoint);

    public void ResetScenario()
    {
        if (_controller == null)
            return;
        if (_controller.IsMockActive)
            _gameData.ResetSteamMockSimulation();
        else
            _controller.ResetScenario();
        EmitSignal(SignalName.SimulationReset);
        Refresh(_controller.Snapshot);
    }

    public void AdvancePhase()
    {
        _controller?.AdvancePhase();
        if (_controller != null)
            Refresh(_controller.Snapshot);
    }

    private void OnScenarioSelected(long index)
    {
        if (_updatingSelection || _controller == null)
            return;
        var scenario = (DebugSteamScenario)_scenarioOption.GetItemId((int)index);
        var sandboxWasActive = _gameData.IsSteamMockSimulationActive;
        if (scenario != DebugSteamScenario.RealSteam
            && !sandboxWasActive
            && !_gameData.SetSteamMockSimulationActive(true))
        {
            GD.PushWarning("[Steam Mock] 无法进入调试沙箱；请先结束当前盲盒、本地测试或平台交易。");
            RestoreScenarioSelection(_controller.Snapshot.Scenario);
            return;
        }

        if (!_controller.TrySelectScenario(scenario, out var message))
        {
            GD.PushWarning($"[Steam Mock] {message}");
            if (!sandboxWasActive)
                _gameData.SetSteamMockSimulationActive(false);
            RestoreScenarioSelection(_controller.Snapshot.Scenario);
            return;
        }

        var sandboxReady = scenario == DebugSteamScenario.RealSteam
            ? _gameData.SetSteamMockSimulationActive(false)
            : _gameData.ResetSteamMockSimulation();
        if (!sandboxReady)
        {
            GD.PushWarning("[Steam Mock] 无法切换调试沙箱；请先结束当前盲盒、本地测试或平台交易。");
            _controller.TrySelectScenario(DebugSteamScenario.RealSteam, out _);
            RestoreScenarioSelection(DebugSteamScenario.RealSteam);
        }
        EmitSignal(SignalName.SimulationReset);
        Refresh(_controller.Snapshot);
    }

    private void Refresh(DebugSteamMockSnapshot snapshot)
    {
        if (_scenarioOption == null)
            return;
        RestoreScenarioSelection(snapshot.Scenario);
        _connectionValue.Text = snapshot.ConnectionState.ToString();
        _phaseValue.Text = snapshot.Phase.ToString();
        _phaseTimerValue.Text = $"{snapshot.PhaseElapsedSeconds:0.0} 秒";
        _voucherValue.Text = snapshot.VoucherQuantity.ToString();
        _transactionValue.Text = snapshot.HasPendingTransaction ? "有" : "无";
        _lastEventValue.Text = snapshot.LastEvent;
        _eventLog.Text = string.Join('\n', snapshot.Events.Select(BbcodeEscape));
        _scenarioOption.Disabled = snapshot.HasPendingTransaction || _gameData?.PendingBlindBoxReward != null;
        _advanceButton.Disabled = !snapshot.HasPendingTransaction;
    }

    private void RestoreScenarioSelection(DebugSteamScenario scenario)
    {
        _updatingSelection = true;
        for (var i = 0; i < _scenarioOption.ItemCount; i++)
        {
            if (_scenarioOption.GetItemId(i) == (int)scenario)
            {
                _scenarioOption.Select(i);
                break;
            }
        }
        _updatingSelection = false;
    }

    private static string BbcodeEscape(string value) => value
        .Replace("[", "[lb]")
        .Replace("]", "[rb]");
}
#endif
