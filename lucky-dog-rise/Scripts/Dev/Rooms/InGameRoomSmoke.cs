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
            main.RoomSnapForSmoke(true);
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
            var unitScaleStep = Enumerable.Range(SettingsManager.DesktopPetScaleStepMin,
                SettingsManager.DesktopPetScaleStepMax - SettingsManager.DesktopPetScaleStepMin + 1)
                .Single(step => SettingsManager.GetDesktopPetScaleFactor(step) == 1);
            main.ApplyDesktopPetScaleStep(unitScaleStep);
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
            var remotes = client.View.Members.Where(member => member.Id != client.Id)
                .Select(member => main.RoomDesktopForSmoke.RemoteDogs.Single(dog => dog.MemberId == member.Id)).ToArray();
            var roomWindowPosition = DisplayServer.WindowGetPosition();
            var roomWindowSize = DisplayServer.WindowGetSize();
            var usable = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
            Check(Mathf.IsEqualApprox(localDog.GlobalPosition.X, usable.Position.X + usable.Size.X / 2f - roomWindowPosition.X),
                "observer starts at center regardless of host identity");
            Check(Mathf.IsEqualApprox(remotes[0].Position.X - localDog.GlobalPosition.X, panel.PanelSize.X)
                && Mathf.IsEqualApprox(localDog.GlobalPosition.X - remotes[1].Position.X, panel.PanelSize.X),
                "remote dogs start right then left at displayed panel-width intervals");
            Check(remotes.All(dog => dog.GetParent() is CanvasLayer { Layer: < 0 }),
                "all remote parts including absolute-Z claws and tongue render beneath the local dog");
            var contentA = main.GetNode<Node2D>("BossKeyContent/ContentA");
            Check(panel.PanelRect.Position.Y > 0 && Mathf.IsEqualApprox(panel.PanelRect.End.Y, contentA.Position.Y),
                "room panel uses ordinary 1x slot 8 spacing above dog, not screen top");
            await Capture(main, "in-game-room-three-dogs");
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
            main.ApplyDesktopPetScaleStep(unitScaleStep);
            await Frame();
            var localCenter = DisplayServer.WindowGetPosition() + (Vector2I)localDog.GlobalPosition;
            Check(main.RoomHitTestForSmoke(localCenter), "original dog hit area follows room layout");
            Check(!main.RoomHitTestForSmoke(DisplayServer.WindowGetPosition() + new Vector2I(5, 5)),
                "transparent empty desktop remains click-through");
            // Feed real viewport mouse events through ModeManager._Input. Moving
            // the OS pointer would disturb the developer's current desktop work.
            void PointerButton(Vector2 point, bool pressed) => main.GetViewport().PushInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left, Pressed = pressed, Position = point, GlobalPosition = point,
                ButtonMask = pressed ? MouseButtonMask.Left : 0,
            }, true);
            void PointerMotion(Vector2 point, bool held = true) => main.GetViewport().PushInput(new InputEventMouseMotion
            {
                Position = point, GlobalPosition = point, ButtonMask = held ? MouseButtonMask.Left : 0,
            }, true);
            async Task Drag(Vector2 dogPosition, Vector2 delta)
            {
                var point = dogPosition + new Vector2(0, -40 * localDog.GlobalScale.X / 0.25f);
                PointerButton(point, true);
                PointerMotion(point + delta);
                await Frame();
                Check(main.RoomDraggingForSmoke && !main.RoomClickThroughForSmoke,
                    "active drag captures movement across transparent desktop");
                PointerButton(point + delta, false);
                await Frame();
                Check(!main.RoomDraggingForSmoke, "mouse release stops individual drag");
            }
            var ownStart = localDog.GlobalPosition;
            var panelStart = panel.PanelRect.Position;
            var remoteStarts = remotes.Select(dog => dog.Position).ToArray();
            for (int i = 0; i < remotes.Length; i++)
            {
                var point = roomWindowPosition + (Vector2I)(remotes[i].Position + new Vector2(0, -40));
                Check(main.RoomHitTestForSmoke(point), "remote dog participates in native click-through hit test");
                var delta = new Vector2(i == 0 ? 90 : -90, -170);
                await Drag(remotes[i].Position, delta);
                Check(remotes[i].Position.IsEqualApprox(remoteStarts[i] + delta)
                    && localDog.GlobalPosition.IsEqualApprox(ownStart) && panel.PanelRect.Position.IsEqualApprox(panelStart),
                    "remote drag only moves the chosen dog and leaves own dog/panel fixed");
            }
            var remotePositions = remotes.Select(dog => dog.Position).ToArray();
            await Drag(ownStart, new Vector2(-130, -210));
            Check(localDog.GlobalPosition.IsEqualApprox(ownStart + new Vector2(-130, -210))
                && remotes.Select((dog, i) => dog.Position.IsEqualApprox(remotePositions[i])).All(equal => equal),
                "local drag leaves both remote positions unchanged");
            Check(!panel.PanelRect.Position.IsEqualApprox(panelStart)
                && panel.PanelRect.Position.X >= usable.Position.X - roomWindowPosition.X
                && panel.PanelRect.End.X <= usable.End.X - roomWindowPosition.X,
                "own panel follows dragged local dog and remains within work-area width");
            Check(Mathf.IsEqualApprox(panel.PanelRect.Position.Y, panelStart.Y - 210)
                && Mathf.IsEqualApprox(panel.PanelRect.End.Y, contentA.Position.Y),
                "slot 8 follows local dog vertically without changing its normal gap");
            await Drag(remotes[0].Position, localDog.GlobalPosition + new Vector2(35, 15) - remotes[0].Position);
            remotes[0].Modulate = new Color(0.5f, 0.7f, 1);
            await Capture(main, "in-game-room-overlap");
            var overlappedRemote = remotes[0].Position;
            var ownOverlap = localDog.GlobalPosition;
            await Drag(ownOverlap, new Vector2(0, -85));
            Check(localDog.GlobalPosition.IsEqualApprox(ownOverlap + new Vector2(0, -85))
                && remotes[0].Position.IsEqualApprox(overlappedRemote), "overlap picks local dog first");
            Check(DisplayServer.WindowGetPosition() == roomWindowPosition && DisplayServer.WindowGetSize() == roomWindowSize,
                "all individual drags leave the native room window fixed");
            // Losing button state must not leave a dog attached to the mouse.
            var remoteHead = remotes[1].Position + new Vector2(0, -40);
            PointerButton(remoteHead, true);
            PointerMotion(remoteHead + new Vector2(20, 0));
            PointerMotion(remoteHead + new Vector2(60, 0), held: false);
            await Frame();
            Check(!main.RoomDraggingForSmoke, "missing release recovers from mouse button mask");
            var draggedOwn = localDog.GlobalPosition;
            var draggedRemotes = remotes.Select(dog => dog.Position).ToArray();
            client.SetAppearance(client.SkinId, client.HeadwearId, client.Reaction);
            await Settle();
            Check(localDog.GlobalPosition.IsEqualApprox(draggedOwn)
                && remotes.Select((dog, i) => dog.Position.IsEqualApprox(draggedRemotes[i])).All(equal => equal),
                "room snapshot refresh preserves dragged positions");
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
            Check(localDog.GlobalPosition.IsEqualApprox(draggedOwn)
                && remotes.Select((dog, i) => dog.Position.IsEqualApprox(draggedRemotes[i])).All(equal => equal),
                "tab switches and closing/reopening panel preserve dragged positions");
            main.RoomModeForSmoke(true);
            await main.ToSignal(tree.CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
            main.RoomModeForSmoke(false);
            await main.ToSignal(tree.CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
            Check(localDog.GlobalPosition.IsEqualApprox(draggedOwn)
                && remotes.Select((dog, i) => dog.Position.IsEqualApprox(draggedRemotes[i])).All(equal => equal),
                "poker round-trip preserves each dragged position");
            panel.Open();
            await Frame();
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
            var desktop = main.RoomDesktopForSmoke;
            remotes = client.View.Members.Where(member => member.Id != client.Id)
                .Select(member => desktop.RemoteDogs.Single(dog => dog.MemberId == member.Id)).ToArray();
            float taskbarOffset = (main.GetNode<Marker2D>("BossKeyContent/ContentA/TaskBar").Position.Y - localDog.Position.Y);
            float TaskbarY() => DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen()).End.Y
                - DisplayServer.WindowGetPosition().Y;
            Vector2 DogPosition(int id) => id == client.Id ? localDog.GlobalPosition
                : remotes.Single(dog => dog.MemberId == id).Position;
            foreach (int id in client.View.Members.Select(member => member.Id))
            {
                Check(desktop.IsTaskbarSnapped(id) && Mathf.IsEqualApprox(DogPosition(id).Y + taskbarOffset, TaskbarY()),
                    "each dog initially attaches its own anchor to the taskbar");
                var others = client.View.Members.Where(m => m.Id != id).ToDictionary(m => m.Id, m => DogPosition(m.Id));
                var position = DogPosition(id);
                await Drag(position, new Vector2(20, -20));
                Check(desktop.IsTaskbarSnapped(id) && Mathf.IsEqualApprox(DogPosition(id).Y, position.Y),
                    "attached dog slides horizontally and resists small upward drags");
                await Drag(DogPosition(id), new Vector2(0, -50));
                Check(!desktop.IsTaskbarSnapped(id) && Mathf.IsEqualApprox(DogPosition(id).Y, position.Y - 50),
                    "dragging up beyond ordinary threshold detaches this dog");
                await Drag(DogPosition(id), new Vector2(0, 42));
                Check(desktop.IsTaskbarSnapped(id) && Mathf.IsEqualApprox(DogPosition(id).Y + taskbarOffset, TaskbarY()),
                    "dragging near taskbar snaps this dog back and release retains attachment");
                Check(others.All(other => DogPosition(other.Key).IsEqualApprox(other.Value)),
                    "snapping one dog never moves other members");
            }
            main.ApplyDesktopPetScaleStep(twiceScaleStep);
            await Frame();
            Check(client.View.Members.All(member => desktop.IsTaskbarSnapped(member.Id)
                && Mathf.IsEqualApprox(DogPosition(member.Id).Y + taskbarOffset * 2, TaskbarY())),
                "2x keeps every attached dog on the taskbar");
            await Capture(main, "in-game-room-taskbar-2x");
            main.ApplyDesktopPetScaleStep(unitScaleStep);
            await Frame();
            main.RoomSnapForSmoke(false);
            await Frame();
            foreach (int id in client.View.Members.Select(member => member.Id))
            {
                var before = DogPosition(id);
                await Drag(before, new Vector2(10, -8));
                Check(!desktop.IsTaskbarSnapped(id) && DogPosition(id).IsEqualApprox(before + new Vector2(10, -8)),
                    "disabled snap permits free movement near taskbar for every dog");
            }
            main.RoomSnapForSmoke(true);
            foreach (int id in client.View.Members.Select(member => member.Id))
                await Drag(DogPosition(id), new Vector2(10, 0));
            panel.Open();
            await Frame();
            var beforeEdge = localDog.GlobalPosition;
            await Drag(beforeEdge, new Vector2(25 - beforeEdge.X, 0));
            Check(panel.PanelRect.Position.X >= -5 && panel.PanelRect.End.X <= DisplayServer.WindowGetSize().X + 5,
                "near left edge room panel uses the ordinary alternate slots without leaving the screen");
            await Capture(main, "in-game-room-panel-edge");
            await Drag(localDog.GlobalPosition, beforeEdge - localDog.GlobalPosition);
            await Capture(main, "in-game-room-taskbar");
            // Deliver a newer model between presentation and the next input,
            // reproducing the child service tick occurring after the host tick.
            var beforeDeparture = client.View;
            var departing = main.RoomDesktopForSmoke.RemoteDogs.First();
            var staleHead = departing.Position + new Vector2(0, -40);
            client.Receive(beforeDeparture with
            {
                Revision = beforeDeparture.Revision + 1,
                Members = beforeDeparture.Members.Where(m => m.Id != departing.MemberId).ToArray(),
            });
            PointerButton(staleHead, true);
            PointerMotion(staleHead + new Vector2(40, 0));
            Check(!main.RoomDraggingForSmoke, "clicking a departed member's stale node is ignored");
            PointerButton(staleHead, false);
            client.Receive(beforeDeparture with { Revision = beforeDeparture.Revision + 2 });
            await Frame();
            PointerButton(staleHead, true);
            PointerMotion(staleHead + new Vector2(40, 0));
            Check(main.RoomDraggingForSmoke, "begin drag before session ends");
            client.Leave();
            PointerMotion(staleHead + new Vector2(80, 0));
            PointerButton(staleHead, false);
            Check(!main.RoomDraggingForSmoke, "session ending cancels drag before presentation catches up");
            await Settle();
            // An initial snapshot may also arrive before seats exist.
            client.BeginSession(beforeDeparture.Code);
            await Frame();
            client.Receive(beforeDeparture);
            var newLocalHead = localDog.GlobalPosition + new Vector2(0, -40);
            PointerButton(newLocalHead, true);
            PointerMotion(newLocalHead + new Vector2(40, 0));
            PointerButton(newLocalHead, false);
            Check(!main.RoomDraggingForSmoke, "new-session input waits for matching presentation seats");
            await Frame();
            GD.Print("[InGameRoomSmoke] PASS initial localization, room UI, centered seats, independent dragging, overlap priority, panel avoidance, local scale, transparent hit areas, window restoration, hidden state and poker round-trip.");
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
