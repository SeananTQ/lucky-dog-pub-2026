using System;
using System.Linq;
using Godot;

namespace LuckyDogRise;

public static class WishlistCallToAction
{
    private static string SteamStoreUri => $"steam://store/{BuildInfo.ReleaseSteamAppId}";

    public static int CooldownSeconds => Math.Max(
        0,
        LubanData.Tables.TbGameDevelopConfig.DataList.FirstOrDefault()
            ?.WishlistCallToActionDialogCooldownSeconds ?? 0);

    public static int GetShyRequestFontSize(string locale)
    {
        return locale switch
        {
            L10n.SimplifiedChineseLocale => 14,
            L10n.TraditionalChineseLocale => 14,
            L10n.KoreanLocale => 13,
            _ => 12,
        };
    }

    public static Error OpenConfiguredUrl(string source)
    {
        var url = LubanData.Tables.TbGameDevelopConfig.DataList.FirstOrDefault()
            ?.WishlistCallToActionUrl;

        var steamResult = OS.ShellOpen(SteamStoreUri);
        if (steamResult == Error.Ok)
        {
            GD.Print($"[WishlistCallToAction] Opened {source} in Steam client: {SteamStoreUri}");
            return Error.Ok;
        }

        GD.PushWarning(
            $"[WishlistCallToAction] Failed to open {source} in Steam client: " +
            $"{SteamStoreUri} ({steamResult}). Falling back to the web store.");

        if (string.IsNullOrWhiteSpace(url))
        {
            GD.PushWarning(
                $"[WishlistCallToAction] {source} clicked, but no fallback URL has been configured.");
            return Error.Unconfigured;
        }

        var webResult = OS.ShellOpen(url);
        if (webResult == Error.Ok)
            GD.Print($"[WishlistCallToAction] Opened {source} in web browser: {url}");
        else
            GD.PushWarning($"[WishlistCallToAction] Failed to open {source}: {url} ({webResult}).");
        return webResult;
    }
}
