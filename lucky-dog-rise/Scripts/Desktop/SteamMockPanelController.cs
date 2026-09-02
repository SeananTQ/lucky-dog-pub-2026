#if DEBUG
using System;
using System.Linq;
using Godot;

namespace LuckyDogRise;

public partial class SteamMockPanelController : CanvasLayer
{
    [Signal] public delegate void CloseRequestedEventHandler();
    [Signal] public delegate void SimulationResetEventHandler();
    [Signal] public delegate void RecoveredItemsPreviewRequestedEventHandler(int itemCount);

    [Export] private PanelContainer _panel = null!;
    [Export] private OptionButton _scenarioOption = null!;
    [Export] private OptionButton _progressOption = null!;
    [Export] private OptionButton _recoveredItemsPreviewOption = null!;
    [Export] private Button _recoveredItemsPreviewButton = null!;
    [Export] private Button _resetButton = null!;
    [Export] private Button _advanceButton = null!;
    [Export] private Button _platformModeButton = null!;
    [Export] private Button _closeButton = null!;
    [Export] private Label _connectionValue = null!;
    [Export] private Label _phaseValue = null!;
    [Export] private Label _phaseTimerValue = null!;
    [Export] private Label _rewardValue = null!;
    [Export] private Label _transactionValue = null!;
    [Export] private Label _lastEventValue = null!;
    [Export] private RichTextLabel _eventLog = null!;

    private IDebugSteamMockController _controller = null!;
    private GameData _gameData = null!;
    private bool _updatingSelection;
    private string _renderedEventLog = string.Empty;
    private float _panelBottomY;

    public Rect2 PanelRect => _panel == null ? default : new Rect2(_panel.Position, _panel.Size);
    public Vector2 PanelSize => _panel == null
        ? Vector2.Zero
        : new Vector2(
            Mathf.Max(_panel.Size.X, _panel.CustomMinimumSize.X),
            Mathf.Max(_panel.Size.Y, _panel.CustomMinimumSize.Y));

    public override void _Ready()
    {
        _scenarioOption.AddItem("正常掉落规则：按游玩资格与窗口返回奖励或空结果", (int)DebugSteamScenario.NormalSuccess);
        _scenarioOption.AddItem("强制快速成功：每次请求都生成奖励", (int)DebugSteamScenario.ForcedSuccess);
        _scenarioOption.AddItem("请求前不可用：盲盒使用 Fallback，LinkTree 保持 Loading", (int)DebugSteamScenario.UnavailableBeforeOpen);
        _scenarioOption.AddItem("慢响应：提交后等待 3 秒，随后成功", (int)DebugSteamScenario.SlowSuccess);
        _scenarioOption.AddItem("LinkTree 动画验收：领奖后全页同步 3 秒", (int)DebugSteamScenario.LinkTreePostClaimSyncDelay);
        _scenarioOption.AddItem("请求超时：等待 10 秒，再复查 10 秒并确认成功", (int)DebugSteamScenario.TimeoutVerifiedSuccess);
        _scenarioOption.AddItem("请求无回执：复查库存后未发现新奖励；展示点使用 Fallback", (int)DebugSteamScenario.TimeoutVerifiedFallback);
        _scenarioOption.AddItem("提交后持续断联：1 秒后断线，等待手动恢复", (int)DebugSteamScenario.DisconnectAfterSubmit);
        _scenarioOption.AddItem("断联后恢复：断线 10 秒，再复查 10 秒并成功", (int)DebugSteamScenario.DisconnectRecoverSuccess);
        _scenarioOption.ItemSelected += OnScenarioSelected;
        _progressOption.AddItem("新手流程：从第 1 个盲盒开始", (int)DebugBlindBoxProgressMode.BeginnerSequence);
        _progressOption.AddItem("普通循环：跳过新手 12 个盲盒", (int)DebugBlindBoxProgressMode.Loop);
        _progressOption.Select(1);
        _progressOption.ItemSelected += OnProgressSelected;
        _recoveredItemsPreviewOption.AddItem("单个物品：检查最小内容布局", 1);
        _recoveredItemsPreviewOption.AddItem("6 个物品：检查常规两行布局", 6);
        _recoveredItemsPreviewOption.AddItem("12 个物品：检查滚动与溢出布局", 12);
        _recoveredItemsPreviewOption.Select(1);
        _recoveredItemsPreviewButton.Pressed += () => EmitSignal(
            SignalName.RecoveredItemsPreviewRequested,
            _recoveredItemsPreviewOption.GetSelectedId());
        _resetButton.Pressed += ResetScenario;
        _advanceButton.Pressed += AdvancePhase;
        _platformModeButton.Pressed += TogglePlatformMode;
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
        RestoreProgressSelection(_gameData.SteamMockProgressMode);
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
        ActivateScenario(scenario);
    }

