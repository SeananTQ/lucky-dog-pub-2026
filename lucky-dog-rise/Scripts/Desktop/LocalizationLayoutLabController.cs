using Godot;

public partial class LocalizationLayoutLabController : Control
{
    private static readonly Color DropDownNormal = Color.FromHtml("#1c464f");
    private static readonly Color DropDownHover = Color.FromHtml("#235763");

    public override void _Ready()
    {
        foreach (var node in FindChildren("*", "OptionButton", true, false))
        {
            if (node is OptionButton optionButton)
            {
                StyleOptionButton(optionButton);
            }
        }
    }

    private static void StyleOptionButton(OptionButton optionButton)
    {
        var widePopup = optionButton.Name == "LanguageOption";
        // optionButton.CustomMinimumSize = new Vector2(widePopup ? 268 : 172, 28);
        optionButton.AddThemeFontSizeOverride("font_size", 15);
        optionButton.AddThemeConstantOverride("h_separation", 6);

        optionButton.AddThemeStyleboxOverride("normal", CreateOptionStyle(DropDownNormal));
        optionButton.AddThemeStyleboxOverride("hover", CreateOptionStyle(DropDownHover));
        optionButton.AddThemeStyleboxOverride("pressed", CreateOptionStyle(DropDownHover));
        optionButton.AddThemeStyleboxOverride("hover_pressed", CreateOptionStyle(DropDownHover));
        optionButton.AddThemeStyleboxOverride("focus", CreateOptionStyle(DropDownHover));
        optionButton.AddThemeStyleboxOverride("disabled", CreateOptionStyle(DropDownNormal));

        var popup = optionButton.GetPopup();
        popup.AddThemeStyleboxOverride("panel", CreatePopupPanelStyle());
        popup.AddThemeStyleboxOverride("hover", CreatePopupItemStyle(DropDownHover));
        popup.AddThemeFontSizeOverride("font_size", 15);
        popup.AddThemeConstantOverride("v_separation", 2);
        popup.AddThemeConstantOverride("item_start_padding", 8);
        popup.AddThemeConstantOverride("item_end_padding", 8);
    }

    private static StyleBoxFlat CreateOptionStyle(Color color)
    {
        return new StyleBoxFlat
        {
            BgColor = color,
            ContentMarginLeft = 10,
            ContentMarginTop = 2,
            ContentMarginRight = 22,
            ContentMarginBottom = 2,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomRight = 5,
            CornerRadiusBottomLeft = 5,
        };
    }

    private static StyleBoxFlat CreatePopupPanelStyle()
    {
        return new StyleBoxFlat
        {
            BgColor = DropDownNormal,
            ContentMarginLeft = 4,
            ContentMarginTop = 4,
            ContentMarginRight = 4,
            ContentMarginBottom = 4,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomRight = 5,
            CornerRadiusBottomLeft = 5,
        };
    }

    private static StyleBoxFlat CreatePopupItemStyle(Color color)
    {
        return new StyleBoxFlat
        {
            BgColor = color,
            ContentMarginLeft = 4,
            ContentMarginTop = 2,
            ContentMarginRight = 4,
            ContentMarginBottom = 2,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomRight = 4,
            CornerRadiusBottomLeft = 4,
        };
    }
}
