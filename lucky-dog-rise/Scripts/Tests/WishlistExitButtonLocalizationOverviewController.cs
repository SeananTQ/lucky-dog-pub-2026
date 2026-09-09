using Godot;

namespace LuckyDogRise;

public partial class WishlistExitButtonLocalizationOverviewController : ColorRect
{
    private const int WindowWidth = 850;
    private const int WindowHeight = 620;

    private static readonly string[] Locales =
    [
        L10n.EnglishLocale,
        L10n.SimplifiedChineseLocale,
        L10n.TraditionalChineseLocale,
        L10n.JapaneseLocale,
        L10n.SpanishSpainLocale,
        L10n.SpanishLatinAmericaLocale,
        L10n.PortugueseBrazilLocale,
        L10n.PortuguesePortugalLocale,
        L10n.FrenchLocale,
        L10n.GermanLocale,
        L10n.DanishLocale,
        L10n.IndonesianLocale,
        L10n.NorwegianLocale,
        L10n.SwedishLocale,
        L10n.DutchLocale,
        L10n.VietnameseLocale,
        L10n.MalayLocale,
        L10n.KoreanLocale,
        L10n.RussianLocale,
        L10n.UkrainianLocale,
    ];

    [Export] private GridContainer _previewGrid = null!;
    [Export] private Control _previewTemplate = null!;

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
            L10n.SetLocale(locale, save: false);

            var preview = (Control)_previewTemplate.Duplicate();
            preview.Name = $"Preview_{locale}";
            preview.Visible = true;
            preview.GetNode<Label>("LocaleCode").Text = locale;
            preview.GetNode<Button>("ExitButton").Text =
                L10n.Tr(L10nKey.WishlistCallToAction_ExitAndOpenSteamButton);
            _previewGrid.AddChild(preview);
        }

        L10n.SetLocale(originalLocale, save: false);
    }
}
