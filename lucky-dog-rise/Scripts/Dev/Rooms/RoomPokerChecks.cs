#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace LuckyDogRise.Rooms;

internal static class RoomPokerChecks
{
    public static void Layout()
    {
        RoomDesktopBoundsChecks.Geometry();
        foreach (var screen in new[] { new Vector2I(1280, 680), new Vector2I(1920, 1040), new Vector2I(3840, 2080), new Vector2I(1080, 1880) })
        foreach (float ui in new[] { .75f, 1f, 1.25f, 1.5f, 2f })
        foreach (bool compact in new[] { false, true })
        {
            var available = new Vector2I(compact ? screen.X / 2 : screen.X, screen.Y);
            float scale = RoomPokerLayout.FitScale(available, ui, 246, 600, 80);
            Check(scale <= ui && scale > 0 && Mathf.CeilToInt(246 * scale) + Mathf.CeilToInt(600 * scale) + 10 <= available.X
                && Mathf.CeilToInt(600 * scale) + 90 <= available.Y,
                "entire poker content fits " + screen + "/" + ui + "/" + compact, quiet: true);
        }
        GD.Print("[RoomPokerChecks] PASS poker fit matrix: 4 resolutions x 5 UI scales x 2 host sizes.");
    }

    public static async Task Run(ModeManager main, InGameRoomPreview page)
    {
        Layout();
        var tree = main.GetTree();
        var panel = main.SettingsPanelObj;
        var client = page.PreviewClient;
        var host = page.MockClientForSmoke(2);
        var guest = page.MockClientForSmoke(3);
        async Task Wait(double seconds = .15) => await main.ToSignal(tree.CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        async Task Step(double seconds = .5) { page.AdvancePreview(seconds); await Wait(); }
        static void Click(Button button) => button.EmitSignal(BaseButton.SignalName.Pressed);
        static bool HeadClipped(DogVisual dog) =>
            ((ShaderMaterial)dog.GetNode<Sprite2D>("HeadRoot/Head").Material).GetShaderParameter("clip_enabled").AsBool();
        async Task Capture(string name)
        {
            await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var folder = ProjectSettings.GlobalizePath("res://../.local-build/room-poker");
            Directory.CreateDirectory(folder);
            main.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder, name + ".png"));
        }
        L10n.SetLocale("zh_CN", save: false);
        main.ApplyDesktopPetScaleStep(1);
        main.RoomSnapForSmoke(true);
        await JoinWhilePlaying(main, page);
        SelectMode(page, RoomDisplayMode.HalfScreenCompact);
        client.Join(host.JoinedCode);
        await Step();
        foreach (int id in new[] { 4, 5, 6 }) page.AddMockClientForSmoke(id).Join(host.JoinedCode);
        await Step();
        panel.CloseImmediate();
        var desktop = main.RoomDesktopForSmoke;
        var remote = desktop.RemoteDogs.First(d => d.MemberId == guest.Id);
        desktop.MoveMember(guest.Id, remote.Position + new Vector2(-40, -100), 0, false, 0, 0);
        await Wait();
        Check(!HeadClipped(remote.Dog), "desktop dog away from taskbar shows its full chin");
        var original = ScreenPositions(desktop);
        var compactHost = new Rect2I(DisplayServer.WindowGetPosition(), DisplayServer.WindowGetSize());
        long session = client.LayoutSession;
        main.RoomRequestPokerForSmoke();
        await CheckHandoff(main, original, compactHost, poker: true);
        Check(main.CurrentMode == ModeManager.Mode.Play && main.RoomPokerDisplayForSmoke == RoomPokerDisplay.Free
            && !panel.HasRoomModal, "saved compact preference enters poker without a layout dialog");
        var game = main.GetNode<GameManager>("PlayRoot/SubViewportContainer/SubViewport/Main");
        var welcome = game.GetNode<PokerWelcomeDialogController>("PokerWelcomeDialog");
        if (welcome.Visible) Click(welcome.GetNode<Button>("InputShield/Center/Panel/Margin/Content/Confirm"));
        await Wait();
        var compact = main.RoomPokerDogsForSmoke;
        Check(compact.RemoteDogs.Count == 5 && compact.Visible && !compact.LocalChat.Visible, "compact host shows all five remote dogs");
        Check(!HeadClipped(compact.RemoteDogs.First(d => d.MemberId == guest.Id).Dog), "compact poker preserves detached dog chin");
        Check(HeadClipped(game.GetNode<DogVisual>("DogArea")), "player's poker dog retains table clipping");
        var table = main.GetNode<SubViewportContainer>("PlayRoot/SubViewportContainer");
        var bSize = DisplayServer.WindowGetSize();
        Check(bSize.X == (DisplayServer.ScreenGetSize().X - 2) / 2 && bSize.Y == DisplayServer.ScreenGetSize().Y - 2
            && client.LayoutSession == session && client.View.GameId == "social",
            "compact poker retains the desktop half-screen host and room session");
        await CheckCompactMovement(main, compact, client, panel, Wait);
        await Capture("compact-free");
        var watched = compact.RemoteDogs.First(d => d.MemberId == host.Id);
        host.SetReaction(1002);
        await Step();
        Check((int)watched.Dog.CurrentReaction == 1002, "remote reactions keep synchronizing in poker");
        guest.Leave(); await Step();
        Check(compact.RemoteDogs.Count == 4 && DisplayServer.WindowGetSize() == bSize, "departure removes dog without resizing host");
        guest.Join(host.JoinedCode); await Step();
        Check(compact.RemoteDogs.Count == 5 && DisplayServer.WindowGetSize() == bSize, "new member returns without resizing host");
        main.GetNode<SteamMockPanelController>("SteamMockPanel").EmitSignal(SteamMockPanelController.SignalName.CloseRequested);
        await Wait();
        Check(DisplayServer.WindowGetSize() == bSize && compact.RemoteDogs.All(d => d.IsVisibleInTree()),
            "hiding Debug toolbar keeps the host and free companions");
        var compactPositions = ScreenPositions(compact);
        compactHost = new Rect2I(DisplayServer.WindowGetPosition(), DisplayServer.WindowGetSize());
        main.RoomModeForSmoke(false);
        await CheckHandoff(main, compactPositions, compactHost, poker: false);
        main.RoomRequestPokerForSmoke();
        await CheckHandoff(main, compactPositions, compactHost, poker: true);
        main.RoomModeForSmoke(false);
        await CheckHandoff(main, compactPositions, compactHost, poker: false);
        panel.CloseImmediate();
        client.Leave(); await Step();
        SelectMode(page, RoomDisplayMode.FullScreenFree);
        client.Join(host.JoinedCode); await Step();
        panel.CloseImmediate();
        var fullHost = new Rect2I(DisplayServer.WindowGetPosition(), DisplayServer.WindowGetSize());
        var fullPositions = ScreenPositions(desktop);
        main.RoomRequestPokerForSmoke();
        await CheckHandoff(main, fullPositions, fullHost, poker: true);
        var cHost = DisplayServer.WindowGetPosition();
        Check(DisplayServer.WindowGetSize() == DisplayServer.ScreenGetSize() - Vector2I.One * 2,
            "C is monitor-sized with the ordinary-window margin");
        var poker = main.RoomPokerDogsForSmoke;
        var dogView = poker.RemoteDogs.First(d => d.MemberId == host.Id);
        var oldDog = dogView.Position;
        int screenIndex = DisplayServer.WindowGetCurrentScreen();
        var usable = DisplayServer.ScreenGetUsableRect(screenIndex);
        bool hasBottomTaskbar = usable.End.Y < DisplayServer.ScreenGetPosition(screenIndex).Y
            + DisplayServer.ScreenGetSize(screenIndex).Y;
        Check(HeadClipped(dogView.Dog) == hasBottomTaskbar, "C clips at an actual bottom taskbar only");
        var otherClipping = poker.RemoteDogs.Where(d => d != dogView)
            .ToDictionary(d => d.MemberId, d => HeadClipped(d.Dog));
        Check(main.RoomHitTestForSmoke(cHost + (Vector2I)(oldDog + new Vector2(0, -40))), "C dog participates in click-through hit map");
        Check(!main.RoomHitTestForSmoke(cHost + new Vector2I(5, DisplayServer.ScreenGetSize().Y / 2)),
            "C transparent edge remains click-through (outside the Debug toolbar)");
        void Pointer(Vector2 p, bool down) => main.GetViewport().PushInput(new InputEventMouseButton
        { ButtonIndex = MouseButton.Left, Pressed = down, Position = p, GlobalPosition = p }, true);
        var point = oldDog + new Vector2(0, -40);
        Pointer(point, true);
        main.GetViewport().PushInput(new InputEventMouseMotion { Position = point + new Vector2(30, -70),
            GlobalPosition = point + new Vector2(30, -70), ButtonMask = MouseButtonMask.Left }, true);
        await Wait();
        Pointer(point + new Vector2(30, -70), false);
        await Wait();
        Check(!dogView.Position.IsEqualApprox(oldDog) && DisplayServer.WindowGetPosition() == cHost,
            "C dog drag moves one dog without moving native host");
        Check(!HeadClipped(dogView.Dog), "C drag away from taskbar reveals the full chin");
        Check(poker.RemoteDogs.Where(d => d != dogView).All(d => HeadClipped(d.Dog) == otherClipping[d.MemberId]),
            "dragging one dog does not change other dogs' clipping");
        var floatingDog = dogView.Position;
        poker.MoveMember(dogView.MemberId, oldDog, oldDog.Y, true, 20, 30);
        await Wait();
        Check(HeadClipped(dogView.Dog) == hasBottomTaskbar, "C resnapping restores taskbar clipping");
        poker.MoveMember(dogView.MemberId, floatingDog, oldDog.Y, true, 20, 30);
        await Wait();
        Check(!HeadClipped(dogView.Dog) && HeadClipped(game.GetNode<DogVisual>("DogArea")),
            "C floating dog and player's table dog keep independent clipping");
        var oldTable = table.Position;
        var draggedDog = dogView.Position;
        main.RoomMovePokerTableForSmoke(new Vector2I(40, -30));
        await Wait();
        Check(!table.Position.IsEqualApprox(oldTable) && DisplayServer.WindowGetPosition() == cHost
            && dogView.Position.IsEqualApprox(draggedDog), "C table moves inside fixed host without moving dogs");
        main.Call("ToggleSettingsPanel");
        panel.ShowRoomPage();
        await Wait();
        await Capture("desktop");
        panel.CloseImmediate();
        foreach (int step in Enumerable.Range(SettingsManager.OtherUiScaleStepMin,
            SettingsManager.OtherUiScaleStepMax - SettingsManager.OtherUiScaleStepMin + 1))
        {
            main.RoomOtherScaleForSmoke(step);
            await Wait(.05);
            Check(DisplayServer.WindowGetPosition() == cHost
                && DisplayServer.WindowGetSize() == DisplayServer.ScreenGetSize() - Vector2I.One * 2,
                "C UI-scale preview keeps fixed host", quiet: true);
        }
        main.RoomOtherScaleForSmoke(Enumerable.Range(SettingsManager.OtherUiScaleStepMin,
                SettingsManager.OtherUiScaleStepMax - SettingsManager.OtherUiScaleStepMin + 1)
            .First(step => SettingsManager.GetOtherUiScaleFactor(step) == 1));
        await Wait();
        Check(!UiInteractionOccupancy.IsOccupied(main), "no stale overlay occupancy after layout changes");
        // Even a deliberately overlapping position must not be automatically reseated on re-entry.
        poker.MoveMember(dogView.MemberId, table.Position + new Vector2(240, 120), 0, false, 0, 0);
        await Wait();
        var sharedPositions = ScreenPositions(poker);
        var sharedSnaps = poker.RemoteDogs.ToDictionary(d => d.MemberId, d => poker.IsTaskbarSnapped(d.MemberId));
        var localPosition = desktop.GetPosition(client.Id);
        await Capture("c-positions-before");
        main.RoomModeForSmoke(false);
        await CheckHandoff(main, sharedPositions, fullHost, poker: false);
        await Capture("c-positions-desktop");
        Check(desktop.RemoteDogs.Count == sharedPositions.Count && desktop.RemoteDogs.All(d => d.IsVisibleInTree()),
            "all C dogs remain visible after the desktop handoff");
        using (var rendered = main.GetViewport().GetTexture().GetImage())
        {
            foreach (var dog in desktop.RemoteDogs)
            {
                var bar = dog.GetNode<PanelContainer>("NameBar");
                var center = bar.GetGlobalTransformWithCanvas() * (bar.Size / 2);
                Check(rendered.GetPixel(Mathf.RoundToInt(center.X), Mathf.RoundToInt(center.Y)).A > .5f,
                    "desktop handoff actually draws member " + dog.MemberId + "'s name bar");
            }
        }
        Check(ScreenPositions(desktop).All(pair => pair.Value.IsEqualApprox(sharedPositions[pair.Key])),
            "C to desktop preserves every remote dog's screen position");
        Check(desktop.RemoteDogs.All(d => desktop.IsTaskbarSnapped(d.MemberId) == sharedSnaps[d.MemberId])
            && !HeadClipped(desktop.RemoteDogs.First(d => d.MemberId == host.Id).Dog),
            "C to desktop preserves each attachment and the free dog's full chin");
        Check(desktop.GetPosition(client.Id).IsEqualApprox(localPosition), "remote placement handoff does not move the local desktop dog");
        main.RoomRequestPokerForSmoke();
        await CheckHandoff(main, sharedPositions, fullHost, poker: true);
        Check(ScreenPositions(main.RoomPokerDogsForSmoke).All(pair => pair.Value.IsEqualApprox(sharedPositions[pair.Key])),
            "desktop to C retains shared screen positions even where the table overlaps a dog");
        main.RoomModeForSmoke(false); await Wait(.8);
        SelectMode(page, RoomDisplayMode.HalfScreenCompact);
        Check(page.SelectedDisplayMode == RoomDisplayMode.FullScreenFree,
            "queued selection while joined cannot change the display mode");
        desktop.MoveMember(host.Id, desktop.GetPosition(host.Id) + new Vector2(-40, 40), 0, false, 0, 0);
        await Wait();
        sharedPositions = ScreenPositions(desktop);
        main.RoomRequestPokerForSmoke();
        await Wait(.5);
        Check(ScreenPositions(main.RoomPokerDogsForSmoke).All(pair => pair.Value.IsEqualApprox(sharedPositions[pair.Key])),
            "new desktop drags replace the earlier C position on the next entry");
        guest.Leave(); await Step();
        guest.Join(host.JoinedCode); await Step();
        var replacementPosition = ScreenPositions(main.RoomPokerDogsForSmoke)[guest.Id];
        main.RoomModeForSmoke(false); await Wait(.8);
        Check(ScreenPositions(desktop)[guest.Id].IsEqualApprox(replacementPosition),
            "a member who rejoins during C returns with its current presence placement");
        main.RoomRequestPokerForSmoke();
        await Wait(.5);
        client.Leave(); await Step();
        Check(main.CurrentMode == ModeManager.Mode.Play && !poker.Visible
            && main.RoomPokerDisplayForSmoke == RoomPokerDisplay.Hidden, "leaving C restores ordinary poker without exiting the hand");
        main.RoomModeForSmoke(false); await Wait(.8);
        main.RoomRequestPokerForSmoke(); await Wait(.3);
        Check(main.CurrentMode == ModeManager.Mode.Play && !panel.HasRoomModal, "solo entry has no room choice");
        GD.Print("[RoomPokerChecks] ALL PASS main window, saved preference, layout, synchronization, membership, drag, click-through, restore and stale actions (Mock).");
    }

    private static async Task JoinWhilePlaying(ModeManager main, InGameRoomPreview page)
    {
        var panel = main.SettingsPanelObj;
        var client = page.PreviewClient;
        var host = page.MockClientForSmoke(2);
        async Task Wait(double seconds = .2) =>
            await main.ToSignal(main.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        async Task Step(double seconds = .5) { page.AdvancePreview(seconds); await Wait(); }
        static void Click(Button button) => button.EmitSignal(BaseButton.SignalName.Pressed);
        panel.CloseImmediate();
        main.RoomRequestPokerForSmoke();
        await Wait(.8);
        var game = main.GetNode<GameManager>("PlayRoot/SubViewportContainer/SubViewport/Main");
        var welcome = game.GetNode<PokerWelcomeDialogController>("PokerWelcomeDialog");
        var confirmWelcome = welcome.GetNode<Button>("InputShield/Center/Panel/Margin/Content/Confirm");
        if (welcome.Visible) Click(confirmWelcome);
        Check(main.RoomDesktopForSmoke == null, "poker-first path has no desktop room renderer to seed from");
        game.Call("OnBetRequested");
        await Wait(1);
        game.Call("OnCardClicked", 0);
        await Wait();
        var cards = game.GetNode<CardTableController>("CardArea");
        string Hand() => string.Join(",", Enumerable.Range(0, 5).Select(i =>
            $"{cards.GetCard(i).CardValue}:{cards.GetCard(i).IsHeld}"));
        Check(game.State == GameState.Holding, "single-player hand is in progress before joining");
        string hand = Hand();

        client.Join("MISSING"); await Step();
        Check(client.JoinedCode.Length == 0 && !panel.HasRoomModal, "failed join in poker does not ask for dog layout");
        client.Join(host.JoinedCode); await Step();
        Check(!panel.HasRoomModal && main.RoomPokerDisplayForSmoke == RoomPokerDisplay.Free
            && main.CurrentMode == ModeManager.Mode.Play && Hand() == hand,
            "poker-first join automatically uses the saved free layout and preserves the hand");
        client.Leave(); await Step();
        SelectMode(page, RoomDisplayMode.HalfScreenCompact);
        panel.CloseImmediate();
        game.RequestPokerWelcome(true); await Wait();
        client.Join(host.JoinedCode); await Step();
        Check(welcome.Visible && main.RoomPokerDisplayForSmoke == RoomPokerDisplay.Hidden && !panel.IsOpen,
            "automatic layout waits for an occupied welcome overlay");
        Click(confirmWelcome); await Wait();
        Check(main.RoomPokerDisplayForSmoke == RoomPokerDisplay.Free && main.RoomPokerDogsForSmoke.RemoteDogs.Count == 2
            && game.State == GameState.Holding && Hand() == hand && !panel.HasRoomModal,
            "welcome completion applies compact layout without another dialog or restarting the hand");

        client.Leave(); await Step();
        panel.CloseImmediate();
        game.RequestPokerWelcome(true); await Wait();
        client.Join(host.JoinedCode); await Step();
        client.Leave(); await Step();
        Click(confirmWelcome); await Wait();
        Check(main.RoomPokerDisplayForSmoke == RoomPokerDisplay.Hidden && !panel.IsOpen,
            "leaving while layout is deferred cancels pending presentation");
        SelectMode(page, RoomDisplayMode.FullScreenFree);
        client.Create("Poker-first room"); await Step();
        Check(!panel.HasRoomModal && client.View.Members.Length == 1 && !client.IsLocalArrival
            && main.RoomPokerDisplayForSmoke == RoomPokerDisplay.Free,
            "ordinary creation while playing automatically uses the saved layout and starts owner-only");
        client.Leave(); await Step();
        page.SeedLegacyCompanionRoomForSmoke("Poker-first legacy room"); await Step();
        Check(main.RoomPokerDisplayForSmoke == RoomPokerDisplay.Free
            && main.RoomPokerDogsForSmoke.RemoteDogs.Count == 3
            && DisplayServer.WindowGetSize() == DisplayServer.ScreenGetSize() - Vector2I.One * 2
            && game.State == GameState.Holding && Hand() == hand,
            "poker-first free layout displays companions and preserves the existing hand");
        var firstPoker = main.RoomPokerDogsForSmoke;
        var companion = firstPoker.RemoteDogs.First();
        firstPoker.MoveMember(companion.MemberId, companion.Position + new Vector2(-25, -150), 0, false, 0, 0);
        await Wait();
        var firstPositions = ScreenPositions(firstPoker);
        main.RoomModeForSmoke(false); await Wait(.8);
        Check(ScreenPositions(main.RoomDesktopForSmoke).All(pair => pair.Value.IsEqualApprox(firstPositions[pair.Key]))
            && !main.RoomDesktopForSmoke.IsTaskbarSnapped(companion.MemberId),
            "first desktop reveal after joining in C preserves companion positions and detached state");
        main.RoomRequestPokerForSmoke();
        await Wait(.5);
        Check(ScreenPositions(main.RoomPokerDogsForSmoke).All(pair => pair.Value.IsEqualApprox(firstPositions[pair.Key])),
            "poker-first companion positions survive another C entry");
        client.Leave(); await Step();

        SelectMode(page, RoomDisplayMode.HalfScreenCompact);
        var plan = RoomCompanionPlan.Create(new[] { client.SkinId }, new[] { 0 }, 0, seed: 73);
        Check(client.BeginArrival("Poker arrival", plan, -2), "local companion room can begin during poker");
        await Wait();
        Check(!panel.HasRoomModal && main.RoomPokerDisplayForSmoke == RoomPokerDisplay.Free,
            "local companion room automatically uses the saved compact layout");
        Check(client.Arrival?.Presented == true, "selected poker dogs release the local arrival presentation gate");
        long arrivalSession = client.LayoutSession;
        for (int i = 0; i < 10 && client.IsLocalArrival; i++) await Step(1);
        Check(!client.IsLocalArrival && client.View?.OwnerId == client.Id && client.LayoutSession == arrivalSession
            && !panel.HasRoomModal && main.RoomPokerDisplayForSmoke == RoomPokerDisplay.Free,
            "companion host handoff does not ask for the same layout twice");
        Check(game.State == GameState.Holding && Hand() == hand, "all join/create/handoff paths preserve held cards");
        client.Leave(); await Step();
        panel.CloseImmediate();
        main.RoomModeForSmoke(false);
        await Wait(.8);
    }

    private static async Task CheckHandoff(ModeManager main, Dictionary<int, Vector2> expected, Rect2I host, bool poker)
    {
        // Inspect the first rendered frames, not only the settled result after a
        // timer. A temporary solo host/hidden renderer must fail this regression.
        for (int frame = 0; frame < 5; frame++)
        {
            await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var renderer = poker ? main.RoomPokerDogsForSmoke : main.RoomDesktopForSmoke;
            var positions = ScreenPositions(renderer);
            Check(renderer.IsVisibleInTree() && renderer.RemoteDogs.All(d => d.IsVisibleInTree())
                && positions.Count == expected.Count && expected.All(p => positions[p.Key].IsEqualApprox(p.Value))
                && new Rect2I(DisplayServer.WindowGetPosition(), DisplayServer.WindowGetSize()) == host,
                $"frame {frame}: {(poker ? "poker" : "desktop")} host and all remote positions survive handoff", quiet: true);
        }
        GD.Print("[RoomPokerChecks] PASS first five rendered frames preserve room host and remote positions: " + (poker ? "poker" : "desktop"));
    }

    private static async Task CheckCompactMovement(ModeManager main, RoomDesktopPreview dogs, RoomClient client,
        SystemPanelController panel, Func<double, Task> wait)
    {
        var host = new Rect2I(DisplayServer.WindowGetPosition(), DisplayServer.WindowGetSize());
        var screen = new Rect2I(DisplayServer.ScreenGetPosition(), DisplayServer.ScreenGetSize());
        var dog = dogs.RemoteDogs.First();
        // Put a dog below the table so pointer input is unambiguously on the dog.
        dogs.MoveMember(dog.MemberId, new Vector2(host.Size.X / 2f, host.Size.Y - 110), 0, false, 0, 0);
        await wait(.15);
        var others = dogs.RemoteDogs.Where(d => d != dog).ToDictionary(d => d.MemberId, d => d.Position);
        var point = dog.Position + new Vector2(0, -40);
        main.GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true,
            Position = point, GlobalPosition = point }, true);
        main.GetViewport().PushInput(new InputEventMouseMotion { ButtonMask = MouseButtonMask.Left,
            Position = point + new Vector2(-5000, 0), GlobalPosition = point + new Vector2(-5000, 0) }, true);
        await wait(.2);
        Check(DisplayServer.WindowGetPosition() == host.Position && dogs.Boundary.Visible
            && new Rect2(Vector2.Zero, host.Size).Grow(.1f).Encloses(new Rect2(dog.Position + dog.Footprint.Position, dog.Footprint.Size))
            && others.All(p => dogs.GetPosition(p.Key).IsEqualApprox(p.Value)), "compact remote pointer drag clamps independently and shows boundary");
        main.GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false,
            Position = point, GlobalPosition = point }, true);
        var positions = dogs.RemoteDogs.ToDictionary(d => d.MemberId, d => d.Position);
        int pushDelta = -Mathf.CeilToInt(main.RoomPokerTableRectForSmoke.Position.X + 100);
        main.RoomMovePokerTableForSmoke(new Vector2I(pushDelta, 0));
        await wait(.2);
        var pushed = DisplayServer.WindowGetPosition();
        Check(pushed.X < host.Position.X && dogs.RemoteDogs.All(d => d.Position.IsEqualApprox(positions[d.MemberId])),
            "table pushes half host while other dogs keep their host-relative positions");
        main.RoomMovePokerTableForSmoke(new Vector2I(pushDelta, 0), begin: false);
        main.RoomMovePokerTableForSmoke(new Vector2I(pushDelta, 12), begin: false);
        await wait(.8);
        Check(DisplayServer.WindowGetPosition() == pushed && dogs.Boundary.OpacityForSmoke(RoomBoundaryEdges.Left) > .99f
            && dogs.Boundary.OpacityForSmoke(RoomBoundaryEdges.Right) > .99f, "table contact does not drift or start fading on stationary/vertical motion");
        main.RoomMovePokerTableForSmoke(new Vector2I(8, 0));
        await wait(.2);
        Check(DisplayServer.WindowGetPosition() == pushed, "reversing table inside the area does not pull half host");
        main.RoomMovePokerTableForSmoke(new Vector2I(-100000, -100000));
        await wait(.15);
        Check(DisplayServer.WindowGetPosition().X == screen.Position.X + 1, "table cannot push half host into another monitor");
        main.RoomMovePokerTableForSmoke(new Vector2I(100000, 100000));
        await wait(.2);
        Check(DisplayServer.WindowGetPosition() + host.Size == screen.End - Vector2I.One,
            "table push stops at right/bottom monitor limits");
        main.GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false,
            Position = new Vector2(10, 10), GlobalPosition = new Vector2(10, 10) }, true);
        await wait(1.3);
        Check(dogs.Boundary.OpacityForSmoke(RoomBoundaryEdges.Right) is > 0 and < .99f,
            "releasing table starts the shared two-second edge fade");
        var screenPositions = ScreenPositions(dogs);
        main.Call("ToggleSettingsPanel"); panel.ShowRoomPage();
        int savedStep = SettingsManager.LoadOtherUiScaleStep();
        foreach (int step in Enumerable.Range(SettingsManager.OtherUiScaleStepMin,
            SettingsManager.OtherUiScaleStepMax - SettingsManager.OtherUiScaleStepMin + 1))
        {
            main.RoomOtherScaleForSmoke(step); await wait(.08);
            var available = DisplayServer.ScreenGetUsableRect().Intersection(new Rect2I(DisplayServer.WindowGetPosition(), host.Size));
            var bounds = new Rect2(available.Position - DisplayServer.WindowGetPosition(), available.Size);
            Check(bounds.Encloses(main.RoomPokerTableRectForSmoke) && bounds.Encloses(panel.PanelRect)
                && DisplayServer.WindowGetSize() == host.Size && SettingsManager.LoadOtherUiScaleStep() == savedStep
                && screenPositions.All(p => ScreenPositions(dogs)[p.Key].IsEqualApprox(p.Value)),
                $"compact scale {step}: bounds={bounds}, table={main.RoomPokerTableRectForSmoke}, panel={panel.PanelRect}, "
                    + $"host={DisplayServer.WindowGetSize()}/{host.Size}, saved={SettingsManager.LoadOtherUiScaleStep()}/{savedStep}, "
                    + "dogs=" + string.Join(";", screenPositions.Select(p => $"{p.Key}:{p.Value}->{ScreenPositions(dogs)[p.Key]}")), quiet: true);
        }
        main.RoomOtherScaleForSmoke(savedStep); panel.CloseImmediate(); await wait(.15);
        Check(client.JoinedCode.Length > 0, "compact layout changes never leave or recreate the room");
    }

    private static void SelectMode(InGameRoomPreview page, RoomDisplayMode mode)
    {
        var option = page.GetNode<OptionButton>("Lobby/Display/Mode");
        option.EmitSignal(OptionButton.SignalName.ItemSelected, option.GetItemIndex((int)mode));
    }

    private static Dictionary<int, Vector2> ScreenPositions(RoomDesktopPreview preview) => preview.RemoteDogs.ToDictionary(
        dog => dog.MemberId, dog => (Vector2)DisplayServer.WindowGetPosition() + dog.GetGlobalTransformWithCanvas().Origin);

    private static void Check(bool ok, string message, bool quiet = false)
    {
        if (!ok) throw new InvalidOperationException(message);
        if (!quiet) GD.Print("[RoomPokerChecks] PASS " + message);
    }
}
#endif
