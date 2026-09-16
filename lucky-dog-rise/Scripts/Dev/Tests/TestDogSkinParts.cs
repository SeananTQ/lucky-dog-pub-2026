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
            Check(draft.Nose == "Nose_Brown.png" && draft.Mouse == "Mouse_Natural.png", "table to draft");
            var copy = JsonSerializer.Deserialize<DogSkinDraft>(JsonSerializer.Serialize(draft.CloneWithId(1012)))!;
            dog.SetPreviewAppearance(copy.ToAppearanceSpec());
            Check(nose.Visible && mouth.Visible, "new face visibility");
            Check(head.Position.IsEqualApprox(new Vector2(10.5f, -137f)), "v3 head PSD reference offset");
            Check(nose.Position.IsEqualApprox(new Vector2(11f, -223.5f)), "nose relative to reference head");
            Check(mouth.Position.IsEqualApprox(new Vector2(10f, -93f)), "mouth relative to reference head");
            var material = (ShaderMaterial)head.Material;
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
                Check(nose.Visible == !string.IsNullOrWhiteSpace(skin.Nose), $"nose switch {skin.Id}");
                Check(mouth.Visible == !string.IsNullOrWhiteSpace(skin.Mouse), $"mouth switch {skin.Id}");
                if (string.IsNullOrWhiteSpace(skin.Nose))
                    Check(nose.Texture == null && mouth.Texture == null, "old skin clears optional textures");
            }

            var assets = new DogSkinAssetCatalog();
            Check(assets.FolderPaths.Contains("v3\\Shiba\\Sesame"), "editor v3 folder");
            Check(assets.GetFiles(draft.FolderPath, "Nose_").Contains(draft.Nose), "editor nose choices");
            var editor = new DogSkinEditorController();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(DogSkinEditorController).GetField("_catalog", flags)!.SetValue(editor,
                new DogSkinCatalogDraft { DogSkins = new() { draft } });
            var csv = (string)typeof(DogSkinEditorController).GetMethod("BuildCsv", flags)!.Invoke(editor, null)!;
            var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            var headers = lines[0].Split(',');
            var values = lines[1].Split(',');
            Check(headers.Length == values.Length, "CSV column count");
            Check(values[Array.IndexOf(headers, "Nose")] == draft.Nose, "CSV nose column");
            Check(values[Array.IndexOf(headers, "Mouse")] == draft.Mouse, "CSV mouth column");
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
