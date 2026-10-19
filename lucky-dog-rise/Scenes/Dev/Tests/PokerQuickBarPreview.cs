#if DEBUG
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LuckyDogRise;

public partial class PokerQuickBarPreview : Control
{
    [Export] private PokerQuickBarController _bar = null!;
    [Export] private PokerHandShowcaseController _help = null!;
    [Export] private TutorialManager _tutorial = null!;
    [Export] private Label _status = null!;
    private bool _automated;
    private bool _desktop;
    private int _desktopRequests;
    private int _behindClicks;
    private static readonly string[] Locales =
        ["en", "zh_CN", "zh_TW", "ja", "es_ES", "es_419", "pt_BR", "pt_PT", "fr", "de", "da", "id", "nb", "sv", "nl", "vi", "ms", "ko", "ru", "uk"];

    public override void _EnterTree()
    {
        _automated = OS.GetCmdlineUserArgs().Contains("--poker-toolbar-validate");
        if (!_automated) return;
        const string sandbox = "LuckyDogRise/Tests/PokerQuickBar";
        ProjectSettings.SetSetting("application/config/use_custom_user_dir", true);
        ProjectSettings.SetSetting("application/config/custom_user_dir_name", sandbox);
        Require(ProjectSettings.GlobalizePath("user://").Replace('\\', '/').EndsWith(sandbox + "/"),
            "Test storage must be isolated.");
        Directory.CreateDirectory(ProjectSettings.GlobalizePath("user://"));
    }

