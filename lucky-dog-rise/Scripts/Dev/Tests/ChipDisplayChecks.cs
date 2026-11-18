#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using LuckyDogRise.Rooms;
using LuckyDogRise.Rooms.Poker;

namespace LuckyDogRise;

// Actual packed scenes and font metrics, through the isolated main-scene Mock entry.
// Only presentation values change; no player balance, ledger or save is modified.
internal static class ChipDisplayChecks
{
    public static async Task Run(ModeManager main)
    {
        async Task Frames(int count = 4)
        {
            for (int i = 0; i < count; i++)
                await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        var info = GD.Load<PackedScene>("res://Scenes/Poker/InfoPanel.tscn").Instantiate<InfoPanelController>();
        var hand = GD.Load<PackedScene>("res://Scenes/Rooms/Poker/RoomPokerHand.tscn").Instantiate<RoomPokerHandView>();
        main.AddChild(info);
        main.AddChild(hand);
        var data = main.GameDataObj;
        int balance = data.Chips;
        info.Bind(data);
        var panel = info.GetNode<PanelContainer>("Panel");
        var label = info.GetNode<Label>("Panel/RootVBox/ChipsPanel/ChipsVBox/ChipsValuePanel/ChipsValue");
        var counter = main.FindChildren("MainText", "Label", true, false).OfType<Label>().Single();
        var handChips = hand.GetNode<Label>("Plate/Chips");
        var culture = CultureInfo.CurrentCulture;
        int scale = SettingsManager.LoadDesktopPetScaleStep();
        try
        {
            await Frames();
            int normalSize = label.LabelSettings.FontSize;
            float baselineHeight = label.Size.Y;
            foreach (string format in new[] { "en-US", "fr-FR", "de-DE", "hi-IN" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(format);
                foreach (int value in new[] { 0, 99, 999, 9999, 88888, 999999, 9999999, 10000000, 99999999, int.MaxValue })
                {
                    info.SetChips(value);
                    await Frames();
                    string expected = value < data.BetAmount ? $"{value:N0}/{data.BetAmount:N0}" : value.ToString("N0");
                    Check(label.Text == expected, "full amount and insufficient-bet amount preserved");
                    Check(panel.Size.IsEqualApprox(InfoPanelController.BasePanelSize),
                        $"{format} {value}: panel expanded to {panel.Size}, expected {InfoPanelController.BasePanelSize}");
                    Fits(label, $"{format} {value}: info balance");
                    Check(Mathf.IsEqualApprox(label.Size.Y, baselineHeight), "chip row does not jump when font shrinks");
                }
                foreach (long? value in new long?[] { null, 1000, 41000, long.MaxValue, 1000 })
                {
                    info.SetSessionChips(value, "Table chips", sessionActive: true);
                    await Frames();
                    Check(label.Text == (value?.ToString("N0") ?? "—"), "session display keeps exact balance or waiting dash");
                    Check(panel.Size.IsEqualApprox(InfoPanelController.BasePanelSize), "session amount cannot widen panel");
                    Fits(label, "session balance");
                }
                Check(label.LabelSettings.FontSize == normalSize, "normal font restored after large session balance");
                info.SetSessionChips(null);
                await Frames();
                Check(label.Text == balance.ToString("N0"), "leaving session restores personal amount");

                var match = new MischiefPokerMatch(new[] { 1, 2 }, 1);
                match.StartHand(0, Enumerable.Range(0, 52).ToArray());
                var view = match.GetPlayerView(1);
                foreach (long value in new long[] { 0, 1000, 6000, 41000 })
                {
                    var state = view.Public with { Seats = view.Public.Seats.Select(s => s.PlayerId == 1 ? s with { Chips = value } : s).ToArray() };
                    hand.Present(state, 1, view.HoleCards, null, 0, false);
                    await Frames();
                    Fits(handChips, "player plate at current session protocol limit");
                }
                GD.Print($"[ChipDisplay] PASS {format}: personal int range, session long range, waiting/return, player plate.");
            }
            foreach (float factor in new[] { .75f, 1f, 1.25f, 1.5f, 2f })
            {
                info.SetRenderScale(factor);
                info.SetChips(int.MaxValue);
                await Frames();
                Fits(label, "scaled info balance");
                Check(panel.Size.IsEqualApprox(InfoPanelController.BasePanelSize), "UI scaling preserves design width");
            }
            for (int step = SettingsManager.DesktopPetScaleStepMin; step <= SettingsManager.DesktopPetScaleStepMax; step++)
            {
                main.ApplyDesktopPetScaleStep(step);
                await Frames();
                var font = counter.GetThemeFont("font");
                Check(font.GetStringSize(int.MaxValue.ToString(), fontSize: counter.GetThemeFontSize("font_size")).X <= counter.Size.X,
                    "desktop counter fits full personal int range at every pet scale");
            }
            Check(data.Chips == balance, "presentation checks did not change personal chips");
            GD.Print("[ChipDisplay] PASS five UI scales and six pet scales; no chip or save mutation.");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            main.ApplyDesktopPetScaleStep(scale);
            // Bound GameData signals are owned by this scene until the smoke process exits.
            info.Hide(); hand.Hide();
        }
    }

    private static void Fits(Label label, string context)
    {
        Font font = label.LabelSettings?.Font ?? label.GetThemeFont("font");
        int size = label.LabelSettings?.FontSize ?? label.GetThemeFontSize("font_size");
        float width = font.GetStringSize(label.Text, fontSize: size).X;
        Check(width <= label.Size.X + .5f, $"{context}: text {width}px exceeds {label.Size.X}px at font {size}");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[ChipDisplay] " + message);
    }
}
#endif
