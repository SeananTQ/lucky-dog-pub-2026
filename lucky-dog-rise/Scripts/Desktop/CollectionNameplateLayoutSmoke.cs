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
            var render = typeof(CollectionEventPageController).GetMethod(
                "RenderNameplate", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var stateType = render.GetParameters()[0].ParameterType;
            var plaque = page.GetNode<Control>("VictoryRewardMargins/CollectionVictoryNameplate");
            string[] names = { "Copy", "ClaimableCopy", "ClaimedCopy" };
            foreach (var locale in new[] { "zh_CN", "en", "fr" })
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
                        Require(title.Text.Replace("\n", " ").Contains("Captain Lucky Paws"), "Preview nickname missing");
                        Require(previousLongText == null || previousLongText == title.Text,
                            "Repeated preview changed wrapping");
                        previousLongText = title.Text;
                    }
                    Require(detail.GetLineCount() <= 2, "Detail exceeds two lines");
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
            Require(Math.Abs(left.RotationDegrees - 7) < 0.01f
                && Math.Abs(left.Rotation + right.Rotation) < 0.0001f,
                "First inward sway is not mirrored");
            page._Process(0.55);
            Require(Math.Abs(left.RotationDegrees - 5) < 0.01f
                && Math.Abs(left.Rotation + right.Rotation) < 0.0001f,
                "Second inward sway is not mirrored");
            page._Process(0.3);
            Require(left.Rotation == 0 && right.Rotation == 0, "No rest after two sways");
            page._Process(2);
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
}
#endif
