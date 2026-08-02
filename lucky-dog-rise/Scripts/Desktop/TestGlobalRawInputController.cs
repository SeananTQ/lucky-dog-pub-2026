using Godot;

namespace LuckyDogRise;

public partial class TestGlobalRawInputController : Control
{
    [Export] private RawInputMouseListener _listener = null!;
    [Export] private Label _statusLabel = null!;
    [Export] private Label _rawPacketCountLabel = null!;
    [Export] private Label _movementPacketCountLabel = null!;
    [Export] private Label _pressCountLabel = null!;
    [Export] private Label _lastPressLabel = null!;
    [Export] private Button _toggleListeningButton = null!;
    [Export] private Button _resetButton = null!;
    [Export] private Button _quitButton = null!;

    private long _pressCount;
    private string _lastPressText = "尚未收到点击";
    private double _refreshTimer;

    public override void _Ready()
    {
        DisplayServer.WindowSetSize(new Vector2I(760, 560));
        var usableRect = DisplayServer.ScreenGetUsableRect();
        DisplayServer.WindowSetPosition(usableRect.Position + (usableRect.Size - new Vector2I(760, 560)) / 2);

        _listener.RawMousePressed += OnRawMousePressed;
        _toggleListeningButton.Pressed += ToggleListening;
        _resetButton.Pressed += ResetDiagnostics;
        _quitButton.Pressed += () => GetTree().Quit();
        RefreshPresentation();
    }

    public override void _Process(double delta)
    {
        _refreshTimer -= delta;
        if (_refreshTimer > 0.0)
            return;

        _refreshTimer = 0.1;
        RefreshPresentation();
    }

    private void OnRawMousePressed(int button, Vector2I screenPosition)
    {
        _pressCount++;
        _lastPressText = $"{FormatButton((MouseButton)button)} @ ({screenPosition.X}, {screenPosition.Y})";
        RefreshPresentation();
    }

    private void ToggleListening()
    {
        if (_listener.IsListening)
            _listener.StopListening();
        else
            _listener.StartListening();
        RefreshPresentation();
    }

    private void ResetDiagnostics()
    {
        _pressCount = 0;
        _lastPressText = "尚未收到点击";
        _listener.ResetDiagnostics();
        RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        var error = _listener.LastError;
        _statusLabel.Text = _listener.IsAutoStartPending
            ? "状态：等待 Godot 输入初始化后注册…"
            : _listener.IsListening
            ? "状态：Raw Input 后台监听中"
            : string.IsNullOrEmpty(error)
                ? "状态：监听已停止"
                : $"状态：启动失败 — {error}";
        _statusLabel.Modulate = _listener.IsAutoStartPending
            ? new Color(0.95f, 0.75f, 0.25f)
            : _listener.IsListening
            ? new Color(0.2f, 0.85f, 0.45f)
            : string.IsNullOrEmpty(error)
                ? new Color(0.95f, 0.75f, 0.25f)
                : new Color(1f, 0.35f, 0.35f);
        _rawPacketCountLabel.Text = _listener.TotalRawPackets.ToString();
        _movementPacketCountLabel.Text = _listener.IgnoredMovementPackets.ToString();
        _pressCountLabel.Text = _pressCount.ToString();
        _lastPressLabel.Text = _lastPressText;
        _toggleListeningButton.Text = _listener.IsAutoStartPending
            ? "立即开始监听"
            : _listener.IsListening
                ? "停止监听"
                : "开始监听";
    }

    private static string FormatButton(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => "左键",
            MouseButton.Right => "右键",
            MouseButton.Middle => "中键",
            MouseButton.Xbutton1 => "侧键 1",
            MouseButton.Xbutton2 => "侧键 2",
            _ => button.ToString(),
        };
    }
}
