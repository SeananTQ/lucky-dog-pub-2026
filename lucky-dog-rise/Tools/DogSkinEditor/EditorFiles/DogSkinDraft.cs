using System;
using System.Collections.Generic;
using DataTables;

namespace LuckyDogRise.Tools;

public sealed class DogSkinCatalogDraft
{
    public int Version { get; set; } = 3;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<DogSkinDraft> DogSkins { get; set; } = new();
}

public sealed class DogSkinDraft
{
    public int Id { get; set; }
    public string Alias { get; set; } = "";
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

    public static DogSkinDraft FromDogSkin(DogSkin skin)
    {
        return new DogSkinDraft
        {
            Id = skin.Id,
            Alias = skin.Alias,
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

    public DogSkinDraft CloneWithId(int id)
    {
        return new DogSkinDraft
        {
            Id = id,
            Alias = Alias,
            IconName = IconName,
            DefaultEars = DefaultEars,
            DefaultEyes = DefaultEyes,
            DefaultTongue = DefaultTongue,
            FixedEyewear = FixedEyewear,
            FolderPath = FolderPath,
            Head = Head,
            DefaultNose = DefaultNose,
            DefaultMouse = DefaultMouse,
            MouseSilent = MouseSilent,
            ClawLeftBack = ClawLeftBack,
            ClawRightPalms = ClawRightPalms,
            TongueRegular = TongueRegular,
            EarsNormal = EarsNormal,
            EarsDrooped = EarsDrooped,
            EyesBored = EyesBored,
            EyesNormalMood = EyesNormalMood,
            EyesHappy = EyesHappy,
            EyesAdmiring = EyesAdmiring,
            EyesStunned = EyesStunned,
            EyesSignaling = EyesSignaling,
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
            DefaultNose = DefaultNose,
            DefaultMouse = DefaultMouse,
            MouseSilent = MouseSilent,
            ClawLeftBack = ClawLeftBack,
            ClawRightPalms = ClawRightPalms,
            TongueRegular = TongueRegular,
            EarsNormal = EarsNormal,
            EarsDrooped = EarsDrooped,
            EyesBored = EyesBored,
            EyesNormalMood = EyesNormalMood,
            EyesHappy = EyesHappy,
            EyesAdmiring = EyesAdmiring,
            EyesStunned = EyesStunned,
            EyesSignaling = EyesSignaling,
        };
    }
}
