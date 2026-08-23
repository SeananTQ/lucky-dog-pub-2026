#nullable enable

using System;
using System.Collections.Generic;
using DataTables;
using Godot;

namespace LuckyDogRise;

public partial class BalloonHintController : PanelContainer
{
#if DEBUG
    public static bool ShowPaymentSourceLabels { get; set; }
#endif

    public enum TailSide
    {
        Left,
        Right,
    }

    [Signal] public delegate void PressedEventHandler();

    [Export] private TextureRect _iconRect = null!;
    [Export] private RichTextLabel _textLabel = null!;
    [Export] private ColorRect _strikeLine = null!;
    [Export] private LoadingIndicatorController _loadingIndicator = null!;
    [Export] private Polygon2D _tail = null!;
    [Export] public TailSide TailPlacement { get; set; } = TailSide.Left;
    [Export] public float TailInset { get; set; } = 16f;

    private Tween? _flashTween;
    private Tween? _visibilityTween;
    private string _currentTextBbcode = string.Empty;
    private readonly Color _normalTextColor = new(0.40784314f, 0.22745098f, 0.19607843f);
    private readonly Color _warningTextColor = new(0.90588236f, 0.54901963f, 0.627451f); // #E78CA0
    private bool _isDisplayVisible = true;
    private bool _interactionEnabled = true;
    private bool _showingLoading;
    private string _strikeText = string.Empty;
    private Font? _sourceTextFont;
    private readonly Dictionary<int, Font> _oversampledTextFonts = new();
    private StyleBoxFlat? _panelStyle;
    private float _panelBaseAntiAliasingSize = 1f;

    public override void _Ready()
    {
        _sourceTextFont = _textLabel.GetThemeFont("normal_font");
        CapturePanelStyle();
        MouseFilter = MouseFilterEnum.Stop;
        _iconRect.Visible = false;
        _loadingIndicator.SetLoading(false);
        HideStrikeLine();
        SetTextContent(string.Empty);
        UpdateTail();
        PivotOffset = Size * 0.5f;
    }

    /// <summary>
    /// CanvasLayer scaling does not automatically increase dynamic-font rasterization resolution.
    /// Give this balloon its own oversampled font copy so enlarged countdown/cost text stays crisp.
    /// </summary>
    public void SetRenderScale(float scale)
    {
        _sourceTextFont ??= _textLabel.GetThemeFont("normal_font");
        ApplyPanelRenderScale(scale);
        _loadingIndicator.SetRenderScale(scale);
        var oversampling = Math.Max(1, Mathf.CeilToInt(scale));
        if (!_oversampledTextFonts.TryGetValue(oversampling, out var font))
        {
            font = DuplicateFontForOversampling(_sourceTextFont, oversampling);
            _oversampledTextFonts[oversampling] = font;
        }

        _textLabel.AddThemeFontOverride("normal_font", font);
        UpdateStrikeLine();
    }

    private void CapturePanelStyle()
    {
        if (GetThemeStylebox("panel") is not StyleBoxFlat style)
            return;

        _panelStyle = (StyleBoxFlat)style.Duplicate();
        _panelBaseAntiAliasingSize = _panelStyle.AntiAliasingSize;
        AddThemeStyleboxOverride("panel", _panelStyle);
    }

    private void ApplyPanelRenderScale(float scale)
    {
        if (_panelStyle == null)
            return;

        // The CanvasLayer transform also scales StyleBoxFlat's antialiasing ring.
        // Keep that ring at one physical pixel instead of letting it become 3-4 px blur.
        _panelStyle.AntiAliasingSize = _panelBaseAntiAliasingSize / Mathf.Max(0.01f, scale);
    }

    private static Font DuplicateFontForOversampling(Font source, float oversampling)
    {
        var copy = (Font)source.Duplicate();
        if (copy is FontFile fontFile)
            fontFile.Oversampling = oversampling;
        else if (copy is SystemFont systemFont)
            systemFont.Oversampling = oversampling;

        if (copy is FontVariation variation && variation.BaseFont != null)
            variation.BaseFont = DuplicateFontForOversampling(variation.BaseFont, oversampling);

        if (source.Fallbacks.Count > 0)
        {
            var fallbacks = new Godot.Collections.Array<Font>();
            foreach (var fallback in source.Fallbacks)
                fallbacks.Add(DuplicateFontForOversampling(fallback, oversampling));
            copy.Fallbacks = fallbacks;
        }

        return copy;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            UpdateTail();
            UpdateStrikeLine();
            PivotOffset = Size * 0.5f;
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_interactionEnabled
            && @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            EmitSignal(SignalName.Pressed);
    }

