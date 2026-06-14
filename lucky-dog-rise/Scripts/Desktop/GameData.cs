using Godot;

namespace LuckyDogRise;

public partial class GameData : Node
{
    [Signal] public delegate void ChipsChangedEventHandler(int chips);
    [Signal] public delegate void HandResolvedEventHandler(HandRank rank, int payout);
    [Signal] public delegate void NewHandStartedEventHandler();

    public void EmitHandResolved(HandRank rank, int payout)
    {
        EmitSignal(SignalName.HandResolved, (int)rank, payout);
    }

    public void EmitNewHandStarted()
    {
        EmitSignal(SignalName.NewHandStarted);
    }

    public int Chips { get; private set; } = 1000;
    public int BetAmount => 50;
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
        Chips = 1000;
        Progression.Reset();
        EmitSignal(SignalName.ChipsChanged, Chips);
    }
}
