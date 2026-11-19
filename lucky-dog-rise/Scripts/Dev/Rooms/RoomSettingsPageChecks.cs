#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace LuckyDogRise.Rooms;

internal static class RoomSettingsPageChecks
{
    internal static readonly string[] Locales = { "zh_CN", "zh_TW", "ja", "es_ES", "es_419", "pt_BR", "pt_PT",
        "fr", "de", "da", "id", "nb", "sv", "nl", "vi", "ms", "ko", "ru", "uk", "en" };

    public static async Task RunFeedback(Node parent)
    {
        var service = new Service();
        var page = GD.Load<PackedScene>("res://Scenes/Rooms/InGameRoomPage.tscn").Instantiate<InGameRoomPreview>();
        page.Theme = GD.Load<Theme>("res://Themes/DefaultTheme.tres");
        page.Size = new Vector2(396, 600);
        page.Configure(new Provider(service), null);
        parent.AddChild(page);
        var notice = GD.Load<PackedScene>("res://Scenes/Prefabs/ConfirmOverlay.tscn")
            .Instantiate<ConfirmOverlayController>();
        parent.AddChild(notice);
        notice.SetOverlayRect(Vector2.Zero, new Vector2(420, 600));
        page.BindJoinFailureNotice(notice);
        string locale = TranslationServer.GetLocale();
        bool clipboardSupported = DisplayServer.HasFeature(DisplayServer.Feature.Clipboard)
            && !DisplayServer.ClipboardHasImage();
        string clipboard = clipboardSupported ? DisplayServer.ClipboardGet() : "";
        async Task Frame()
        {
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        try
        {
            await Frame();
            var client = page.CurrentClient;
            client.Create("Feedback test");
            await Frame();
            var code = page.GetNode<LinkButton>("Room/CodeRow/Code");
            var feedback = page.GetNode<Label>("Room/CodeRow/CopyFeedback");
            var status = page.GetNode<Label>("Status");
            var copy = page.GetNode<Button>("Room/CodeRow/Copy");
            Check(feedback.Modulate.A == 0 && feedback.MouseFilter == Control.MouseFilterEnum.Ignore,
                "copy check reserves space but starts invisible and cannot intercept clicks");
            foreach (string language in Locales)
            {
                L10n.SetLocale(language, save: false);
                await Frame();
                page.FitCodeRow();
                page.Size = new Vector2(396, 600);
                await Frame();
                Check(code.GetParent<Control>().Size.X <= 396.5f,
                    $"{language} code row must fit 396px: row={code.GetParent<Control>().Size.X}, page minimum={page.GetCombinedMinimumSize().X}, code={code.GetCombinedMinimumSize().X}, copy={copy.GetCombinedMinimumSize().X}, privacy={page.GetNode<Button>("Room/CodeRow/Visibility").GetCombinedMinimumSize().X}");
                Check(code.GetGlobalRect().End.X <= feedback.GlobalPosition.X
                    && feedback.GetGlobalRect().End.X <= copy.GlobalPosition.X,
                    $"{language} the copy check follows the code without overlapping actions");
                Check(feedback.GetThemeFont("font").HasChar('✓'), $"{language} the check glyph is available");
            }
            if (clipboardSupported)
            {
                var width = code.GetParent<Control>().Size;
                var copyPosition = copy.Position;
                code.EmitSignal(BaseButton.SignalName.Pressed);
                await Frame();
                Check(DisplayServer.ClipboardGet() == client.JoinedCode && feedback.Modulate.A > 0.99f
                    && !status.Visible && code.GetParent<Control>().Size == width && copy.Position == copyPosition,
                    "code click copies the full code and shows a check without a status line or layout jump");
                await parent.ToSignal(parent.GetTree().CreateTimer(1.7), SceneTreeTimer.SignalName.Timeout);
                Check(feedback.Modulate.A > 0 && feedback.Modulate.A < 1, "copy check fades before expiry");
                copy.EmitSignal(BaseButton.SignalName.Pressed);
                Check(feedback.Modulate.A == 1, "another copy restarts the same check");
                await parent.ToSignal(parent.GetTree().CreateTimer(2.1), SceneTreeTimer.SignalName.Timeout);
                Check(feedback.Modulate.A == 0 && !status.Visible, "copy feedback expires without leaving text");
                code.EmitSignal(BaseButton.SignalName.Pressed);
                client.Leave();
                await Frame();
                Check(feedback.Modulate.A == 0, "leaving clears copy feedback");
            }
            else
                GD.Print("[RoomFeedbackChecks] Clipboard unavailable or contains an image: native copy and timed fade checks skipped.");

            client.Leave();
            service.JoinFailure = RoomFailure.Banned;
            client.Join("WWWWWWWWWWWWW");
            await Frame();
            Check(page.HasPendingJoinFailureNotice && !notice.Visible && !status.Visible,
                "a banned code join queues one dialog without a permanent status line");
            using (UiInteractionOccupancy.Track(parent, () => true))
                Check(!page.TryShowPendingJoinFailureNotice(), "join rejection waits behind existing interaction occupancy");
            Check(page.TryShowPendingJoinFailureNotice() && notice.Visible
                && UiInteractionOccupancy.IsOccupied(page), "shared notice opens and registers modal occupancy");
            var content = notice.GetNode<VBoxContainer>("OverlayPanel/Margin/Content");
            var accept = content.GetNode<Button>("ButtonRow/ConfirmButton");
            var cancel = content.GetNode<Button>("ButtonRow/CancelButton");
            foreach (string language in Locales)
            {
                L10n.SetLocale(language, save: false);
                await Frame();
                Check(content.GetNode<Label>("Title").Text == L10n.Tr("Rooms_JoinFailedTitle")
                    && content.GetNode<Label>("Title").Text != "Rooms_JoinFailedTitle"
                    && content.GetNode<Label>("MessageBg/Message").Text == L10n.Tr("Rooms_Banned")
                    && accept.Text == L10n.Tr(L10nKey.PokerWelcome_Confirm) && !cancel.Visible,
                    $"{language} rejection title, body and sole acknowledgement use the shared catalogs");
                Check(content.GetMinimumSize().Y <= content.Size.Y + 0.5f
                    && accept.GetGlobalRect().End.Y <= notice.GetGlobalRect().End.Y,
                    $"{language} rejection dialog content and acknowledgement fit the panel");
            }
            accept.EmitSignal(BaseButton.SignalName.Pressed);
            client.SearchInBackground();
            await Frame();
            Check(!notice.Visible && !page.HasPendingJoinFailureNotice && !status.Visible
                && !UiInteractionOccupancy.IsOccupied(page),
                "acknowledgement clears the dialog; background refresh cannot resurrect it or its text");
            client.Join("WWWWWWWWWWWWW");
            await Frame();
            Check(page.TryShowPendingJoinFailureNotice(), "a deliberate new rejected join shows a new notice");
            page._Input(new InputEventKey { Pressed = true, Keycode = Key.Escape });
            Check(!notice.Visible && !page.HasPendingJoinFailureNotice, "Escape dismisses the notice");
            client.Join("WWWWWWWWWWWWW");
            Check(page.TryShowPendingJoinFailureNotice(), "notice can be shown for a subsequent attempt");
            page.Hide();
            await Frame();
            Check(!notice.Visible && !UiInteractionOccupancy.IsOccupied(parent), "hiding the page removes the sibling overlay");
            page.Show();
            client.Join("WWWWWWWWWWWWW");
            service.JoinFailure = RoomFailure.None;
            client.Create("New room");
            await Frame();
            Check(!page.HasPendingJoinFailureNotice && !notice.Visible && !status.Visible,
                "a new successful operation invalidates an unshown rejection");
            client.Leave();
            service.JoinFailure = RoomFailure.Full;
            client.Join("WWWWWWWWWWWWW");
            await Frame();
            Check(!page.HasPendingJoinFailureNotice && !notice.Visible && status.Text == L10n.Tr("Rooms_Full"),
                "other join failure presentation remains available");
            GD.Print("[RoomFeedbackChecks] PASS code layout in 20 locales, rejection/acknowledgement, repeated attempts, refresh, occupancy, locale changes and lifecycle.");
        }
        finally
        {
            if (clipboardSupported) DisplayServer.ClipboardSet(clipboard);
            L10n.SetLocale(locale, save: false);
            page.Free();
            notice.Free();
        }
    }

    public static async Task Run(Node parent)
    {
        var service = new Service();
        var page = GD.Load<PackedScene>("res://Scenes/Rooms/InGameRoomPage.tscn").Instantiate<InGameRoomPreview>();
        page.Configure(new Provider(service), null);
        parent.AddChild(page);
        var confirm = GD.Load<PackedScene>("res://Scenes/Prefabs/ConfirmOverlay.tscn")
            .Instantiate<ConfirmOverlayController>();
        parent.AddChild(confirm);
        page.BindKickConfirmation(confirm);
        var accept = confirm.GetNode<Button>("OverlayPanel/Margin/Content/ButtonRow/ConfirmButton");
        var cancel = confirm.GetNode<Button>("OverlayPanel/Margin/Content/ButtonRow/CancelButton");
        async Task Frame()
        {
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        static void Click(Button button) => button.EmitSignal(BaseButton.SignalName.Pressed);
        string locale = TranslationServer.GetLocale();
        try
        {
            await Frame();
            var client = page.CurrentClient;
            client.Create("Rooms_Social");
            await Frame();
            var name = page.GetNode<LineEdit>("Room/TitleRow/Name");
            var count = page.GetNode<Label>("Room/TitleRow/Count");
            var members = page.GetNode<VBoxContainer>("Room/Members");
            var code = page.GetNode<LinkButton>("Room/CodeRow/Code");
            void Draft(string text) { name.GrabFocus(); name.Text = text; }
            void Submit() => name.EmitSignal(LineEdit.SignalName.TextSubmitted, name.Text);
            Check(name.Editable && name.Text == "Rooms_Social" && count.Text == "3/6"
                && name.AutoTranslateMode == Node.AutoTranslateModeEnum.Disabled,
                "room name is editable host content with a separate population count");
            Check(members.GetChild(0).GetNode<Label>("Name").Text == "♛ Rooms_Social",
                "member list preserves the local Steam persona instead of replacing it with You");
            var originalRows = members.GetChildren().Select(row => row.GetInstanceId()).ToArray();
            var originalButtons = members.GetChildren().Select(row => row.GetNode<Button>("Kick").GetInstanceId()).ToArray();
            void CheckOriginalRows(string reason)
            {
                Check(members.GetChildren().Select(row => row.GetInstanceId()).SequenceEqual(originalRows)
                    && members.GetChildren().Select(row => row.GetNode<Button>("Kick").GetInstanceId()).SequenceEqual(originalButtons),
                    reason);
            }
            var nameBar = GD.Load<PackedScene>("res://Scenes/Rooms/RoomDogView.tscn").Instantiate<RoomDogView>();
            parent.AddChild(nameBar);
            var nameLabel = nameBar.GetNode<Label>("NameBar/Name");
            var companion = client.View.Members.First(member => member.IsCompanion);
            var companionKeys = new[] { "Rooms_CompanionCoder", "Rooms_CompanionArtist", "Rooms_CompanionDesigner" };
            foreach (string nextLocale in Locales)
            {
                L10n.SetLocale(nextLocale, save: false);
                await Frame();
                foreach (string key in new[] { "Rooms_Settings_OnlineFeatures", "Rooms_Settings_DogAutoChat",
                    "Rooms_Settings_DogAutoChatHint", "Rooms_AutoChat_Win", "Rooms_AutoChat_Tie",
                    "Rooms_AutoChat_CloseLoss", "Rooms_AutoChat_NextHand", "Rooms_Poker_HighCard" })
                    Check(InGameRoomPreview.ChatText(key) != key && InGameRoomPreview.ChatText(key) == L10n.Tr(key),
                        $"{nextLocale} independent room text lookup matches the registered catalog: {key}");
                foreach (var rank in Enum.GetValues<DataTables.EHandRank>())
                {
                    string message = RoomPokerText.AutoChat(new Poker.PokerAutoChatLine(Poker.PokerAutoChatKind.Win, rank));
                    Check(RoomRules.ValidateChat(message).Length == 0 && !message.Contains("{0}")
                        && !message.Contains("Rooms_") && !message.Contains("Paytable_"),
                        $"{nextLocale} {rank} auto-chat line fits the chat limit and resolves its hand name");
                }
                Check(name.Text == "Rooms_Social" && name.PlaceholderText == L10n.Tr("Rooms_Name")
                    && page.GetNode<OptionButton>("Room/Game/Choices/Current").Text == InGameRoomPreview.GameName("social"),
                    "locale changes translate game labels and placeholders but never the room name");
                CheckOriginalRows("locale changes preserve live member rows and their buttons");
                Check(members.GetChild(0).GetNode<Label>("Name").Text == "♛ Rooms_Social"
                    && members.GetChild(2).GetNode<Label>("Name").Text == L10n.Tr(companionKeys[0]),
                    "locale changes translate companion names, never human personas");
                // A real player can deliberately use a companion's name or even
                // a translation key. Only the companion identity may localize.
                Check(InGameRoomPreview.MemberName(companion with { IsCompanion = false }) == companion.Name,
                    "human identities cannot accidentally receive translated companion names");
                for (int slot = 0; slot < companionKeys.Length; slot++)
                {
                    string text = L10n.Tr(companionKeys[slot]);
                    Check(text != companionKeys[slot] && !text.Contains('\n') && !text.Contains('\r')
                        && !text.Contains("\\n"), $"{nextLocale} companion nickname is translated and single line");
                    nameBar.Display(companion with { Id = -slot - 1 }, false, true, true, "");
                    await Frame();
                    Check(nameLabel.Text == "♛ " + text && nameLabel.GetLineCount() == 1,
                        $"{nextLocale} companion name bar uses the translated single line");
                    float width = nameLabel.GetThemeFont("font").GetStringSize(nameLabel.Text,
                        HorizontalAlignment.Left, -1, nameLabel.GetThemeFontSize("font_size")).X;
                    Check(width <= nameLabel.Size.X + 0.5f,
                        $"{nextLocale} {text}: name including crown is {width}px, available {nameLabel.Size.X}px");
                }
            }
            nameBar.Free();
            Draft("  New room name  ");
            service.ActivitySequence++;
            service.Reaction++;
            service.TongueActive = true;
            service.Publish(client);
            await Frame();
            Check(name.Text == "  New room name  " && service.NameWrites == 0,
                "background companion/member snapshots preserve an active name draft");
            CheckOriginalRows("expression and activity refreshes preserve buttons between pointer press and release");
            name.ReleaseFocus();
            await Frame();
            Check(service.Name == "New room name" && name.Text == service.Name && service.NameWrites == 1,
                "focus loss commits one trimmed name");
            CheckOriginalRows("room name changes preserve live member buttons");
            service.Access = RoomAccess.FriendsOnly;
            service.RemoteName = "Updated Steam persona";
            service.Publish(client);
            await Frame();
            CheckOriginalRows("permission and persona updates change presentation without replacing member buttons");
            Check(members.GetChild(1).GetNode<Label>("Name").Text == service.RemoteName,
                "a stable member row still receives the latest persona");
            Draft("Enter name");
            Submit();
            await Frame();
            Check(service.Name == "Enter name" && service.NameWrites == 2 && !name.HasFocus(),
                "Enter commits once and releases editing");
            Draft("cancel this");
            page._Input(new InputEventKey { Pressed = true, Keycode = Key.Escape });
            await Frame();
            Check(name.Text == "Enter name" && service.NameWrites == 2, "Escape restores the confirmed name without writing");
            Draft(" ");
            Submit();
            await Frame();
            Check(name.Text == "Enter name" && service.NameWrites == 2
                && page.GetNode<Label>("Status").Text == L10n.Tr("Rooms_InvalidName"),
                "blank edits roll back with the existing validation message");
            name.MaxLength = 0; // Allow a hostile programmatic over-limit input.
            Draft(new string('x', 41));
            Submit();
            await Frame();
            Check(name.Text == "Enter name" && service.NameWrites == 2, "over-limit text cannot bypass name validation");
            name.MaxLength = 40;
            service.NameError = "Rooms_NameUpdateFailed";
            Draft("failed name");
            Submit();
            await Frame();
            Check(name.Text == "Enter name" && service.Name == "Enter name" && service.NameWrites == 3,
                "backend failure restores the confirmed name");
            service.NameError = "";
            Draft("candidate still composing");
            bool composing = true;
            page.NameImeForSmoke = () => composing;
            page._Input(new InputEventKey { Pressed = true, Keycode = Key.Enter });
            composing = false; // IME finalizes its candidate before TextSubmitted.
            Submit();
            Check(service.NameWrites == 3, "IME candidate confirmation cannot also submit the room name");
            page._Input(new InputEventKey { Pressed = true, Keycode = Key.A });
            composing = true;
            name.ReleaseFocus();
            await Frame();
            Check(name.Text == "Enter name" && service.NameWrites == 3,
                "blur during unfinished composition rolls back rather than publishing partial text");
            composing = false;
            page.NameImeForSmoke = null;
            Draft("former host edit");
            service.Owner = 2;
            service.Publish(client);
            Submit();
            await Frame();
            Check(!name.Editable && service.NameWrites == 3 && name.Text == "Enter name",
                "host transfer invalidates the old owner's captured draft");
            CheckOriginalRows("host transfer updates existing member rows without replacing them");
            Check(members.GetChildren().All(row => !row.GetNode<Button>("Kick").Visible)
                && members.GetChild(1).GetNode<Label>("Name").Text == "♛ " + service.RemoteName,
                "host transfer updates both kick visibility and the host crown");
            service.Owner = 1;
            service.Publish(client);
            await Frame();
            CheckOriginalRows("regaining host authority reuses the original member buttons");
            Check(members.GetChild(1).GetNode<Button>("Kick").Visible
                && members.GetChild(2).GetNode<Button>("Kick").Visible,
                "regaining host authority enables human and companion removal");
            Draft("previous session edit");
            client.BeginSession("SECOND_ROOM");
            service.Name = "Other room";
            service.Publish(client);
            Submit();
            await Frame();
            Check(name.Text == "Other room" && service.NameWrites == 3,
                "session changes cannot submit an old draft to the new room");
            Check(members.GetChildren().Select(row => row.GetInstanceId()).All(id => !originalRows.Contains(id)),
                "a new room session replaces old member callback contexts");
            client.BeginSession("WWWWWWWWWWWWW");
            service.Publish(client);
            await Frame();
            Check(code.Text.Length == 13 && code.Underline == LinkButton.UnderlineMode.Always
                && code.GetParent() == page.GetNode("Room/CodeRow")
                && page.GetNodeOrNull("Room/CodeHeader") == null,
                "full room code is an underlined copy link on the action row with no redundant title");
            code.EmitSignal(BaseButton.SignalName.Pressed);
            Check(page.CurrentClient.JoinedCode == code.Text, "clicking the code leaves room identity unchanged");

            var kick = members.GetChild(2).GetNode<Button>("Kick");
            var beforeReplacementRows = members.GetChildren().Select(row => row.GetInstanceId()).ToArray();
            ulong beforeReplacementButton = kick.GetInstanceId();
            Check(kick.Visible && !kick.Disabled, "host can remove a companion using the member-row action");
            Click(kick);
            Check(confirm.Visible && confirm.GetNode<Label>("OverlayPanel/Margin/Content/MessageBg/Message").Text
                .StartsWith(L10n.Tr("Rooms_CompanionCoder") + "\n", StringComparison.Ordinal),
                "companion removal uses the shared named confirmation");
            Click(cancel);
            Check(!confirm.Visible && service.KickWrites == 0 && client.View.Members.Any(member => member.IsCompanion),
                "canceling the companion confirmation leaves its membership unchanged");
            Click(kick);
            service.CompanionPresence++;
            service.Publish(client);
            await Frame();
            Check(members.GetChild(0).GetInstanceId() == beforeReplacementRows[0]
                && members.GetChild(1).GetInstanceId() == beforeReplacementRows[1]
                && members.GetChild(2).GetInstanceId() != beforeReplacementRows[2]
                && members.GetChild(2).GetNode<Button>("Kick").GetInstanceId() != beforeReplacementButton,
                "only the replaced companion generation gets a new row and callback");
            Click(accept);
            Check(service.KickWrites == 0, "a replaced companion generation invalidates old confirmation");
            Click(members.GetChild(2).GetNode<Button>("Kick"));
            Click(accept);
            await Frame();
            Check(service.KickWrites == 1 && client.View.Members.All(member => !member.IsCompanion),
                "confirmed companion removal dispatches its negative identity and current presence");
            Check(members.GetChild(0).GetInstanceId() == beforeReplacementRows[0]
                && members.GetChild(1).GetInstanceId() == beforeReplacementRows[1],
                "removing a companion preserves every remaining member button");
            GD.Print("[RoomSettingsPageChecks] PASS 20-locale single-line companion names with crown, literal personas, editable names, draft retention, blur/Enter/Escape, validation/IME guards, rollback, owner/session changes, code link, stable member buttons and companion removal (fake service).");
        }
        finally { L10n.SetLocale(locale, save: false); page.Free(); confirm.Free(); }
    }

    public static async Task RunInputMenus(Node parent)
    {
        var service = new Service();
        var page = GD.Load<PackedScene>("res://Scenes/Rooms/InGameRoomPage.tscn").Instantiate<InGameRoomPreview>();
        page.Theme = GD.Load<Theme>("res://Themes/DefaultTheme.tres");
        page.Position = new Vector2(24, 24);
        page.Size = new Vector2(390, 600);
        page.Configure(new Provider(service), null);
        parent.AddChild(page);
        string locale = TranslationServer.GetLocale();
        bool clipboardSupported = DisplayServer.HasFeature(DisplayServer.Feature.Clipboard);
        string clipboard = clipboardSupported ? DisplayServer.ClipboardGet() : "";
        async Task Frame()
        {
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        bool Disabled(PopupMenu menu, LineEdit.MenuItems action) => menu.IsItemDisabled(menu.GetItemIndex((int)action));
        void Choose(PopupMenu menu, LineEdit.MenuItems action)
        {
            menu.EmitSignal(PopupMenu.SignalName.IdPressed, (int)action);
            menu.Hide();
        }
        async Task Open(LineEdit input)
        {
            await Frame();
            var position = input.GetGlobalTransformWithCanvas() * (input.Size / 2);
            parent.GetViewport().PushInput(new InputEventMouseButton
            { ButtonIndex = MouseButton.Right, Pressed = true, Position = position, GlobalPosition = position }, true);
            parent.GetViewport().PushInput(new InputEventMouseButton
            { ButtonIndex = MouseButton.Right, Pressed = false, Position = position, GlobalPosition = position }, true);
            await Frame();
            Check(input.IsMenuVisible(), "right-click opens the real LineEdit context menu");
        }
        try
        {
            await Frame();
            var name = page.GetNode<LineEdit>("Lobby/CreateRow/Name");
            var code = page.GetNode<LineEdit>("Lobby/JoinRow/Code");
            var title = page.GetNode<LineEdit>("Room/TitleRow/Name");
            var actions = new[] { "Cut", "Copy", "Paste", "SelectAll", "Clear", "Undo", "Redo" };
            name.Text = "literal room name";
            code.Text = "031G002KAW0FD";
            foreach (string language in Locales)
            {
                L10n.SetLocale(language, save: false);
                await Frame();
                foreach (var input in new[] { name, code, title })
                {
                    var menu = input.GetMenu();
                    var labels = Enumerable.Range(0, menu.ItemCount).Where(i => !menu.IsItemSeparator(i))
                        .Select(menu.GetItemText).ToArray();
                    var expected = actions.Select(action => L10n.Tr("Rooms_Edit" + action)).ToArray();
                    Check(labels.SequenceEqual(expected) && labels.All(text => !text.StartsWith("Rooms_")),
                        $"{language}: all three inputs contain exactly seven translated edit actions");
                }
                Check(name.Text == "literal room name" && code.Text == "031G002KAW0FD",
                    "menu translation never translates player-entered names or codes");
            }
            L10n.SetLocale("zh_CN", save: false);
            name.Text = "";
            await Frame();
            await Open(name);
            var popup = name.GetMenu();
            Check(Disabled(popup, LineEdit.MenuItems.Copy) && Disabled(popup, LineEdit.MenuItems.Cut)
                && Disabled(popup, LineEdit.MenuItems.Clear) && Disabled(popup, LineEdit.MenuItems.SelectAll)
                && Disabled(popup, LineEdit.MenuItems.Undo), "empty input disables inapplicable actions");
            L10n.SetLocale("fr", save: false);
            Check(popup.GetItemText(popup.GetItemIndex((int)LineEdit.MenuItems.Paste)) == "Coller",
                "an already open menu updates when the language changes");
            popup.Hide();
            name.Text = "Keep this";
            name.SelectAll();
            await Open(name);
            Check(!Disabled(popup, LineEdit.MenuItems.Copy) && !Disabled(popup, LineEdit.MenuItems.Cut),
                "selected text enables native copy and cut");
            Choose(popup, LineEdit.MenuItems.Clear);
            Check(name.Text == "", "native Clear empties the complete input");
            await Open(name);
            Check(!Disabled(popup, LineEdit.MenuItems.Undo), "native Clear retains undo history");
            Choose(popup, LineEdit.MenuItems.Undo);
            Check(name.Text == "Keep this", "native Undo restores text");
            await Open(name);
            Check(!Disabled(popup, LineEdit.MenuItems.Redo), "undo makes native redo available");
            Choose(popup, LineEdit.MenuItems.Redo);
            Check(name.Text == "", "native Redo reapplies clear");
            if (clipboardSupported)
            {
                DisplayServer.ClipboardSet("031G002KAW0FD");
                code.Text = "";
                await Open(code);
                Choose(code.GetMenu(), LineEdit.MenuItems.Paste);
                Check(code.Text == "031G002KAW0FD" && page.CurrentClient.JoinedCode.Length == 0,
                    "native paste fills the code without joining a room");
                code.SelectAll();
                await Open(code);
                Choose(code.GetMenu(), LineEdit.MenuItems.Cut);
                Check(code.Text == "" && DisplayServer.ClipboardGet() == "031G002KAW0FD",
                    "native Cut writes selection to the clipboard");
            }
            page.CurrentClient.Create("Original room");
            await Frame();
            title.GrabFocus();
            title.Text = "Edited room";
            await Open(title);
            title.ReleaseFocus();
            await Frame();
            Check(service.NameWrites == 0 && title.Text == "Edited room",
                "opening the context menu does not prematurely commit the room name");
            Choose(title.GetMenu(), LineEdit.MenuItems.SelectAll);
            title.ReleaseFocus();
            await Frame();
            Check(service.Name == "Edited room" && service.NameWrites == 1,
                "room rename commits after the menu operation and real focus loss");
            service.Owner = 2;
            service.Publish(page.CurrentClient);
            await Frame();
            title.SelectAll();
            await Open(title);
            Check(!title.Editable && !Disabled(title.GetMenu(), LineEdit.MenuItems.Copy)
                && new[] { LineEdit.MenuItems.Cut, LineEdit.MenuItems.Paste, LineEdit.MenuItems.Clear,
                    LineEdit.MenuItems.Undo, LineEdit.MenuItems.Redo }.All(action => Disabled(title.GetMenu(), action)),
                "guest can copy the room title but cannot modify it through the menu");
            page.CloseRoomDropdowns();
            Check(page.InputMenus.All(menu => !menu.Visible), "closing the page also closes text menus");
            GD.Print($"[RoomInputMenus] PASS native right-click, 20 locales, live translation, edit/undo/redo, read-only, rename focus and popup cleanup; clipboard={clipboardSupported}.");
        }
        finally
        {
            page.CloseRoomDropdowns();
            page.Free();
            L10n.SetLocale(locale, save: false);
            if (clipboardSupported) DisplayServer.ClipboardSet(clipboard);
        }
    }

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private sealed class Provider(Service service) : IGamePlatformService, IPlatformRoomServiceProvider
    {
        public IRoomService RoomService => service;
        public bool RoomRestartRequired => false;
        public string ProviderName => "Steam";
        public string StatusMessage => "";
        public bool IsAvailable => true;
        public uint AppId => 2583700;
        public string PersonaName => "Rooms_Social";
        public string AccountProvider => "steam";
        public string AccountId => "room-settings-test";
        public event Action UserStatsReady { add { } remove { } }
        public void RunCallbacks() { }
        public bool OpenFriendsOverlay() => false;
        public PlatformAchievementReadResult ReadAchievementStates(IEnumerable<string> names)
            => new(false, "Test provider", Array.Empty<PlatformAchievementState>());
        public void Dispose() { }
    }
    private sealed class Service : IRoomService
    {
        public double Now => 1000;
        public string Name = "Rooms_Social";
        public string NameError = "";
        public int Owner = 1;
        public int NameWrites;
        public int KickWrites;
        public string RemoteName = "Another Steam persona";
        public int Reaction = 1001;
        public long ActivitySequence;
        public bool TongueActive;
        public RoomAccess Access = RoomAccess.Public;
        public long CompanionPresence = 91;
        public RoomFailure JoinFailure;
        private bool _companion = true;
        private long _revision;
        public void Request(RoomClient client, RoomRequest request) => client.TryComplete(request.Id, () =>
        {
            if (request.Operation == RoomOperation.Search) return new RoomResult(RoomFailure.None, Array.Empty<RoomListing>());
            if (request.Operation == RoomOperation.Join && JoinFailure != RoomFailure.None) return new RoomResult(JoinFailure);
            client.BeginSession("WWWWWWWWWWWWW");
            Publish(client);
            return RoomResult.Success;
        });
        public void Publish(RoomClient client)
        {
            var members = new List<RoomMember>
            {
                new(1, client.Name, client.SkinId, 0, Reaction, 1, ActivitySequence, TongueActive),
                new(2, RemoteName, client.SkinId, 0, Reaction, 2, ActivitySequence, TongueActive)
            };
            if (_companion) members.Add(new(-1, "熬夜的程序员", client.SkinId, 0, Reaction, CompanionPresence,
                ActivitySequence, TongueActive, IsCompanion: true));
            client.Receive(new RoomSnapshot(client.JoinedCode, Name, "social", Owner, ++_revision, members.ToArray(), Access));
        }
        public string SetName(RoomClient client, string name)
        {
            NameWrites++;
            if (NameError.Length > 0) return NameError;
            Name = name;
            Publish(client);
            return "ok";
        }
        public string Kick(RoomClient client, int memberId, long presence)
        {
            if (memberId != -1 || presence != CompanionPresence) return "Rooms_KickInvalidTarget";
            KickWrites++;
            _companion = false;
            Publish(client);
            return "";
        }
        public void Cancel(RoomClient client, long id) { }
        public void Leave(RoomClient client) => client.BeginSession("");
        public void UpdateActivity(RoomClient client) { }
        public void UpdateAppearance(RoomClient client) { }
        public string SetGame(RoomClient client, string gameId) => "";
        public string SetAccess(RoomClient client, RoomAccess access) => "ok";
        public string SendChat(RoomClient client, string text) => "";
    }
}
#endif
