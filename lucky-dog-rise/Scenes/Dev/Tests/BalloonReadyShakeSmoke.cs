#if DEBUG
using System;
using Godot;
using DataTables;
namespace LuckyDogRise;
public partial class BalloonReadyShakeSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var balloon = GD.Load<PackedScene>("res://Scenes/Prefabs/BalloonHint.tscn").Instantiate<BalloonHintController>();
            AddChild(balloon);
            var texture = GD.Load<Texture2D>("res://Assets/UI/BlindBox/BlindBox_Common_Closed.png");
            void ReadyState(bool ready = true) => balloon.ShowValueFromAssetPath(null, texture, EBlindBoxValueMode.Chips, 100, ready ? 100 : 99, readyToOpen: ready);
            ReadyState();
            balloon.SetProcess(false);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var icon = balloon.GetNode<TextureRect>("Overlay/Root/IconSlot/Icon");
            var slot = balloon.GetNode<Control>("Overlay/Root/IconSlot");
            var label = balloon.GetNode<Control>("Overlay/Root/Text");
            var slotRect = slot.GetRect();
            var labelRect = label.GetRect();
            var bubbleSize = balloon.Size;
            void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
            balloon._Process(11);
            Require(icon.Rotation == 0, "First shake started before interval");
            ReadyState(); // Regular refresh must not restart the interval.
            balloon._Process(1.1);
            Require(Math.Abs(icon.Rotation) > 0.01, "Ready refresh reset delay or shake missing");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(slot.GetRect() == slotRect && label.GetRect() == labelRect && balloon.Size == bubbleSize, "Shake moved layout");
            balloon._Process(0.8);
            Require(icon.Rotation == 0, "Shake did not reset");
            balloon._Process(11);
            Require(icon.Rotation == 0, "Repeat interval missing");
            balloon._Process(1.1);
            Require(Math.Abs(icon.Rotation) > 0.01, "Repeat shake missing");
            ReadyState(false);
            Require(icon.Rotation == 0 && !balloon.IsProcessing(), "Insufficient chips did not stop shake");
            ReadyState();
            balloon._Process(12.1);
            balloon.ShowLoading();
            Require(icon.Rotation == 0 && !balloon.IsProcessing(), "Opening did not stop shake");
            ReadyState();
            balloon._Process(12.1);
            balloon.SetDisplayVisible(false, false);
            Require(icon.Rotation == 0 && !balloon.IsProcessing(), "Hidden balloon did not stop shake");
            balloon.SetDisplayVisible(true, false);
            ReadyState();
            balloon._Process(1);
            Require(icon.Rotation == 0, "Redisplay skipped first delay");
            balloon.ShowCountdown(TimeSpan.FromSeconds(10));
            Require(!slot.Visible && !balloon.IsProcessing(), "Countdown retained icon placeholder or animation");
            GD.Print("[BalloonReadyShakeSmoke] Delay, repeat, stable layout, insufficient chips, loading, hidden and countdown passed.");
            GetTree().Quit();
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
}
#endif
