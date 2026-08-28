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
    public string Head { get; set; } = "";
    public string ClawLeftBack { get; set; } = "";
    public string ClawRightPalms { get; set; } = "";
    public string TongueRegular { get; set; } = "";
    public string EarsHappy { get; set; } = "";
    public string EarsPlane { get; set; } = "";
    public string EyesBored { get; set; } = "";
    public string EyesCute { get; set; } = "";
    public string EyesHappy { get; set; } = "";
    public string EyesLucky { get; set; } = "";
    public string EyesNeutral { get; set; } = "";
    public string EyesWink { get; set; } = "";

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
            ClawLeftBack = skin.ClawLeftBack,
            ClawRightPalms = skin.ClawRightPalms,
            TongueRegular = skin.TongueRegular,
            EarsHappy = skin.EarsHappy,
            EarsPlane = skin.EarsPlane,
            EyesBored = skin.EyesBored,
            EyesCute = skin.EyesCute,
            EyesHappy = skin.EyesHappy,
            EyesLucky = skin.EyesLucky,
            EyesNeutral = skin.EyesNeutral,
            EyesWink = skin.EyesWink,
        };
    }
}
