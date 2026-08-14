using Godot;

namespace LuckyDogRise;

public partial class StartupAccountGateController : Control
{
    [Signal] public delegate void RetryRequestedEventHandler();
    [Signal] public delegate void QuitRequestedEventHandler();

    [Export] private Label _statusLabel = null!;
    [Export] private Button _retryButton = null!;
    [Export] private Button _quitButton = null!;
    [Export] private LoadingIndicatorController _loadingIndicator = null!;

    public override void _Ready()
    {
        _retryButton.Pressed += () => EmitSignal(SignalName.RetryRequested);
        _quitButton.Pressed += () => EmitSignal(SignalName.QuitRequested);
        _loadingIndicator.SetLoading(true);
    }

    public void SetStatus(string status, bool retryEnabled)
    {
        _statusLabel.Text = status;
        _retryButton.Visible = true;
        _retryButton.Disabled = !retryEnabled;
        _loadingIndicator.Visible = true;
        _loadingIndicator.SetLoading(true);
    }

    public void SetAccessDenied(string status)
    {
        _statusLabel.Text = status;
        _retryButton.Visible = false;
        _loadingIndicator.SetLoading(false);
        _loadingIndicator.Visible = false;
        _quitButton.GrabFocus();
    }
}
