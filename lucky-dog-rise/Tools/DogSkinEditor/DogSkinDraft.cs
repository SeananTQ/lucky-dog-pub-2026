using System;
using System.Collections.Generic;
using DataTables;

namespace LuckyDogRise.Tools;

public sealed class DogSkinCatalogDraft
{
    public int Version { get; set; } = 1;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<DogSkinDraft> DogSkins { get; set; } = new();
}

public sealed class DogSkinDraft
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

    public static DogSkinDraft FromDogSkin(DogSkin skin)
    {
        return new DogSkinDraft
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

    public DogSkinDraft CloneWithId(int id)
    {
        return new DogSkinDraft
        {
            Id = id,
            IconName = IconName,
            DefaultEars = DefaultEars,
            DefaultEyes = DefaultEyes,
            DefaultTongue = DefaultTongue,
            FixedEyewear = FixedEyewear,
            FolderPath = FolderPath,
            Head = Head,
            ClawLeftBack = ClawLeftBack,
            ClawRightPalms = ClawRightPalms,
            TongueRegular = TongueRegular,
            EarsHappy = EarsHappy,
            EarsPlane = EarsPlane,
            EyesBored = EyesBored,
            EyesCute = EyesCute,
            EyesHappy = EyesHappy,
            EyesLucky = EyesLucky,
            EyesNeutral = EyesNeutral,
            EyesWink = EyesWink,
        };
    }

    public DogAppearanceSpec ToAppearanceSpec()
    {
        return new DogAppearanceSpec
        {
            Id = Id,
            IconName = IconName,
            DefaultEars = DefaultEars,
            DefaultEyes = DefaultEyes,
            DefaultTongue = DefaultTongue,
            FixedEyewear = FixedEyewear,
            FolderPath = FolderPath,
            Head = Head,
            ClawLeftBack = ClawLeftBack,
            ClawRightPalms = ClawRightPalms,
            TongueRegular = TongueRegular,
            EarsHappy = EarsHappy,
            EarsPlane = EarsPlane,
            EyesBored = EyesBored,
            EyesCute = EyesCute,
            EyesHappy = EyesHappy,
            EyesLucky = EyesLucky,
            EyesNeutral = EyesNeutral,
            EyesWink = EyesWink,
        };
    }
}