    public void ShowCountdown(TimeSpan remaining)
    {
        StopLoading();
        HideStrikeLine();
        _iconRect.Visible = false;
        _textLabel.Visible = true;
        SetTextContent($"[font_size=20]{Math.Max(0, (int)remaining.TotalMinutes):00}:{remaining.Seconds:00}[/font_size]");
    }

    public void ShowCost(Texture2D? icon, int cost, int currentChips = -1)
    {
        StopLoading();
        HideStrikeLine();
        _iconRect.Texture = icon;
        _iconRect.Visible = icon != null;
        _textLabel.Visible = true;
        var isInsufficient = currentChips >= 0 && currentChips < cost;
        var text = isInsufficient
            ? $"[font_size=20]{currentChips}[/font_size][font_size=10]/{cost}[/font_size]"
            : $"[font_size=20]{cost}[/font_size]";
        SetTextContent(text);
    }

    public void ShowValueFromAssetPath(
        string? iconPath,
        Texture2D? fallbackIcon,
        EBlindBoxValueMode valueMode,
        int cost,
        int currentChips = -1,
        BlindBoxPaymentSource paymentSource = BlindBoxPaymentSource.Unknown,
        bool strikeThrough = false)
    {
        StopLoading();
        var icon = LoadAssetTexture(iconPath) ?? fallbackIcon;
        if (valueMode == EBlindBoxValueMode.Chips)
        {
            if (strikeThrough)
            {
                _iconRect.Texture = icon;
                _iconRect.Visible = icon != null;
                _textLabel.Visible = true;
                SetTextContent($"[font_size=20]{cost}[/font_size]");
                ShowStrikeLine(cost.ToString());
            }
            else
            {
                ShowCost(icon, cost, currentChips);
            }
#if DEBUG
            AddPaymentSourceLabel(paymentSource);
#endif
            return;
        }

        _iconRect.Texture = icon;
        _iconRect.Visible = icon != null;
        _textLabel.Visible = true;
        HideStrikeLine();
        SetTextContent(valueMode switch
        {
            EBlindBoxValueMode.Count => "[font_size=20]×1[/font_size]",
            EBlindBoxValueMode.Free => $"[font_size=14]{L10n.Tr(L10nKey.BlindBox_Free)}[/font_size]",
            _ => $"[font_size=20]{cost}[/font_size]",
        });
#if DEBUG
        AddPaymentSourceLabel(paymentSource);
#endif
    }

#if DEBUG
    private void AddPaymentSourceLabel(BlindBoxPaymentSource paymentSource)
    {
        if (!ShowPaymentSourceLabels)
            return;

        var label = paymentSource switch
        {
            BlindBoxPaymentSource.Chips => "CHIPS",
            BlindBoxPaymentSource.LocalRefreshment => "LOCAL",
            BlindBoxPaymentSource.SteamPrepared => "STEAM",
            BlindBoxPaymentSource.SteamLate => "STEAM",
            BlindBoxPaymentSource.SteamFallback => "FALLBACK",
            _ => string.Empty,
        };
        if (!string.IsNullOrEmpty(label))
            SetTextContent($"[font_size=9]{label}[/font_size]\n{_currentTextBbcode}");
    }
#endif

    public void ShowIconOnly(Texture2D? icon)
    {
        StopLoading();
        HideStrikeLine();
        _iconRect.Texture = icon;
        _iconRect.Visible = icon != null;
        _textLabel.Visible = false;
        SetTextContent(string.Empty);
    }

    public void ShowLoading()
    {
        if (_showingLoading)
        {
            _interactionEnabled = false;
            return;
        }

        _showingLoading = true;
        HideStrikeLine();
        _iconRect.Visible = false;
        _textLabel.Visible = false;
        SetTextContent(string.Empty);
        _loadingIndicator.SetLoading(true);
        _interactionEnabled = false;
    }

    private void ShowStrikeLine(string text)
    {
        _strikeText = text;
        _strikeLine.Visible = true;
        CallDeferred(MethodName.UpdateStrikeLine);
    }

    private void HideStrikeLine()
    {
        _strikeText = string.Empty;
        _strikeLine.Visible = false;
    }

