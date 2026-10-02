#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using Godot;

namespace LuckyDogRise.Rooms;

// Shared by the isolated lab and the real desktop. No player state or transport ownership.
public partial class RoomChatView : Node2D
{
    [Export] private PanelContainer _message = null!;
    [Export] private Label _text = null!;
    [Export] private PanelContainer _composer = null!;
    [Export] private LineEdit _input = null!;
    [Export] private Label _error = null!;
    [Export] private Button _send = null!;
    [Export] private Button _cancel = null!;
    [Export] private Button _open = null!;
    [Export] private Polygon2D _tail = null!;
    public event Action<string> SendRequested;
    public event Action Opened;
    public event Action<bool> BubbleActivityChanged;
    // Evaluated at the input boundary as well as each frame: an overlay can start
    // after hover/rendering but before a queued click or Enter signal arrives.
    public Func<bool> InteractionAllowed { get; set; }
    private bool CanInteract => !_displaySuppressed && _local && IsVisibleInTree() && (InteractionAllowed?.Invoke() ?? true);
    public bool Editing => _composer.Visible;
    public bool HasBubble => !_displaySuppressed && IsVisibleInTree() && (Editing || _messageText.Length > 0);
    private bool _displaySuppressed;
    public void SetDisplaySuppressed(bool suppressed)
    {
        if (_displaySuppressed == suppressed) return;
        _displaySuppressed = suppressed;
        if (suppressed)
        {
            CloseChat(); // Preserve a draft while a local system notice takes priority.
            _message.Hide(); _open.Hide(); _tail.Hide();
        }
        ReportBubbleActivity();
    }
    private bool _reportedBubbleActivity;
    private bool _local;
    private double _hoverGrace;
    private string _messageText = "";
    private string _errorKey = "";
    private bool _imeAtKey;
    private const double ErrorShakeDuration = 0.36;
    private double _errorShakeRemaining;

    public override void _Ready()
    {
        _input.MaxLength = RoomRules.MaxChatCharacters;
        // A native popup lies outside the transparent host hit map; omit it.
        _input.ContextMenuEnabled = false;
        _open.Pressed += OpenChat;
        _send.Pressed += Send;
        _cancel.Pressed += CloseChat;
        _input.TextSubmitted += _ => { if (!_imeAtKey) Send(); };
        _input.TextChanged += _ => { _errorKey = ""; RefreshText(); };
        RefreshText();
        Reset();
    }

