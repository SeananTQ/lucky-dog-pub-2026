using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using LuckyDogRise.Tools;

namespace LuckyDogRise;

/// <summary>Headless smoke path for countdown, playback, convergence, and reset.</summary>
public partial class SteamStoreCaptureSmoke : Node
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public override async void _Ready()
    {
        try
        {
            var scene = GD.Load<PackedScene>("res://Tools/DogSkinEditor/SteamStoreCapture.tscn");
            var stage = scene.Instantiate<SteamStoreCaptureController>();
            AddChild(stage);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var tiles = (IList)GetField(stage, "_tiles");
            Check(tiles.Count > 0, "matrix was not created");
            var settingsPanel = (PanelContainer)GetField(stage, "_settingsPanel");
            var exitButton = (Button)GetField(stage, "_exitButton");
            Check(settingsPanel.Visible && settingsPanel.ZIndex > 3,
                "settings panel must render above dog parts");
            Check(exitButton.Visible && exitButton.ZIndex > settingsPanel.ZIndex,
                "configuration needs a visible exit button");
            var dogId = tiles[0]!.GetType().GetField("DogId")!;
            var firstDogCount = 0;
            foreach (var tile in tiles)
                if ((int)dogId.GetValue(tile)! == 1001) firstDogCount++;
            Check(firstDogCount == 1, "1001 occurs more than once");

            var settings = GetField(stage, "_settings");
            var columns = (int)settings.GetType().GetProperty("Columns")!.GetValue(settings)!;
            var rows = (int)settings.GetType().GetProperty("Rows")!.GetValue(settings)!;
            CheckBackgrounds(tiles, columns, rows);
            CheckLayering(tiles);
            settings.GetType().GetProperty("RandomBackgrounds")!.SetValue(settings, true);
            settings.GetType().GetProperty("FlightSeconds")!.SetValue(settings, 0.2);
            settings.GetType().GetProperty("StaggerSeconds")!.SetValue(settings, 0.005);
            SetField(stage, "_state", "Countdown");
            SetField(stage, "_countdownRemaining", 0.01);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(GetField(stage, "_state").ToString() == "Playing", "countdown did not enter playback");
            CheckBackgrounds(tiles, columns, rows);
            CheckLayering(tiles);
            var actions = (IList<int>)settings.GetType().GetProperty("Actions")!.GetValue(settings)!;
            var dog = (DogVisual)tiles[0]!.GetType().GetField("Dog")!.GetValue(tiles[0])!;
            Check((int)GetField(dog, "_currentReaction") == actions[0], "first action did not start");
            SetField(stage, "_phaseRemaining", 0.01);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check((int)GetField(dog, "_currentReaction") == actions[1],
                "expression must switch directly to the next action without restoring Default");
            CheckBackgrounds(tiles, columns, rows);
            CheckLayering(tiles);

            stage.GetType().GetMethod("BeginOutro", Private)!.Invoke(stage, null);
            Check(GetField(stage, "_state").ToString() == "Outro", "ending did not start");
            await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
            Check(GetField(stage, "_state").ToString() == "Finished", "ending did not finish");
            var visibleCount = 0;
            var visibleDogId = 0;
            foreach (var tile in tiles)
            {
                var root = (Control)tile!.GetType().GetField("Root")!.GetValue(tile)!;
                if (!root.Visible) continue;
                visibleCount++;
                visibleDogId = (int)dogId.GetValue(tile)!;
            }
            Check(visibleCount == 1 && visibleDogId == 1001, "final frame must keep only 1001");

            stage.GetType().GetMethod("ReturnToConfiguration", Private)!.Invoke(stage, null);
            Check(GetField(stage, "_state").ToString() == "Configure", "return to settings failed");
            Check(settingsPanel.Visible && exitButton.Visible, "configuration controls were not restored");
            foreach (var tile in tiles)
            {
                var root = (Control)tile!.GetType().GetField("Root")!.GetValue(tile)!;
                Check(root.Visible, "reset did not restore every tile");
            }
            CheckLayering(tiles);
            GD.Print("SteamStoreCaptureSmoke: settings, backgrounds, back/palm paw depth, action switching, ending, and reset passed.");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError("SteamStoreCaptureSmoke failed: " + exception);
            GetTree().Quit(1);
        }
    }

    private static object GetField(object target, string name) =>
        target.GetType().GetField(name, Private)!.GetValue(target)!;

    private static void CheckBackgrounds(IList tiles, int columns, int rows)
    {
        var width = columns + 2;
        var values = new int[width * (rows + 2)];
        var names = new Dictionary<string, int>();
        foreach (var tile in tiles)
        {
            var type = tile!.GetType();
            var column = (int)type.GetField("Column")!.GetValue(tile)!;
            var row = (int)type.GetField("Row")!.GetValue(tile)!;
            var path = (string)type.GetField("BackgroundPath")!.GetValue(tile)!;
            var background = (TextureRect)type.GetField("Background")!.GetValue(tile)!;
            Check(background.Texture is AtlasTexture atlas && Mathf.IsZeroApprox(atlas.Region.Position.Y),
                "background crop must start at the image top edge");
            if (!names.TryGetValue(path, out var id)) names[path] = id = names.Count + 1;
            values[(row + 1) * width + column + 1] = id;
        }
        Check(SteamStoreCaptureLayout.NeighborsAreDistinct(values, width, rows + 2),
            "adjacent backgrounds or scroll seam repeat");
    }

    private static void CheckLayering(IList tiles)
    {
        var rowDepths = new Dictionary<int, int>();
        foreach (var tile in tiles)
        {
            var type = tile!.GetType();
            var row = (int)type.GetField("Row")!.GetValue(tile)!;
            var root = (Control)type.GetField("Root")!.GetValue(tile)!;
            var background = (TextureRect)type.GetField("Background")!.GetValue(tile)!;
            var dog = (DogVisual)type.GetField("Dog")!.GetValue(tile)!;
            Check(root.GetChild(0) == background && root.GetChild(1) == dog,
                "each tile must draw its background before its dog");
            Check(dog.ZAsRelative && dog.ZIndex == 2,
                "each dog must sit above its own background and the previous row's back paws");
            var tongue = dog.GetNode<Sprite2D>("HeadRoot/Tonghe");
            Check(tongue.ZAsRelative && tongue.ZIndex == 9,
                "tongue must sit above the next row's background and below its dog");
            rowDepths[row] = root.ZIndex;
            foreach (var clawName in new[] { "ClawLeft", "ClawRight" })
            {
                var claw = dog.GetNode<Node2D>(clawName);
                Check(claw.Visible && claw.ZAsRelative, "paw must remain in its tile's draw order");
                var isBack = claw.GetNode<Sprite2D>("Claw_Back_Left").Visible;
                Check(claw.ZIndex == (isBack ? 9 : 1),
                    "back paws must be in front of the next background; palms behind it");
                Check(claw.GetNode<Sprite2D>("Claw_Palm_Left").Material == null,
                    "palm must keep its original texture without an alpha mask");
            }
        }
        var rows = rowDepths.Keys.OrderBy(row => row).ToArray();
        for (var index = 1; index < rows.Length; index++)
            Check(rowDepths[rows[index]] == rowDepths[rows[index - 1]] + 10,
                "row depths must leave room for palms, backgrounds, back paws, and dogs");
    }

    private static void SetField(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, Private)!;
        field.SetValue(target, field.FieldType.IsEnum ? Enum.Parse(field.FieldType, (string)value) : value);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
