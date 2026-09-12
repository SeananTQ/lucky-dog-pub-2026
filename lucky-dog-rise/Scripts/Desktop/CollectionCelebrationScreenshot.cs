using Godot;
using System;
using System.Threading.Tasks;

namespace LuckyDogRise;

public static class CollectionCelebrationScreenshot
{
    private const float PhotoBorderShortSideRatio = 0.025f;
    private static readonly Color PhotoPaperColor = new("f2efe7");

    internal static Image AddPhotoBorder(Image source)
    {
        var width = source.GetWidth();
        var height = source.GetHeight();
        var border = Math.Max(1, Mathf.RoundToInt(Math.Min(width, height) * PhotoBorderShortSideRatio));
        var photo = Image.CreateEmpty(width + border * 2, height + border * 2, false, Image.Format.Rgb8);
        photo.Fill(PhotoPaperColor);
        photo.BlitRect(source, new Rect2I(0, 0, width, height), new Vector2I(border, border));
        return photo;
    }

    // Freeze only the visual branches. No scripts, signals, account state, or reward logic
    // are copied, and the live page is never reparented, resized, scrolled, or hidden.
    internal static Control CreateSnapshot(Control page)
    {
        var stage = page.GetNode<Control>("GrandPrizeStage");
        var wish = page.GetNode<Control>("WishlistCallToActionModule");
        var bottom = Math.Max(wish.GetNode<Control>("ShyDog").GetRect().End.Y,
            wish.GetNode<Control>("Balloon").GetRect().End.Y) + wish.Position.Y;
        var size = new Vector2(page.Size.X, bottom - stage.Position.Y);
        if (size.X <= 0 || size.Y <= 0)
            throw new InvalidOperationException("Celebration layout is not ready.");
        var snapshot = new Control { Name = "CelebrationSnapshot", Size = size,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        for (Node ancestor = page; ancestor != null; ancestor = ancestor.GetParent())
            if (ancestor is Control control && control.Theme != null)
            {
                snapshot.Theme = control.Theme;
                break;
            }
        snapshot.Theme ??= GD.Load<Theme>("res://Themes/DefaultTheme.tres");
        snapshot.AddChild(new ColorRect { Color = new Color("1b272a"), Size = size,
            MouseFilter = Control.MouseFilterEnum.Ignore });
        foreach (var path in new[] { "GrandPrizeStage", "CollectionGrid", "VictoryRewardMargins", "WishlistCallToActionModule" })
        {
            var source = page.GetNode<Control>(path);
            var copy = (Control)source.Duplicate(0);
            snapshot.AddChild(copy);
            copy.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            copy.Position = source.Position - new Vector2(0, stage.Position.Y);
            copy.Size = source.Size;
            copy.MouseFilter = Control.MouseFilterEnum.Ignore;
            if (path == "WishlistCallToActionModule")
                foreach (var child in new[] { "WishlistButton", "ShareButton", "ModuleHitArea" })
                {
                    var excluded = copy.GetNode(child);
                    copy.RemoveChild(excluded);
                    excluded.Free();
                }
        }
        return snapshot;
    }

    public static async Task<Image> CaptureAsync(Control page)
    {
        if (DisplayServer.GetName() == "headless")
            throw new InvalidOperationException("Screenshot capture requires a rendering device.");
        using var snapshot = CreateSnapshot(page);
        // Render at 2x; preserve the live logical width so localization wraps identically.
        var viewport = new SubViewport { Size = new Vector2I(
            Mathf.CeilToInt(snapshot.Size.X * 2), Mathf.CeilToInt(snapshot.Size.Y * 2)),
            Disable3D = true, TransparentBg = false,
            // Control.Scale does not raise font rasterization resolution automatically.
            Oversampling = true, OversamplingOverride = 2f,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        snapshot.Scale = Vector2.One * 2;
        try
        {
            page.AddChild(viewport);
            viewport.AddChild(snapshot);
            // Allow duplicated Containers to sort before the captured render completes.
            for (var i = 0; i < 2; i++)
                await page.ToSignal(page.GetTree(), SceneTree.SignalName.ProcessFrame);
            await page.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = viewport.GetTexture().GetImage();
            if (image == null || image.IsEmpty())
                throw new InvalidOperationException("Celebration render is empty.");
            image.Convert(Image.Format.Rgb8);
            return AddPhotoBorder(image);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(viewport)) viewport.Free();
        }
    }
}