    private static string Tr(string key) => InGameRoomPreview.ChatText(key);
    private void RefreshText()
    {
        _input.PlaceholderText = Tr("Rooms_ChatPlaceholder");
        _send.Text = Tr("Rooms_ChatSend");
        _cancel.Text = Tr("Rooms_ChatCancel");
        _open.TooltipText = Tr("Rooms_ChatOpen");
        _error.Text = _errorKey.Length > 0 ? Tr(_errorKey) : "";
        _error.Visible = _errorKey.Length > 0;
    }
    public override void _Notification(int what)
    {
        if (what == NotificationTranslationChanged && IsNodeReady()) RefreshText();
        if (what == NotificationVisibilityChanged && IsNodeReady() && !IsVisibleInTree()) Reset();
        if (what == NotificationWMWindowFocusOut && IsNodeReady()) CloseChat();
    }
    public void Present(bool local, bool hovered, string text)
    {
        if (_local != local) CloseChat();
        _local = local;
        if (RefreshInteractionAvailability() && hovered) _hoverGrace = 0.45;
        _messageText = text ?? "";
        _text.Text = _messageText;
        ReportBubbleActivity();
    }
    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        if (_displaySuppressed) return;
        bool interactive = RefreshInteractionAvailability();
        _errorShakeRemaining = HasBubble ? Math.Max(0, _errorShakeRemaining - delta) : 0;
        if (interactive && RoomDogView.ContainsWindowPoint(_open, GetGlobalMousePosition())) _hoverGrace = 0.45;
        else _hoverGrace -= delta;
        _open.Visible = interactive && !Editing && _hoverGrace > 0;
        if (_open.Visible)
        {
            var inverse = GetGlobalTransformWithCanvas().AffineInverse();
            var min = inverse * new Vector2(8, 8);
            var max = inverse * (GetViewportRect().Size - new Vector2(8, 8));
            _open.Position = new Vector2(Mathf.Clamp(-16, min.X, Mathf.Max(min.X, max.X - _open.Size.X)),
                Mathf.Clamp(-24, min.Y, Mathf.Max(min.Y, max.Y - _open.Size.Y)));
        }
        _message.Visible = !Editing && _messageText.Length > 0;
        _tail.Visible = Editing || _message.Visible;
        LayoutBubble();
    }
    private void LayoutBubble()
    {
        var panel = Editing ? _composer : _message;
        if (!panel.Visible) return;
        var inv = GetGlobalTransformWithCanvas().AffineInverse();
        var viewport = GetViewportRect().Size;
        var min = inv * new Vector2(8, 8);
        var max = inv * (viewport - new Vector2(8, 8));
        float available = Mathf.Max(100, max.X - min.X);
        float width;
        if (Editing) width = Mathf.Min(320, available);
        else
        {
            var font = _text.GetThemeFont("font");
            int size = _text.GetThemeFontSize("font_size");
            width = Mathf.Min(available, Mathf.Clamp(font.GetStringSize(_messageText, fontSize: size).X + 28, 100, 300));
        }
        panel.CustomMinimumSize = new Vector2(width, 0);
        panel.Size = new Vector2(width, panel.Size.Y);
        // Containers normally sort later in the frame. Give wrapped labels their
        // actual width before asking for height, including the first visible frame.
        // Otherwise a fresh label measures almost one character per line.
        SortBubbleContents(panel);
        if (!Editing)
        {
            _text.Size = new Vector2(width - panel.GetThemeStylebox("panel").GetMinimumSize().X, _text.Size.Y);
            // Force text shaping now; its minimum-size cache otherwise still has
            // the line count measured before the container assigned this width.
            _ = _text.GetLineCount();
            _text.UpdateMinimumSize();
        }
        panel.UpdateMinimumSize();
        float height = panel.GetCombinedMinimumSize().Y;
        panel.Size = new Vector2(width, height);
        width = panel.Size.X;
        height = panel.Size.Y;
        // Match the countdown balloon's left edge and stationary tail at 1x.
        float progress = 1 - (float)(_errorShakeRemaining / ErrorShakeDuration);
        float shake = Mathf.Sin(progress * Mathf.Tau * 3) * 7 * (1 - progress);
        float x = Mathf.Clamp(-92 + shake, min.X, Mathf.Max(min.X, max.X - width));
        float y = -14 - height;
        if (y < min.Y) y = 16; // Near screen top, keep the editor reachable below its anchor.
        y = Mathf.Clamp(y, min.Y, Mathf.Max(min.Y, max.Y - height));
        panel.Position = new Vector2(x, y);
        float baseX = Mathf.Clamp(shake, x + 12, x + width - 12);
        float edgeY = y >= 0 ? y : y + height;
        _tail.Polygon = new[] { Vector2.Zero, new Vector2(baseX - 16, edgeY), new Vector2(baseX, edgeY) };
    }
    private static void SortBubbleContents(Container container)
    {
        container.Notification((int)Container.NotificationSortChildren);
        foreach (var child in container.GetChildren())
        {
            if (child is Container nested) SortBubbleContents(nested);
            else if (child is Label label)
            {
                _ = label.GetLineCount();
                label.UpdateMinimumSize();
            }
        }
    }
    private void ReportBubbleActivity()
    {
        bool active = HasBubble;
        if (_reportedBubbleActivity == active) return;
        _reportedBubbleActivity = active;
        BubbleActivityChanged?.Invoke(active);
    }
    public bool RefreshInteractionAvailability()
    {
        bool allowed = CanInteract;
        if (!allowed)
        {
            _hoverGrace = 0;
            _open.Hide();
            // Keep the draft, but release keyboard focus and any stale error state.
            if (Editing) CloseChat();
        }
        return allowed;
    }
    public bool ContainsPoint(Vector2 point) => CanInteract
        && (RoomDogView.ContainsWindowPoint(_open, point) || RoomDogView.ContainsWindowPoint(_composer, point));
    public override void _Input(InputEvent @event)
    {
        if (!RefreshInteractionAvailability() || !Editing) return;
        if (@event is InputEventKey { Pressed: true } key)
        {
            _imeAtKey = _input.HasImeText();
            if (key.Keycode == Key.Escape && !_imeAtKey)
            { CloseChat(); GetViewport().SetInputAsHandled(); }
        }
        if (@event is InputEventMouseButton { Pressed: true } mouse && !ContainsPoint(mouse.Position))
            CloseChat();
    }
    public void OpenChat()
    {
        if (!RefreshInteractionAvailability()) return;
        Opened?.Invoke();
        _composer.Show();
        ReportBubbleActivity();
        _open.Hide();
        _message.Hide();
        LayoutBubble();
        _input.GrabFocus();
        _input.Edit();
    }
    private void Send()
    {
        if (!RefreshInteractionAvailability() || !Editing || _input.HasImeText()) return;
        SendRequested?.Invoke(_input.Text);
    }
    public void SetSendResult(string error)
    {
        if (string.IsNullOrEmpty(error)) { _input.Clear(); CloseChat(); }
        else { _errorKey = error; RefreshText(); ShakeError(); _input.GrabFocus(); _input.Edit(); }
    }
    // Pure visual feedback also used by the lab's target-send button. It must
    // never focus a remote/hidden input or manufacture a message that was not sent.
    public void ShakeError()
    {
        if (HasBubble) _errorShakeRemaining = ErrorShakeDuration;
    }
    public void CloseChat()
    {
        _errorShakeRemaining = 0;
        _composer.Hide();
        _input.ReleaseFocus();
        _input.Unedit();
        _errorKey = "";
        RefreshText();
        ReportBubbleActivity();
    }
    public void Reset()
    {
        CloseChat();
        _input.Clear();
        _messageText = "";
        _text.Text = "";
        _message.Hide(); _open.Hide(); _tail.Hide();
        _hoverGrace = 0;
        ReportBubbleActivity();
    }
#if DEBUG
    public Rect2 ComposerRectForSmoke => _composer.GetGlobalRect();
    public Rect2 MessageRectForSmoke => _message.GetGlobalRect();
    public Rect2 ButtonRectForSmoke => _open.GetGlobalRect();
    public string ErrorForSmoke => _errorKey;
    public void SubmitForSmoke(string text) { OpenChat(); _input.Text = text; _send.EmitSignal(Button.SignalName.Pressed); }
#endif
}
#endif
