#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace LuckyDogRise.Rooms;

internal static class RoomDesktopBoundsChecks
{
    public static void Geometry()
    {
        // Primary, left-hand monitor (negative coordinates), stacked and portrait.
        foreach (var screen in new[] { new Rect2I(0, 0, 1920, 1080), new(-2560, -200, 2560, 1440),
            new Rect2I(400, -1080, 1920, 1080), new(1920, 0, 1080, 1920) })
        {
            var host = RoomDesktopGeometry.Host(screen, true);
            Check(host.End.X == screen.End.X - 1 && host.Size.X == (screen.Size.X - 2) / 2, "initial right half", true);
            var footprint = new Rect2(-120, -180, 240, 230);
            var pushed = RoomDesktopGeometry.Push(host, screen, host.Position + new Vector2(60, 400), footprint);
            Check(pushed.Position.X < host.Position.X && pushed.End.X <= screen.End.X, "push within selected monitor", true);
            Check(RoomDesktopGeometry.Push(pushed, screen, pushed.GetCenter(), footprint) == pushed, "interior reverse drag never pulls host", true);
            foreach (var requested in new[] { new Vector2(-100000, -100000), new Vector2(100000, 100000) })
            {
                var limit = RoomDesktopGeometry.Push(host, screen, requested, footprint);
                Check(((Rect2)screen).Encloses(limit), "host never straddles simulated monitors", true);
                var dog = RoomDesktopGeometry.Clamp(requested, footprint, limit);
                Check(((Rect2)limit).Grow(.01f).Encloses(new Rect2(dog + footprint.Position, footprint.Size)), "entire dog stays inside host", true);
            }
            var panel = new LegacyRealtimeGridStrategy();
            foreach (float x in new[] { 120f, host.Size.X / 2f, host.Size.X - 120f })
            {
                var usable = screen.Intersection(host);
                var c = new PanelPlacementContext(PanelHostMode.BossKey, host.Position, host.Size, usable,
                    new Vector2(x - 132, 700), new(265, 235), new(420, 600), 900, null, null, 100, 10, false,
                    new Vector2(x - 552, 100), true);
                var p = panel.CalculatePanelPlacement(c).PanelPosition + host.Position;
                Check(((Rect2)usable).Encloses(new Rect2(p, new Vector2(420, 100))), "panel controls inside compact region", true);
            }
        }
        GD.Print("[RoomDesktopBounds] PASS simulated negative, stacked, portrait and ordinary monitor geometry.");
    }

