using Godot;

namespace LuckyDogRise;

public partial class WishlistLocalizationOverviewController : ColorRect
{
    private const int WindowWidth = 1050;
    private const int WindowHeight = 620;

    private static readonly LocalePreview[] Locales =
    [
        new(L10n.EnglishLocale, 12),
        new(L10n.SimplifiedChineseLocale, 14),
        new(L10n.TraditionalChineseLocale, 14),
        new(L10n.JapaneseLocale, 12),
        new(L10n.SpanishSpainLocale, 12),
        new(L10n.SpanishLatinAmericaLocale, 12),
        new(L10n.PortugueseBrazilLocale, 12),
        new(L10n.PortuguesePortugalLocale, 12),
        new(L10n.FrenchLocale, 12),
        new(L10n.GermanLocale, 12),
        new(L10n.DanishLocale, 12),
        new(L10n.IndonesianLocale, 12),
        new(L10n.NorwegianLocale, 12),
        new(L10n.SwedishLocale, 12),
        new(L10n.DutchLocale, 12),
        new(L10n.VietnameseLocale, 12),
        new(L10n.MalayLocale, 12),
        new(L10n.KoreanLocale, 13),
        new(L10n.RussianLocale, 12),
        new(L10n.UkrainianLocale, 12),
    ];

    [Export] private GridContainer _previewGrid = null!;
    [Export] private VBoxContainer _previewTemplate = null!;

    public override void _Ready()
    {
        ConfigureWindow();
        PopulatePreviews();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
            GetTree().Quit();
    }

    private void ConfigureWindow()
    {
        var size = new Vector2I(WindowWidth, WindowHeight);
        DisplayServer.WindowSetSize(size);
        var usableRect = DisplayServer.ScreenGetUsableRect();
        DisplayServer.WindowSetPosition(usableRect.Position + (usableRect.Size - size) / 2);
        RenderingServer.SetDefaultClearColor(Color);
    }

    private void PopulatePreviews()
    {
        var originalLocale = L10n.CurrentLocale;
        foreach (var locale in Locales)
        {
            L10n.SetLocale(locale.Code, save: false);

            var preview = (VBoxContainer)_previewTemplate.Duplicate();
            preview.Name = $"Preview_{locale.Code}";
            preview.Visible = true;
            preview.GetNode<Label>("LocaleCode").Text = locale.Code;

            var message = preview.GetNode<Label>("Balloon/CopyMargins/Message");
            message.AddThemeFontSizeOverride("font_size", locale.FontSize);
            message.Text = L10n.Tr(L10nKey.CollectionEvent_WishlistShyRequest);
            _previewGrid.AddChild(preview);
        }

        L10n.SetLocale(originalLocale, save: false);
    }

    private readonly record struct LocalePreview(string Code, int FontSize);
}
