#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using LuckyDogRise.Rooms.Poker;

namespace LuckyDogRise.Rooms;

internal static class NetworkPokerMainChecks
{
    public static async Task Run(ModeManager main, InGameRoomPreview page)
    {
        var tree = main.GetTree();
        GameManager game;
        var data = main.GameDataObj;
        int soloChips = data.Chips;
        var client = page.PreviewClient;
        var panel = main.SettingsPanelObj;
        var lobby = page.PokerLobby;
        async Task Wait(double seconds = 0.1) => await main.ToSignal(tree.CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        async Task Until(Func<bool> condition, string reason, double limit = 20)
        {
            for (double t = 0; !condition() && t < limit; t += 0.05) await Wait(0.05);
            Check(condition(), reason);
        }
        void Click(BaseButton button) { Check(!button.Disabled, $"{button.Name} enabled"); button.EmitSignal(BaseButton.SignalName.Pressed); }
        async Task PointerClick(Control card)
        {
            var container = main.GetNode<SubViewportContainer>("PlayRoot/SubViewportContainer");
            var viewport = container.GetNode<SubViewport>("SubViewport");
            var point = container.GetGlobalTransformWithCanvas()
                * ((card.GetGlobalTransformWithCanvas() * (card.Size / 2)) * (container.Size / viewport.Size));
            main.GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
            await Wait(.02);
            foreach (bool pressed in new[] { true, false })
                main.GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point,
                    ButtonIndex = MouseButton.Left, Pressed = pressed }, true);
        }
        void HideTest()
        {
            if (!OperatingSystem.IsWindows() || DisplayServer.GetName() == "headless") return;
            var handle = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
            if (handle != IntPtr.Zero) WindowNative.ShowWindow(handle, WindowNative.SW_HIDE);
        }
        tree.ProcessFrame += HideTest;
        AudioManager.Instance.SetSfxVolume(0); AudioManager.Instance.StopBgm();
        try
        {
            main.RoomRequestPokerForSmoke();
            await Until(() => main.HasNode("PlayRoot/SubViewportContainer/SubViewport/Main"), "solo view initialized");
            game = main.GetNode<GameManager>("PlayRoot/SubViewportContainer/SubViewport/Main");
            if (game.IsPokerWelcomeVisible)
                Click(game.GetNode<Button>("PokerWelcomeDialog/InputShield/Center/Panel/Margin/Content/Confirm"));
            panel.CloseImmediate();
            game.Call("OnBetRequested");
            await Wait(.2);
            Check(data.Chips < soloChips, "solo hand paid its own ante before opting into multiplayer");
            soloChips = data.Chips;
            foreach (int id in new[] { 2, 3 }) page.MockClientForSmoke(id).Leave();
            client.Create("Network table test");
            await Until(() => client.View?.OwnerId == client.Id, "ordinary room creation finishes");
            foreach (int id in new[] { 2, 3 }) page.MockClientForSmoke(id).Join(client.JoinedCode);
            await Until(() => client.View.Members.Length == 3 && lobby.Session?.Connected == true, "three independent Mock endpoints connected");
            Check(client.View.GameId == "social" && lobby.Session.State.Status == PokerTableStatus.Idle, "default chat room has no poker preparation");
            Check(client.ProposeGameChange(RoomRules.MischiefPoker) == "ok", "poker is selected through the room game vote");
            await Until(() => client.View.GameChange != null, "vote published");
            string proposal = client.View.GameChange.Id;
            foreach (int id in new[] { 2, 3 })
                Check(page.MockClientForSmoke(id).RespondGameChange(proposal, true) == "ok", "simulated human accepts the room game");
            await Until(() => client.CanConfirmGameChange, "majority agrees");
            Check(client.ConfirmGameChange(proposal) == "ok", "owner confirms game change");
            await Until(() => lobby.Session.CanStart && game.IsSessionPokerReserved, "accepted vote automatically prepares consenting members");
            var session = lobby.Session;
            await Wait(1.2);
            Check(game.State == GameState.WaitingForBet && data.Chips == soloChips, "unfinished solo hand discarded without refund or late reward");
            var info = main.GetNode<InfoPanelController>("InfoPanel");
            var waitingPanel = info.SessionContent.GetChild<RoomPokerPanelView>(0);
            Check(game.IsSessionPokerActive && info.SessionContent.Visible
                && waitingPanel.GetNode<Button>("Actions/Primary").Text == lobby.GetNode<Button>("Actions/Primary").Text,
                "poker information panel replaces solo UI before start and mirrors host action");
            game.Call("OnBetRequested");
            Check(data.Chips == soloChips, "poker room blocks solo bets even before starting");
            Check(!lobby.HasNode("Roster") && !lobby.HasNode("Ready") && !lobby.HasNode("Stop"), "single member roster replaces duplicate poker controls");
            game.EmitSignal(GameManager.SignalName.DesktopModeRequested); await Wait(.2);
            await Until(() => main.RoomDesktopForSmoke.LocalPokerHand.IsWaiting, "own desktop dog shows an empty waiting panel before starting");
            Check(main.RoomDesktopForSmoke.RemoteDogs.All(d => d.PokerHand.Visible && d.PokerHand.IsWaiting), "all remote dogs show waiting panels in the poker room");
            var desktopMode = main.CurrentMode;
            Click(lobby.GetNode<Button>("Actions/Primary"));
            await Until(() => session.IsParticipant && main.RoomDesktopForSmoke.LocalPokerHand.FirstCard.Visible, "host joins by starting; desktop panel receives its hand");
            Check(main.CurrentMode == desktopMode, "network start never forces desktop into poker");
            main.RoomRequestPokerForSmoke(); panel.CloseImmediate();
            await Until(() => game.IsSessionPokerActive, "explicit mode switch attaches the existing network table");
            await Wait();
            var gameChoice = page.GetNode<OptionButton>("Room/Game/Choices/Current");
            Check(gameChoice.Selected == 2 && gameChoice.Disabled, "current gameplay shows poker and running table locks game switching");
            var table = game.GetNode<RoomPokerTableView>("RoomPokerTable");
            var pokerPanel = info.SessionContent.GetChild<RoomPokerPanelView>(0);
            var pause = pokerPanel.GetNode<Button>("Actions/Primary");
            Check(main.PokerPreviewForSmoke == null, "formal game is independent of the manual local preview");
            Check(session.State.Hand.Public.Pot == 300 && session.State.Hand.HoleCards.Count == 2, "initial ante and own two private cards");
            await Until(() => main.RoomPokerDogsForSmoke?.RemoteDogs.Count() == 2, "remote dogs have table presentations");
            Check(main.RoomPokerDogsForSmoke.RemoteDogs.All(d => d.PokerHand.Visible && !d.PokerHand.CardsRevealed), "opponents show backs during play");
            Check(game.RoomPokerDog.RoomPokerReaction == DataTables.EDogReactionTrigger.Default
                && main.RoomPokerDogsForSmoke.RemoteDogs.All(d => d.Dog.RoomPokerReaction == DataTables.EDogReactionTrigger.Default),
                "active hand uses neutral expressions for local and remote dogs");
            await Until(() => session.DisplayView.Public.ActivePlayerId == client.Id && session.State.DealRemaining == 0 && !table.IsDealing,
                "simulated clients pass to the local owner");
            Click(pause); await Wait();
            Check(session.State.Continuation == PokerContinuation.PauseRequested, "host panel requests pause after this hand");
            Click(pause); await Wait();
            Check(session.State.Continuation == PokerContinuation.Automatic, "same button can undo a pending pause");
            Click(pause); await Wait();
            long revision = session.DisplayView.Public.Revision;
            panel.ShowRoomPage(); await Wait(.2);
            var container = main.GetNode<SubViewportContainer>("PlayRoot/SubViewportContainer");
            panel.SetPanelPosition(container.Position + new Vector2(container.Size.X * container.Scale.X + 8, 0));
            // Production placement updates the dismiss shield after moving the panel.
            // This manually arranged fixture must do the same, even if it opened over poker.
            main.Call("UpdatePlayPanelDismissOverlay");
            await Wait(.1);
            Check(panel.IsOpen && !panel.PanelRect.Intersects(new Rect2(container.Position, container.Size * container.Scale)),
                "settings panel is open beside the poker table");
            await PointerClick(table.GetNode<TextureButton>("Board/Card1"));
            await Until(() => session.DisplayView.Public.Revision > revision, "real viewport click reaches authority with side panel open", 2);
            Check(session.DisplayView.Public.Board[0].PendingReplacement, "accepted flip is reflected in table state");
            Check(table.Paws.ActiveCount == 1 && game.RoomPokerDog.PokerPaw(false).Modulate.A == 0
                && game.RoomPokerDog.PokerPaw(true).Modulate.A == 1,
                "network local flip hides only the first paw and plays once");
            var pawLayer = table.Paws.GetNode<CanvasLayer>("Overlay");
            var flyingPaw = pawLayer.GetChildren().OfType<Sprite2D>().Single(s => s.Visible);
            var boardCenter = container.GetGlobalTransformWithCanvas()
                * ((table.GetNode<Control>("Board").GetGlobalTransformWithCanvas() * new Vector2(60, 84))
                    * (container.Size / container.GetNode<SubViewport>("SubViewport").Size));
            Check(pawLayer.CustomViewport == main.GetViewport() && flyingPaw.Position.DistanceTo(boardCenter) < 90,
                "paw crosses the poker SubViewport into the correct window-space card position");
            panel.SetPanelPosition(container.Position);
            main.Call("UpdatePlayPanelDismissOverlay");
            await Wait(.1);
            Check(table.GetNode<TextureButton>("Board/Card2").Disabled, "overlapping settings panel still blocks cards");
            panel.CloseImmediate(); await Wait(.1);
            Check(!table.GetNode<TextureButton>("Board/Card2").Disabled, "closing the overlay restores input without switching modes");
            game.GetNode<HandAreaController>("HandArea").EmitSignal(HandAreaController.SignalName.HandKnocked);
            await Until(() => session.DisplayView.Public.Phase == PokerPhase.Settled, "arm finishes turn and all clients receive settlement");
            await Until(() => client.Bubbles.ContainsKey(2) || client.Bubbles.ContainsKey(3), "Mock settlement chat uses normal bubbles; a loser may stay silent");
            Check(client.ChatAllowed, "poker rooms permit chat");
            await Until(() => session.State.Continuation == PokerContinuation.Paused && table.Round.Stage == PokerRoundStage.AwaitingNext,
                "pause retains all cards after winner payout", 25);
            var result = session.DisplayView.Public;
            Check(game.RoomPokerDog.RoomPokerReaction == table.DogReactionFor(client.Id)
                && main.RoomPokerDogsForSmoke.RemoteDogs.All(d => d.Dog.RoomPokerReaction == table.DogReactionFor(d.MemberId)),
                "formal showdown presents each dog's own result reaction");
            game.EmitSignal(GameManager.SignalName.DesktopModeRequested); await Wait(.2);
            var desktopDog = main.GetNode<DogVisual>("BossKeyContent/ContentA/DogArea");
            Check(desktopDog.RoomPokerReaction == table.DogReactionFor(client.Id)
                && main.RoomDesktopForSmoke.RemoteDogs.All(d => d.Dog.RoomPokerReaction == table.DogReactionFor(d.MemberId))
                && client.Reaction == (int)desktopDog.CurrentReaction,
                "desktop round trip retains result poses without echoing them as member activity");
            main.RoomRequestPokerForSmoke(); await Wait(.2);
            Guid tableId = session.State.Table;
            await Wait(1.0);
            Check(session.DisplayView.Public.HandNumber == 1 && data.Chips == soloChips, "pause adds no ante and payouts do not touch personal chips");
            Check(pause.Text == RoomPokerText.T("继续", "Resume")
                && pokerPanel.GetNode<Button>("Actions/Restart").Visible
                && !pokerPanel.GetNode<Button>("Actions/Restart").Disabled, "paused host sees both resume and restart");
            Click(pause);
            await Until(() => session.DisplayView.Public.HandNumber == 2, "resume collects cards and begins next hand in the same session", 8);
            Check(session.State.Table == tableId && session.DisplayView.Public.Dealer == result.Settlement.NextDealer, "resume keeps original session and planned dealer");
            await Wait();
            Check(game.RoomPokerDog.RoomPokerReaction == DataTables.EDogReactionTrigger.Default,
                "automatic next-hand presentation clears previous win/loss reaction");
            Check(session.DisplayView.Public.Seats.Sum(s => s.Chips) + session.DisplayView.Public.Pot
                + session.DisplayView.Public.TotalSystemTips == 3000, "one ante and one payout per hand");

            // A visible box consumes only local input. The authority's clock and continuation remain live.
            var reward = new PendingBlindBoxReward { BlindBoxId = 1001, RewardShown = false,
                RevealPathId = LubanData.Tables.TbBlindBoxRevealPath.DataList[0].Id,
                ItemId = LubanData.Tables.TbItem.DataList.First(i => i.ItemType == DataTables.EItemType.Headwear).Id };
            var rewardProperty = typeof(GameData).GetProperty(nameof(GameData.PendingBlindBoxReward));
            rewardProperty.SetValue(data, reward);
            game.ShowPendingBlindBoxReward(reward);
            await Wait(.2);
            Check(UiInteractionOccupancy.IsOccupied(main), "box presentation occupies local interaction during network play");
            await Until(() => session.DisplayView.Public.HandNumber == 3, "host's box overlay cannot block timeout or automatic next hand", 40);
            Check(UiInteractionOccupancy.IsOccupied(main) && data.PendingBlindBoxReward == reward,
                "box remains unopened across the entire hand without affecting network progression");
            game.HidePendingBlindBoxReward(); rewardProperty.SetValue(data, null);
            await Wait(.2);
            Check(game.IsSessionPokerActive && table.Round.DisplayView.Public.HandNumber == 3 && data.Chips == soloChips,
                "closing a box shows the latest hand without changing personal assets");

            var late = page.AddMockClientForSmoke(4); late.Join(client.JoinedCode);
            await Until(() => session.State.Players.Length == 4, "late room join is tracked separately from current hand");
            Check(session.State.Hand.Public.Seats.Count == 3, "late member waits for host restart");
            await Until(() => main.RoomPokerDogsForSmoke.RemoteDogs.Any(d => d.MemberId == 4 && d.PokerHand.IsWaiting), "late dog displays waiting instead of private cards");
            Check(main.RoomPokerDogsForSmoke.RemoteDogs.Single(d => d.MemberId == 4).Dog.RoomPokerReaction == null,
                "late waiting dog keeps its normal activity expression");
            game.EmitSignal(GameManager.SignalName.DesktopModeRequested); await Wait();
            Check(main.RoomDesktopForSmoke.LocalPokerHand.IsVisibleInTree(), "own panel remains visible on return to desktop");
            main.RoomRequestPokerForSmoke(); await Wait();
            Check(lobby.Session == session && session.State.Table == tableId, "desktop/poker round trip preserves the network session");
            Click(pause);
            await Until(() => session.State.Continuation == PokerContinuation.Paused, "second pause completes without blocking timeout", 40);
            await Until(() => pokerPanel.GetNode<Button>("Actions/Restart").Visible
                && !pokerPanel.GetNode<Button>("Actions/Restart").Disabled, "restart UI catches up with authority pause");
            Click(pokerPanel.GetNode<Button>("Actions/Restart"));
            await Until(() => session.State.Table != tableId && session.State.Hand?.Public.HandNumber == 1, "restart resets the match after card collection", 8);
            Check(session.State.Hand.Public.Dealer.PlayerId == session.State.Hand.PlayerId
                && session.State.Hand.Public.Seats.All(s => s.Chips == 900)
                && session.State.Hand.Public.Pot == 300, "restart resets 1000 each, takes one ante and gives host first dealership");
            Check(game.GetChildren().OfType<RoomPokerTableView>().Count() == 1, "restart replaces the old table without duplicate views");
            page.MockClientForSmoke(2).Leave();
            await Until(() => session.State.Status == PokerTableStatus.Interrupted && session.State.Hand == null, "participant departure interrupts the match safely");
            Check(game.IsSessionPokerReserved && game.IsSessionPokerActive && data.Chips == soloChips, "interruption stays in poker room UI without enabling solo play");
            panel.ShowRoomPage(); await Wait();
            Click(lobby.GetNode<Button>("Actions/Primary")); await Wait(.2);
            Check(game.IsSessionPokerReserved, "fresh preparation reserves the solo table");
            game.Call("OnBetRequested");
            Check(game.State == GameState.WaitingForBet && data.Chips == soloChips, "ready state cannot debit a solo hand");
            client.Leave(); await Wait(.5);
            Check(lobby.Session == null && !game.IsSessionPokerReserved, "leaving room disposes the channel and ready reservation");
            Check(game.RoomPokerDog.RoomPokerReaction == null && desktopDog.RoomPokerReaction == null,
                "leaving room removes both local dog presentation overrides");
            panel.CloseImmediate();
            var deck = (DeckManager)typeof(GameManager).GetField("_deck", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(game);
            int winningSeed = Enumerable.Range(1, 1000).First(seed =>
            {
                var candidate = new DeckManager(seed); candidate.Deal();
                return CardEvaluator.GetPayout(candidate.CurrentHand, data.BetAmount) > 0;
            });
            deck.SetFixedSeed(winningSeed);
            game.Call("OnBetRequested"); await Wait(1.2);
            Check(game.State == GameState.Dealt, "solo hand available outside the poker room");
            game.GetNode<HandAreaController>("HandArea").EmitSignal(HandAreaController.SignalName.FirstKnockLanded);
            await Until(() => game.PendingReward != null, "unclaimed solo reward exists before joining poker room");
            int beforeReadyChips = data.Chips;
            var mock = main.GetNode<SteamMockPanelController>("SteamMockPanel");
            Click(mock.GetNode<Button>("Panel/Root/TabRow/NetworkPokerTab"));
            Click(mock.GetNode<Button>("Panel/Root/NetworkPokerPage/NetworkPokerRow/Guest"));
            await Until(() => lobby.Session?.State?.Status == PokerTableStatus.Preparing, "Mock controls create a separate remote authority");
            Check(!lobby.Session.IsOwner && !lobby.Session.IsReady, "unanswered vote leaves local guest waiting");
            Check(page.PokerLobby.Session.Context.Game == RoomRules.MischiefPoker, "Mock setup uses the real room game");
            panel.CloseImmediate();
            await Until(() => game.IsSessionPokerReserved && game.PendingReward == null, "entering poker room discards solo reward before guest readiness");
            game.Call("OnBetRequested"); game.Call("OnChipCollected");
            Check(game.State == GameState.WaitingForBet && data.Chips == beforeReadyChips, "unready observer cannot bet solo or collect a stale reward");
            panel.ShowRoomPage(); await Wait();
            var readyButton = page.GetNode<VBoxContainer>("Room/Members").GetChildren().OfType<HBoxContainer>()
                .Select(row => row.GetNode<Button>("Ready")).Single(button => button.Visible);
            pokerPanel = info.SessionContent.GetChild<RoomPokerPanelView>(0);
            var guestAction = pokerPanel.GetNode<Button>("Actions/Primary");
            Check(guestAction.Text == readyButton.Text && guestAction.Disabled == readyButton.Disabled, "guest preparation controls match in both panels");
            Click(guestAction);
            await Until(() => lobby.Session.IsParticipant, "information-panel ready reaches simulated authority");
            game.Call("OnChipCollected");
            Check(game.PendingReward == null && data.Chips == beforeReadyChips, "ready discards unclaimed solo reward; stale collection cannot credit it");
            panel.CloseImmediate();
            session = lobby.Session;
            table = game.GetNode<RoomPokerTableView>("RoomPokerTable");
            await Until(() => session.DisplayView.Public.ActivePlayerId == client.Id && session.State.DealRemaining == 0 && !table.IsDealing
                && !table.GetNode<TextureButton>("Board/Card2").Disabled,
                "guest UI receives its turn from another endpoint");
            revision = session.DisplayView.Public.Revision;
            Click(table.GetNode<TextureButton>("Board/Card2"));
            await Until(() => session.DisplayView.Public.Revision > revision && !session.ActionPending, "guest command is acknowledged by remote authority");
            Check(session.DisplayView.Public.Board[1].PendingReplacement, "guest flip preserves real local dog identity");
            rewardProperty.SetValue(data, reward); game.ShowPendingBlindBoxReward(reward);
            await Until(() => session.DisplayView.Public.HandNumber == 2, "guest box overlay also permits authoritative timeout and next hand", 40);
            Check(UiInteractionOccupancy.IsOccupied(main) && session.IsReady, "guest box remains open without revoking readiness");
            game.HidePendingBlindBoxReward(); rewardProperty.SetValue(data, null);
            await Wait(.2);
            pokerPanel = info.SessionContent.GetChild<RoomPokerPanelView>(0);
            guestAction = pokerPanel.GetNode<Button>("Actions/Primary");
            Click(guestAction); await Wait(.2);
            Check(!session.IsReady && guestAction.Text == RoomPokerText.T("取消离座", "Cancel"), "guest queues sit-out with visible cancellation");
            Click(guestAction); await Wait(.2);
            Check(session.IsReady, "guest can cancel sit-out before the hand ends");
            Click(guestAction);
            await Until(() => session.DisplayView.Public.HandNumber == 3 && !session.IsInHand, "sit-out begins at next hand", 40);
            await Wait(.2);
            Check(table.LocalPanel.IsWaiting && session.DisplayView.HoleCards.Count == 0
                && guestAction.Text == RoomPokerText.T("入座", "Sit in"), "sitting-out guest has no cards and sees sit-in action");
            var oldSeat = session.DisplayView.Public.Seats.Single(s => s.PlayerId == session.DisplayView.PlayerId);
            Click(guestAction); await Wait(.2);
            Check(session.IsReady && !session.IsInHand && guestAction.Text == RoomPokerText.T("取消准备", "Cancel"), "reentry waits for next hand and is cancellable");
            await Until(() => session.DisplayView.Public.HandNumber == 4 && session.IsInHand, "returning guest rejoins the existing seat", 30);
            var restored = session.DisplayView.Public.Seats.Single(s => s.PlayerId == session.DisplayView.PlayerId);
            Check(restored.SeatNumber == oldSeat.SeatNumber && restored.Chips == oldSeat.Chips - 100
                && session.DisplayView.HoleCards.Count == 2, "reentry preserves seat and balance with exactly one ante");
            Click(mock.GetNode<Button>("Panel/Root/NetworkPokerPage/NetworkPokerRow/Host"));
            await Until(() => lobby.Session?.IsOwner == true && lobby.Session.State?.Status == PokerTableStatus.Preparing,
                "Mock role reset during play reaches a fresh host preparation");
            client.Leave(); await Until(() => !game.IsSessionPokerActive && lobby.Session == null, "guest leave cleans up its presentation");
            panel.CloseImmediate();
            if (game.IsPokerWelcomeVisible)
                Click(game.GetNode<Button>("PokerWelcomeDialog/InputShield/Center/Panel/Margin/Content/Confirm"));
            game.Call("OnBetRequested"); await Wait(1.2);
            Check(game.State == GameState.Dealt, "single-player poker remains available after leaving");
            GD.Print("[NetworkPokerMainChecks] PASS real card input with side panel, shared preparation controls, observer solo lock, pause/resume/restart, sit-out/return, box-independent continuation, private hands, Mock chat and solo restoration.");
        }
        finally
        {
            AudioManager.Instance.SetSfxVolume(0); AudioManager.Instance.StopBgm();
            await Wait(0.2);
            tree.ProcessFrame -= HideTest;
        }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Network poker: " + message);
        GD.Print("[NetworkPokerMainChecks] " + message);
    }
}
#endif
