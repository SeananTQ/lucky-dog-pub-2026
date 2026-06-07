using Godot;

namespace LuckyDogRise;

public partial class DebugHUDController : PanelContainer
{
    [Signal]
    public delegate void RandomizeRequestedEventHandler();
    [Signal]
    public delegate void RandomizeDogRequestedEventHandler();

    private Button _seedLabel = null!;
    private LineEdit _seedInput = null!;
    private Button _randomizeButton = null!;
    private Button _dogButton = null!;

    public bool DebugEnabled { get; set; } = true;
    public int CurrentSeed { get; set; }

    public override void _Ready()
    {
        _seedLabel = GetNode<Button>("DebugVBox/SeedLabel");
        _seedInput = GetNode<LineEdit>("DebugVBox/SeedInput");
        _randomizeButton = GetNode<Button>("DebugVBox/RandomizeButton");

        _dogButton = new Button();
        _dogButton.Text = "Randomize Dog";
        _dogButton.AddThemeFontSizeOverride("font_size", 16);
        _dogButton.Pressed += () => EmitSignal(SignalName.RandomizeDogRequested);
        var vbox = _randomizeButton.GetParent();
        vbox.AddChild(_dogButton);
        vbox.MoveChild(_dogButton, vbox.GetChildCount() - 1);

        _seedLabel.Pressed += OnSeedClicked;
        _randomizeButton.Pressed += () => EmitSignal(SignalName.RandomizeRequested);
    }

    public override void _Process(double delta)
    {
        Visible = DebugEnabled;
    }

    private void OnSeedClicked()
    {
        DisplayServer.ClipboardSet(CurrentSeed.ToString());
        _seedLabel.Text = $"Seed: {CurrentSeed} (copied!)";
    }

    public void UpdateSeed(int seed)
    {
        CurrentSeed = seed;
        _seedLabel.Text = $"Seed: {seed} (click to copy)";
    }

    public bool TryGetFixedSeed(out int seed)
    {
        seed = 0;
        return DebugEnabled && _seedInput.Text.Length > 0 && int.TryParse(_seedInput.Text, out seed);
    }
}
