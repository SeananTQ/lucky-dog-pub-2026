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
            Check(!lobby.HasNode("Roster") && !lobby.HasNode("Ready") && !lobby.HasNode("Stop"), "single member roster replaces duplicate poker controls");
            game.EmitSignal(GameManager.SignalName.DesktopModeRequested); await Wait(.2);
            await Until(() => main.RoomDesktopForSmoke.LocalPokerHand.IsWaiting, "own desktop dog shows an empty waiting panel before starting");
            Check(main.RoomDesktopForSmoke.RemoteDogs.All(d => d.PokerHand.Visible && d.PokerHand.IsWaiting), "all remote dogs show waiting panels in the poker room");
            var desktopMode = main.CurrentMode;
            Click(lobby.GetNode<Button>("Start"));
            await Until(() => session.IsParticipant && main.RoomDesktopForSmoke.LocalPokerHand.FirstCard.Visible, "host joins by starting; desktop panel receives its hand");
            Check(main.CurrentMode == desktopMode && !game.IsSessionPokerActive, "network start never forces desktop into poker");
            main.RoomRequestPokerForSmoke(); panel.CloseImmediate();
            await Until(() => game.IsSessionPokerActive, "explicit mode switch attaches the existing network table");
            await Wait();
            var gameChoice = page.GetNode<OptionButton>("Room/Game/Choices/Current");
            Check(gameChoice.Selected == 2 && gameChoice.Disabled, "current gameplay shows poker and running table locks game switching");
            var table = game.GetNode<RoomPokerTableView>("RoomPokerTable");
            var info = main.GetNode<InfoPanelController>("InfoPanel");
            var pokerPanel = info.SessionContent.GetChild<RoomPokerPanelView>(0);
            var pause = pokerPanel.GetNode<Button>("Pause");
            Check(main.PokerPreviewForSmoke == null, "formal game is independent of the manual local preview");
            Check(session.State.Hand.Public.Pot == 300 && session.State.Hand.HoleCards.Count == 2, "initial ante and own two private cards");
            await Until(() => main.RoomPokerDogsForSmoke?.RemoteDogs.Count() == 2, "remote dogs have table presentations");
            Check(main.RoomPokerDogsForSmoke.RemoteDogs.All(d => d.PokerHand.Visible && !d.PokerHand.CardsRevealed), "opponents show backs during play");
            await Until(() => session.DisplayView.Public.ActivePlayerId == client.Id && session.State.DealRemaining == 0 && !table.IsDealing,
                "simulated clients pass to the local owner");
            Click(pause); await Wait();
            Check(session.State.Continuation == PokerContinuation.PauseRequested, "host panel requests pause after this hand");
            Click(pause); await Wait();
            Check(session.State.Continuation == PokerContinuation.Automatic, "same button can undo a pending pause");
            Click(pause); await Wait();
            long revision = session.DisplayView.Public.Revision;
            Click(table.GetNode<TextureButton>("Board/Card1"));
            await Until(() => session.DisplayView.Public.Revision > revision, "board click reaches authority");
            Check(session.DisplayView.Public.Board[0].PendingReplacement, "accepted flip is reflected in table state");
            game.GetNode<HandAreaController>("HandArea").EmitSignal(HandAreaController.SignalName.HandKnocked);
            await Until(() => session.DisplayView.Public.Phase == PokerPhase.Settled, "arm finishes turn and all clients receive settlement");
            await Until(() => client.Bubbles.ContainsKey(2) && client.Bubbles.ContainsKey(3), "Mock humans send overlapping settlement bubbles through normal chat");
            Check(client.ChatAllowed, "poker rooms permit chat");
            await Until(() => session.State.Continuation == PokerContinuation.Paused && table.Round.Stage == PokerRoundStage.AwaitingNext,
                "pause retains all cards after winner payout", 25);
            var result = session.DisplayView.Public;
            Guid tableId = session.State.Table;
            await Wait(1.0);
            Check(session.DisplayView.Public.HandNumber == 1 && data.Chips == soloChips, "pause adds no ante and payouts do not touch personal chips");
            Click(pause);
            await Until(() => session.DisplayView.Public.HandNumber == 2, "resume collects cards and begins next hand in the same session", 8);
            Check(session.State.Table == tableId && session.DisplayView.Public.Dealer == result.Settlement.NextDealer, "resume keeps original session and planned dealer");
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
            Check(session.State.Hand.Public.Seats.Count == 3, "late member waits for next hand");
            await Until(() => main.RoomPokerDogsForSmoke.RemoteDogs.Any(d => d.MemberId == 4 && d.PokerHand.IsWaiting), "late dog displays waiting instead of private cards");
            game.EmitSignal(GameManager.SignalName.DesktopModeRequested); await Wait();
            Check(main.RoomDesktopForSmoke.LocalPokerHand.IsVisibleInTree(), "own panel remains visible on return to desktop");
            main.RoomRequestPokerForSmoke(); await Wait();
            Check(lobby.Session == session && session.State.Table == tableId, "desktop/poker round trip preserves the network session");
            page.MockClientForSmoke(2).Leave();
            await Until(() => session.State.Status == PokerTableStatus.Interrupted && !game.IsSessionPokerActive, "participant departure tears down views safely");
            Check(!game.HasNode("RoomPokerTable") && data.Chips == soloChips, "table cleanup leaves personal assets intact");
            panel.ShowRoomPage(); await Wait();
            Click(lobby.GetNode<Button>("Start")); await Wait(.2);
            Check(game.IsSessionPokerReserved, "fresh preparation reserves the solo table");
            game.Call("OnBetRequested");
            Check(game.State == GameState.WaitingForBet && data.Chips == soloChips, "ready state cannot debit a solo hand");
            client.Leave(); await Wait(.5);
            Check(lobby.Session == null && !game.IsSessionPokerReserved, "leaving room disposes the channel and ready reservation");
            var mock = main.GetNode<SteamMockPanelController>("SteamMockPanel");
            Click(mock.GetNode<Button>("Panel/Root/TabRow/NetworkPokerTab"));
            Click(mock.GetNode<Button>("Panel/Root/NetworkPokerPage/NetworkPokerRow/Guest"));
            await Until(() => lobby.Session?.State?.Status == PokerTableStatus.Preparing, "Mock controls create a separate remote authority");
            Check(!lobby.Session.IsOwner && !lobby.Session.IsReady, "unanswered vote leaves local guest waiting");
            Check(page.PokerLobby.Session.Context.Game == RoomRules.MischiefPoker, "Mock setup uses the real room game");
            panel.CloseImmediate();
            var deck = (DeckManager)typeof(GameManager).GetField("_deck", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(game);
            int winningSeed = Enumerable.Range(1, 1000).First(seed =>
            {
                var candidate = new DeckManager(seed); candidate.Deal();
                return CardEvaluator.GetPayout(candidate.CurrentHand, data.BetAmount) > 0;
            });
            deck.SetFixedSeed(winningSeed);
            game.Call("OnBetRequested"); await Wait(1.2);
            Check(game.State == GameState.Dealt && !game.IsSessionPokerReserved, "unready room member can still play solo");
            game.GetNode<HandAreaController>("HandArea").EmitSignal(HandAreaController.SignalName.FirstKnockLanded);
            await Until(() => game.PendingReward != null, "solo winner has a real unclaimed reward before readiness");
            int beforeReadyChips = data.Chips;
            panel.ShowRoomPage(); await Wait();
            var readyButton = page.GetNode<VBoxContainer>("Room/Members").GetChildren().OfType<HBoxContainer>()
                .Select(row => row.GetNode<Button>("Ready")).Single(button => button.Visible);
            Click(readyButton);
            await Until(() => game.IsSessionPokerActive, "simulated host starts after local readiness");
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
            Click(mock.GetNode<Button>("Panel/Root/NetworkPokerPage/NetworkPokerRow/Host"));
            await Until(() => lobby.Session?.IsOwner == true && lobby.Session.State?.Status == PokerTableStatus.Preparing,
                "Mock role reset during play reaches a fresh host preparation");
            client.Leave(); await Until(() => !game.IsSessionPokerActive && lobby.Session == null, "guest leave cleans up its presentation");
            panel.CloseImmediate();
            if (game.IsPokerWelcomeVisible)
                Click(game.GetNode<Button>("PokerWelcomeDialog/InputShield/Center/Panel/Margin/Content/Confirm"));
            game.Call("OnBetRequested"); await Wait(1.2);
            Check(game.State == GameState.Dealt, "single-player poker remains available after leaving");
            GD.Print("[NetworkPokerMainChecks] PASS room-game consent, solo discard, desktop panels, privacy, pause/undo/resume, box-independent continuation, Mock chat, waiting member, guest authority and solo restoration.");
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
