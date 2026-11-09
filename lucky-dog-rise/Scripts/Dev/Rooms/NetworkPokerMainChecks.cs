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
            foreach (int id in new[] { 2, 3 }) page.MockClientForSmoke(id).Leave();
            client.Create("Network table test");
            await Until(() => client.View?.OwnerId == client.Id, "ordinary room creation finishes");
            foreach (int id in new[] { 2, 3 }) page.MockClientForSmoke(id).Join(client.JoinedCode);
            await Until(() => client.View.Members.Length == 3 && lobby.Session?.Connected == true, "three independent Mock endpoints connected");
            var session = lobby.Session;
            Click(lobby.GetNode<Button>("Propose"));
            await Until(() => session.CanStart, "Mock humans consent through serialized ready messages");
            Click(lobby.GetNode<Button>("Start"));
            await Until(() => main.HasNode("PlayRoot/SubViewportContainer/SubViewport/Main/RoomPokerTable"), "formal start lazily creates the real poker view from desktop mode");
            game = main.GetNode<GameManager>("PlayRoot/SubViewportContainer/SubViewport/Main");
            var table = game.GetNode<RoomPokerTableView>("RoomPokerTable");
            Check(main.PokerPreviewForSmoke == null && !table.Round.PauseAfterPayout, "network session is independent of the manual local preview");
            Check(session.State.Hand.Public.Pot == 300 && session.State.Hand.HoleCards.Count == 2, "initial ante and own two private cards");
            await Until(() => main.RoomPokerDogsForSmoke?.RemoteDogs.Count() == 2, "remote dogs have table presentations");
            Check(main.RoomPokerDogsForSmoke.RemoteDogs.All(d => d.PokerHand.Visible && !d.PokerHand.CardsRevealed), "opponents show backs during play");
            await Until(() => session.DisplayView.Public.ActivePlayerId == client.Id && session.State.DealRemaining == 0 && !table.IsDealing,
                "simulated clients pass to the local owner");
            long revision = session.DisplayView.Public.Revision;
            Click(table.GetNode<TextureButton>("Board/Card1"));
            await Until(() => session.DisplayView.Public.Revision > revision, "board click reaches authority");
            Check(session.DisplayView.Public.Board[0].PendingReplacement, "accepted flip is reflected in table state");
            game.GetNode<HandAreaController>("HandArea").EmitSignal(HandAreaController.SignalName.HandKnocked);
            await Until(() => session.DisplayView.Public.Phase == PokerPhase.Settled, "arm finishes turn and all clients receive settlement");
            Check(data.Chips == soloChips, "network payout does not touch single-player chips");
            await Until(() => session.DisplayView.Public.HandNumber == 2, "real presentation barrier collects cards and starts the next hand", 30);
            Check(session.DisplayView.Public.Seats.Sum(s => s.Chips) + session.DisplayView.Public.Pot
                + session.DisplayView.Public.TotalSystemTips == 3000, "one ante and one payout per hand");

            var late = page.AddMockClientForSmoke(4); late.Join(client.JoinedCode);
            await Until(() => session.State.Players.Length == 4, "late room join is tracked separately from current hand");
            Check(session.State.Hand.Public.Seats.Count == 3, "late member waits for next hand");
            await Until(() => main.RoomPokerDogsForSmoke.RemoteDogs.Any(d => d.MemberId == 4 && d.PokerHand.IsWaiting), "late dog displays waiting instead of private cards");
            // Existing round-trips must not create a second authority or charge another ante.
            Guid tableId = session.State.Table;
            game.EmitSignal(GameManager.SignalName.DesktopModeRequested); await Wait();
            main.RoomRequestPokerForSmoke(); await Wait();
            Check(lobby.Session == session && session.State.Table == tableId, "desktop/poker round trip preserves the network session");
            page.MockClientForSmoke(2).Leave();
            await Until(() => session.State.Status == PokerTableStatus.Interrupted && !game.IsSessionPokerActive, "participant departure tears down views safely");
            Check(!game.HasNode("RoomPokerTable") && data.Chips == soloChips, "table cleanup leaves solo assets and chips intact");
            if (game.IsPokerWelcomeVisible)
                Click(game.GetNode<Button>("PokerWelcomeDialog/InputShield/Center/Panel/Margin/Content/Confirm"));
            panel.ShowRoomPage(); await Wait();
            Click(lobby.GetNode<Button>("Propose")); await Wait(0.2);
            Check(game.IsSessionPokerReserved, "preparation reserves the solo table");
            game.Call("OnBetRequested");
            Check(game.State == GameState.WaitingForBet && data.Chips == soloChips, "ready state cannot debit a solo hand");
            client.Leave(); await Wait(0.5);
            Check(lobby.Session == null && !game.IsSessionPokerReserved, "leaving room disposes the channel and ready reservation");
            var mock = main.GetNode<SteamMockPanelController>("SteamMockPanel");
            Click(mock.GetNode<Button>("Panel/Root/SteamPage/NetworkPokerRow/Guest"));
            await Until(() => lobby.Session?.State?.Status == PokerTableStatus.Preparing, "Mock controls create a separate remote authority");
            Check(!lobby.Session.IsOwner && !lobby.Session.IsReady, "guest view waits for explicit local consent");
            Click(lobby.GetNode<Button>("Ready"));
            await Until(() => game.IsSessionPokerActive, "simulated host starts only after local Ready");
            session = lobby.Session;
            table = game.GetNode<RoomPokerTableView>("RoomPokerTable");
            await Until(() => session.DisplayView.Public.ActivePlayerId == client.Id && session.State.DealRemaining == 0 && !table.IsDealing
                && !table.GetNode<TextureButton>("Board/Card2").Disabled,
                "guest UI receives its turn from another endpoint");
            revision = session.DisplayView.Public.Revision;
            Click(table.GetNode<TextureButton>("Board/Card2"));
            await Until(() => session.DisplayView.Public.Revision > revision && !session.ActionPending, "guest command is acknowledged by remote authority");
            Check(session.DisplayView.Public.Board[1].PendingReplacement, "guest flip preserves real local dog identity");
            Click(mock.GetNode<Button>("Panel/Root/SteamPage/NetworkPokerRow/Host"));
            await Until(() => lobby.Session?.IsOwner == true && lobby.Session.State?.Status == PokerTableStatus.Preparing,
                "Mock role reset during play reaches a fresh host preparation");
            client.Leave(); await Until(() => !game.IsSessionPokerActive && lobby.Session == null, "guest leave also cleans up its presentation");
            panel.CloseImmediate();
            if (game.IsPokerWelcomeVisible)
                Click(game.GetNode<Button>("PokerWelcomeDialog/InputShield/Center/Panel/Margin/Content/Confirm"));
            game.Call("OnBetRequested"); await Wait(1.2);
            Check(game.State == GameState.Dealt, "single-player poker remains available after leaving");
            GD.Print("[NetworkPokerMainChecks] PASS formal readiness/start, private hands, real input, automatic round transition, waiting member, mode round-trip, interruption and solo restoration.");
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
