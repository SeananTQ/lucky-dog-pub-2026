using DataTables;
using LuckyDogRise.Rooms.Poker;

internal static class AutoChatChecks
{
    public static void Run()
    {
        var match = new MischiefPokerMatch(new[] { 1, 2, 3 }, 1);
        match.StartHand(0, Check.Deck(new[] { Check.Cards("Ac Ad"), Check.Cards("Kc Qd"), Check.Cards("9c 8d") }, "Ah As 2h 4d 6s"));
        var acting = new PokerNetworkState(Guid.NewGuid(), 1, PokerTableStatus.Playing,
            Array.Empty<PokerParticipant>(), match.GetPlayerView(1), 0, "");
        Check.PassAll(match, 0);
        var settled = acting with { Hand = match.GetPlayerView(1), SettlementElapsed = 3.4 };
        PokerNetworkState Next(PokerNetworkState state, long hand) => state with
        { Hand = state.Hand! with { Public = state.Hand.Public with { HandNumber = hand } } };
        PokerAutoChat Fresh()
        {
            var chat = new PokerAutoChat();
            Check.Equal(chat.Tick("room", acting, true, false), null, "no speech before public settlement");
            return chat;
        }

        var chat = Fresh();
        Check.Equal(chat.Tick("room", settled with { SettlementElapsed = 2.5 }, true, false), null, "wait for reveal before speech");
        var line = chat.Tick("room", settled, true, false);
        Check.Equal(line?.Kind, PokerAutoChatKind.Win, "winner chats after reveal");
        Check.Equal(line?.Rank, EHandRank.FourOfAKind, "winning line uses the actual evaluated rank");
        for (int i = 0; i < 10; i++)
            Check.Equal(chat.Tick("room", settled, true, false), null, "repeated snapshots do not repeat a line");

        var delayedReveal = Fresh();
        Check.Equal(delayedReveal.Tick("room", settled, true, false, revealReady: false), null, "slow visual reveal cannot leak the result in chat");
        Check.That(delayedReveal.Tick("room", settled, true, false, revealReady: true) != null, "revealed result may chat within its opportunity");

        chat.ManualMessageSent("room", settled);
        Check.That(chat.TakenOver, "successful manual message takes over the current match");
        chat.Tick("room", Next(acting, 2), true, false);
        Check.Equal(chat.Tick("room", Next(settled, 2), true, false), null, "takeover survives the next hand");
        chat.Tick("room", Next(settled, 2) with { Continuation = PokerContinuation.Paused }, true, false);
        Check.That(chat.TakenOver && chat.Enabled, "pause retains takeover without changing saved preference");
        var restarted = acting with { Table = Guid.NewGuid() };
        chat.Tick("room", restarted, true, false);
        Check.That(!chat.TakenOver, "restart clears takeover");
        Check.That(chat.Tick("room", settled with { Table = restarted.Table }, true, false) != null, "new match restores auto chat");

        chat = Fresh(); chat.SetEnabled(false);
        Check.Equal(chat.Tick("room", settled, true, false), null, "disabled setting suppresses speech");
        chat.SetEnabled(true);
        Check.Equal(chat.Tick("room", settled, true, false), null, "re-enabling does not replay an old result");
        chat.Tick("room", Next(acting, 2), true, false);
        Check.That(chat.Tick("room", Next(settled, 2), true, false) != null, "re-enabled setting permits a future hand");
        chat.ManualMessageSent("room", Next(settled, 2));
        chat.SetEnabled(false); chat.SetEnabled(true);
        Check.That(!chat.TakenOver, "explicit off/on releases manual takeover");
        Check.Equal(chat.Tick("room", Next(settled, 2), true, false), null, "off/on never duplicates the same hand");
        chat.SetEnabled(false);
        chat.Tick("other room", restarted, true, false);
        Check.That(!chat.Enabled, "new room does not override the personal off switch");

        foreach (var condition in new[] { "typing", "offline", "stale", "collecting" })
        {
            chat = Fresh();
            var state = condition == "stale" ? settled with { SettlementElapsed = 30 }
                : condition == "collecting" ? settled with { Continuation = PokerContinuation.Collecting } : settled;
            Check.Equal(chat.Tick("room", state, condition != "offline", condition == "typing"), null, condition + " skips the opportunity");
            Check.Equal(chat.Tick("room", settled, true, false), null, condition + " never queues a stale line");
        }
        chat = new();
        Check.Equal(chat.Tick("room", settled, true, false), null, "late view attachment does not replay settlement");
        chat.ManualMessageSent("room", settled);
        chat.Tick(null, null, false, false);
        Check.That(!chat.TakenOver, "leaving clears match speech state");

        var loser = settled.Hand! with { PlayerId = 2 };
        Check.Equal(PokerAutoChat.ChooseLine(loser), null, "some losses remain silent");
        Check.Equal(PokerAutoChat.ChooseLine(loser with { Public = loser.Public with { HandNumber = 2 } })?.Kind,
            PokerAutoChatKind.CloseLoss, "loss line one");
        Check.Equal(PokerAutoChat.ChooseLine(loser with { Public = loser.Public with { HandNumber = 3 } })?.Kind,
            PokerAutoChatKind.NextHand, "loss line two");
        Check.Equal(PokerAutoChat.ChooseLine(settled.Hand! with { PlayerId = 4 }), null, "waiting spectators do not claim results");
        var tie = new MischiefPokerMatch(new[] { 1, 2 }, 1);
        tie.StartHand(0, Check.Deck(new[] { Check.Cards("2h 3d"), Check.Cards("4h 5d") }, "Ac Tc Jc Qc Kc"));
        Check.PassAll(tie, 0);
        Check.Equal(PokerAutoChat.ChooseLine(tie.GetPlayerView(1))?.Kind, PokerAutoChatKind.Tie, "split pot gets a tie line");
        Console.WriteLine("PASS auto chat takeover, restart, settings, public reveal, timing and skipped opportunities");
    }
}
