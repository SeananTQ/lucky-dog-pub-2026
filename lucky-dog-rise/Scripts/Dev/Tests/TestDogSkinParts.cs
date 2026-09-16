using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DataTables;
using LuckyDogRise.Tools;

namespace LuckyDogRise;

// Headless regression: real DogArea and table data, with no GameData or Steam session.
public partial class TestDogSkinParts : Node
{
    public override async void _Ready()
    {
        try
        {
            var scene = GD.Load<PackedScene>("res://Scenes/Shared/DogArea.tscn");
            var dog = scene.Instantiate<DogVisual>();
            AddChild(dog);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var head = dog.GetNode<Sprite2D>("HeadRoot/Head");
            var nose = dog.GetNode<Sprite2D>("HeadRoot/Nose");
            var mouth = dog.GetNode<Sprite2D>("HeadRoot/Mouth");
            var draft = DogSkinDraft.FromDogSkin(LubanData.Tables.TbDogSkin.Get(1012));
            Check(draft.DefaultNose == "Nose_Brown.png" && draft.DefaultMouse == "Mouse_Natural.png", "table to draft");
            var copy = JsonSerializer.Deserialize<DogSkinDraft>(JsonSerializer.Serialize(draft.CloneWithId(1012)))!;
            dog.SetPreviewAppearance(copy.ToAppearanceSpec());
            Check(nose.Visible && mouth.Visible, "new face visibility");
            Check(head.Position.IsEqualApprox(new Vector2(-0.5f, -137f)), "v3 head PSD reference offset");
            Check(nose.Position.IsEqualApprox(new Vector2(0f, -223.5f)), "nose relative to reference head");
            Check(mouth.Position.IsEqualApprox(new Vector2(-1f, -93f)), "mouth relative to reference head");
            Check(dog.ToLocal(dog.GetNode<Sprite2D>("ClawLeft/Claw_Back_Left").GlobalPosition)
                .IsEqualApprox(new Vector2(-319.5f, -15f)), "left back paw PSD plane");
            Check(dog.ToLocal(dog.GetNode<Sprite2D>("ClawRight/Claw_Palm_Left").GlobalPosition)
                .IsEqualApprox(new Vector2(304.5f, 32f)), "right palm mirrored reference delta");
            var material = (ShaderMaterial)head.Material;
            var tongue = dog.GetNode<Sprite2D>("HeadRoot/Tonghe");
            copy.MouseSilent = "Mouse_Shy.png";
            copy.EyesNormalMood = copy.EyesBored;
            dog.SetPreviewAppearance(copy.ToAppearanceSpec());
            dog.ApplyReaction(EDogReactionTrigger.Serious);
            Check(dog.GetNode<Sprite2D>("HeadRoot/Eyes").Texture.ResourcePath.EndsWith(copy.EyesBored), "eye field resolves configured filename");
            dog.ApplyReaction(EDogReactionTrigger.Silent);
            Check(mouth.Texture.ResourcePath.EndsWith("Mouse_Shy.png") && !tongue.Visible, "silent mouth and hidden tongue");
            dog.ApplyReaction(EDogReactionTrigger.HintExhausted);
            Check(mouth.Texture.ResourcePath.EndsWith("Mouse_Shy.png") && !tongue.Visible, "inherited silent mouth");
            dog.SetIntroPartVisibility(true, true, true);
            Check(!tongue.Visible, "Rise cannot reveal silent tongue");
            dog.ApplyReaction(EDogReactionTrigger.Default);
            Check(mouth.Texture.ResourcePath.EndsWith(copy.DefaultMouse) && tongue.Visible, "default mouth and tongue restored");
            copy.MouseSilent = "";
            dog.SetPreviewAppearance(copy.ToAppearanceSpec());
            dog.ApplyReaction(EDogReactionTrigger.Silent);
            Check(mouth.Texture.ResourcePath.EndsWith(copy.DefaultMouse) && tongue.Visible, "empty silent mouth fallback");
            dog.ApplyReaction(EDogReactionTrigger.Default);
            copy.MouseSilent = draft.MouseSilent;
            Check(Mathf.IsEqualApprox(material.GetShaderParameter("cutoff_y").AsSingle(), 137f), "head clip plane");
            dog.SetIntroPartVisibility(false, false, true);
            Check(!nose.Visible && !mouth.Visible, "Rise claw-only face hiding");
            dog.SetIntroPartVisibility(true, true, true);
            Check(nose.Visible && mouth.Visible, "Rise face restore");

            var other = scene.Instantiate<DogVisual>();
            AddChild(other);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(other.GetNode<Sprite2D>("HeadRoot/Head").Material != material, "independent clip materials");
            foreach (var skin in LubanData.Tables.TbDogSkin.DataList)
            {
                dog.SetPreviewAppearance(DogAppearanceSpec.FromDogSkin(skin));
                foreach (var reaction in LubanData.Tables.TbDogReaction.DataList)
                    dog.ApplyReaction((EDogReactionTrigger)reaction.Id);
                Check(nose.Visible == !string.IsNullOrWhiteSpace(skin.DefaultNose), $"nose switch {skin.Id}");
                Check(mouth.Visible == !string.IsNullOrWhiteSpace(skin.DefaultMouse), $"mouth switch {skin.Id}");
                if (string.IsNullOrWhiteSpace(skin.DefaultNose))
                    Check(nose.Texture == null && mouth.Texture == null, "old skin clears optional textures");
            }

            var assets = new DogSkinAssetCatalog();
            Check(assets.FolderPaths.Contains("v3\\Shiba\\Sesame"), "editor v3 folder");
            Check(assets.GetFiles(draft.FolderPath, "Nose_").Contains(draft.DefaultNose), "editor nose choices");
            var editor = new DogSkinEditorController();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(DogSkinEditorController).GetField("_catalog", flags)!.SetValue(editor,
                new DogSkinCatalogDraft { DogSkins = new() { draft } });
            var csv = (string)typeof(DogSkinEditorController).GetMethod("BuildCsv", flags)!.Invoke(editor, null)!;
            var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            var headers = lines[0].Split(',');
            var values = lines[1].Split(',');
            Check(headers.Length == values.Length, "CSV column count");
            var tableJson = FileAccess.GetFileAsString("res://Data/Json/tbdogskin.json");
            using var tableDocument = JsonDocument.Parse(tableJson);
            Check(headers.SequenceEqual(tableDocument.RootElement[0].EnumerateObject().Select(field => field.Name)), "CSV follows current table schema");
            Check(copy.MouseSilent == draft.MouseSilent, "silent mouth round-trip");
            var migrate = typeof(DogSkinEditorController).GetMethod("MigrateDraftFieldNames", BindingFlags.Static | BindingFlags.NonPublic)!;
            var migratedJson = (string)migrate.Invoke(null, new object[] { "{\"Version\":2,\"DogSkins\":[{\"Nose\":\"old-nose.png\",\"Mouse\":\"old-mouth.png\",\"EyesCute\":\"old-eyes.png\",\"EarsHappy\":\"old-ears.png\"}]}" })!;
            var migrated = JsonSerializer.Deserialize<DogSkinCatalogDraft>(migratedJson)!.DogSkins[0];
            Check(migrated.DefaultNose == "old-nose.png" && migrated.DefaultMouse == "old-mouth.png"
                && migrated.EyesNormalMood == "old-eyes.png" && migrated.EarsNormal == "old-ears.png", "old draft field migration");
            Check(values[Array.IndexOf(headers, "DefaultNose")] == draft.DefaultNose, "CSV nose column");
            Check(values[Array.IndexOf(headers, "DefaultMouse")] == draft.DefaultMouse, "CSV mouth column");
            editor.Free();
            dog.QueueFree();
            other.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print("DOG_SKIN_PARTS_PASS: v3 coordinates, optional parts, skin/reaction switches, Rise visibility, isolated clip materials, editor choices, draft round-trip and CSV.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