    public static async Task Run(ModeManager main, InGameRoomPreview page)
    {
        Geometry();
        async Task Wait(double seconds = .15) => await main.ToSignal(main.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        async Task Step() { page.AdvancePreview(.5); await Wait(); }
        void Button(Vector2 screenPoint, bool pressed)
        {
            var p = screenPoint - DisplayServer.WindowGetPosition();
            main.GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left,
                Pressed = pressed, ButtonMask = pressed ? MouseButtonMask.Left : 0, Position = p, GlobalPosition = p }, true);
        }
        void Motion(Vector2 screenPoint)
        {
            var p = screenPoint - DisplayServer.WindowGetPosition();
            main.GetViewport().PushInput(new InputEventMouseMotion { ButtonMask = MouseButtonMask.Left, Position = p, GlobalPosition = p }, true);
        }
        var panel = main.SettingsPanelObj;
        var client = page.PreviewClient;
        var mode = page.GetNode<OptionButton>("Lobby/Display/Mode");
        client.Leave(); await Step();
        mode.EmitSignal(OptionButton.SignalName.ItemSelected, mode.GetItemIndex((int)RoomDisplayMode.HalfScreenCompact));
        main.ApplyDesktopPetScaleStep(1);
        main.RoomSnapForSmoke(true);
        client.Join(page.MockClientForSmoke(2).JoinedCode); await Step();
        panel.CloseImmediate(); await Wait();
        var desktop = main.RoomDesktopForSmoke;
        int screenIndex = DisplayServer.WindowGetCurrentScreen();
        var screen = new Rect2I(DisplayServer.ScreenGetPosition(screenIndex), DisplayServer.ScreenGetSize(screenIndex));
        var initialHost = new Rect2I(DisplayServer.WindowGetPosition(), DisplayServer.WindowGetSize());
        Check(initialHost == RoomDesktopGeometry.Host(screen, true), "compact desktop starts in right half");
        var remotes = desktop.RemoteDogs.ToArray();
        Check(remotes.All(d => new Rect2(Vector2.Zero, initialHost.Size).Grow(.1f).Encloses(
            new Rect2(d.Position + d.Footprint.Grow(-4).Position, d.Footprint.Grow(-4).Size))), "initial full avatars and name bars fit");
        var remote = remotes[0];
        var point = remote.Position + DisplayServer.WindowGetPosition() + new Vector2(0, -40);
        Button(point, true); Motion(point + new Vector2(-screen.Size.X * 2, -60)); await Wait();
        Check(main.RoomDraggingForSmoke && DisplayServer.WindowGetPosition() == initialHost.Position, "remote drag clamps without pushing host");
        Motion(point + new Vector2(-screen.Size.X * 2, -48)); await Wait(1.2);
        Check(desktop.Boundary.OpacityForSmoke(RoomBoundaryEdges.Left) > .99f,
            "remote feedback stays bright while held against its limit");
        Button(point, false); await Wait();
        var beforeRemotes = remotes.ToDictionary(d => d.MemberId, d => d.Position);
        var self = desktop.GetPosition(client.Id);
        var selfPoint = self + DisplayServer.WindowGetPosition() + new Vector2(0, -40);
        Button(selfPoint, true);
        var leftPoint = selfPoint + new Vector2(-initialHost.Size.X / 2f, -130);
        Motion(leftPoint); await Wait();
        var pushedHost = DisplayServer.WindowGetPosition();
        Check(pushedHost.X < initialHost.Position.X, "local dog pushes compact host left");
        Check(remotes.All(d => d.Position.IsEqualApprox(beforeRemotes[d.MemberId])), "remote local positions stay fixed while host moves");
        Motion(leftPoint); await Wait(.2);
        Motion(leftPoint + new Vector2(0, 12)); await Wait(1.2);
        Check(DisplayServer.WindowGetPosition() == pushedHost && main.RoomDraggingForSmoke
            && desktop.Boundary.OpacityForSmoke(RoomBoundaryEdges.Left) > .99f
            && desktop.Boundary.OpacityForSmoke(RoomBoundaryEdges.Right) > .99f,
            "local feedback stays bright through stationary and vertical motion at the pushing edge");
        Motion(leftPoint + new Vector2(100, 0)); await Wait();
        Check(DisplayServer.WindowGetPosition() == pushedHost, "reversing inside area does not pull host");
        Check(desktop.Boundary.OpacityForSmoke(RoomBoundaryEdges.Left) > .9f
            && desktop.Boundary.OpacityForSmoke(RoomBoundaryEdges.Right) > .9f,
            "both edges linger when dragging back inside without releasing");
        for (int i = 0; i < 3; i++)
        {
            Motion(leftPoint + new Vector2(110 + i * 10, 0));
            await Wait(.4);
        }
        float faded = desktop.Boundary.OpacityForSmoke(RoomBoundaryEdges.Right);
        Check(faded > 0 && faded < .9f, "continued interior movement does not restart the fade delay");
        Motion(leftPoint + new Vector2(-20, 0));
        Check(Mathf.IsEqualApprox(faded, desktop.Boundary.OpacityForSmoke(RoomBoundaryEdges.Right)),
            "touching the edge during fade preserves opacity without flashing");
        await Wait(.2);
        Check(desktop.Boundary.OpacityForSmoke(RoomBoundaryEdges.Right) > .99f, "retouched edge smoothly regains brightness");
        Motion(new Vector2(screen.Position.X - 1000, leftPoint.Y)); await Wait();
        Check(DisplayServer.WindowGetPosition().X == screen.Position.X + 1, "local drag stops at selected monitor boundary");
        Check(desktop.Boundary.Visible && !main.RoomHitTestForSmoke(DisplayServer.WindowGetPosition() + new Vector2I(3, screen.Size.Y / 2)), "red edge feedback leaves blank pixels click-through");
        var folder = ProjectSettings.GlobalizePath("res://../.local-build/room-half-screen");
        Directory.CreateDirectory(folder);
        GD.Print("[RoomDesktopBounds] capture: " + string.Join("; ", remotes.Select(d =>
            $"id={d.MemberId} pos={d.Position} global={d.GetGlobalTransformWithCanvas().Origin} visible={d.IsVisibleInTree()} scale={d.Scale}")));
        await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        main.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder, "compact-boundary.png"));
        Button(leftPoint, false); await Wait(.4);
        Check(desktop.Boundary.Visible && desktop.Boundary.OpacityForSmoke(RoomBoundaryEdges.Left) > .99f,
            "boundary remains bright briefly after release");
        await Wait(.9);
        float releasedOpacity = desktop.Boundary.OpacityForSmoke(RoomBoundaryEdges.Left);
        Check(releasedOpacity > 0 && releasedOpacity < .9f && !main.RoomHitTestForSmoke(
            DisplayServer.WindowGetPosition() + new Vector2I(12, screen.Size.Y / 2)),
            "lingering glow fades without intercepting desktop clicks");
        await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        main.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder, "compact-boundary-fading.png"));
        await Wait(1.5);
        Check(!desktop.Boundary.Visible && !desktop.Boundary.IsProcessing(), "boundary disappears and stops processing after fade");
        var hostBeforePanel = DisplayServer.WindowGetPosition();
        main.Call("ToggleSettingsPanel"); panel.ShowRoomPage(); await Wait();
        Check(DisplayServer.WindowGetPosition() == hostBeforePanel, "opening panel never pushes host");
        Check(panel.PanelRect.Position.X >= 0 && panel.PanelRect.End.X <= DisplayServer.WindowGetSize().X,
            "panel avoids compact host edges");
        await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        main.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder, "compact-panel.png"));
        panel.CloseImmediate();
        var positions = desktop.RemoteDogs.ToDictionary(d => d.MemberId, d => d.Position);
        desktop.Boundary.Indicate(new Rect2(Vector2.Zero, initialHost.Size), RoomBoundaryEdges.Right);
        await Wait();
        main.RoomRequestPokerForSmoke(); await Wait(.7);
        Check(main.RoomPokerDisplayForSmoke == RoomPokerDisplay.Free, "compact poker uses freely draggable companions");
        Check(!desktop.Boundary.Visible && !desktop.Boundary.IsProcessing(), "mode switch clears lingering boundary immediately");
        main.RoomModeForSmoke(false); await Wait(.9);
        Check(!desktop.Boundary.Visible, "returning to desktop never revives old boundary feedback");
        Check(DisplayServer.WindowGetPosition() == hostBeforePanel && desktop.RemoteDogs.All(d => d.Position.IsEqualApprox(positions[d.MemberId])), "compact poker roundtrip restores region and remote placements");
        var attachedIds = remotes.Where(d => desktop.IsTaskbarSnapped(d.MemberId)).Select(d => d.MemberId).ToArray();
        Check(attachedIds.Length > 0, "taskbar attachment remains after room and poker transitions");
        foreach (int step in Enumerable.Range(SettingsManager.DesktopPetScaleStepMin,
            SettingsManager.DesktopPetScaleStepMax - SettingsManager.DesktopPetScaleStepMin + 1))
        {
            main.ApplyDesktopPetScaleStep(step); await Wait(.05);
            Check(DisplayServer.WindowGetSize() == initialHost.Size && attachedIds.All(desktop.IsTaskbarSnapped),
                "all pet scales retain half host and taskbar attachment", true);
        }
        main.ApplyDesktopPetScaleStep(1); await Wait();
        main.Call("ToggleSettingsPanel"); panel.ShowRoomPage();
        foreach (int step in Enumerable.Range(SettingsManager.OtherUiScaleStepMin,
            SettingsManager.OtherUiScaleStepMax - SettingsManager.OtherUiScaleStepMin + 1))
        {
            main.RoomOtherScaleForSmoke(step); await Wait(.05);
            Check(DisplayServer.WindowGetSize() == initialHost.Size && panel.PanelRect.Position.X >= 0
                && panel.PanelRect.End.X <= initialHost.Size.X, "UI scale preserves half host and reachable panel", true);
        }
        main.RoomOtherScaleForSmoke(SettingsManager.LoadOtherUiScaleStep());
        panel.CloseImmediate();
        GD.Print("[RoomDesktopBounds] PASS pet and UI scale matrices, attachment and panel safety.");
        desktop.Boundary.Indicate(new Rect2(Vector2.Zero, initialHost.Size), RoomBoundaryEdges.Left);
        await Wait();
        client.Leave(); await Step();
        Check(!main.RoomDesktopWindowActive && Mathf.IsEqualApprox(panel.RenderScale, SettingsManager.GetOtherUiScaleFactor(SettingsManager.LoadOtherUiScaleStep())), "leaving restores ordinary host and panel scale");
        Check(!desktop.Boundary.Visible && !desktop.Boundary.IsProcessing(), "leaving clears all boundary animation");
        GD.Print("[RoomDesktopBounds] PASS main scene compact drag, panel and mode roundtrip.");
    }

    private static void Check(bool pass, string text, bool quiet = false)
    { if (!pass) throw new InvalidOperationException(text); if (!quiet) GD.Print("[RoomDesktopBounds] PASS " + text); }

    public static async Task NameBars(ModeManager main, InGameRoomPreview page)
    {
        async Task Wait(double seconds = .05) => await main.ToSignal(
            main.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        async Task Step() { page.AdvancePreview(0); await Wait(); }
        var client = page.PreviewClient;
        var remoteClient = page.MockClientForSmoke(2);
        bool savedCenter = SettingsManager.LoadCenterCounterOnTaskbar();
        int savedScale = SettingsManager.LoadDesktopPetScaleStep();
        try
        {
            SettingsManager.SaveCenterCounterOnTaskbar(true);
            main.RoomSnapForSmoke(true);
            foreach (var mode in new[] { RoomDisplayMode.FullScreenFree, RoomDisplayMode.HalfScreenCompact })
            {
                main.RoomModeForSmoke(false);
                client.Leave(); await Step();
                var selector = page.GetNode<OptionButton>("Lobby/Display/Mode");
                selector.EmitSignal(OptionButton.SignalName.ItemSelected, selector.GetItemIndex((int)mode));
                client.Join(remoteClient.JoinedCode); await Step(); await Wait(1);
                main.SettingsPanelObj.CloseImmediate();
                foreach (bool poker in new[] { false, true })
                {
                    if (poker) main.RoomRequestPokerForSmoke();
                    await Wait(.8);
                    var desktop = poker ? main.RoomPokerDogsForSmoke : main.RoomDesktopForSmoke;
                    var dog = desktop.RemoteDogs.Single(d => d.MemberId == remoteClient.Id);
                    var bar = dog.GetNode<Control>("NameBar");
                    var tongue = dog.Dog.GetNode<Sprite2D>("HeadRoot/Tonghe");
                    GD.Print($"[RoomNameBar] host={DisplayServer.WindowGetPosition()}/{DisplayServer.WindowGetSize()}, screen={DisplayServer.ScreenGetSize()}, usable={DisplayServer.ScreenGetUsableRect()}, centered={SettingsManager.LoadCenterCounterOnTaskbar()}, attached={desktop.IsTaskbarSnapped(dog.MemberId)}");
                    foreach (int step in Enumerable.Range(SettingsManager.DesktopPetScaleStepMin,
                        SettingsManager.DesktopPetScaleStepMax - SettingsManager.DesktopPetScaleStepMin + 1))
                    {
                        main.ApplyDesktopPetScaleStep(step);
                        // All fur colors plus the thin and round heads; each skin refreshes the layout.
                        foreach (int skin in new[] { 1001, 1002, 1003, 1004, 1005 })
                        {
                            remoteClient.SetAppearance(skin, 0, 1003); await Step();
                            var barPosition = bar.Position;
                            var dogPosition = dog.Position;
                            var barSize = bar.Size;
                            var tonguePosition = tongue.Position;
                            bool moved = false;
                            float minimumGap = float.PositiveInfinity;
                            for (int frame = 0; frame < 20; frame++)
                            {
                                remoteClient.NotifyInputActivity(); page.AdvancePreview(0);
                                await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
                                moved |= !tongue.Position.IsEqualApprox(tonguePosition);
                                Check(bar.Position.IsEqualApprox(barPosition) && bar.Size.IsEqualApprox(barSize),
                                    $"name bar stable during tongue motion: {mode}, poker={poker}, scale={step}, skin={skin}, before={barPosition}, now={bar.Position}", true);
                                Check(dog.Position.IsEqualApprox(dogPosition) && desktop.IsTaskbarSnapped(dog.MemberId),
                                    "tongue motion never moves or detaches the dog", true);
                                var tongueRect = tongue.GetGlobalTransformWithCanvas() * tongue.GetRect();
                                var barRect = bar.GetGlobalTransformWithCanvas() * new Rect2(Vector2.Zero, bar.Size);
                                minimumGap = Mathf.Min(minimumGap, barRect.Position.Y - tongueRect.End.Y);
                            }
                            Check(moved, "test samples actual tongue movement", true);
                            Check(minimumGap >= -.1f, "name bar leaves room for the moving tongue", true);
                            remoteClient.StopInputActivity(); await Step();
                            Check(bar.Position.IsEqualApprox(barPosition), "tongue stop keeps the same reserved clearance", true);
                        }
                    }
                    // A larger pant offset is an authored parameter, not a fixed pixel assumption.
                    main.ApplyDesktopPetScaleStep(SettingsManager.DesktopPetScaleStepMax); await Wait();
                    dog.Dog.Set("_tonguePantOffset", 24f);
                    remoteClient.SetReaction(1009); await Step();
                    var pantBarPosition = bar.Position;
                    var pantTonguePosition = tongue.Position;
                    bool pantMoved = false;
                    for (int frame = 0; frame < 24; frame++)
                    {
                        await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
                        pantMoved |= !tongue.Position.IsEqualApprox(pantTonguePosition);
                        Check(bar.Position.IsEqualApprox(pantBarPosition), "pant tween also leaves the name bar still", true);
                        Check((bar.GetGlobalTransformWithCanvas() * Vector2.Zero).Y >=
                            (tongue.GetGlobalTransformWithCanvas() * tongue.GetRect()).End.Y - .1f,
                            "name bar clears the authored pant range", true);
                    }
                    Check(pantMoved, "test samples actual pant movement", true);
                    dog.Dog.Set("_tonguePantOffset", 10f);
                    remoteClient.SetReaction(1003); await Step();
                    SettingsManager.SaveCenterCounterOnTaskbar(false); await Wait();
                    var idle = bar.Position;
                    SettingsManager.SaveCenterCounterOnTaskbar(true); await Wait();
                    var snappedPosition = dog.Position;
                    desktop.MoveMember(dog.MemberId, snappedPosition - new Vector2(0, 200), 0, false, 0, 0);
                    await Wait();
                    Check(!desktop.IsTaskbarSnapped(dog.MemberId) && bar.Position.IsEqualApprox(idle),
                        "detached name bar restores its original layout", true);
                    desktop.MoveMember(dog.MemberId, snappedPosition, snappedPosition.Y, true, 16, 28);
                    await Wait();
                    Check(desktop.IsTaskbarSnapped(dog.MemberId), "dragging back restores taskbar attachment", true);
                    main.RoomSnapForSmoke(false); await Wait();
                    Check(bar.Position.IsEqualApprox(idle), "disabling taskbar attachment restores original name bar", true);
                    main.RoomSnapForSmoke(true);
                    desktop.MoveMember(dog.MemberId, snappedPosition, snappedPosition.Y, true, 16, 28);
                    await Wait();
                    GD.Print($"[RoomNameBar] PASS {mode}, poker={poker}: six scales, five skins, typing, stop, pant, centering and attachment.");
                }
            }
        }
        finally
        {
            remoteClient.StopInputActivity(); client.Leave();
            SettingsManager.SaveCenterCounterOnTaskbar(savedCenter);
            main.ApplyDesktopPetScaleStep(savedScale);
        }
        GD.Print("[RoomNameBar] PASS stable name bars across desktop/poker and full/half-screen hosts.");
    }
}
#endif
