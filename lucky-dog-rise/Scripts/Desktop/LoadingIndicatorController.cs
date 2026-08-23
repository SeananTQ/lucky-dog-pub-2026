#nullable enable

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
    private bool _loadingStateInitialized;
    private bool _isLoading;
    private DpiTexture? _rotatingRingDpiTexture;
    private DpiTexture? _steamLogoDpiTexture;

    public override void _Ready()
    {
        if (_rotatingRing == null || _steamLogo == null)
        {
            GD.PushError("[LoadingIndicator] Scene bindings are missing.");
            SetProcess(false);
            return;
        }

        _rotatingRingDpiTexture = MakeLocalDpiTexture(_rotatingRing);
        _steamLogoDpiTexture = MakeLocalDpiTexture(_steamLogo);
        Resized += UpdatePivots;
        UpdatePivots();
        SetLoading(Visible);
    }

    public void SetRenderScale(float scale)
    {
        var rasterScale = Mathf.Max(1f, scale);
        if (_rotatingRingDpiTexture != null)
            _rotatingRingDpiTexture.BaseScale = rasterScale;
        if (_steamLogoDpiTexture != null)
            _steamLogoDpiTexture.BaseScale = rasterScale;
    }

    private static DpiTexture? MakeLocalDpiTexture(TextureRect textureRect)
    {
        if (textureRect.Texture is not DpiTexture source)
            return null;

        var local = (DpiTexture)source.Duplicate();
        textureRect.Texture = local;
        return local;
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

        // UI state refreshes may repeat while the same platform request is pending.
        // Restart the animation only on an actual hidden -> loading transition.
        if (_loadingStateInitialized && _isLoading == loading)
            return;

        _loadingStateInitialized = true;
        _isLoading = loading;
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