    public override async void _Ready()
    {
        // 不创建 GameData/平台服务；自动验收仅在隔离目录读写测试账号。
        GetWindow().ContentScaleSize = new Vector2I(1200, 1200);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        GetWindow().Size = new Vector2I(600, 600);
        L10n.SetLocale("zh_CN", save: false);
        _bar.HelpRequested += _help.ShowBasics;
        _bar.TutorialRequested += _tutorial.ShowOnce;
        _bar.DesktopRequested += () => { _desktop = true; _desktopRequests++; _status.Text = "桌宠入口已触发（独立预览）"; RefreshContext(); };
        _help.ShowcaseVisibilityChanged += _ => RefreshContext();
        _tutorial.OverlayVisibilityChanged += _ => RefreshContext();
        GetNode<Button>("BehindBar").Pressed += () => _behindClicks++;
        RefreshContext();
        if (!_automated) return;
        try
        {
            await RunChecks();
            GD.Print("[PokerToolbar] Hover/delay/re-entry/drag blocking, first-use hot zone, account persistence, help/tutorial/desktop actions, settings preservation and 20 locales passed.");
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

    private void RefreshContext()
    {
        _tutorial.SetPokerContext(!_desktop, _help.IsOverlayVisible);
        _bar.SetAvailable(!_desktop && !_help.IsOverlayVisible && !_tutorial.IsOverlayVisible);
        _bar.SetReminderContext(!_desktop && !_help.IsOverlayVisible && !_tutorial.IsOverlayVisible);
    }

    private void Pointer(bool inside, double delta = 0, bool pressed = false, bool dragging = false) =>
        _bar.UpdatePointer(inside, inside ? new Vector2(200, 60) : new Vector2(200, 400), dragging, pressed, delta);

    private async Task RunChecks()
    {
        CheckPersistence();
        await Frames();
        CheckIdleReminder();
        Require(!_bar.IsExpanded, "Entry must not automatically reveal the toolbar.");
        var discoveryPoint = new Vector2(200, 120);
        Require(_bar.ContainsPoint(discoveryPoint), "An unused toolbar must include the larger discovery region.");
        _bar.UpdatePointer(true, discoveryPoint, false, false, 0);
        Require(_bar.IsExpanded, "The discovery-only region must reveal the unused toolbar.");
        Pointer(false, .6);
        Require(_bar.ContainsPoint(discoveryPoint), "Hovering alone must not shrink the hot zone.");
        Pointer(true);
        await Wait(.22);
        Require(_bar.IsExpanded, "Hover must expand.");
        var panel = _bar.GetNode<Control>("Clip/Bar");
        Require(Math.Abs(panel.Position.Y) < .1, "Toolbar must slide fully into view.");
        Pointer(false, .49);
        Require(_bar.IsExpanded, "Must remain open for the first 0.49 seconds outside.");
        Pointer(true);
        Pointer(false, .49);
        Require(_bar.IsExpanded, "Re-entry must reset the delay.");
        Pointer(false, .02);
        Require(!_bar.IsExpanded, "Must retract after 0.5 seconds outside.");
        await Wait(.22);
        Require(!panel.Visible, "Collapsed controls must not intercept clicks.");
        Pointer(true, dragging: true);
        Require(!_bar.IsExpanded, "A window drag crossing the hot zone must not open the toolbar.");
        Pointer(false);
        Pointer(true);
        Pointer(false, .6, pressed: true);
        Require(_bar.IsExpanded, "A pressed button must retain its release event outside the bar.");
        Pointer(false, pressed: false);
        Require(!_bar.IsExpanded, "Releasing outside must allow retraction.");

        foreach (var locale in Locales)
        {
            L10n.SetLocale(locale, save: false);
            Pointer(true);
            await Wait(.22);
            var buttons = new[] { "Help", "Tutorial", "DesktopPet" }.Select(name => _bar.GetNode<Button>("Clip/Bar/Buttons/" + name)).ToArray();
            Require(panel.Size.X <= 1200 && panel.Size.Y <= 96,
                $"{locale}: fixed bar must not grow: {panel.Size}, minimum {panel.GetCombinedMinimumSize()}; "
                + string.Join("; ", buttons.Select(b => $"{b.Text} {b.Size}/{b.GetCombinedMinimumSize()}")));
            for (int i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                var rect = button.GetGlobalRect();
                Require(rect.Position.X >= 0 && rect.End.X <= 1200 && rect.End.Y <= 96, locale + ": button outside bar.");
                Require(button.Size.X >= button.GetCombinedMinimumSize().X, locale + ": button text clipped.");
                Require(!button.Text.StartsWith("PokerToolbar_"), locale + ": localization missing.");
                if (i > 0) Require(buttons[i - 1].GetGlobalRect().End.X <= rect.Position.X, locale + ": buttons overlap.");
                var font = button.GetThemeFont("font");
                foreach (var character in button.Text.Where(c => !char.IsWhiteSpace(c)))
                    Require(font.HasChar(character), $"{locale}: missing glyph U+{(int)character:X4}.");
            }
            await Capture(locale);
        }

        L10n.SetLocale("en", save: false);
        await Frames();
        await Click(_bar.GetNode<Button>("Clip/Bar/Buttons/Help"));
        Require(_help.IsOverlayVisible && !_bar.IsExpanded, "Help action must open help and retract the toolbar.");
        RequireCompactHotZone("Help as the first action");
        Require(_help.GetNode<Control>("Content/Scroll/Pages/Basics").Visible, "Help must open the basics page.");
        Require(!_bar.ContainsPoint(new Vector2(100, 50)), "Help overlay must release the toolbar hot zone.");
        await Click(_help.GetNode<Button>("Header/CloseButton"));
        Pointer(true);
        Require(!_bar.IsExpanded, "Closing help over the hot zone must not immediately reopen the toolbar.");
        Pointer(false);
        _bar.UpdatePointer(true, discoveryPoint, false, false, 0);
        Require(!_bar.IsExpanded && !_bar.ContainsPoint(discoveryPoint),
            "After first use, the old discovery-only region must neither reveal nor intercept.");
        Pointer(true);
        await Wait(.22);

        string settingsPath = ProjectSettings.GlobalizePath("user://settings.cfg");
        byte[] before = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : [];
        _bar.SetHasUsedFunction(false);
        await Click(_bar.GetNode<Button>("Clip/Bar/Buttons/Tutorial"));
        Require(_tutorial.IsOverlayVisible && !_bar.IsExpanded, "Tutorial action must show the existing guide.");
        RequireCompactHotZone("Tutorial as the first action");
        await Wait(2.05);
        await Click(_tutorial.GetNode<Control>("OverlayInput"));
        Require(!_tutorial.IsOverlayVisible, "The manual controls guide must dismiss after its two-second lock.");
        byte[] after = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : [];
        Require(before.SequenceEqual(after), "Opening and dismissing the guide must not write settings.");
        GD.Print("[PokerToolbar] Manual controls guide preserved settings file bytes.");

        Pointer(false);
        Pointer(true);
        await Wait(.22);
        _bar.SetHasUsedFunction(false);
        await Click(_bar.GetNode<Button>("Clip/Bar/Buttons/DesktopPet"));
        RequireCompactHotZone("Desktop as the first action");
        Require(_desktopRequests == 1 && !_bar.IsExpanded && !_bar.ContainsPoint(new Vector2(100, 50)),
            "Desktop action must fire once and disable poker controls.");
        Require(_behindClicks == 0, "Toolbar actions must not click through to the table.");
        _desktop = false;
        RefreshContext();
        Pointer(true);
        Require(!_bar.IsExpanded, "Returning from desktop must not automatically reopen the toolbar.");
        Pointer(false);
        _bar.UpdatePointer(true, discoveryPoint, false, false, 0);
        Require(!_bar.IsExpanded, "Returning to poker must retain the compact hot zone.");
        Pointer(true);
        Require(_bar.IsExpanded, "The actual bar region must still reveal the toolbar after first use.");
        _bar.UpdatePointer(true, discoveryPoint, false, false, .49);
        Require(_bar.IsExpanded, "The smaller region must preserve the 0.5-second hide delay.");
        _bar.UpdatePointer(true, discoveryPoint, false, false, .02);
        Require(!_bar.IsExpanded, "The removed discovery region now counts as outside.");
        Pointer(false, 60);
        Require(!_bar.IsIdleReminderActive && !_bar.IsExpanded, "A toolbar action permanently disables idle reminders.");
    }

    private void CheckIdleReminder()
    {
        _bar.SetHasUsedFunction(false);
        Pointer(false, 19.9);
        Require(!_bar.IsExpanded && !_tutorial.IsOverlayVisible, "Entry and the first 19.9 seconds must not show any guide.");
        Pointer(false, .2);
        Require(_bar.IsExpanded && _bar.IsIdleReminderActive && !_tutorial.IsOverlayVisible,
            "Idle time must reveal only the toolbar.");
        Pointer(false, 60);
        Pointer(true);
        Pointer(false, 1);
        Require(_bar.IsExpanded, "Idle reminder must persist through absence and hover/leave.");
        _bar.NotifyEffectiveInteraction();
        Pointer(false, .49);
        Require(_bar.IsExpanded && !_bar.IsIdleReminderActive, "Effective play must release the hold with the normal hide delay.");
        Pointer(false, .02);
        Require(!_bar.IsExpanded, "Effective play must allow normal retraction.");
        Pointer(false, 19.5);
        Require(_bar.IsIdleReminderActive, "A new idle period must remind again until a toolbar action is used.");
        _bar.SetReminderContext(false);
        Pointer(false, 60);
        Require(!_bar.IsIdleReminderActive && !_bar.IsExpanded, "Other interfaces suspend and clear the idle reminder.");
        _bar.SetReminderContext(true);
        Pointer(false, 19.9);
        Require(!_bar.IsExpanded, "Returning to the table must begin a fresh 20-second interval.");
        Pointer(false, .2, pressed: true);
        Require(!_bar.IsExpanded, "Reminder must wait for an in-progress mouse gesture.");
        Pointer(false);
        Require(_bar.IsIdleReminderActive, "An ineffective click must not reset the idle timer.");
        _bar.SetHasUsedFunction(true);
        Pointer(false, 60);
        Require(!_bar.IsIdleReminderActive && !_bar.IsExpanded, "Persisted completion must suppress idle reminders.");
        _bar.SetHasUsedFunction(false);
        _bar.SetReminderContext(false);
        _bar.SetPinnedOpen(true);
        Pointer(false, 60);
        _bar.NotifyEffectiveInteraction();
        Pointer(false, 1);
        Require(_bar.IsExpanded && !_bar.IsIdleReminderActive, "Welcome hold must remain separate from idle reminders.");
        _bar.SetPinnedOpen(false);
        Pointer(false, .6);
        _bar.SetReminderContext(true);
        GD.Print("[PokerToolbar] Repeatable idle reminder, holds, effective actions and context suspension passed.");
    }

    private void RequireCompactHotZone(string action)
    {
        Require(Math.Abs(_bar.GetNode<Control>("HotZone").Size.Y - _bar.GetNode<Control>("Clip/Bar").Size.Y) < .01,
            action + " must shrink the actual input control to the bar height, excluding its shadow.");
    }

    private static void CheckPersistence()
    {
        string run = Guid.NewGuid().ToString("N");
        var storage = AccountStorageContext.ForDevelopment("quickbar-" + run);
        var account = new AccountStateManager(storage);
        Require(!account.LoadPokerQuickBarUsed(), "Missing first-use flag must retain the discovery region.");
        account.SavePokerWelcomeAcknowledged();
        Require(!account.LoadPokerQuickBarUsed(), "Welcome acknowledgement must not imply using the toolbar.");
        account.SavePokerQuickBarUsed();
        var reloaded = new AccountStateManager(storage);
        Require(reloaded.LoadPokerQuickBarUsed() && reloaded.LoadPokerWelcomeAcknowledged(),
            "First use must survive a new account-state instance and preserve welcome acknowledgement.");
        Require(!new PokerWelcomeProgress(reloaded, sessionOnly: true).IsAcknowledged && reloaded.LoadPokerQuickBarUsed(),
            "The temporary welcome replay must not reset toolbar discovery progress.");
        var other = new AccountStateManager(AccountStorageContext.ForDevelopment("quickbar-other-" + run));
        Require(!other.LoadPokerQuickBarUsed(), "First use must not cross accounts.");
        other.SavePokerQuickBarUsed();
        Require(!other.LoadPokerWelcomeAcknowledged(), "Using the toolbar must not acknowledge the welcome.");
        var legacyComplete = new AccountStateManager(AccountStorageContext.ForDevelopment("quickbar-complete-" + run));
        Require(legacyComplete.LoadOrMigratePokerQuickBarUsed(false), "Completed legacy guidance must migrate to no reminder.");
        Require(!legacyComplete.LoadPokerWelcomeAcknowledged(), "Legacy guidance migration must not acknowledge the new welcome.");
        var legacyBeginner = new AccountStateManager(AccountStorageContext.ForDevelopment("quickbar-beginner-" + run));
        Require(!legacyBeginner.LoadOrMigratePokerQuickBarUsed(true), "Legacy beginners and fresh profiles retain discovery reminders.");
        Require(!legacyBeginner.LoadOrMigratePokerQuickBarUsed(false), "A recorded false value must not be overwritten by repeated migration.");
        Require(reloaded.LoadOrMigratePokerQuickBarUsed(true), "A genuine toolbar click must win over an old incomplete marker.");
        legacyComplete.SavePokerQuickBarUsed(false);
        Require(!legacyComplete.LoadOrMigratePokerQuickBarUsed(false), "Explicit debug reset must survive restart and migration.");
        CheckDiscoveryReplay(storage, account, run);
        GD.Print("[PokerToolbar] First-use persistence and independent welcome/account states passed.");
    }

    private static void CheckDiscoveryReplay(AccountStorageContext storage, AccountStateManager account, string run)
    {
        foreach (bool savedUsed in new[] { false, true })
        {
            account.SavePokerQuickBarUsed(savedUsed);
            string before = Godot.FileAccess.GetFileAsString(storage.AccountStatePath);
            var replay = new PokerQuickBarProgress(account, sessionOnly: true);
            replay.MigrateLegacyGuidance(false);
            Require(!replay.HasUsedFunction, "Every replay launch must start undiscovered, regardless of saved or legacy progress.");
            replay.RecordUse();
            Require(replay.HasUsedFunction, "Replay must still remember a toolbar action within the same run.");
            Require(!new PokerQuickBarProgress(account, sessionOnly: true).HasUsedFunction,
                "Restarting replay must restore discovery without a manual debug reset.");
            replay.Reset();
            Require(!replay.HasUsedFunction, "Manual replay reset must restore discovery in memory.");
            Require(Godot.FileAccess.GetFileAsString(storage.AccountStatePath) == before,
                "Replay load, migration, click and reset must leave the account file byte-for-byte unchanged.");
        }

        var untouchedStorage = AccountStorageContext.ForDevelopment("quickbar-replay-" + run);
        var untouchedAccount = new AccountStateManager(untouchedStorage);
        var freshReplay = new PokerQuickBarProgress(untouchedAccount, sessionOnly: true);
        freshReplay.MigrateLegacyGuidance(false);
        freshReplay.RecordUse();
        freshReplay.Reset();
        Require(!Godot.FileAccess.FileExists(untouchedStorage.AccountStatePath),
            "Replay must not create a persisted discovery flag on an account without one.");

        var formal = new PokerQuickBarProgress(untouchedAccount, sessionOnly: false);
        formal.MigrateLegacyGuidance(false);
        Require(formal.HasUsedFunction, "Disabling replay must restore the normal legacy migration.");
        formal.Reset();
        Require(!new PokerQuickBarProgress(untouchedAccount, sessionOnly: false).HasUsedFunction,
            "Formal reset must persist after disabling replay.");
        formal.RecordUse();
        Require(new PokerQuickBarProgress(untouchedAccount, sessionOnly: false).HasUsedFunction,
            "Formal first use must persist after disabling replay.");
        GD.Print("[PokerToolbar] Per-launch discovery replay, unchanged account files and formal persistence passed.");
    }

    private async Task Click(Control control)
    {
        var point = control.GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
        GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = true }, true);
        await Frames();
        GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = false }, true);
        await Frames();
    }

    private async Task Capture(string locale)
    {
        if (DisplayServer.GetName() == "headless") return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string directory = ProjectSettings.GlobalizePath("res://../.local-build/poker-quickbar");
        Directory.CreateDirectory(directory);
        Require(GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(directory, locale + ".png")) == Error.Ok, "Screenshot failed.");
    }

    private async Task Frames()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private async Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