    private void UpdateStrikeLine()
    {
        if (_strikeLine == null || !_strikeLine.Visible || string.IsNullOrEmpty(_strikeText))
            return;

        const int fontSize = 20;
        var font = _textLabel.GetThemeFont("normal_font");
        var textWidth = Math.Max(1f, font.GetStringSize(_strikeText, fontSize: fontSize).X);
        var labelPosition = _textLabel.Position;
        if (_textLabel.GetParent() is Control labelParent)
            labelPosition += labelParent.Position;
        var x = labelPosition.X + Math.Max(0f, (_textLabel.Size.X - textWidth) * 0.5f);
        var y = labelPosition.Y + Math.Max(0f, _textLabel.Size.Y - fontSize * 0.55f) - 2f;
        _strikeLine.Position = new Vector2(x, y);
        _strikeLine.Size = new Vector2(textWidth, 2f);
    }

    public void SetInteractionEnabled(bool enabled)
    {
        _interactionEnabled = enabled;
    }

    public void FlashTextRed()
    {
        _flashTween?.Kill();
        _flashTween = CreateTween();
        for (var i = 0; i < 2; i++)
        {
            _flashTween.TweenCallback(Callable.From(() => SetTextColor(_warningTextColor)));
            _flashTween.TweenInterval(0.12);
            _flashTween.TweenCallback(Callable.From(ResetTextColor));
            _flashTween.TweenInterval(0.12);
        }
    }

    public void SetDisplayVisible(bool visible, bool animate = true)
    {
        if (_isDisplayVisible == visible && animate)
            return;

        _isDisplayVisible = visible;
        MouseFilter = visible ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        _visibilityTween?.Kill();

        if (!animate || !IsInsideTree())
        {
            Modulate = Colors.White with { A = visible ? 1f : 0f };
            Scale = visible ? Vector2.One : new Vector2(0.98f, 0.98f);
            return;
        }

        if (visible)
        {
            Modulate = Colors.White with { A = 0f };
            Scale = new Vector2(0.96f, 0.96f);
            _visibilityTween = CreateTween();
            _visibilityTween.SetEase(Tween.EaseType.Out);
            _visibilityTween.SetTrans(Tween.TransitionType.Back);
            _visibilityTween.TweenProperty(this, "scale", Vector2.One, 0.16);
            _visibilityTween.Parallel().TweenProperty(this, "modulate:a", 1f, 0.12);
        }
        else
        {
            _visibilityTween = CreateTween();
            _visibilityTween.SetEase(Tween.EaseType.Out);
            _visibilityTween.SetTrans(Tween.TransitionType.Quad);
            _visibilityTween.TweenProperty(this, "scale", new Vector2(0.98f, 0.98f), 0.1);
            _visibilityTween.Parallel().TweenProperty(this, "modulate:a", 0f, 0.1);
        }
    }

    private void ResetTextColor()
    {
        SetTextColor(_normalTextColor);
    }

    private void SetTextColor(Color color)
    {
        _textLabel.Text = $"[color=#{color.ToHtml()}]{_currentTextBbcode}[/color]";
    }

    private void SetTextContent(string bbcode)
    {
        _currentTextBbcode = bbcode;
        ResetTextColor();
    }

    private void StopLoading()
    {
        _showingLoading = false;
        _loadingIndicator.SetLoading(false);
        CustomMinimumSize = new Vector2(120, 54);
        _textLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        _interactionEnabled = true;
    }

    private static Texture2D? LoadAssetTexture(string? lubanPath)
    {
        if (string.IsNullOrWhiteSpace(lubanPath))
            return null;

        var path = "res://Assets/" + lubanPath.Replace('\\', '/');
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    public static Texture2D? LoadHintTexture(string? lubanPath) => LoadAssetTexture(lubanPath);

    private void UpdateTail()
    {
        if (_tail == null)
            return;

        var x = TailPlacement == TailSide.Left
            ? TailInset
            : Mathf.Max(TailInset, Size.X - TailInset - 18f);
        _tail.Position = new Vector2(x, Size.Y - 2f);
        _tail.Polygon = TailPlacement == TailSide.Left
            ? [new Vector2(0, 0), new Vector2(18, 0), new Vector2(0, 16)]
            : [new Vector2(0, 0), new Vector2(18, 0), new Vector2(18, 16)];
    }
}
