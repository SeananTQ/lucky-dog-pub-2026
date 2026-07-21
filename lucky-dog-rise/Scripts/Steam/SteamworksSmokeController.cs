using Godot;

namespace LuckyDogRise;

public partial class SteamworksSmokeController : Control
{
    [Export] private Label _statusLabel = null!;
    [Export] private Label _configurationLabel = null!;
    [Export] private Label _detailsLabel = null!;
    [Export] private Button _retryButton = null!;
    [Export] private Button _overlayButton = null!;
    [Export] private Button _quitButton = null!;

    private SteamworksRuntime _steamworks = null!;

    public override void _Ready()
    {
        _retryButton.Pressed += InitializeSteamworks;
        _overlayButton.Pressed += OpenOverlay;
        _quitButton.Pressed += () => GetTree().Quit();
        InitializeSteamworks();
    }

    public override void _Process(double delta)
    {
        _steamworks?.RunCallbacks();
    }

    public override void _ExitTree()
    {
        _steamworks?.Dispose();
        _steamworks = null;
    }

    private void InitializeSteamworks()
    {
        _steamworks?.Dispose();
        _steamworks = new SteamworksRuntime();
        var initialized = _steamworks.TryInitialize();

        _statusLabel.Text = _steamworks.StatusMessage;
        _configurationLabel.Text = _steamworks.RequestedAppId == 0
            ? "本地配置 AppID：未找到（Steam 正式启动时可忽略）"
            : $"本地配置 AppID：{_steamworks.RequestedAppId}";
        _detailsLabel.Text = initialized
            ? $"AppID: {_steamworks.AppId}\n玩家：{_steamworks.PersonaName}\nSteamID: {_steamworks.SteamId}\n回调：每帧运行"
            : "AppID: -\n玩家：-\nSteamID: -\n回调：未启动";
        _overlayButton.Disabled = !initialized;

        if (initialized)
            GD.Print($"[Steamworks] Initialized. AppID={_steamworks.AppId}, Persona={_steamworks.PersonaName}");
        else
            GD.Print($"[Steamworks] Not initialized. {_steamworks.StatusMessage}");
    }

    private void OpenOverlay()
    {
        if (_steamworks?.OpenFriendsOverlay() == true)
            _statusLabel.Text = "已请求打开 Steam 好友 Overlay。";
    }
}
