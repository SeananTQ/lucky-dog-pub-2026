#if DEBUG
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LuckyDogRise;

public partial class PokerWelcomePreview : Control
{
    [Export] private PokerWelcomeDialogController _welcome = null!;
    [Export] private PokerQuickBarController _bar = null!;
    private bool _automated;
    private int _confirmed;
    private int _toolbarClicks;
    private static readonly string[] Locales =
        ["en", "zh_CN", "zh_TW", "ja", "es_ES", "es_419", "pt_BR", "pt_PT", "fr", "de", "da", "id", "nb", "sv", "nl", "vi", "ms", "ko", "ru", "uk"];

    public override void _EnterTree()
    {
        _automated = OS.GetCmdlineUserArgs().Contains("--poker-welcome-validate");
        if (!_automated) return;
        // Isolate the persistence checks before any child reads settings. No real platform/GameData.
        const string sandbox = "LuckyDogRise/Tests/PokerWelcome";
        ProjectSettings.SetSetting("application/config/use_custom_user_dir", true);
        ProjectSettings.SetSetting("application/config/custom_user_dir_name", sandbox);
        Require(ProjectSettings.GlobalizePath("user://").Replace('\\', '/').EndsWith(sandbox + "/"),
            "Test storage must be isolated.");
        Directory.CreateDirectory(ProjectSettings.GlobalizePath("user://"));
    }

