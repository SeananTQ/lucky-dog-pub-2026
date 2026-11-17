#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using Godot;

namespace LuckyDogRise.Rooms;

// Persistent preview of the same localized recovery overlay used by the live poker table.
public partial class PokerConnectionOverlayLab : Control
{
    [Export] private RoomPokerConnectionOverlay _connectionOverlay;
    [Export] private DogVisual _dog;
    [Export] private OptionButton _language;
    [Export] private Label _title;
    [Export] private Button _close;
    [Export] private Control _previewArea;
    [Export] private SubViewportContainer _preview;

    public override void _Ready()
    {
        if (BuildInfo.Channel != BuildChannel.Dev || BuildInfo.IsDebugDemo || OS.HasFeature("lucky_demo"))
        {
            GetTree().Quit(1);
            return;
        }

        GetViewport().TransparentBg = false;
        var window = GetWindow();
        window.Title = "小狗来啦 · 牌局交接遮罩预览";
        window.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        window.ContentScaleSize = Vector2I.Zero;
        window.Mode = Window.ModeEnum.Windowed;
        var screen = DisplayServer.ScreenGetUsableRect(window.CurrentScreen);
        // Leave room below the preview for feedback, without changing project window settings.
        window.Size = new Vector2I(648, Mathf.Clamp(screen.Size.Y - 200, 480, 720));
        window.Position = screen.Position + new Vector2I(Mathf.Max(0, (screen.Size.X - window.Size.X) / 2), 16);

        _close.Pressed += () => GetTree().Quit();
        _title.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                DisplayServer.WindowStartDrag();
        };
        _previewArea.Resized += FitPreview;
        _language.ItemSelected += SelectLanguage;
        string locale = TranslationServer.GetLocale();
        _language.Select(locale.StartsWith("zh") ? 0 : locale.StartsWith("ru") ? 2 : locale.StartsWith("fr") ? 3 : 1);
        _dog.SetPreviewAppearance(DogAppearanceSpec.FromDogSkin(LubanData.Tables.TbDogSkin.Get(1001)));
        SelectLanguage(_language.Selected);
        Callable.From(FitPreview).CallDeferred();
    }

    private void SelectLanguage(long index)
    {
        string locale = index switch { 0 => "zh_CN", 2 => "ru", 3 => "fr", _ => "en" };
        L10n.SetLocale(locale, save: false);
        _title.Text = index == 0 ? "牌局交接遮罩预览" : "Host transfer preview";
        _close.TooltipText = index == 0 ? "关闭预览" : "Close preview";
        _connectionOverlay.ShowWaiting(migrating: true);
    }

    private void FitPreview()
    {
        // Match the live 1200px poker SubViewport, scaling its entire image as one square.
        float edge = Mathf.Max(1, Mathf.Min(_previewArea.Size.X, _previewArea.Size.Y));
        _preview.Scale = Vector2.One * edge / 1200f;
        _preview.Position = (_previewArea.Size - Vector2.One * edge) / 2;
    }
}
#endif
