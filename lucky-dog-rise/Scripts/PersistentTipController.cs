using Godot;
using System;

namespace LuckyDogRise;

/// <summary>Reusable non-modal note; its scene anchor belongs to the containing interface.</summary>
public partial class PersistentTipController : Control
{
    [Export] private PanelContainer _panel = null!;
    [Export] private Label _message = null!;
    [Export] private Label _dismissLabel = null!;
    [Export] private Button _dismissButton = null!;
    [Export] public string TipKey { get; set; } = PersistentTipProgress.PokerDraw;
    [Export] public string MessageKey { get; set; } = L10nKey.PersistentTip_PokerDraw;
    [Export] public bool AnchorToBottom { get; set; } = true;
    public event Action InterfaceActivity = delegate { };
    private PersistentTipProgress _progress;
    private bool _layoutQueued;

    public override void _Ready()
    {
        Hide();
        _dismissButton.ButtonDown += OnButtonDown;
        _dismissButton.Pressed += Dismiss;
        _panel.Resized += AnchorPanel;
        _panel.MinimumSizeChanged += QueueFitToContent;
        Resized += QueueFitToContent;
        L10n.Changed += RefreshText;
        RefreshText();
    }

    public override void _ExitTree()
    {
        L10n.Changed -= RefreshText;
        _dismissButton.ButtonDown -= OnButtonDown;
        _dismissButton.Pressed -= Dismiss;
        _panel.Resized -= AnchorPanel;
        _panel.MinimumSizeChanged -= QueueFitToContent;
        Resized -= QueueFitToContent;
    }

    public void SetContext(PersistentTipProgress progress, bool available)
    {
        _progress = progress;
        bool show = available && progress?.GetState(TipKey) == PersistentTipState.Active;
        if (Visible == show) return;
        Visible = show;
        if (show) FitToContent();
    }

    public bool ContainsPoint(Vector2 point) => IsVisibleInTree() && _panel.GetGlobalRect().HasPoint(point);
    private void OnButtonDown() => InterfaceActivity();
    private void AnchorPanel() => _panel.Position = new Vector2(0, AnchorToBottom ? -_panel.Size.Y : 0);

    private void QueueFitToContent()
    {
        if (_layoutQueued) return;
        _layoutQueued = true;
        CallDeferred(nameof(FitToContent));
    }

    private void FitToContent()
    {
        _layoutQueued = false;
        if (!IsInsideTree()) return;
        var style = _panel.GetThemeStylebox("panel");
        float textWidth = Mathf.Max(1, Size.X - style.GetContentMargin(Side.Left) - style.GetContentMargin(Side.Right));
        _message.CustomMinimumSize = new Vector2(textWidth, 0);
        _dismissLabel.CustomMinimumSize = new Vector2(textWidth, 0);
        _message.Size = new Vector2(textWidth, 0);
        _dismissLabel.Size = new Vector2(textWidth, 0);
        _message.UpdateMinimumSize();
        _dismissLabel.UpdateMinimumSize();
        _message.GetParent<Control>().UpdateMinimumSize();
        _panel.UpdateMinimumSize();
        // A standalone Container grows with its minimum size but does not shrink automatically.
        // Wrapping is initially measured before the final width; refit whenever that measurement
        // changes, including while hidden, rather than waiting for a later locale refresh.
        _panel.Size = new Vector2(Size.X, 0);
        AnchorPanel();
    }

    private void Dismiss()
    {
        if (!Visible || _progress == null) return;
        InterfaceActivity();
        try
        {
            _progress.Dismiss(TipKey);
            Hide();
        }
        catch (Exception error) { GD.PushWarning($"[PersistentTip] Could not save dismissal: {error.Message}"); }
    }

    private void RefreshText()
    {
        _message.Text = L10n.Tr(MessageKey);
        _dismissLabel.Text = L10n.Tr(L10nKey.PersistentTip_Dismiss);
        QueueFitToContent();
    }
}