    private void OnProgressSelected(long index)
    {
        if (_updatingSelection || _gameData == null)
            return;
        var mode = (DebugBlindBoxProgressMode)_progressOption.GetItemId((int)index);
        _gameData.SetSteamMockProgressMode(mode);
    }

    private void ActivateScenario(DebugSteamScenario scenario)
    {
        var sandboxWasActive = _gameData.IsSteamMockSimulationActive;
        if (!sandboxWasActive
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

        var sandboxReady = _gameData.ResetSteamMockSimulation();
        if (!sandboxReady)
        {
            GD.PushWarning("[Steam Mock] 无法切换调试沙箱；请先结束当前盲盒、本地测试或平台交易。");
            if (_controller.TryUseRealSteam(out _))
                _gameData.SetSteamMockSimulationActive(false);
            RestoreScenarioSelection(_controller.Snapshot.Scenario);
        }
        EmitSignal(SignalName.SimulationReset);
        Refresh(_controller.Snapshot);
    }

    private void TogglePlatformMode()
    {
        if (_controller == null)
            return;
        if (!_controller.IsMockActive)
        {
            ActivateScenario((DebugSteamScenario)_scenarioOption.GetSelectedId());
            return;
        }

        if (!_controller.TryUseRealSteam(out var message))
        {
            GD.PushWarning($"[Steam Mock] {message}");
            return;
        }
        if (!_gameData.SetSteamMockSimulationActive(false))
        {
            GD.PushWarning("[Steam Mock] 无法退出调试沙箱；请先结束当前盲盒表演。");
            return;
        }
        EmitSignal(SignalName.SimulationReset);
        Refresh(_controller.Snapshot);
    }

    private void Refresh(DebugSteamMockSnapshot snapshot)
    {
        if (_scenarioOption == null)
            return;
        RestoreScenarioSelection(snapshot.Scenario);
        RestoreProgressSelection(_gameData?.SteamMockProgressMode ?? DebugBlindBoxProgressMode.Loop);
        _connectionValue.Text = FormatConnectionState(snapshot);
        _phaseValue.Text = FormatPhase(snapshot.Phase);
        _phaseTimerValue.Text = $"{snapshot.PhaseElapsedSeconds:0.0} 秒";
        var lateSuffix = _gameData?.SteamMockBlindBoxRewardIsLate == true ? "（迟到）" : string.Empty;
        var dropRuleSuffix = snapshot.DropIntervalSeconds > 0.0
            ? $" | 模拟 {snapshot.SimulatedPlaytimeSeconds / 60.0:0.0} 分"
              + $" | 资格 {snapshot.DropIntervalSeconds / 60.0:0.0} 分"
              + $" | 窗口 {snapshot.GrantsInWindow}/{snapshot.DropMaxPerWindow}"
            : string.Empty;
        var activationSuffix = snapshot.PendingRequestIsActivation
            ? "（预热请求）"
            : snapshot.GeneratorActivated ? "（已激活）" : string.Empty;
        _rewardValue.Text = (snapshot.RewardInstanceId == 0
            ? $"Generator {snapshot.GeneratorItemDefId}{activationSuffix}"
            : $"实例 {snapshot.RewardInstanceId}{lateSuffix}") + dropRuleSuffix;
        var businessPhase = _gameData?.SteamMockBlindBoxBusinessPhase ?? "Idle";
        _transactionValue.Text = CompactText($"{snapshot.PendingOperation} / {businessPhase}", 24);
        _lastEventValue.Text = CompactText(snapshot.LastEvent, 72);
        var eventLog = string.Join('\n', snapshot.Events.Select(BbcodeEscape));
        if (!string.Equals(_renderedEventLog, eventLog, StringComparison.Ordinal))
        {
            _renderedEventLog = eventLog;
            _eventLog.Text = eventLog;
        }
        _scenarioOption.Disabled = snapshot.HasPendingTransaction
                                   || _gameData?.PendingBlindBoxReward != null
                                   || _gameData?.PendingLinkTreeClaim != null;
        _advanceButton.Disabled = !snapshot.HasPendingTransaction;
        _advanceButton.Text = GetAdvanceButtonText(snapshot);
        _platformModeButton.Visible = _controller.CanUseRealSteam;
        _platformModeButton.Disabled = snapshot.HasPendingTransaction
                                       || _gameData?.PendingBlindBoxReward != null
                                       || _gameData?.PendingLinkTreeClaim != null;
        _platformModeButton.Text = _controller.IsMockActive ? "真实 Steam" : "启用 Mock";
    }

    private static string GetAdvanceButtonText(DebugSteamMockSnapshot snapshot)
    {
        if (!snapshot.HasPendingTransaction)
            return "推进到下一阶段";

        return snapshot.Phase switch
        {
            DebugSteamPhase.PlaytimeDropWaiting or DebugSteamPhase.PromoGrantWaiting
                when snapshot.Scenario is DebugSteamScenario.DisconnectAfterSubmit
                    or DebugSteamScenario.DisconnectRecoverSuccess => "模拟连接中断",
            DebugSteamPhase.PlaytimeDropWaiting or DebugSteamPhase.PromoGrantWaiting
                when snapshot.Scenario is DebugSteamScenario.TimeoutVerifiedSuccess
                    or DebugSteamScenario.TimeoutVerifiedFallback => "进入库存复查",
            DebugSteamPhase.PlaytimeDropWaiting or DebugSteamPhase.PromoGrantWaiting => "立即完成请求",
            DebugSteamPhase.Unavailable => "模拟恢复连接",
            DebugSteamPhase.InventoryVerification => "完成库存复查",
            _ => "推进到下一阶段",
        };
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

    private void RestoreProgressSelection(DebugBlindBoxProgressMode mode)
    {
        _updatingSelection = true;
        for (var i = 0; i < _progressOption.ItemCount; i++)
        {
            if (_progressOption.GetItemId(i) == (int)mode)
            {
                _progressOption.Select(i);
                break;
            }
        }
        _updatingSelection = false;
    }

    private static string BbcodeEscape(string value) => value
        .Replace("[", "[lb]")
        .Replace("]", "[rb]");

    private static string FormatConnectionState(DebugSteamMockSnapshot snapshot)
    {
        var connection = snapshot.ConnectionState switch
        {
            PlatformConnectionState.Offline => "离线",
            PlatformConnectionState.Connecting => "连接中",
            PlatformConnectionState.InventorySyncing => "同步中",
            PlatformConnectionState.Ready => "可用",
            PlatformConnectionState.Unavailable => "不可用",
            _ => snapshot.ConnectionState.ToString(),
        };

        var trust = snapshot.InventoryTrustState switch
        {
            PlatformInventoryTrustState.Unknown => "未确认",
            PlatformInventoryTrustState.Trusted => "可信",
            PlatformInventoryTrustState.RevalidationRequired => "需复查",
            _ => snapshot.InventoryTrustState.ToString(),
        };
        return $"{connection} / {trust}";
    }

    private static string FormatPhase(DebugSteamPhase phase) => phase switch
    {
        DebugSteamPhase.Ready => "就绪",
        DebugSteamPhase.PlaytimeDropWaiting => "奖励准备",
        DebugSteamPhase.PromoGrantWaiting => "奖励发放",
        DebugSteamPhase.Unavailable => "不可用",
        DebugSteamPhase.InventoryVerification => "库存复查",
        DebugSteamPhase.Completed => "已完成",
        _ => phase.ToString(),
    };

    private static string CompactText(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;
        return value[..Math.Max(0, maxLength - 1)] + "…";
    }
}
#endif
