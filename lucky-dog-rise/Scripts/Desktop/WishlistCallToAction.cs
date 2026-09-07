using System;
using System.Linq;
using Godot;

namespace LuckyDogRise;

public static class WishlistCallToAction
{
    public static int CooldownSeconds => Math.Max(
        0,
        LubanData.Tables.TbGameDevelopConfig.DataList.FirstOrDefault()
            ?.WishlistCallToActionDialogCooldownSeconds ?? 0);

    public static Error OpenConfiguredUrl(string source)
    {
        var url = LubanData.Tables.TbGameDevelopConfig.DataList.FirstOrDefault()
            ?.WishlistCallToActionUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            GD.PushWarning($"[WishlistCallToAction] {source} clicked, but no URL has been configured.");
            return Error.Unconfigured;
        }

        var result = OS.ShellOpen(url);
        if (result == Error.Ok)
            GD.Print($"[WishlistCallToAction] Opened {source}: {url}");
        else
            GD.PushWarning($"[WishlistCallToAction] Failed to open {source}: {url} ({result}).");
        return result;
    }
}
