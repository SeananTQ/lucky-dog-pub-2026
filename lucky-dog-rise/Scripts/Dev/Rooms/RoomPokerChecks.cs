#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace LuckyDogRise.Rooms;

internal static class RoomPokerChecks
{
    public static void Layout()
    {
        foreach (var screen in new[] { new Vector2I(1280, 680), new Vector2I(1920, 1040), new Vector2I(3840, 2080) })
        foreach (float ui in new[] { .75f, 1f, 1.25f, 1.5f, 2f })
        foreach (float dog in new[] { .5f, 1f, 2f, 3f, 4f })
        {
            var spec = RoomPokerLayout.Calculate(screen, (int)(240 * ui), (int)(600 * ui), (int)(420 * ui), 80, dog);
            if (spec.DogScale == 0) continue;
            Check(spec.Row.End.Y <= spec.Origin.Y && spec.Size.Y <= screen.Y,
                "row and poker fit vertically " + screen + "/" + ui + "/" + dog, quiet: true);
            Check(spec.DogScale <= dog && spec.DogScale <= 1 && spec.Row.Size.X / 5 >= 200 * spec.DogScale,
                "five dogs fit without raising desktop scale", quiet: true);
        }
        GD.Print("[RoomPokerChecks] PASS layout matrix: 3 resolutions x 5 UI sizes x 5 pet sizes.");
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
        var original = desktop.RemoteDogs.ToDictionary(d => d.MemberId, d => d.Position);
        long session = client.LayoutSession;
        main.RoomRequestPokerForSmoke();
        await Wait();
        var choice = panel.GetNode<RoomPokerDisplayChoice>("RoomPokerDisplayChoice");
        var buttons = choice.GetNode<VBoxContainer>("OverlayPanel/Margin/Content/ButtonRow");
        Check(main.CurrentMode == ModeManager.Mode.BossKey && choice.Visible && UiInteractionOccupancy.IsOccupied(main),
            "entry is gated by the registered choice overlay");
        Check(!buttons.GetChildren().OfType<Button>().Any(b => b.HasFocus()), "answers are not preselected");
        await Capture("choice");
        Click(buttons.GetNode<Button>("Cancel"));
        Check(main.CurrentMode == ModeManager.Mode.BossKey && !choice.Visible, "cancel stays in desktop mode");
        main.RoomRequestPokerForSmoke();
        Click(buttons.GetNode<Button>("Row"));
        await Wait(.6);
        Check(main.CurrentMode == ModeManager.Mode.Play && main.RoomPokerDisplayForSmoke == RoomPokerDisplay.Row,
            "B enters single-player poker");
        var game = main.GetNode<GameManager>("PlayRoot/SubViewportContainer/SubViewport/Main");
        var welcome = game.GetNode<PokerWelcomeDialogController>("PokerWelcomeDialog");
        if (welcome.Visible) Click(welcome.GetNode<Button>("InputShield/Center/Panel/Margin/Content/Confirm"));
        await Wait();
        var row = main.RoomPokerDogsForSmoke;
        Check(row.RemoteDogs.Count == 5 && row.Visible && !row.LocalChat.Visible, "B fits five remote dogs in a full six-player room");
        Check(row.RemoteDogs.All(d => !HeadClipped(d.Dog)), "B floating row dogs show full chins");
        Check(HeadClipped(game.GetNode<DogVisual>("DogArea")), "player's poker dog retains table clipping");
        var table = main.GetNode<SubViewportContainer>("PlayRoot/SubViewportContainer");
        Check(row.RemoteDogs.All(d => d.Position.Y + 60 * d.Scale.Y < table.Position.Y), "B dogs stay above poker");
        var bSize = DisplayServer.WindowGetSize();
        Check(bSize.Y < DisplayServer.ScreenGetSize().Y && client.LayoutSession == session && client.View.GameId == "social",
            "B uses a compact host and leaves room gameplay/session unchanged");
        await Capture("row");
        var watched = row.RemoteDogs.First(d => d.MemberId == host.Id);
        host.SetReaction(1002);
        await Step();
        Check((int)watched.Dog.CurrentReaction == 1002, "remote reactions keep synchronizing in poker");
        Check(!HeadClipped(watched.Dog), "reaction changes preserve the full chin in B");
        guest.Leave(); await Step();
        Check(row.RemoteDogs.Count == 4 && DisplayServer.WindowGetSize() == bSize, "departure removes dog without resizing host");
        guest.Join(host.JoinedCode); await Step();
        Check(row.RemoteDogs.Count == 5 && DisplayServer.WindowGetSize() == bSize, "new member returns without resizing host");
        Check(row.RemoteDogs.All(d => !HeadClipped(d.Dog)), "new arrivals in B also show full chins");
        main.GetNode<SteamMockPanelController>("SteamMockPanel").EmitSignal(SteamMockPanelController.SignalName.CloseRequested);
        await Wait();
        Check(DisplayServer.WindowGetSize().Y < bSize.Y && row.RemoteDogs.All(d => d.IsVisibleInTree()),
            "hiding the Debug toolbar frees space without hiding the companion row");
        await Capture("row-without-toolbar");
        main.RoomModeForSmoke(false);
        await Wait(.8);
        Check(desktop.RemoteDogs.First(d => d.MemberId == host.Id).Position.IsEqualApprox(original[host.Id]),
            "B round-trip preserves the remaining desktop dog position");
        panel.CloseImmediate();
        // Capture the current desktop positions; the rejoined member legitimately has a new presence.
        original = desktop.RemoteDogs.ToDictionary(d => d.MemberId, d => d.Position);
        main.RoomRequestPokerForSmoke();
        Click(buttons.GetNode<Button>("Desktop"));
        await Wait(.5);
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
        main.RoomModeForSmoke(false);
        await Wait(.8);
        Check(desktop.RemoteDogs.All(d => d.Position.IsEqualApprox(original[d.MemberId])), "C drags never overwrite desktop placements");
        Check(HeadClipped(desktop.RemoteDogs.First(d => d.MemberId == host.Id).Dog) == hasBottomTaskbar,
            "return to desktop restores the dog's taskbar clipping with its original position");
        main.RoomRequestPokerForSmoke();
        client.Leave(); await Step();
        Click(buttons.GetNode<Button>("Desktop"));
        Check(!choice.Visible && main.CurrentMode == ModeManager.Mode.BossKey, "leave invalidates pending choice and stale clicks");
        client.Join(host.JoinedCode); await Step();
        panel.CloseImmediate();
        main.RoomRequestPokerForSmoke();
        Click(buttons.GetNode<Button>("Desktop"));
        await Wait(.5);
        client.Leave(); await Step();
        Check(main.CurrentMode == ModeManager.Mode.Play && !poker.Visible
            && main.RoomPokerDisplayForSmoke == RoomPokerDisplay.Hidden, "leaving C restores ordinary poker without exiting the hand");
        main.RoomModeForSmoke(false); await Wait(.8);
        main.RoomRequestPokerForSmoke(); await Wait(.3);
        Check(main.CurrentMode == ModeManager.Mode.Play && !choice.Visible, "solo entry has no room choice");
        GD.Print("[RoomPokerChecks] ALL PASS main window, choices, layout, synchronization, membership, drag, click-through, restore and stale actions (Mock).");
    }

    private static void Check(bool ok, string message, bool quiet = false)
    {
        if (!ok) throw new InvalidOperationException(message);
        if (!quiet) GD.Print("[RoomPokerChecks] PASS " + message);
    }
}
#endif
