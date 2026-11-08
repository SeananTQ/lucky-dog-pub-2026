#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using LuckyDogRise.Rooms.Poker;

namespace LuckyDogRise.Rooms;

internal static class MischiefPokerMainChecks
{
    public static async Task Run(ModeManager main, InGameRoomPreview page)
    {
        var tree = main.GetTree();
        var panel = main.SettingsPanelObj;
        var client = page.PreviewClient;
        async Task Wait(double seconds = 0.12) => await main.ToSignal(tree.CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        void Click(BaseButton button) => button.EmitSignal(BaseButton.SignalName.Pressed);
        async Task Capture(string name)
        {
            if (DisplayServer.GetName() == "headless") return;
            await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string folder = ProjectSettings.GlobalizePath("res://../.local-build/mischief-poker");
            Directory.CreateDirectory(folder);
            main.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder, name + ".png"));
        }
        void DismissWelcome(GameManager game)
        {
            if (game.IsPokerWelcomeVisible)
                Click(game.GetNode<Button>("PokerWelcomeDialog/InputShield/Center/Panel/Margin/Content/Confirm"));
        }
        foreach (int mode in new[] { 0, 1 })
        {
            L10n.SetLocale(mode == 0 ? "zh_CN" : "en", save: false);
            var display = page.GetNode<OptionButton>("Lobby/Display/Mode");
            display.EmitSignal(OptionButton.SignalName.ItemSelected, (long)mode);
            client.Join(page.MockClientForSmoke(2).JoinedCode);
            if (mode == 1)
                foreach (int id in new[] { 4, 5, 6 }) page.AddMockClientForSmoke(id).Join(page.MockClientForSmoke(2).JoinedCode);
            page.AdvancePreview(0.5);
            await Wait();
            panel.ShowRoomPage();
            var start = page.GetNode<Button>("Room/PokerPreview");
            Check(start.Visible && !start.Disabled, "Mock entry enabled for joined room");
            Click(start);
            await Wait();
            var game = main.GetNode<GameManager>("PlayRoot/SubViewportContainer/SubViewport/Main");
            var session = main.PokerPreviewForSmoke;
            Check(session != null && game.IsSessionPokerActive, "preview starts inside real poker main scene");
            long soloChips = game.GameData.Chips;
            var ui = main.GetNode<RoomPokerPanelView>("InfoPanel/Panel/RootVBox/SessionContent/PokerSession");
            var mock = main.GetNode<SteamMockPanelController>("SteamMockPanel");
            var controls = mock.GetNode<RoomPokerDebugPanelView>("Panel/Root/PokerPage/PokerControls");
            Click(controls.GetNode<Button>("Actions/Pause"));
            Check(session.Paused && mock.PokerContent.Visible, "Mock poker page controls the existing match clock");
            Click(mock.GetNode<Button>("Panel/Root/TabRow/SteamTab"));
            await Wait();
            Check(!mock.PokerContent.Visible && mock.GetNode<Control>("Panel/Root/SteamPage").Visible
                && main.PokerPreviewForSmoke == session && session.Paused, "switching to Steam preserves match and pause state");
            Click(mock.GetNode<Button>("Panel/Root/TabRow/PokerTab"));
            await Wait();
            Check(mock.PokerContent.Visible && mock.PanelSize.Y <= 420.1f,
                $"poker debug controls fit existing Mock panel ({mock.PanelSize})");
            Check(!ui.GetNode<Button>("Next").Visible, "next hand stays hidden while a hand is in progress");
            var table = game.GetNode<RoomPokerTableView>("RoomPokerTable");
            var state = session.View.Public;
            Check(state.Pot == state.Seats.Count * 100 && state.Seats.All(s => s.Chips == 900) && session.View.HoleCards.Count == 2,
                "separate 1000 balance, 100 ante and two private cards");
            Check(!game.IsPokerWelcomeVisible && !game.GetNode<Node2D>("CardArea").Visible,
                "solo onboarding/cards suspended without acknowledging the welcome");
            Check(main.RoomPokerDogsForSmoke.RemoteDogs.All(d => d.PokerHand.Visible && !d.PokerHand.CardsRevealed),
                "remote cards hidden before showdown");
            var renderDogs = main.RoomPokerDogsForSmoke;
            if (mode == 1)
            {
                int slot = 0;
                // Keep all six panels visible for screenshots, without changing production placement rules.
                foreach (var dog in renderDogs.RemoteDogs.OrderBy(d => d.MemberId))
                {
                    renderDogs.MoveMember(dog.MemberId, new Vector2(130 + slot % 3 * 400,
                        DisplayServer.WindowGetSize().Y - 270 + slot / 3 * 200), 0, false, 0, 0);
                    slot++;
                }
                await Wait();
            }
            Check(table.LocalPanel.GetNode<TextureRect>("Plate/Winner").Texture != null
                && table.LocalPanel.GetNode<TextureRect>("Waiting").Texture != null
                && table.LocalPanel.GetNode<TextureRect>("Plate/Autoplay").Texture != null, "all placeholder icons are imported and bound");
            double remaining = session.View.Public.SecondsRemaining;
            await Wait(0.3);
            Check(session.View.Public.SecondsRemaining == remaining, "debug pause freezes authority clock");
            for (int step = 0; session.View.Public.ActivePlayerId != client.Id && step < 12; step++)
                Click(controls.GetNode<Button>("Actions/Step"));
            await Wait();
            long beforeOverlay = session.View.Public.Revision;
            game.EmitSignal(GameManager.SignalName.RoomChatRequested);
            await Wait();
            Check(main.RoomPokerDogsForSmoke.LocalChat.Editing, "existing toolbar chat opens during preview");
            Click(table.GetNode<TextureButton>("Board/Card1"));
            Check(session.View.Public.Revision == beforeOverlay, "chat input blocks board actions");
            main.RoomPokerDogsForSmoke.LocalChat.Reset();
            game.Call("OnQuickBarHelpRequested");
            await Wait();
            Check(game.IsPokerHandShowcaseVisible, "preview help opens hand-rank overlay");
            Click(table.GetNode<TextureButton>("Board/Card1"));
            Check(session.View.Public.Revision == beforeOverlay, "modal help blocks board actions");
            game.GetNode("PokerHandShowcase").Call("HideOverlay");
            await Wait();
            state = session.View.Public;
            int original = state.Board[0].Card;
            bool pendingBefore = state.Board[0].PendingReplacement;
            Click(table.GetNode<TextureButton>("Board/Card1"));
            await Wait(0.2);
            Check(session.View.Public.Board[0].PendingReplacement != pendingBefore && session.View.Public.FlipsRemaining == 1,
                "real board click flips once");
            Click(table.GetNode<TextureButton>("Board/Card1"));
            await Wait(0.2);
            Check(session.View.Public.Board[0].PendingReplacement == pendingBefore && session.View.Public.Board[0].Card == original
                && session.View.Public.ActivePlayerId != client.Id, "second click restores same card and ends turn");
            long revision = session.View.Public.Revision;
            Click(table.GetNode<TextureButton>("Board/Card2"));
            await Wait();
            Check(session.View.Public.Revision == revision, "wrong-turn UI commands rejected");
            await Capture(mode == 0 ? "free-acting-zh" : "compact-acting-en");
            Complete(session, client.Id);
            await Wait();
            Check(main.RoomPokerDogsForSmoke.RemoteDogs.All(d => d.PokerHand.CardsRevealed), "showdown reveals opponent hands");
            Check(!ui.GetNode<Button>("Next").Disabled, "next hand available only after settlement");
            await Capture(mode == 0 ? "free-showdown-zh" : "compact-showdown-en");
            var infoRect = main.GetNode<PanelContainer>("InfoPanel/Panel");
            Check(infoRect.Size.Y <= 600.1f, $"preview controls fit original panel height ({infoRect.Size.Y}, minimum {infoRect.GetCombinedMinimumSize().Y})");

            // Visual-only panel states never change the match or mislabel normal bots as disconnected.
            var sampleDog = main.RoomPokerDogsForSmoke.RemoteDogs.First(d => d.MemberId == session.View.Public.Seats.First(s => s.PlayerId != client.Id).PlayerId);
            Check(!sampleDog.PokerHand.IsAutoplay && !sampleDog.PokerHand.IsWaiting, "normal bot is neither disconnected nor waiting to join");
            long panelRevision = session.View.Public.Revision;
            controls.GetNode<OptionButton>("PanelState").EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
            await Wait();
            Check(sampleDog.PokerHand.IsWaiting && !sampleDog.PokerHand.FirstCard.Visible && !sampleDog.PokerHand.SecondCard.Visible
                && !sampleDog.PokerHand.IsWinner && !sampleDog.PokerHand.IsActing, "waiting fixture has no fake card backs or winner/action state");
            await Capture(mode == 0 ? "panel-waiting-zh" : "panel-waiting-en");
            controls.GetNode<OptionButton>("PanelState").EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
            await Wait();
            Check(sampleDog.PokerHand.IsAutoplay && sampleDog.PokerHand.CardsRevealed
                && session.View.Public.Revision == panelRevision, "autoplay fixture changes presentation only and retains cards");
            await Capture(mode == 0 ? "panel-autoplay-zh" : "panel-autoplay-en");
            controls.GetNode<OptionButton>("PanelState").EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
            await Wait();

            foreach (int preset in new[] { 4, 5, 6, 7, 8 })
            {
                controls.GetNode<OptionButton>("PresetRow/Preset").EmitSignal(OptionButton.SignalName.ItemSelected, (long)preset);
                Click(ui.GetNode<Button>("Next"));
                await Wait();
                Check(!table.LocalPanel.IsWinner && !table.LocalPanel.FirstCard.Dimmed && !table.LocalPanel.FirstCard.Outlined
                    && main.RoomPokerDogsForSmoke.RemoteDogs.All(d => !d.PokerHand.IsWinner && !d.PokerHand.CardsRevealed),
                    "next hand clears crowns, outlines, dimming and opponent faces");
                var acting = session.View.Public.ActivePlayerId;
                Check((acting == client.Id ? table.LocalPanel : main.RoomPokerDogsForSmoke.RemoteDogs.Single(d => d.MemberId == acting).PokerHand).IsActing,
                    "actual actor has a countdown strip");
                Complete(session, client.Id);
                session.Paused = false;
                await Wait(1.1);
                session.Paused = true;
                var emphasis = table.Emphasis;
                Check(emphasis != null, "showdown reveal precedes emphasis");
                foreach (var dog in main.RoomPokerDogsForSmoke.RemoteDogs)
                    Check(dog.PokerHand.IsWinner == session.View.Public.Settlement.Winners.Contains(dog.MemberId), "winner crown is independent of current sequence focus");
                var first = table.LocalPanel.FirstCard;
                var second = table.LocalPanel.SecondCard;
                Check(first.Outlined == emphasis.LocalCards.Contains(first.VisibleCard) && second.Outlined == emphasis.LocalCards.Contains(second.VisibleCard),
                    "local outline follows local combination, including dimmed local cards");
                if (preset == 4)
                {
                    var winnerDog = main.RoomPokerDogsForSmoke.RemoteDogs.Single(d => d.MemberId == emphasis.WinnerId);
                    Check(first.Dimmed && first.Outlined && second.Dimmed && !second.Outlined, "bear example: local 10 is dim-green, local 2 dim without outline");
                    var kicker = winnerDog.PokerHand.SecondCard;
                    Check(kicker.Decisive && !kicker.Dimmed && kicker.Scale.IsEqualApprox(Vector2.One * 1.2f), "winner K finishes at 120 percent");
                    var boardFive = table.GetNode<RoomPokerCardView>("Board/Card2");
                    Check(boardFive.Outlined && boardFive.Dimmed && !table.GetNode<RoomPokerCardView>("Board/Card5").Outlined,
                        "left public 5 has the local outline without loser shrinking");
                    var hostPosition = DisplayServer.WindowGetPosition();
                    var originalPosition = winnerDog.Position;
                    var pointer = winnerDog.PokerHand.GetGlobalRect().GetCenter();
                    main.GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true,
                        Position = pointer, GlobalPosition = pointer }, true);
                    var far = pointer + new Vector2(5000, -200);
                    main.GetViewport().PushInput(new InputEventMouseMotion { ButtonMask = MouseButtonMask.Left,
                        Position = far, GlobalPosition = far }, true);
                    await Wait();
                    main.GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false,
                        Position = far, GlobalPosition = far }, true);
                    await Wait();
                    Check(DisplayServer.WindowGetPosition() == hostPosition
                        && !winnerDog.Position.IsEqualApprox(originalPosition)
                        && new Rect2(Vector2.Zero, DisplayServer.WindowGetSize()).Grow(1).Encloses(winnerDog.PokerHand.GetGlobalRect()),
                        "dragging by the player panel includes its full bounds without moving the host");
                    renderDogs.MoveMember(winnerDog.MemberId, originalPosition, 0, false, 0, 0);
                    await Wait();
                }
                if (preset == 6) Check(emphasis.DecisiveCard == null, "different two-pair core has no kicker animation");
                if (preset == 8) Check(table.LocalPanel.IsWinner && emphasis.DecisiveCard == null && emphasis.LocalCards.SetEquals(emphasis.WinnerCards),
                    "local winner sees matching core masks without loser analysis");
                await Capture($"showdown-{preset}-{(mode == 0 ? "zh" : "en")}");
                if (preset == 7)
                {
                    int initial = emphasis.WinnerId;
                    long[] balances = session.View.Public.Seats.Select(s => s.Chips).ToArray();
                    session.Paused = false;
                    await Wait(2.5);
                    session.Paused = true;
                    Check(table.Emphasis.WinnerId != initial && table.Emphasis.DecisiveCard != null,
                        "other tied winners take turns explaining loss to the local viewer");
                    Check(main.RoomPokerDogsForSmoke.RemoteDogs.Count(d => d.PokerHand.IsWinner) == 2
                        && balances.SequenceEqual(session.View.Public.Seats.Select(s => s.Chips)), "both tied crowns persist and payout animation never awards twice");
                    await Capture($"tie-second-{(mode == 0 ? "zh" : "en")}");
                }
            }

            controls.GetNode<OptionButton>("PresetRow/Preset").Select(1);
            controls.GetNode<OptionButton>("PresetRow/Preset").EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
            Click(ui.GetNode<Button>("Next"));
            await Wait();
            for (int step = 0; session.View.Public.ActivePlayerId != client.Id && step < 12; step++) session.Step();
            await Wait();
            game.GetNode<HandAreaController>("HandArea").EmitSignal(HandAreaController.SignalName.HandKnocked);
            Check(session.View.Public.CompletedTurns.Any(t => t.PlayerId == client.Id && t.FlipsUsed == 0 && t.Reason == PokerTurnEnd.Passed),
                "arm ends turn without spending flips or touching solo state");
            Complete(session, client.Id);
            await Wait();
            Check(session.View.Public.Settlement.Winners.Count == state.Seats.Count && session.View.Public.Settlement.DealerChange == PokerDealerChange.Tie,
                "preset board tie divides pot and reverses dealer order");
            await Capture(mode == 0 ? "free-tie-zh" : "compact-tie-en");
            Click(ui.GetNode<Button>("Next"));
            await Wait();
            int timedOut = session.View.Public.ActivePlayerId.Value;
            Click(controls.GetNode<Button>("Actions/Timeout"));
            Check(session.View.Public.CompletedTurns.Any(t => t.PlayerId == timedOut && t.Reason == PokerTurnEnd.TimedOut),
                "timeout finishes current turn through the real authority");
            Check(game.GameData.Chips == soloChips && game.State == GameState.WaitingForBet, "preview does not alter solo chips or state");

            main.Call("OnSteamMockPanelVisibilityChanged", true);
            await Wait();
            await Capture(mode == 0 ? "mock-poker-zh" : "mock-poker-en");
            Click(controls.GetNode<Button>("Actions/Pause"));
            Check(!session.Paused, "Mock resume restores the running clock");
            double beforeClose = session.View.Public.SecondsRemaining;
            int? actorBeforeClose = session.View.Public.ActivePlayerId;
            Click(mock.GetNode<Button>("Panel/Root/TabRow/CloseButton"));
            await Wait(0.3);
            Check(!mock.Visible && !session.Paused && main.PokerPreviewForSmoke == session
                && (session.View.Public.ActivePlayerId != actorBeforeClose || session.View.Public.SecondsRemaining < beforeClose),
                "closing Mock keeps the same match running");
            main.Call("OnSteamMockPanelVisibilityChanged", true);
            await Wait();
            Click(controls.GetNode<Button>("Actions/Pause"));
            Check(session.Paused && controls.GetNode<OptionButton>("PresetRow/Preset").Selected == 1,
                "reopening Mock retains preset and controls the same match");
            main.Call("OnSteamMockPanelVisibilityChanged", false);

            game.EmitSignal(GameManager.SignalName.DesktopModeRequested);
            await Wait();
            Check(main.PokerPreviewForSmoke == session, "desktop switch retains the same match");
            main.RoomRequestPokerForSmoke();
            await Wait();
            Check(main.PokerPreviewForSmoke == session && game.IsSessionPokerActive, "poker return resumes same match without a second ante");
            Check(main.RoomPokerDogsForSmoke.RemoteDogs.All(d => d.Footprint.HasArea()), "remote footprint includes card panel");
            Click(controls.GetNode<Button>("Actions/Exit"));
            await Wait();
            Check(main.PokerPreviewForSmoke == null && !game.IsSessionPokerActive
                && !main.GetNode<InfoPanelController>("InfoPanel").SessionContent.Visible
                && main.RoomPokerDogsForSmoke.RemoteDogs.All(d => !d.PokerHand.Visible), "exit restores solo UI and removes card adornments");
            Check(!mock.PokerContent.Visible && !mock.GetNode<Button>("Panel/Root/TabRow/PokerTab").Visible
                && mock.GetNode<Control>("Panel/Root/SteamPage").Visible && mock.PokerContent.GetChildCount() == 0,
                "ending preview removes debug controls and returns Mock to Steam");
            DismissWelcome(game);
            panel.ShowRoomPage();
            Click(start);
            await Wait();
            Check(main.PokerPreviewForSmoke != null, "preview can be restarted");
            if (mode == 1)
            {
                var guest = page.MockClientForSmoke(3);
                guest.Leave();
                page.AdvancePreview(0.5);
                await Wait();
                Check(main.PokerPreviewForSmoke == null && !game.IsSessionPokerActive, "roster changes end the fixed-roster preview");
                DismissWelcome(game);
                guest.Join(page.MockClientForSmoke(2).JoinedCode);
                page.AdvancePreview(0.5);
                await Wait();
                panel.ShowRoomPage();
                Click(start);
                await Wait();
                Check(main.PokerPreviewForSmoke != null, "new roster can start a fresh preview");
            }
            client.Leave();
            page.AdvancePreview(0.5);
            await Wait();
            Check(main.PokerPreviewForSmoke == null && !game.IsSessionPokerActive, "room leave tears down match and handlers");
            DismissWelcome(game);
            game.EmitSignal(GameManager.SignalName.DesktopModeRequested);
            await Wait();
            panel.ShowRoomPage();
        }
        // A paid solo hand must survive an attempt to enter the local preview.
        client.Join(page.MockClientForSmoke(2).JoinedCode);
        page.AdvancePreview(0.5);
        await Wait();
        panel.CloseImmediate();
        main.RoomRequestPokerForSmoke();
        await Wait();
        var solo = main.GetNode<GameManager>("PlayRoot/SubViewportContainer/SubViewport/Main");
        DismissWelcome(solo);
        solo.Call("OnBetRequested");
        await Wait(1.2);
        var soloCards = solo.GetNode<CardTableController>("CardArea");
        var heldHand = Enumerable.Range(0, 5).Select(i => soloCards.GetCard(i).CardValue).ToArray();
        Check(solo.State == GameState.Dealt, "solo hand still starts after leaving preview");
        panel.ShowRoomPage();
        Click(page.GetNode<Button>("Room/PokerPreview"));
        await Wait();
        Check(main.PokerPreviewForSmoke == null && solo.State == GameState.Dealt
            && heldHand.SequenceEqual(Enumerable.Range(0, 5).Select(i => soloCards.GetCard(i).CardValue)),
            "preview entry cannot discard an unfinished solo hand");
        GD.Print("[MischiefPokerMainChecks] PASS main UI, private cards, turns, timeout, settlement, full/half host, desktop round-trip, isolation and cleanup.");
    }

    private static void Complete(MischiefPokerPreviewSession session, int localId)
    {
        for (int step = 0; session.View.Public.Phase != PokerPhase.Settled && step < 20; step++)
            if (session.View.Public.ActivePlayerId == localId) session.EndTurn(); else session.Step();
        Check(session.View.Public.Phase == PokerPhase.Settled, "hand reaches settlement");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        GD.Print("[MischiefPokerMainChecks] OK " + message);
    }
}
#endif
