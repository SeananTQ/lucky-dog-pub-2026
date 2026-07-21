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
    [Export] private OptionButton _achievementOption = null!;
    [Export] private CheckButton _enableWritesCheck = null!;
    [Export] private Button _unlockAchievementButton = null!;
    [Export] private Button _clearAchievementButton = null!;
    [Export] private Label _operationStatusLabel = null!;
    [Export] private Button _retryButton = null!;
    [Export] private Button _overlayButton = null!;
    [Export] private Button _quitButton = null!;

    private IGamePlatformService _platformService = null!;
    private IPlatformAchievementTestOperations _testOperations = null!;
    private uint _requestedAppId;

    public override void _Ready()
    {
        _retryButton.Pressed += InitializeSteamworks;
        _overlayButton.Pressed += OpenOverlay;
        _enableWritesCheck.Toggled += _ => UpdateWriteControls();
        _unlockAchievementButton.Pressed += () => WriteSelectedAchievement(unlocked: true);
        _clearAchievementButton.Pressed += () => WriteSelectedAchievement(unlocked: false);
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
        _testOperations = null;
    }

    private void InitializeSteamworks()
    {
        _platformService?.Dispose();
        _testOperations = null;
        _enableWritesCheck.ButtonPressed = false;
        _achievementOption.Clear();
        _operationStatusLabel.Text = "真实写入尚未启用。";
        var runtime = new SteamworksRuntime();
        var initialized = runtime.TryInitialize();
        _requestedAppId = runtime.RequestedAppId;
        if (initialized)
        {
            _platformService = new SteamGamePlatformService(runtime);
            _platformService.UserStatsReady += RefreshAchievementStates;
            _testOperations = (IPlatformAchievementTestOperations)_platformService;
            _testOperations.StoreStatusChanged += OnStoreStatusChanged;
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
        UpdateWriteControls();

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
        PopulateAchievementOptions(configured.Select(state => state.ApiName).ToArray());
        UpdateWriteControls();
        GD.Print($"[Steamworks] {result.Message} MatchedTableRows={configured.Length}");
    }

    private void PopulateAchievementOptions(string[] apiNames)
    {
        var selectedText = _achievementOption.ItemCount > 0
            ? _achievementOption.GetItemText(_achievementOption.Selected)
            : string.Empty;
        _achievementOption.Clear();
        foreach (var apiName in apiNames)
            _achievementOption.AddItem(apiName);

        if (!string.IsNullOrEmpty(selectedText))
        {
            for (var index = 0; index < _achievementOption.ItemCount; index++)
            {
                if (_achievementOption.GetItemText(index) == selectedText)
                {
                    _achievementOption.Selected = index;
                    break;
                }
            }
        }
    }

    private void UpdateWriteControls()
    {
        var canWrite = _enableWritesCheck.ButtonPressed
            && _achievementOption.ItemCount > 0
            && _testOperations?.IsReadyForWrites == true;
        _achievementOption.Disabled = _achievementOption.ItemCount == 0;
        _unlockAchievementButton.Disabled = !canWrite;
        _clearAchievementButton.Disabled = !canWrite;
    }

    private void WriteSelectedAchievement(bool unlocked)
    {
        if (!_enableWritesCheck.ButtonPressed || _achievementOption.ItemCount == 0 || _testOperations == null)
            return;

        var apiName = _achievementOption.GetItemText(_achievementOption.Selected);
        _testOperations.TrySetAchievementForTesting(apiName, unlocked, out var message);
        _operationStatusLabel.Text = message;
    }

    private void OnStoreStatusChanged(string message)
    {
        _operationStatusLabel.Text = message;
        RefreshAchievementStates();
    }
}
