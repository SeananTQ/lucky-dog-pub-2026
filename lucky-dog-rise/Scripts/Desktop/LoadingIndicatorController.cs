using Godot;

namespace LuckyDogRise;

/// <summary>
/// Small platform-loading indicator. The ring keeps moving without a hard stop,
/// while the Steam mark breathes more slowly so the state reads as waiting, not disabled.
/// </summary>
public partial class LoadingIndicatorController : Control
{
    [Export] private TextureRect _rotatingRing = null!;
    [Export] private TextureRect _steamLogo = null!;

    private double _elapsedSeconds;

    public override void _Ready()
    {
        if (_rotatingRing == null || _steamLogo == null)
        {
            GD.PushError("[LoadingIndicator] Scene bindings are missing.");
            SetProcess(false);
            return;
        }

        Resized += UpdatePivots;
        UpdatePivots();
        SetLoading(Visible);
    }

    public override void _Process(double delta)
    {
        _elapsedSeconds += delta;

        const double ringPeriodSeconds = 1.4;
        const float speedVariation = 0.38f;
        var ringPhase = Mathf.Tau * (float)(_elapsedSeconds / ringPeriodSeconds);
        // Its derivative is always positive, so each revolution has a natural rhythm without pausing.
        _rotatingRing.Rotation = ringPhase + speedVariation * Mathf.Sin(ringPhase);

        const double logoBreathPeriodSeconds = 1.4;
        var logoPhase = Mathf.Tau * (float)(_elapsedSeconds / logoBreathPeriodSeconds) - Mathf.Pi / 2f;
        var breath = 0.5f + 0.5f * Mathf.Sin(logoPhase);
        _steamLogo.Modulate = new Color(0.09803922f, 0.6f, 1f, Mathf.Lerp(0.25f, 1f, breath));
    }

    public void SetLoading(bool loading)
    {
        if (_rotatingRing == null || _steamLogo == null)
        {
            Visible = false;
            SetProcess(false);
            return;
        }

        Visible = loading;
        SetProcess(loading);
        if (loading)
            _elapsedSeconds = 0.0;
    }

    private void UpdatePivots()
    {
        _rotatingRing.PivotOffset = _rotatingRing.Size * 0.5f;
        _steamLogo.PivotOffset = _steamLogo.Size * 0.5f;
    }
}
