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
            async Task Wait(double seconds) =>
                await main.ToSignal(tree.CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
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
            var roomKickConfirm = panel.GetNode<ConfirmOverlayController>("RoomKickConfirm");
            var roomKickAccept = roomKickConfirm.GetNode<Button>("OverlayPanel/Margin/Content/ButtonRow/ConfirmButton");
            var roomKickCancel = roomKickConfirm.GetNode<Button>("OverlayPanel/Margin/Content/ButtonRow/CancelButton");
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
            client.Join("missing-notice-test");
            await Settle();
            var notice = main.RoomJoinNoticeForSmoke;
            Check(notice?.HasBubble == true && !notice.Editing && main.RoomChatSuppressesHintForSmoke,
                "first join failure uses passive dog bubble even before entering a room");
            Check(notice.GetNode<Label>("Message/Text").Text.Contains(InGameRoomPreview.ChatText("Rooms_NotFound"))
                && !notice.GetNode<Label>("Message/Text").Text.Contains("Rooms_"), "notice includes localized failure reason");
            Check(DisplayServer.WindowGetSize() == originalWindowSize && !main.RoomDesktopWindowActive
                && client.Bubbles.Count == 0, "failure notice neither expands window nor broadcasts chat");
            Check(notice.GetParent<CanvasLayer>().Layer > panel.Layer && !notice.ContainsPoint(notice.GlobalPosition),
                "passive notice renders above settings without intercepting input");
            await Capture(main, "join-failed-alone");
            main.RoomFullscreenHideForSmoke(true);
            main.RoomAdvanceJoinNoticeForSmoke(0);
            var remaining = main.RoomJoinNoticeRemainingForSmoke;
            main.RoomAdvanceJoinNoticeForSmoke(20);
            Check(!notice.HasBubble && main.RoomJoinNoticeRemainingForSmoke == remaining,
                "hidden dog defers notice without consuming its display lifetime");
            main.RoomFullscreenHideForSmoke(false);
            main.RoomAdvanceJoinNoticeForSmoke(0);
            Check(notice.HasBubble, "deferred failure returns when dog becomes visible");
            main.RoomAdvanceJoinNoticeForSmoke(RoomRules.ChatLifetime + 1);
            Check(!notice.HasBubble && main.RoomChatSuppressesHintForSmoke, "notice expires but blind-box delay remains");
            main.RoomAdvanceChatHintForSmoke(4.9);
            Check(main.RoomChatSuppressesHintForSmoke, "blind-box hint stays suppressed for five seconds after notice");
            main.RoomAdvanceChatHintForSmoke(0.2);
            Check(!main.RoomChatSuppressesHintForSmoke, "blind-box suppression ends after notice cooldown");
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
            // Verify room restoration before a poker round-trip: that separate
            // mode transition intentionally repositions the single-dog window.
            var restoreCode = client.JoinedCode;
            client.Leave();
            await Settle();
            Check(DisplayServer.WindowGetSize() == originalWindowSize
                && DisplayServer.WindowGetPosition() == originalWindowPosition,
                "leaving a room restores the original single-dog window");
            client.Join(restoreCode);
            await Settle();
            var remotes = client.View.Members.Where(member => member.Id != client.Id)
                .Select(member => main.RoomDesktopForSmoke.RemoteDogs.Single(dog => dog.MemberId == member.Id)).ToArray();
            var remoteClient = page.MockClientForSmoke(remotes[0].MemberId);
            var chat = main.RoomDesktopForSmoke.LocalChat;
            var rewardHint = main.GetNode<BalloonHintController>("BossKeyContent/CanvasLayer/BlindBoxHint");
            chat.OpenChat();
            var draftInput = chat.GetNode<LineEdit>("Composer/Content/Input");
            draftInput.Text = "unfinished draft";
            client.Join("missing-from-current-room");
            await Settle();
            Check(client.JoinedCode == restoreCode && notice.HasBubble && !chat.Editing && !chat.HasBubble,
                "failed room switch retains membership and temporarily replaces local composer");
            Check(!panel.IsOpen && draftInput.Text == "unfinished draft" && remoteClient.Bubbles.Count == 0,
                "notice preserves draft and closed panel, with no remote chat");
            chat.OpenChat();
            Check(!chat.Editing, "notice prevents overlapping composer");
            // Let the actual frame timer expire once, rather than advancing a test clock.
            await Wait(RoomRules.ChatLifetime + 0.2);
            Check(!notice.HasBubble && main.RoomChatSuppressesHintForSmoke, "frame-driven notice lifetime expires");
            chat.OpenChat();
            Check(chat.Editing && draftInput.Text == "unfinished draft", "composer can reopen with preserved draft");
            draftInput.Clear();
            chat.CloseChat();
            main.RoomAdvanceChatHintForSmoke(5.1);
            GD.Print("[InGameRoomSmoke] JOIN_NOTICE_PASS localized standalone error, passive layering, visibility pause, expiry, blind-box delay, retained room/draft and remote isolation.");
            main.RoomRefreshHintForSmoke();
            await Wait(0.2);
            Check(rewardHint.Modulate.A > 0.99f, "baseline countdown balloon is visible");
            var rewardHintLeft = (rewardHint.GetGlobalTransformWithCanvas() * Vector2.Zero).X;
            chat.OpenChat();
            Check(rewardHint.Modulate.A == 0 && rewardHint.MouseFilter == Control.MouseFilterEnum.Ignore,
                "opening composer immediately removes reward balloon and its hit target");
            chat.CloseChat();
            // Exercise the actual frame-driven timer once; subsequent boundary
            // cases advance that same production timer without waiting in real time.
            await Wait(4.6);
            Check(main.RoomChatSuppressesHintForSmoke && rewardHint.Modulate.A == 0,
                "canceling composer keeps reward balloon hidden before five seconds");
            await Wait(0.7);
            Check(!main.RoomChatSuppressesHintForSmoke && rewardHint.Modulate.A > 0.99f,
                "canceling composer restores reward balloon after five real seconds");
            chat.OpenChat();
            chat.CloseChat();
            main.RoomAdvanceChatHintForSmoke(4);
            chat.OpenChat();
            main.RoomAdvanceChatHintForSmoke(20);
            Check(main.RoomChatSuppressesHintForSmoke && rewardHint.Modulate.A == 0,
                "reopening composer suspends restoration throughout editing");
            chat.CloseChat();
            main.RoomAdvanceChatHintForSmoke(4.99);
            Check(main.RoomChatSuppressesHintForSmoke, "reopening restarts a full five-second wait");
            main.RoomAdvanceChatHintForSmoke(0.02);
            await Wait(0.2);
            Check(rewardHint.Modulate.A > 0.99f, "reopened and canceled composer eventually restores balloon");
            chat.OpenChat();
            await Frame();
            Check(chat.Editing && !panel.IsOpen, "main dog opens composer and releases overlapping settings panel");
            var chatPoint = chat.ComposerRectForSmoke.GetCenter();
            Check(main.RoomHitTestForSmoke(DisplayServer.WindowGetPosition() + (Vector2I)chatPoint),
                "composer participates in transparent-window hit testing");
            main._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = chatPoint });
            main._Input(new InputEventMouseMotion { ButtonMask = MouseButtonMask.Left, Position = chatPoint + new Vector2(30, 0) });
            Check(!main.RoomDraggingForSmoke, "composer click cannot start dog dragging");
            chat.SubmitForSmoke("你好，房间里的朋友！");
            page.AdvancePreview(0);
            await Frame();
            Check(!chat.Editing && remoteClient.Bubbles.ContainsKey(client.Id), "main input sends through shared service");
            Check(remoteClient.SendChat("Hello from the other dog!") == "", "remote can send reply");
            page.AdvancePreview(0);
            await Frame();
            Check(client.Bubbles.ContainsKey(remoteClient.Id), "main receives remote bubble");
            Check(Mathf.IsEqualApprox(chat.MessageRectForSmoke.Position.X, rewardHintLeft),
                "chat body starts at the countdown balloon's normal left edge");
            Check(rewardHint.Modulate.A == 0, "sent local message keeps reward balloon hidden");
            main.RoomAdvanceChatHintForSmoke(20);
            Check(main.RoomChatSuppressesHintForSmoke, "restoration does not count down while a sent message remains");
            await Capture(main, "chat-in-game");
            page.AdvancePreview(7);
            await Frame();
            main.RoomAdvanceChatHintForSmoke(4);
            Check(main.RoomChatSuppressesHintForSmoke && rewardHint.Modulate.A == 0,
                "message expiry starts a fresh five-second wait");
            main.RoomAdvanceChatHintForSmoke(1.1);
            await Wait(0.2);
            Check(!main.RoomChatSuppressesHintForSmoke && rewardHint.Modulate.A > 0.99f,
                "reward balloon restores five seconds after local message disappears");
            Check(remoteClient.SendChat("A remote-only message") == "", "remote sends independent message");
            page.AdvancePreview(0);
            await Frame();
            Check(!main.RoomChatSuppressesHintForSmoke && rewardHint.Modulate.A > 0.99f,
                "another dog's message does not suppress the local reward balloon");
            var alwaysShowRewardHint = SettingsManager.LoadAlwaysShowBlindBoxBubble();
            try
            {
                Check(main.GameDataObj.GetBlindBoxHintState().Status == BlindBoxHintStatus.Waiting,
                    "hidden-countdown test starts before a reward becomes available");
                SettingsManager.SaveAlwaysShowBlindBoxBubble(false);
                main.RoomRefreshHintForSmoke();
                await Wait(0.2);
                chat.OpenChat();
                chat.CloseChat();
                main.RoomAdvanceChatHintForSmoke(5.1);
                await Wait(0.2);
                Check(!main.RoomChatSuppressesHintForSmoke && rewardHint.Modulate.A == 0,
                    "restoration respects an otherwise hidden countdown instead of forcing it visible");
            }
            finally
            {
                SettingsManager.SaveAlwaysShowBlindBoxBubble(alwaysShowRewardHint);
                main.RoomRefreshHintForSmoke();
            }
            GD.Print("[InGameRoomSmoke] CHAT_HINT_DELAY_PASS cancel, message expiry, reopening, remote isolation and normal visibility rules.");
            chat.OpenChat();
            main.RoomModeForSmoke(true);
            await Frame();
            Check(!chat.Editing, "poker transition closes composer");
            main.RoomModeForSmoke(false);
            await Settle();
            page.AdvancePreview(7);
            panel.Open();
            await Frame();
            foreach (int reaction in new[] { 1005, 1006, 1003 })
            {
                remoteClient.SetReaction(reaction);
                page.AdvancePreview(0);
                await Frame();
                Check((int)remotes[0].Dog.CurrentReaction == reaction, "remote activity reaches main-game dog");
            }
            remoteClient.NotifyInputActivity();
            page.AdvancePreview(0);
            await Frame();
            Check(remotes[0].Dog.RoomTongueActive && !remotes[1].Dog.RoomTongueActive,
                "main-game remote tongue states are independent");
            page.AdvancePreview(1);
            await Frame();
            Check(!remotes[0].Dog.RoomTongueActive, "main-game remote tongue stops after input");
            main.RoomActivityForSmoke(1006, true);
            page.AdvancePreview(0);
            await Frame();
            Check(remoteClient.View.Members.Single(m => m.Id == client.Id).Reaction == 1006
                && remoteClient.IsTongueActive(client.Id), "main-game own reaction and input reach other clients");
            var ownHat = client.HeadwearId;
            var ownSkin = client.SkinId;
            client.SetAppearance(0, 0, client.Reaction);
            main.RoomEquipmentRefreshForSmoke();
            page.AdvancePreview(0);
            Check(client.Reaction == 1006 && client.SkinId == ownSkin && client.HeadwearId == ownHat,
                "equipment update preserves current reaction");
            main.RoomActivityForSmoke(1001, false);
            client.StopInputActivity();
            await Frame();
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
            var codeLabel = page.GetNode<LinkButton>("Room/CodeRow/Code");
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
            Check(DisplayServer.WindowGetSize() == originalWindowSize,
                "leave restores single-dog window size after poker repositioning");
            page.GetNode<LineEdit>("Lobby/CreateRow/Name").Text = "UI regression";
            Click("Lobby/CreateRow/Create");
            await Settle();
            Check(client.View?.Members.Count(member => !member.IsCompanion) == 1
                && client.View.Members.Count(member => member.IsCompanion) == 3
                && client.View.OwnerId == client.Id, "create own room with three companion dogs");
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

            // C-stage companions use the actual main-scene room UI and desktop
            // presentation. The earlier three-player A-stage baseline stays intact.
            client.Leave();
            page.MockClientForSmoke(2).Leave();
            page.MockClientForSmoke(3).Leave();
            await Settle();
            page.RefreshRooms();
            await Settle();
            var emptyDirectory = page.GetNode<VBoxContainer>("Lobby/RoomList");
            Check(client.Listings.Length == 0 && client.JoinedCode.Length == 0
                && !page.GetNode<Control>("Lobby/Empty").Visible && emptyDirectory.GetChildCount() == 1,
                "successful empty search shows one local room entry without creating a real room");
            var companionEntry = emptyDirectory.GetChild<InGameRoomDirectoryRow>(0);
            Check(companionEntry.GetNode<Label>("Count").Text == "3/6"
                && !companionEntry.GetNode<Button>("Join").Disabled,
                "empty lobby offers an actionable three-companion room");
            await Capture(main, "in-game-empty-lobby-companions");
            companionEntry.GetNode<Button>("Join").EmitSignal(BaseButton.SignalName.Pressed);
            companionEntry.GetNode<Button>("Join").EmitSignal(BaseButton.SignalName.Pressed);
            Check(client.Operation == RoomOperation.Create && client.IsBusy,
                "joining the local room starts one real creation request");
            await Settle();
            string companionRoom = client.JoinedCode;
            var companionMembers = client.View.Members.Where(member => member.IsCompanion).ToArray();
            Check(client.View.Members.Length == 4 && companionMembers.Length == 3
                && client.View.OwnerId == client.Id
                && companionMembers.Select(member => member.Id).Order().SequenceEqual(new[] { -3, -2, -1 }),
                "new main-game room contains one real owner and three stable negative companion identities");
            var companionDogs = companionMembers.Select(member => desktop.RemoteDogs.Single(dog => dog.MemberId == member.Id)).ToArray();
            var companionAppearances = companionMembers.ToDictionary(member => member.Id,
                member => (member.Name, member.SkinId, member.HeadwearId, member.Presence));
            var roomName = page.GetNode<LineEdit>("Room/TitleRow/Name");
            roomName.GrabFocus();
            await Frame();
            roomName.Text = "秋哥的朋友小屋";
            page.AdvancePreview(1);
            await Frame();
            Check(roomName.Text == "秋哥的朋友小屋", "companion activity does not replace a room-name draft");
            roomName.ReleaseFocus();
            await Settle();
            Check(client.View.Name == "秋哥的朋友小屋", "room name is committed when its input loses focus");
            foreach (var locale in new[] { "en", "zh_CN" })
            {
                L10n.SetLocale(locale, save: false);
                await Frame();
                Check(roomName.Text == "秋哥的朋友小屋" && client.View.Name == "秋哥的朋友小屋",
                    "changing language leaves the room's original name intact");
            }
            var accessOption = page.GetNode<OptionButton>("Room/Access/Current");
            Check(panel.IsOpen && page.IsVisibleInTree() && !accessOption.Disabled,
                "permission popup opens from a visible enabled host page");
            accessOption.ShowPopup();
            await Frame();
            Check(accessOption.GetPopup().Visible && accessOption.GetPopup().ItemCount == 3,
                "room access uses the settings-style dropdown");
            var accessPopup = accessOption.GetPopup();
            Check(panel.ContainsPoint((Vector2)accessPopup.Position + (Vector2)accessPopup.Size / 2),
                "room permission popup participates in desktop click-through hit testing");
            await Capture(main, "in-game-room-access-dropdown");
            // Route through the window/embedded-popup dispatcher, not directly
            // into the popup's child Controls (which bypasses Window shortcuts).
            Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = Key.Escape });
            await Frame();
            Input.ParseInputEvent(new InputEventKey { Pressed = false, Keycode = Key.Escape });
            Check(!accessPopup.Visible, "Escape dismisses the permission popup through its native input path");
            accessOption.ShowPopup();
            title.GetNode<Button>("OutfitPresetTab").EmitSignal(BaseButton.SignalName.Pressed);
            Check(!accessPopup.Visible, "switching away from the room tab closes its permission popup");
            tab.EmitSignal(BaseButton.SignalName.Pressed);
            await Frame();
            accessOption.ShowPopup();
            panel.Close();
            Check(!accessPopup.Visible, "closing the panel immediately closes the permission popup before fading");
            await main.ToSignal(tree.CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
            panel.Open();
            await main.ToSignal(tree.CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
            await Frame();
            var originalClipboard = DisplayServer.ClipboardGet();
            try
            {
                page.GetNode<LinkButton>("Room/CodeRow/Code").EmitSignal(BaseButton.SignalName.Pressed);
                Check(DisplayServer.ClipboardGet() == companionRoom, "clicking the underlined room code copies it");
                Click("Room/CodeRow/Visibility");
                Click("Room/CodeRow/Copy");
                Check(DisplayServer.ClipboardGet() == companionRoom
                    && page.GetNode<LinkButton>("Room/CodeRow/Code").Text != companionRoom,
                    "the copy button remains usable when the code is hidden");
                Click("Room/CodeRow/Visibility");
            }
            finally { DisplayServer.ClipboardSet(originalClipboard); }
            Check(companionMembers.Select(member => member.Name).Order().SequenceEqual(
                    new[] { "熬夜的程序员", "没洗头的美术", "打喷嚏的策划" }.Order()),
                "companions use the agreed playful display names without extra labels");
            var companionUsable = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
            Check(Mathf.IsEqualApprox(localDog.GlobalPosition.X, companionUsable.Position.X
                + companionUsable.Size.X / 2f - DisplayServer.WindowGetPosition().X)
                && companionDogs[0].Position.X > localDog.GlobalPosition.X
                && companionDogs[1].Position.X < localDog.GlobalPosition.X,
                "companion room keeps own dog at center and fills right then left");
            var memberRows = page.GetNode<VBoxContainer>("Room/Members");
            for (int index = 0; index < client.View.Members.Length; index++)
            {
                var member = client.View.Members[index];
                if (!member.IsCompanion) continue;
                var kick = memberRows.GetChild(index).GetNode<Button>("Kick");
                Check(kick.Visible && !kick.Disabled, "the host can remove companion dogs from the member list");
                kick.EmitSignal(BaseButton.SignalName.Pressed);
                Check(roomKickConfirm.Visible && roomKickConfirm.GetParent() == panel
                    && roomKickConfirm.GetNode<Label>("OverlayPanel/Margin/Content/MessageBg/Message").Text
                        .StartsWith(member.Name + "\n", StringComparison.Ordinal),
                    "companion removal opens the named modal above the whole settings panel");
                roomKickCancel.EmitSignal(BaseButton.SignalName.Pressed);
                Check(!roomKickConfirm.Visible && client.View.Members.Length == 4,
                    "canceling a companion confirmation leaves everyone in the room");
            }
            var firstCompanionKick = memberRows.GetChild(1).GetNode<Button>("Kick");
            firstCompanionKick.EmitSignal(BaseButton.SignalName.Pressed);
            title.GetNode<Button>("OutfitPresetTab").EmitSignal(BaseButton.SignalName.Pressed);
            Check(!roomKickConfirm.Visible, "switching away from the room tab immediately clears the removal modal");
            tab.EmitSignal(BaseButton.SignalName.Pressed);
            await Frame();
            firstCompanionKick.EmitSignal(BaseButton.SignalName.Pressed);
            panel.Close();
            Check(!roomKickConfirm.Visible, "closing the settings panel clears the removal modal before fading");
            roomKickAccept.EmitSignal(BaseButton.SignalName.Pressed);
            await Wait(0.2);
            panel.Open();
            await Wait(0.2);
            await Settle();
            Check(!roomKickConfirm.Visible && client.View.Members.Length == 4,
                "reopening the panel cannot revive or accept an old removal confirmation");
            foreach (var dog in companionDogs)
            {
                int sends = 0;
                dog.SendRequested += _ => sends++;
                dog.Display(companionMembers.Single(member => member.Id == dog.MemberId),
                    local: true, owner: true, showName: true, bubble: "");
                dog.OpenChat();
                dog.SubmitChatForSmoke("must not send");
                Check(!dog.Editing && sends == 0 && !dog.GetNode<Label>("NameBar/Name").Text.Contains("♛")
                    && !dog.GetNode<Label>("NameBar/Name").Text.Contains("本机"),
                    "companion presentation rejects accidental local input and host badges");
            }
            page.AdvancePreview(9);
            await Frame();
            Check(client.View.Members.Where(member => member.IsCompanion).All(member =>
                    companionAppearances[member.Id] == (member.Name, member.SkinId, member.HeadwearId, member.Presence))
                && client.Bubbles.Count == 0, "companion appearance stays fixed across activity updates without automatic chat");
            await Capture(main, "in-game-companions-created");
            Click("Room/Browse");
            await Settle();
            Check(client.Listings.Single(listing => listing.Code == companionRoom).Count == 4,
                "browsing the owner's room displays one human plus three companions as 4/6");
            var ownRoomRow = page.GetNode<VBoxContainer>("Lobby/RoomList").GetChildren()
                .OfType<InGameRoomDirectoryRow>().Single();
            Check(ownRoomRow.GetNode<Label>("Count").Text == "4/6",
                "the actual directory row shows the same total population as the room");
            await Capture(main, "in-game-directory-companion-count");
            Click("Lobby/Return");
            await Frame();
            static int ActivityRank(int reaction) => reaction switch
            { 1003 => 0, 1001 => 1, 1005 => 2, 1006 => 3, _ => -1 };
            var previousMoods = client.View.Members.Where(member => member.IsCompanion)
                .ToDictionary(member => member.Id, member => member.Reaction);
            var rises = new System.Collections.Generic.HashSet<int>();
            var falls = new System.Collections.Generic.HashSet<int>();
            bool sawTongueMotion = false;
            bool sawQuietDescent = false;
            for (int step = 0; step < RoomCompanionPlan.MaxActivityCycleSeconds * 2; step++)
            {
                page.AdvancePreview(0.5);
                await Frame();
                foreach (var member in client.View.Members.Where(member => member.IsCompanion))
                {
                    int previousRank = ActivityRank(previousMoods[member.Id]);
                    int rank = ActivityRank(member.Reaction);
                    if (rank == previousRank) continue;
                    var dog = companionDogs.Single(dog => dog.MemberId == member.Id);
                    Check(Math.Abs(rank - previousRank) == 1,
                        "companion moods rise and fall one tier at a time in the actual desktop presentation");
                    var tongue = dog.Dog.GetNode<Sprite2D>("HeadRoot/Tonghe");
                    if (rank > previousRank)
                    {
                        rises.Add(rank);
                        Check(member.TongueActive && dog.Dog.RoomTongueActive,
                            "every rising mood keeps the smooth room tongue animation active");
                        if (!sawTongueMotion)
                        {
                            var position = tongue.Position;
                            for (int sample = 0; sample < 4; sample++)
                            {
                                await Wait(0.07);
                                sawTongueMotion |= !tongue.Position.IsEqualApprox(position);
                            }
                            Check(sawTongueMotion, "rising companion tongue really moves across rendered frames");
                        }
                    }
                    else
                    {
                        falls.Add(rank);
                        Check(!member.TongueActive && !dog.Dog.RoomTongueActive,
                            "cooling companion mood stops the tongue instead of continuing the high activity");
                        if (!sawQuietDescent)
                        {
                            var position = tongue.Position;
                            await Wait(0.2);
                            Check(tongue.Position.IsEqualApprox(position), "descending companion tongue stays still");
                            sawQuietDescent = true;
                        }
                    }
                    previousMoods[member.Id] = member.Reaction;
                }
                if (rises.Count == 3 && falls.Count == 3 && sawTongueMotion && sawQuietDescent) break;
            }
            Check(rises.Count == 3 && falls.Count == 3 && sawTongueMotion && sawQuietDescent,
                "all rising and cooling tiers are exercised through the main game");
            GD.Print("[InGameRoomSmoke] COMPANION_ACTIVITY_PASS progressive moods, moving tongue on rise and quiet descent.");
            var companionOwnPosition = localDog.GlobalPosition;
            for (int index = 0; index < companionDogs.Length; index++)
            {
                var dog = companionDogs[index];
                var initial = dog.Position;
                var otherPositions = desktop.RemoteDogs.Where(other => other.MemberId != dog.MemberId)
                    .ToDictionary(other => other.MemberId, other => other.Position);
                var delta = new Vector2(index % 2 == 0 ? -35 : 35, -100 - index * 30);
                await Drag(initial, delta);
                Check(dog.Position.IsEqualApprox(initial + delta)
                    && localDog.GlobalPosition.IsEqualApprox(companionOwnPosition)
                    && desktop.RemoteDogs.Where(other => other.MemberId != dog.MemberId)
                        .All(other => other.Position.IsEqualApprox(otherPositions[other.MemberId])),
                    "negative companion identities drag independently without moving other dogs");
            }
            var companionPositions = desktop.RemoteDogs.ToDictionary(dog => dog.MemberId, dog => dog.Position);
            main.ApplyDesktopPetScaleStep(twiceScaleStep);
            await Frame();
            Check(desktop.RemoteDogs.All(dog => dog.Scale == Vector2.One * 2), "local scale includes every companion");
            main.ApplyDesktopPetScaleStep(unitScaleStep);
            await Frame();
            Check(desktop.RemoteDogs.All(dog => dog.Position.IsEqualApprox(companionPositions[dog.MemberId])),
                "companion free placements survive local scale changes");
            var secondHuman = page.MockClientForSmoke(2);
            var thirdHuman = page.MockClientForSmoke(3);
            foreach (var joiningHuman in new[] { secondHuman, thirdHuman })
            {
                var previousPositions = desktop.RemoteDogs.ToDictionary(dog => dog.MemberId, dog => dog.Position);
                joiningHuman.Join(companionRoom);
                await Settle();
                int humanCount = joiningHuman.Id;
                Check(client.View.Members.Length == 4
                    && client.View.Members.Count(member => !member.IsCompanion) == humanCount
                    && client.View.Members.Count(member => member.IsCompanion) == 4 - humanCount,
                    "each joining human replaces exactly one companion");
                Check(localDog.GlobalPosition.IsEqualApprox(companionOwnPosition)
                    && desktop.RemoteDogs.Where(dog => previousPositions.ContainsKey(dog.MemberId))
                        .All(dog => dog.Position.IsEqualApprox(previousPositions[dog.MemberId])),
                    "replacing a companion preserves every remaining dog's local position");
                Check(joiningHuman.View.Members.Where(member => member.IsCompanion)
                    .All(member => companionAppearances[member.Id] ==
                        (member.Name, member.SkinId, member.HeadwearId, member.Presence)),
                    "newcomer receives the existing companion identities and fixed appearances");
            }
            await Capture(main, "in-game-companions-replaced");
            var finalCompanion = client.View.Members.Single(member => member.IsCompanion);
            var finalRows = page.GetNode<VBoxContainer>("Room/Members");
            int finalIndex = Array.FindIndex(client.View.Members, member => member.Id == finalCompanion.Id);
            panel.Open();
            await Wait(0.2);
            var scroll = panel.GetNode<ScrollContainer>("Panel/RootVBox/Scroll");
            T PanelState<T>(string field) => (T)typeof(SystemPanelController)
                .GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(panel);
            async Task<Vector2> HoldButton(BaseButton button)
            {
                if (scroll.IsAncestorOf(button)) scroll.EnsureControlVisible(button);
                await Frame();
                var point = button.GetGlobalRect().GetCenter();
                Input.ParseInputEvent(new InputEventMouseMotion { Position = point, GlobalPosition = point });
                await Frame();
                Check(main.GetViewport().GuiGetHoveredControl() == button, "pointer reaches the actual member/action button");
                Input.ParseInputEvent(new InputEventMouseButton
                    { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = true });
                await Frame();
                return point;
            }
            void ReleaseAt(Vector2 point) => Input.ParseInputEvent(new InputEventMouseButton
                { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = false });
            var finalKick = finalRows.GetChild(finalIndex).GetNode<Button>("Kick");
            var kickPoint = await HoldButton(finalKick);
            Check(ReferenceEquals(PanelState<BaseButton>("_panelScrollPressedButton"), finalKick),
                "actual pointer down participates in the panel's pending drag capture");
            page.AdvancePreview(2);
            await Frame();
            Check(GodotObject.IsInstanceValid(finalKick) && finalKick.IsInsideTree(),
                "member activity snapshots preserve the pressed member button");
            ReleaseAt(kickPoint);
            await Frame();
            Check(roomKickConfirm.Visible,
                "real press/release across snapshots opens companion removal confirmation");
            var modalRect = roomKickConfirm.GetGlobalRect();
            var panelRect = panel.GetNode<Control>("Panel").GetGlobalRect();
            Check(modalRect.Position.IsEqualApprox(panelRect.Position) && modalRect.Size.IsEqualApprox(panelRect.Size)
                && !scroll.IsAncestorOf(roomKickConfirm), "removal modal follows the full panel bounds outside scroll content");
            Check(!roomKickAccept.HasFocus() && !roomKickCancel.HasFocus(),
                "opening removal confirmation does not preselect or highlight an action");
            var acceptRect = roomKickAccept.GetGlobalRect();
            var cancelRect = roomKickCancel.GetGlobalRect();
            Check(acceptRect.End.Y <= cancelRect.Position.Y
                && Mathf.IsEqualApprox(acceptRect.Size.X, cancelRect.Size.X)
                && Mathf.IsEqualApprox(acceptRect.Position.X, cancelRect.Position.X),
                "removal actions are stacked at equal width with confirmation above cancellation");
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, Pressed = false });
            await Frame();
            Check(roomKickConfirm.Visible && client.View.Members.Any(member => member.Id == finalCompanion.Id),
                "Enter without choosing a modal action neither removes a member nor cancels");
            await Capture(main, "in-game-room-kick-overlay");
            var presetTab = title.GetNode<Button>("OutfitPresetTab");
            var blockedTabPoint = presetTab.GetGlobalRect().GetCenter();
            Input.ParseInputEvent(new InputEventMouseMotion { Position = blockedTabPoint, GlobalPosition = blockedTabPoint });
            await Frame();
            Check(main.GetViewport().GuiGetHoveredControl() != presetTab,
                "the modal intercepts pointer hover over a background tab");
            Input.ParseInputEvent(new InputEventMouseButton
                { Position = blockedTabPoint, GlobalPosition = blockedTabPoint, ButtonIndex = MouseButton.Left, Pressed = true });
            ReleaseAt(blockedTabPoint);
            await Frame();
            Check(roomKickConfirm.Visible && page.IsVisibleInTree(),
                "clicking a covered background tab does not switch pages or dismiss confirmation");
            int scrollBeforeModalDrag = scroll.ScrollVertical;
            var blockedDragPoint = scroll.GetGlobalRect().Position + new Vector2(20, 20);
            Input.ParseInputEvent(new InputEventMouseButton
                { Position = blockedDragPoint, GlobalPosition = blockedDragPoint, ButtonIndex = MouseButton.Left, Pressed = true });
            await Frame();
            Input.ParseInputEvent(new InputEventMouseMotion
                { Position = blockedDragPoint + new Vector2(0, 40), GlobalPosition = blockedDragPoint + new Vector2(0, 40),
                    Relative = new Vector2(0, 40), ButtonMask = MouseButtonMask.Left });
            ReleaseAt(blockedDragPoint + new Vector2(0, 40));
            await Frame();
            Check(roomKickConfirm.Visible && scroll.ScrollVertical == scrollBeforeModalDrag
                && !PanelState<bool>("_panelScrollDragPotential") && !PanelState<bool>("_panelScrollDragging")
                && PanelState<BaseButton>("_panelScrollPressedButton") == null,
                "dragging the modal background cannot scroll or capture covered member controls");
            var confirmPoint = await HoldButton(roomKickAccept);
            ReleaseAt(confirmPoint);
            await Frame(); // Dispatch the buffered pointer event before advancing simulated transport time.
            Check(!roomKickConfirm.Visible, "real confirmation click completes the removal action");
            await Settle();
            Check(client.View.Members.All(member => !member.IsCompanion)
                && secondHuman.View.Members.All(member => !member.IsCompanion)
                && desktop.RemoteDogs.Count == 2,
                "host removal retires a companion for all viewers without removing real members");
            var leavingIndex = Array.FindIndex(client.View.Members, member => member.Id == secondHuman.Id);
            var leavingButton = finalRows.GetChild(leavingIndex).GetNode<Button>("Kick");
            var leavingPoint = await HoldButton(leavingButton);
            secondHuman.Leave();
            await Settle();
            Check(!GodotObject.IsInstanceValid(leavingButton), "departing member removes the captured button between mouse down and up");
            Input.ParseInputEvent(new InputEventMouseMotion
                { Position = leavingPoint + new Vector2(0, 20), Relative = new Vector2(0, 20), ButtonMask = MouseButtonMask.Left });
            ReleaseAt(leavingPoint + new Vector2(0, 20));
            await Frame();
            Check(!PanelState<bool>("_panelScrollDragPotential") && !PanelState<bool>("_panelScrollDragging")
                && PanelState<BaseButton>("_panelScrollPressedButton") == null,
                "disposed capture is safely cleared after motion and release without sticking the panel");
            int remainingIndex = Array.FindIndex(client.View.Members, member => member.Id == thirdHuman.Id);
            var recoveryPoint = await HoldButton(finalRows.GetChild(remainingIndex).GetNode<Button>("Kick"));
            ReleaseAt(recoveryPoint);
            await Frame();
            Check(roomKickConfirm.Visible, "member buttons remain clickable after captured row removal");
            roomKickCancel.EmitSignal(BaseButton.SignalName.Pressed);
            client.Leave();
            secondHuman.Leave();
            thirdHuman.Leave();
            await Settle();
            client.Search();
            await Settle();
            Check(!client.Listings.Any(listing => listing.Code == companionRoom)
                && !main.RoomDesktopWindowActive && desktop.RemoteDogs.Count == 0,
                "companions cannot keep a room alive after the final human leaves");
            GD.Print("[InGameRoomSmoke] COMPANION_PASS real UI creation, fixed appearance, passive roles, negative-ID drag, local scale, stable replacement and room lifetime.");
            await RoomEmptyDirectoryPageChecks.Run(main);
            await SteamRoomPageChecks.Run(main);
            GD.Print("[InGameRoomSmoke] PASS initial localization, room UI, bubble chat, centered seats, independent dragging, overlap priority, panel avoidance, local scale, transparent hit areas, window restoration, hidden state and poker round-trip.");
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
