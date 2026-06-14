using Godot;

namespace LuckyDogRise;

public partial class GameData : Node
{
    [Signal] public delegate void ChipsChangedEventHandler(int chips);

    public int Chips { get; private set; } = 100;
    public int BetAmount => 5;
    public ProgressionManager Progression { get; } = new();

    public void ModifyChips(int delta)
    {
        Chips += delta;
        Progression.UpdateHighScore(Chips);
        EmitSignal(SignalName.ChipsChanged, Chips);
    }

    public bool CanAffordBet => Chips >= BetAmount;

    public void ResetToStart()
    {
        Chips = 100;
        Progression.Reset();
        EmitSignal(SignalName.ChipsChanged, Chips);
    }
}
