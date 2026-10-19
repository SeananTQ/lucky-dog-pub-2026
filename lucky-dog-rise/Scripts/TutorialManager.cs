using Godot;

namespace LuckyDogRise;

/// <summary>Manual controls reference, opened from the poker toolbar.</summary>
public partial class TutorialManager : CanvasLayer
{
    [Signal] public delegate void OverlayVisibilityChangedEventHandler(bool visible);
    [Export] private Control _overlayInput = null!;

    private const double DismissLockSeconds = 2.0;
    private bool _pokerModeActive;
    private bool _blockedByHigherOverlay;
    private double _dismissLockRemaining;

    public bool IsOverlayVisible => Visible;

    public override void _Ready()
    {
        Visible = false;
        _overlayInput.GuiInput += OnOverlayGuiInput;
    }

    public override void _Process(double delta)
    {
        if (Visible)
            _dismissLockRemaining = Mathf.Max(0.0, _dismissLockRemaining - delta);
    }

    public void SetPokerContext(bool pokerModeActive, bool blockedByHigherOverlay)
    {
        _pokerModeActive = pokerModeActive;
        _blockedByHigherOverlay = blockedByHigherOverlay;
        if (!_pokerModeActive || _blockedByHigherOverlay)
            HideOverlay();
    }

    public void ShowOnce()
    {
        if (!_pokerModeActive || _blockedByHigherOverlay || Visible) return;
        Visible = true;
        _dismissLockRemaining = DismissLockSeconds;
        EmitSignal(SignalName.OverlayVisibilityChanged, true);
    }

    private void HideOverlay()
    {
        if (!Visible) return;
        Visible = false;
        _dismissLockRemaining = 0.0;
        EmitSignal(SignalName.OverlayVisibilityChanged, false);
    }

    private void OnOverlayGuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            return;

        _overlayInput.AcceptEvent();
        if (_dismissLockRemaining <= 0.0)
            HideOverlay();
    }
}
