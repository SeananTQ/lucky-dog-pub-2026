using DataTables;

namespace LuckyDogRise;

/// <summary>
/// Mutable, renderer-facing dog appearance data shared by runtime DogSkin rows
/// and development tools. It deliberately contains presentation fields only.
/// </summary>
public sealed class DogAppearanceSpec
{
    public int Id { get; set; }
    public string IconName { get; set; } = "";
    public string DefaultEars { get; set; } = "";
    public string DefaultEyes { get; set; } = "";
    public string DefaultTongue { get; set; } = "";
    public string FixedEyewear { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public string DefaultNose { get; set; } = "";
    public string DefaultMouse { get; set; } = "";
    public string MouseSilent { get; set; } = "";
    public string Head { get; set; } = "";
    public string ClawLeftBack { get; set; } = "";
    public string ClawRightPalms { get; set; } = "";
    public string TongueRegular { get; set; } = "";
    public string EarsNormal { get; set; } = "";
    public string EarsDrooped { get; set; } = "";
    public string EyesBored { get; set; } = "";
    public string EyesNormalMood { get; set; } = "";
    public string EyesHappy { get; set; } = "";
    public string EyesAdmiring { get; set; } = "";
    public string EyesStunned { get; set; } = "";
    public string EyesSignaling { get; set; } = "";

    public static DogAppearanceSpec FromDogSkin(DogSkin skin)
    {
        return new DogAppearanceSpec
        {
            Id = skin.Id,
            IconName = skin.IconName,
            DefaultEars = skin.DefaultEars,
            DefaultEyes = skin.DefaultEyes,
            DefaultTongue = skin.DefaultTongue,
            FixedEyewear = skin.FixedEyewear,
            FolderPath = skin.FolderPath,
            Head = skin.Head,
            DefaultNose = skin.DefaultNose,
            DefaultMouse = skin.DefaultMouse,
            MouseSilent = skin.MouseSilent,
            ClawLeftBack = skin.ClawLeftBack,
            ClawRightPalms = skin.ClawRightPalms,
            TongueRegular = skin.TongueRegular,
            EarsNormal = skin.EarsNormal,
            EarsDrooped = skin.EarsDrooped,
            EyesBored = skin.EyesBored,
            EyesNormalMood = skin.EyesNormalMood,
            EyesHappy = skin.EyesHappy,
            EyesAdmiring = skin.EyesAdmiring,
            EyesStunned = skin.EyesStunned,
            EyesSignaling = skin.EyesSignaling,
        };
    }
}
