using Godot;

public partial class UIThemePreviewController : Control
{
    [Export] private Button _helloWorldButton = null!;

    public override void _Ready()
    {
        _helloWorldButton ??= GetNodeOrNull<Button>("Grid/SectionButtons/Row/Hello World");

        if (_helloWorldButton == null)
        {
            GD.PushError("UIThemePreviewController: Hello World button is not bound.");
            return;
        }

        GD.Print($"[Export] _helloWorldButton={_helloWorldButton.Name}");
        _helloWorldButton.Pressed += OnHelloWorldButtonPressed;
    }

    private static void OnHelloWorldButtonPressed()
    {
        GD.Print("Hello World");
    }
}
