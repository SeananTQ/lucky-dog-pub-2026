using Godot;

namespace LuckyDogRise;

public partial class DebugHUDController : PanelContainer
{
    [Signal]
    public delegate void RandomizeRequestedEventHandler();

    private Button _seedLabel = null!;
    private LineEdit _seedInput = null!;
    private Button _randomizeButton = null!;

    public bool DebugEnabled { get; set; } = true;
    public int CurrentSeed { get; set; }

    public override void _Ready()
    {
        _seedLabel = GetNode<Button>("DebugVBox/SeedLabel");
        _seedInput = GetNode<LineEdit>("DebugVBox/SeedInput");
        _randomizeButton = GetNode<Button>("DebugVBox/RandomizeButton");
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
