#if DEBUG
using Godot;

namespace LuckyDogRise;

public partial class BlindBoxRegressionSmokeRunner : Node
{
    public override void _Ready()
    {
        BlindBoxRegressionSmoke.Run();
        GetTree().Quit();
    }
}
#endif
