#if DEBUG && !RECORDING_BUILD
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace LuckyDogRise.Rooms;

// Runs the real main scene and settings panel in the existing memory-only Steam
// simulation. A separate instance scope prevents disturbing an open Dev game.
public static class InGameRoomSmoke
{
    public static bool DemoRequested => OS.GetCmdlineUserArgs().Contains("--rooms-page-demo-smoke");
    public static bool Requested => DemoRequested || OS.GetCmdlineUserArgs().Contains("--rooms-page-smoke");
    public static async void Run(ModeManager main)
    {
        try
        {
            var tree = main.GetTree();
            async Task Frame()
            {
                await main.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                await main.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
            await main.ToSignal(tree.CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
            var panel = main.SettingsPanelObj;
            panel.Open();
            await Frame();
            var title = panel.GetNode<HBoxContainer>("Panel/RootVBox/TitleRow");
            var tab = title.GetNodeOrNull<Button>("RoomTab");
            if (DemoRequested)
            {
                Check(tab == null, "Demo room tab absent");
                Check(panel.GetNodeOrNull("Panel/RootVBox/Scroll/ContentVBox/RoomContent") == null,
                    "Demo preview host absent");
                GD.Print("[InGameRoomSmoke] PASS Demo: room entry and preview absent.");
                tree.Quit();
                return;
            }
            Check(tab is { Visible: true }, "room tab visible in the main game");
            tab.EmitSignal(BaseButton.SignalName.Pressed);
            await Frame();
            var host = panel.GetNode<VBoxContainer>("Panel/RootVBox/Scroll/ContentVBox/RoomContent");
            var page = host.GetChild<InGameRoomPreview>(0);
            var client = page.PreviewClient;
            async Task Settle()
            {
                page.AdvancePreview(0.5);
                await Frame();
            }
            void Click(string path) => page.GetNode<Button>(path).EmitSignal(BaseButton.SignalName.Pressed);
            await Settle();
            Check(client.Listings.Length == 1 && client.Listings[0].Count == 2, "initial directory");
            Check(client.JoinedCode.Length == 0, "browsing does not join automatically");
            // Do this before any SetLocale call: switching language refreshes
            // Godot's text caches and would conceal a first-open regression.
            var create = page.GetNode<Button>("Lobby/CreateRow/Create");
            var translatedCreate = L10n.Tr(create.Text);
            Check(translatedCreate != create.Text, "initial room translations registered");
            var expectedCreate = new Button
            {
                Text = translatedCreate,
                ThemeTypeVariation = create.ThemeTypeVariation,
                AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
                Visible = false,
            };
            create.GetParent().AddChild(expectedCreate);
            await Frame();
            Check(Mathf.IsEqualApprox(create.GetMinimumSize().X, expectedCreate.GetMinimumSize().X),
                "first-open button shaped with translated text before changing language");
            expectedCreate.Free();
            await Capture(main, "in-game-lobby-first-open");
            foreach (var locale in new[] { "zh_CN", "en" })
            {
                L10n.SetLocale(locale, save: false);
                await Frame();
                var close = title.GetNode<Button>("CloseBtn");
                Check(close.GetGlobalRect().End.X <= panel.PanelRect.End.X + 1, "all tabs and Close fit");
                Check(page.Size.X <= panel.PanelSize.X, "room content width fits");
                await Capture(main, "in-game-lobby-" + locale);
            }
            var row = page.GetNode<VBoxContainer>("Lobby/RoomList").GetChild(0);
            var originalWindowSize = DisplayServer.WindowGetSize();
            var originalWindowPosition = DisplayServer.WindowGetPosition();
            var localDog = main.GetNode<DogVisual>("BossKeyContent/ContentA/DogArea");
            row.GetNode<Button>("Join").EmitSignal(BaseButton.SignalName.Pressed);
            await Settle();
            Check(client.View?.Members.Length == 3, "join from directory yields three members");
            Check(page.GetNode<Control>("Room").Visible, "joined page visible");
            Check(main.RoomDesktopWindowActive && main.RoomDesktopForSmoke.RemoteDogs.Count == 2,
                "desktop contains two remote dogs alongside the existing local dog");
            Check(DisplayServer.WindowGetSize() == DisplayServer.ScreenGetSize(DisplayServer.WindowGetCurrentScreen()) - Vector2I.One * 2
                && DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Windowed,
                "room host covers display with a one-pixel margin and stays windowed");
            Check(main.RoomDesktopForSmoke.RemoteDogs.All(dog => dog.IsVisibleInTree() && dog.Dog.GameData == null),
                "remote dogs visible and isolated from player state");
            await Capture(main, "in-game-room-three-dogs");
            var originalScaleStep = SettingsManager.LoadDesktopPetScaleStep();
            var twiceScaleStep = Enumerable.Range(SettingsManager.DesktopPetScaleStepMin,
                SettingsManager.DesktopPetScaleStepMax - SettingsManager.DesktopPetScaleStepMin + 1)
                .Single(step => SettingsManager.GetDesktopPetScaleFactor(step) == 2);
            main.ApplyDesktopPetScaleStep(twiceScaleStep);
            await Frame();
            Check(main.GetNode<Node2D>("BossKeyContent/ContentA").Scale.X == 2
                && main.RoomDesktopForSmoke.RemoteDogs.All(dog => dog.Scale.X == 2), "2x applies to every dog locally");
            Check(DisplayServer.WindowGetPosition() == DisplayServer.ScreenGetPosition(DisplayServer.WindowGetCurrentScreen()) + Vector2I.One,
                "scaling keeps the room host on its display");
            await Capture(main, "in-game-room-three-dogs-2x");
            main.ApplyDesktopPetScaleStep(originalScaleStep);
            await Frame();
            var localCenter = DisplayServer.WindowGetPosition() + (Vector2I)localDog.GlobalPosition;
            Check(main.RoomHitTestForSmoke(localCenter), "original dog hit area follows room layout");
            Check(!main.RoomHitTestForSmoke(DisplayServer.WindowGetPosition() + new Vector2I(5, 5)),
                "transparent empty desktop remains click-through");
            var codeLabel = page.GetNode<Label>("Room/CodeRow/Code");
            Check(codeLabel.Text == client.JoinedCode && codeLabel.Size.Y >= 20, "room code has visible text and height");
            var joinedCode = client.JoinedCode;
            title.GetNode<Button>("OutfitPresetTab").EmitSignal(BaseButton.SignalName.Pressed);
            tab.EmitSignal(BaseButton.SignalName.Pressed);
            await Frame();
            Check(ReferenceEquals(host.GetChild(0), page) && client.JoinedCode == joinedCode,
                "switching tabs preserves the room client");
            panel.Close();
            await main.ToSignal(tree.CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
            Check(main.RoomDesktopForSmoke.RemoteDogs.All(dog => dog.IsVisibleInTree()), "closing room panel keeps dogs visible");
            panel.Open();
            await main.ToSignal(tree.CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
            Check(client.JoinedCode == joinedCode, "closing the panel preserves membership");
            tab.EmitSignal(BaseButton.SignalName.Pressed);
            L10n.SetLocale("zh_CN", save: false);
            await Frame();
            await Capture(main, "in-game-room-zh_CN");
            Click("Room/Browse");
            await Settle();
            Check(client.JoinedCode == joinedCode && page.GetNode<Control>("Lobby").Visible,
                "browsing keeps current membership");
            Click("Lobby/Return");
            await Frame();
            Click("Room/Leave");
            await Settle();
            Check(client.JoinedCode.Length == 0, "leave room");
            Check(!main.RoomDesktopWindowActive && main.RoomDesktopForSmoke.RemoteDogs.Count == 0,
                "leave removes remote dogs");
            Check(DisplayServer.WindowGetSize() == originalWindowSize
                && DisplayServer.WindowGetPosition() == originalWindowPosition,
                $"leave restores the single-dog window: expected {originalWindowPosition}/{originalWindowSize}, actual {DisplayServer.WindowGetPosition()}/{DisplayServer.WindowGetSize()}");
            page.GetNode<LineEdit>("Lobby/CreateRow/Name").Text = "UI regression";
            Click("Lobby/CreateRow/Create");
            await Settle();
            Check(client.View?.Members.Length == 1 && client.View.OwnerId == client.Id, "create own room");
            var ownCode = client.JoinedCode;
            Click("Room/Browse");
            await Settle();
            page.GetNode<LineEdit>("Lobby/JoinRow/Code").Text = "NOTFOUND";
            Click("Lobby/JoinRow/Join");
            await Settle();
            Check(client.Failure == RoomFailure.NotFound && client.JoinedCode == ownCode,
                "failed join keeps original room");
            Click("Lobby/Header/Refresh");
            await Frame();
            Check(page.GetNode<Button>("Lobby/CreateRow/Create").Disabled, "pending disables duplicates");
            Click("Cancel");
            await Settle();
            Check(!client.IsBusy && !page.GetNode<Button>("Lobby/CreateRow/Create").Disabled,
                "cancel restores controls");
            Click("Lobby/Random");
            await Settle();
            await Settle();
            Check(client.JoinedCode == joinedCode && client.View?.Members.Length == 3, "random join");
            Click("Room/Leave");
            await Settle();
            page.GetNode<LineEdit>("Lobby/JoinRow/Code").Text = joinedCode;
            Click("Lobby/JoinRow/Join");
            await Settle();
            Check(client.JoinedCode == joinedCode, "join by room code");
            main.RoomFullscreenHideForSmoke(true);
            await Frame();
            Check(main.RoomDesktopForSmoke.RemoteDogs.All(dog => !dog.IsVisibleInTree()), "fullscreen hide includes remote dogs");
            main.RoomFullscreenHideForSmoke(false);
            await Frame();
            Check(main.RoomDesktopForSmoke.RemoteDogs.All(dog => dog.IsVisibleInTree()), "fullscreen recovery restores remote dogs");
            main.RoomModeForSmoke(true);
            await main.ToSignal(tree.CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
            Check(!main.RoomDesktopWindowActive && main.RoomDesktopForSmoke.RemoteDogs.All(dog => !dog.IsVisibleInTree()),
                "poker uses its own window and hides room dogs");
            main.RoomModeForSmoke(false);
            await main.ToSignal(tree.CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
            Check(main.RoomDesktopWindowActive && main.RoomDesktopForSmoke.RemoteDogs.All(dog => dog.IsVisibleInTree()),
                "returning to desktop restores joined room presentation");
            GD.Print("[InGameRoomSmoke] PASS initial localization, room UI, three desktop dogs, local scale, transparent hit areas, window restoration, hidden state and poker round-trip.");
            tree.Quit();
        }
        catch (Exception exception)
        {
            GD.PushError("[InGameRoomSmoke] FAIL: " + exception);
            main.GetTree().Quit(1);
        }
    }
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
    }
    private static async Task Capture(ModeManager main, string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var folder = ProjectSettings.GlobalizePath("res://../.local-build/room-lab");
        Directory.CreateDirectory(folder);
        main.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder, name + ".png"));
    }
}
#endif
