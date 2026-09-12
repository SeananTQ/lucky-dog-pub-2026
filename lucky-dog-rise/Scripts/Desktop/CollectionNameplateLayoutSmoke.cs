#if DEBUG
using Godot;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace LuckyDogRise;

// Isolated UI regression: no GameData, Steam session, reward claim, or save.
public partial class CollectionNameplateLayoutSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            L10n.SetLocale("zh_CN", save: false);
            var page = GD.Load<PackedScene>("res://Scenes/Prefabs/CollectionEventPage.tscn")
                .Instantiate<CollectionEventPageController>();
            page.Theme = GD.Load<Theme>("res://Themes/DefaultTheme.tres");
            page.Size = new Vector2(382, 900);
            AddChild(page);
            const string layoutTestPersonaName = "Nameplate Layout Smoke Test Persona";
            typeof(CollectionEventPageController).GetField(
                "_playerDisplayNameProvider", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(page, (Func<string>)(() => layoutTestPersonaName));
            var render = typeof(CollectionEventPageController).GetMethod(
                "RenderNameplate", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var stateType = render.GetParameters()[0].ParameterType;
            var plaque = page.GetNode<Control>("VictoryRewardMargins/CollectionVictoryNameplate");
            string[] names = { "Copy", "ClaimableCopy", "ClaimedCopy" };
            foreach (var locale in new[] { "en", "zh_CN", "zh_TW", "ja", "es_ES", "es_419", "pt_BR", "pt_PT", "fr", "de", "da", "id", "nb", "sv", "nl", "vi", "ms", "ko", "ru", "uk" })
            {
            L10n.SetLocale(locale, save: false);
            string previousLongText = null;
            for (var pass = 0; pass < 4; pass++)
            {
                foreach (var state in new[] { 2, 0, 1 })
                {
                    render.Invoke(page, new[] { Enum.ToObject(stateType, state), (object)true, false });
                    await Frames();
                    var copy = plaque.GetNode<Control>("ContentMargins/ContentLayer/" + names[state]);
                    var title = copy.GetNode<Label>("Title");
                    var detail = copy.GetNode<Label>("Detail");
                    Require(copy.Visible && title.Size.Y > 0 && title.Text.Length > 0, "Missing title");
                    Require(Math.Abs(plaque.Size.Y - 116) < 0.1f, "Plaque height changed");
                    Require(title.GetGlobalRect().End.Y <= detail.GetGlobalRect().Position.Y + 0.1f,
                        "Title overlaps detail");
                    Require(title.GetGlobalRect().Position.Y >= plaque.GetGlobalRect().Position.Y
                        && detail.GetGlobalRect().End.Y <= plaque.GetGlobalRect().End.Y,
                        "Text outside plaque");
                    if (state != 2)
                        Require(title.GetThemeFontSize("font_size") == (locale == "fr" ? 15 : 18), "Standard title shrank");
                    else
                    {
                        Require(title.Text.Replace("\n", " ").Contains(layoutTestPersonaName),
                            "Injected layout-test nickname missing");
                        Require(previousLongText == null || previousLongText == title.Text,
                            "Repeated preview changed wrapping");
                        previousLongText = title.Text;
                    }
                    Require(detail.GetLineCount() <= 2, "Detail exceeds two lines");
                    if (state == 1)
                    {
                        var underlineLines = (System.Collections.Generic.List<Rect2>)typeof(CollectionEventPageController)
                            .GetMethod("GetClaimableUnderlineLines", BindingFlags.NonPublic | BindingFlags.Instance)!
                            .Invoke(page, null)!;
                        Require(underlineLines.Count == detail.GetLineCount(), "Underline line count differs from text");
                        foreach (var line in underlineLines)
                            Require(line.Position.X >= -0.1f && line.End.X <= detail.Size.X + 0.1f,
                                "Underline exceeds text area");
                    }
                    GD.Print($"[NameplateSmoke] locale={locale} pass={pass} state={state} titleHeight={title.Size.Y} detailLines={detail.GetLineCount()} font={title.GetThemeFontSize("font_size")}");
                }
            }
            }
            var background = plaque.GetNode<NinePatchRect>("NameplateBackground");
            var left = plaque.GetNode<Control>("ContentMargins/ContentLayer/LaurelLeftPivot");
            var right = plaque.GetNode<Control>("ContentMargins/ContentLayer/LaurelRightPivot");
            var before = plaque.GetGlobalRect();
            page.Hide();
            page.Show(); // Reset the gesture clock after asynchronous layout tests.
            Require(page.IsProcessing(), "Claimable sway did not start");
            page._Process(0.275);
            var opacity = typeof(CollectionEventPageController).GetField(
                "_nameplateUnderlineOpacity", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Require(Math.Abs((float)opacity.GetValue(page)! - 1f) < 0.001f,
                "Underline not visible at sway peak");
            Require(Math.Abs(left.RotationDegrees - 7) < 0.01f
                && Math.Abs(left.Rotation + right.Rotation) < 0.0001f,
                "First inward sway is not mirrored");
            page._Process(0.55);
            Require(Math.Abs(left.RotationDegrees - 5) < 0.01f
                && Math.Abs(left.Rotation + right.Rotation) < 0.0001f,
                "Second inward sway is not mirrored");
            page._Process(0.3);
            Require((float)opacity.GetValue(page)! == 0f, "Underline remains during rest");
            Require(left.Rotation == 0 && right.Rotation == 0, "No rest after two sways");
            page._Process(1);
            Require(left.Rotation == 0 && right.Rotation == 0, "Rest is too short");
            Require(background.SelfModulate == Colors.White, "Old brightness animation remains");
            Require(plaque.GetGlobalRect() == before && plaque.Scale == Vector2.One,
                "Sway changed plaque geometry");
            page.Hide();
            Require(!page.IsProcessing() && left.Rotation == 0 && right.Rotation == 0,
                "Hidden page retained animation");
            page.Show();
            Require(page.IsProcessing(), "Sway did not resume on show");
            page._Process(0.275);
            render.Invoke(page, new[] { Enum.ToObject(stateType, 2), (object)true, false });
            Require(!page.IsProcessing() && left.Rotation == 0 && right.Rotation == 0,
                "Claimed plaque retained animation");
            GD.Print("[NameplateSmoke] PASS");
            var stagePosition = page.GetNode<Control>("GrandPrizeStage").Position;
            var snapshot = CollectionCelebrationScreenshot.CreateSnapshot(page);
            try
            {
                var snapshotStage = snapshot.GetNode<Control>("GrandPrizeStage");
                var leftPrize = snapshotStage.GetNode<Control>("GrandPrizeLeft");
                var centerPrize = snapshotStage.GetNode<Control>("GrandPrizeCenter");
                Require(snapshotStage.Position.Y > 0
                    && Math.Abs(snapshotStage.Position.Y - (leftPrize.Position.Y - centerPrize.Position.Y)) < 0.1f,
                    "Screenshot does not leave room above the tallest reward icon");
                var wish = snapshot.GetNode<Control>("WishlistCallToActionModule");
                Require(!wish.HasNode("WishlistButton") && !wish.HasNode("ShareButton"),
                    "Screenshot includes action buttons");
                Require(Math.Abs(snapshot.Size.Y - wish.Position.Y
                    - wish.GetNode<Control>("ShyDog").GetRect().End.Y) < 0.1f,
                    "Screenshot does not end at the shy dog");
                Require(!snapshot.HasNode("DebugToolsModule") && !snapshot.HasNode("RoadmapSpacing"),
                    "Screenshot includes unrelated content");
                VerifyVisualOnly(snapshot);
            }
            finally { snapshot.Free(); }
            Require(page.GetNode<Control>("GrandPrizeStage").Position == stagePosition,
                "Capture changed live layout");
            GD.Print("[CelebrationScreenshotStructure] PASS (no pixels captured, no Steam write)");
            if (Array.Exists(OS.GetCmdlineUserArgs(), arg => arg == "--capture-celebration-preview"))
            {
                L10n.SetLocale("zh_CN", save: false);
                var output = ProjectSettings.GlobalizePath("res://../.local-build/celebration-preview");
                System.IO.Directory.CreateDirectory(output);
                foreach (var state in new[] { 0, 1, 2 })
                {
                    render.Invoke(page, new[] { Enum.ToObject(stateType, state), (object)true, false });
                    await Frames();
                    using var image = await CollectionCelebrationScreenshot.CaptureAsync(page);
                    Require(image.SavePng($"{output}/state-{state}.png") == Error.Ok, "Preview save failed");
                    GD.Print($"[CelebrationScreenshotPixels] state={state} size={image.GetWidth()}x{image.GetHeight()}");
                }
            }
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PushError("[NameplateSmoke] FAIL: " + ex);
            GetTree().Quit(1);
        }
    }

    private async Task Frames()
    {
        for (var i = 0; i < 4; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void VerifyVisualOnly(Node node)
    {
        Require(node.GetScript().VariantType == Variant.Type.Nil, "Screenshot copy retained a script");
        foreach (var child in node.GetChildren()) VerifyVisualOnly(child);
    }
}
#endif