    public override async void _Ready()
    {
        GetWindow().ContentScaleSize = new Vector2I(1200, 1200);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        GetWindow().Size = new Vector2I(600, 600);
        L10n.SetLocale("zh_CN", save: false);
        _welcome.OverlayVisibilityChanged += visible => _bar.SetPinnedOpen(visible);
        _welcome.Confirmed += () => _confirmed++;
        _bar.HelpRequested += () => _toolbarClicks++;
        _bar.SetAvailable(true);
        _welcome.SetRequested(true);
        _welcome.SetContext(true, false);
        if (!_automated) return;
        try
        {
            CheckPersistence();
            await CheckInteractionAndLayout();
            GD.Print("[PokerWelcome] PASS: persistence/account isolation, preview without writes, modal input, toolbar delay and 20 locales.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    public override void _Process(double delta)
    {
        if (_automated) return;
        var screenRect = new Rect2I(DisplayServer.WindowGetPosition(), DisplayServer.WindowGetSize());
        _bar.UpdatePointer(screenRect.HasPoint(DisplayServer.MouseGetPosition()), GetGlobalMousePosition(),
            dragging: false, Input.IsMouseButtonPressed(MouseButton.Left), delta);
    }

    private static void CheckPersistence()
    {
        string run = Guid.NewGuid().ToString("N");
        var storage = AccountStorageContext.ForDevelopment("welcome-" + run);
        var account = new AccountStateManager(storage);
        account.SaveTutorialStepState(AccountStateManager.InitialMeetingTutorialId, AccountStateManager.TutorialStepState.Completed);
        var oldBytes = File.ReadAllBytes(ProjectSettings.GlobalizePath(storage.AccountStatePath));
        var normal = new PokerWelcomeProgress(account, sessionOnly: false);
        Require(!normal.IsAcknowledged, "Existing tutorial completion must not count as welcome acknowledgement.");
        var preview = new PokerWelcomeProgress(account, sessionOnly: true);
        preview.Acknowledge();
        Require(preview.IsAcknowledged && !new PokerWelcomeProgress(account, true).IsAcknowledged,
            "Preview confirms once in memory and resets on next run.");
        Require(oldBytes.SequenceEqual(File.ReadAllBytes(ProjectSettings.GlobalizePath(storage.AccountStatePath))),
            "Preview must not consume the real acknowledgement or change other account flags.");
        var mock = new PokerWelcomeProgress(account, false);
        mock.Acknowledge(allowPersistence: false);
        Require(mock.IsAcknowledged && !account.LoadPokerWelcomeAcknowledged(), "Mock must not write the real acknowledgement.");
        normal.Acknowledge();
        Require(new PokerWelcomeProgress(new AccountStateManager(storage), false).IsAcknowledged,
            "Normal confirmation must survive a new progress instance.");
        Require(account.LoadTutorialStepState(AccountStateManager.InitialMeetingTutorialId) == AccountStateManager.TutorialStepState.Completed,
            "Saving welcome must preserve unrelated tutorial flags.");
        var confirmedBytes = File.ReadAllBytes(ProjectSettings.GlobalizePath(storage.AccountStatePath));
        var replay = new PokerWelcomeProgress(account, true);
        Require(!replay.IsAcknowledged, "Preview must ignore an existing real acknowledgement.");
        replay.Acknowledge();
        Require(confirmedBytes.SequenceEqual(File.ReadAllBytes(ProjectSettings.GlobalizePath(storage.AccountStatePath))),
            "Preview must preserve existing real acknowledgement bytes.");
        var other = new AccountStateManager(AccountStorageContext.ForDevelopment("welcome-other-" + run));
        Require(!new PokerWelcomeProgress(other, false).IsAcknowledged, "Acknowledgement must not cross accounts.");
        GD.Print("[PokerWelcome] PASS: acknowledgement storage and preview isolation.");
    }

    private async Task CheckInteractionAndLayout()
    {
        await Wait(.25);
        Require(_welcome.IsOverlayVisible && _bar.IsExpanded, "Poker entry must reveal welcome and toolbar.");
        Pointer(false, 10);
        Require(_bar.IsExpanded, "Welcome pins toolbar indefinitely outside its hot zone.");
        await Click(_bar.GetNode<Button>("Clip/Bar/Buttons/Help").GetGlobalRect().GetCenter());
        await Click(new Vector2(50, 1000));
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = false });
        Require(_toolbarClicks == 0 && _confirmed == 0 && _welcome.IsOverlayVisible,
            "Outside clicks, toolbar clicks and Escape must not acknowledge or click through.");
        _welcome.SetContext(false, false);
        Require(!_welcome.IsOverlayVisible && _confirmed == 0, "Leaving poker must hide without acknowledgement.");
        _welcome.SetContext(true, false);
        Require(_welcome.IsOverlayVisible, "Unconfirmed entry must show again.");
        _welcome.SetContext(true, true);
        Require(!_welcome.IsOverlayVisible, "Higher overlay temporarily suspends welcome.");
        _welcome.SetContext(true, false);
        Require(_welcome.IsOverlayVisible && _bar.IsExpanded, "Suspended welcome resumes with toolbar pinned.");

        var panel = _welcome.GetNode<Control>("InputShield/Center/Panel");
        foreach (string locale in Locales)
        {
            L10n.SetLocale(locale, save: false);
            await Wait(.08);
            var rect = panel.GetGlobalRect();
            Require(rect.Position.X >= 0 && rect.End.X <= 1200 && rect.Position.Y >= 160 && rect.End.Y <= 1120,
                $"{locale}: dialog outside safe bounds: {rect}.");
            foreach (string node in new[] { "Title", "Message", "Confirm" })
            {
                var control = panel.GetNode<Control>("Margin/Content/" + node);
                string text = control is Label label ? label.Text : ((Button)control).Text;
                Require(!text.Contains("PokerWelcome_") && !text.Contains("{0}") && !text.Contains("{1}"), locale + ": untranslated message.");
                Require(rect.Encloses(control.GetGlobalRect()), locale + ": content escaped panel.");
                var font = control.GetThemeFont("font");
                foreach (char character in text.Where(c => !char.IsWhiteSpace(c)))
                    Require(font.HasChar(character), $"{locale}: missing glyph U+{(int)character:X4}.");
                if (control is Label body)
                    Require(body.GetLineCount() == body.GetVisibleLineCount(), locale + ": clipped lines.");
            }
            if (DisplayServer.GetName() != "headless")
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                string directory = ProjectSettings.GlobalizePath("res://../.local-build/poker-welcome");
                Directory.CreateDirectory(directory);
                Require(GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, locale + ".png")) == Error.Ok, "Screenshot failed.");
            }
            GD.Print($"[PokerWelcome] PASS: {locale} layout {rect.Size}.");
        }

        await Click(panel.GetNode<Control>("Margin/Content/Confirm").GetGlobalRect().GetCenter());
        Require(_confirmed == 1 && !_welcome.IsOverlayVisible && _bar.IsExpanded, "Only confirm acknowledges; bar must stay open initially.");
        Pointer(false, .49);
        Require(_bar.IsExpanded, "Toolbar must wait 0.5 seconds after confirmation.");
        Pointer(false, .02);
        Require(!_bar.IsExpanded, "Toolbar must retract after its natural delay.");
        _welcome.SetContext(false, false);
        _welcome.SetContext(true, false);
        Require(!_welcome.IsOverlayVisible, "Confirmed dialog must stay closed for repeated entries.");
        _welcome.SetRequested(true);
        await Wait(.25);
        await Click(panel.GetNode<Control>("Margin/Content/Confirm").GetGlobalRect().GetCenter());
        Pointer(true, 10);
        Require(_bar.IsExpanded, "Pointer inside must retain toolbar after confirmation.");
    }

    private void Pointer(bool inside, double delta) => _bar.UpdatePointer(true,
        inside ? new Vector2(200, 50) : new Vector2(200, 1000), false, false, delta);

    private async Task Click(Vector2 point)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
        foreach (bool pressed in new[] { true, false })
        {
            GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
    private async Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
