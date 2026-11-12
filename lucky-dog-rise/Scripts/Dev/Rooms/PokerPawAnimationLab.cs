#if DEBUG && !DEMO_BUILD && !RECORDING_BUILD
using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using LuckyDogRise.Rooms.Poker;

namespace LuckyDogRise.Rooms;

// Isolated production-card/paw sandbox. No Steam session, GameData or saves.
public partial class PokerPawAnimationLab : Control
{
    [Export] private RoomPokerTableView _table;
    [Export] private Godot.Collections.Array<DogVisual> _dogs = new();
    [Export] private Button _local, _remote, _double, _pass, _reset, _close;
    [Export] private Label _status;
    private MischiefPokerMatch _match;
    private DogVisual _drag;
    private Vector2 _grab;
    private bool _testing;
    private bool _holdAuthority;
    public override void _Ready()
    {
        GetViewport().TransparentBg = false;
        GetWindow().Title = "小狗来啦 · 爪子翻牌演出";
        GetWindow().Size = new Vector2I(1280, 800);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        _table.Paws.Bind(GetViewport(), (id, right) => _dogs[id - 1].PokerPaw(right), n => n.GetGlobalTransformWithCanvas());
        for (int i = 0; i < _dogs.Count; i++)
            _dogs[i].SetPreviewAppearance(DogAppearanceSpec.FromDogSkin(LubanData.Tables.TbDogSkin.Get(1001 + i)));
        _table.FlipRequested += slot => Flip(slot);
        _local.Pressed += () => Reset(true);
        _remote.Pressed += () => Reset(false);
        _double.Pressed += () => { Flip(2); Flip(2); };
        _pass.Pressed += () => Act(PokerAction.EndTurn);
        _reset.Pressed += () => Reset(true);
        _close.Pressed += () => GetTree().Quit();
        Reset(true);
        if (OS.GetCmdlineUserArgs().Contains("--poker-paw-lab-smoke"))
        {
            _testing = true;
            AudioManager.Instance?.SetSfxVolume(0);
            Callable.From(RunChecks).CallDeferred();
        }
    }
    private void Reset(bool local)
    {
        _table.Paws.Cancel();
        _match = new(new[] { 1, 2, 3 }, local ? 3 : 1);
        _match.StartHand(0);
        _table.Present(_match.GetPlayerView(1), true, animate: false);
        _table.Present(_match.GetPlayerView(1), true);
    }
    private void Flip(int slot) => Act(PokerAction.Flip, slot);
    private void Act(PokerAction action, int slot = -1)
    {
        var h = _match.GetPublicView();
        if (h.ActivePlayerId is not int id) return;
        _match.Act(new(h.SessionId, h.HandNumber, h.Revision, id, action, slot), 0);
        _table.Present(_match.GetPlayerView(1), true);
    }
    public override void _Process(double delta)
    {
        if (_match == null) return;
        _table.Present(_match.GetPlayerView(1), true, delta, actionPending: _holdAuthority);
        _status.Text = $"当前：{_match.GetPublicView().ActivePlayerId} 号 · 1 号为自己（即时），2/3 号为远端（触牌后翻）\n点击公共牌反复测试。可拖动三只狗，检查飞回位置。首翻左爪，次翻右爪。";
        if (_testing && OperatingSystem.IsWindows() && DisplayServer.GetName() != "headless")
            WindowNative.ShowWindow((IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle), WindowNative.SW_HIDE);
    }
    public override void _Input(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (!button.Pressed) { _drag = null; return; }
            _drag = _dogs.FirstOrDefault(d => new Rect2(-420, -500, 840, 640).HasPoint(d.ToLocal(button.Position)));
            if (_drag != null) _grab = button.Position - _drag.Position;
        }
        if (input is InputEventMouseMotion motion && _drag != null) _drag.Position = motion.Position - _grab;
    }

    private async void RunChecks()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++; GD.Print("[PokerPawLab] " + message);
        }
        async Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        var card = _table.GetNode<RoomPokerCardView>("Board/Card1");
        var paws = _table.Paws;
        try
        {
            Reset(true); Flip(0);
            Check(card.IsFlipping && paws.ActiveCount == 1 && _dogs[0].PokerPaw(false).Modulate.A == 0
                && _dogs[0].PokerPaw(true).Modulate.A == 1, "local flip immediate; only left paw suppressed");
            Flip(1);
            Check(paws.ActiveCount == 2 && _dogs[0].PokerPaw(true).Modulate.A == 0, "second flip uses independent right paw");
            await Wait(.65);
            Check(paws.ActiveCount == 0 && _dogs[0].PokerPaw(false).Modulate.A == 1
                && _dogs[0].PokerPaw(true).Modulate.A == 1, "both original paws restored");
            Reset(false);
            int before = paws.TouchCount;
            Flip(0);
            Check(!card.IsFlipping && paws.HasPendingFlip(card) && paws.TouchCount == before, "remote flip waits for contact");
            await Wait(.13);
            Check(card.IsFlipping && paws.TouchCount == before + 1, "remote contact triggers exactly one flip");
            _dogs[1].Position += new Vector2(130, -60);
            await Wait(.55);
            var source = _dogs[1].PokerPaw(false).GetChildren().OfType<Sprite2D>().First(s => s.Visible);
            Check(paws.ActiveCount == 0 && paws.LastReturnPosition.DistanceTo(source.GlobalPosition) < 0.1f,
                "return follows dragged dog exactly");
            Reset(false); Flip(0); Flip(0);
            await Wait(.9);
            Check(paws.ActiveCount == 0 && card.VisibleCard == _match.GetPublicView().Board[0].Card,
                "two rapid flips on the same card preserve left/right contacts and final face");
            Reset(false); Flip(0);
            _table.Present(_match.GetPlayerView(1), false, animate: false);
            Check(paws.ActiveCount == 0 && _dogs[1].PokerPaw(false).Modulate.A == 1, "hidden presentation restores paw immediately");
            await Wait(.2);
            Check(paws.ActiveCount == 0, "hidden/reopened table does not replay old action");
            Reset(false); Flip(0); _dogs[1].Hide();
            await Wait(.1);
            Check(paws.ActiveCount == 0 && _dogs[1].PokerPaw(false).Modulate.A == 1, "departing dog cancels safely");
            _dogs[1].Show();
            Reset(true); Flip(0); Reset(true);
            await Wait(.3);
            Check(paws.ActiveCount == 0 && card.VisibleCard == _match.GetPublicView().Board[0].Card, "new hand cancels old callbacks");
            Reset(true); _holdAuthority = true;
            int contacts = paws.TouchCount;
            _table.PredictLocalFlip(0);
            Check(card.IsFlipping && paws.TouchCount == contacts + 1, "local prediction starts without waiting for network");
            await Wait(.22);
            Check(card.VisibleCard == -1, "unacknowledged prediction stays face-down");
            Flip(0); _holdAuthority = false;
            Check(paws.TouchCount == contacts + 1, "authority acknowledgement does not replay the paw");
            await Wait(.6);
            Reset(true); _holdAuthority = true; _table.PredictLocalFlip(0);
            await Wait(.2); _holdAuthority = false;
            await Wait(.05);
            Check(card.VisibleCard == _match.GetPublicView().Board[0].Card && paws.ActiveCount == 0
                && _dogs[0].PokerPaw(false).Modulate.A == 1, "rejected prediction restores card and paw");
            Check(_dogs.Select(d => d.PokerPaw(false).GetChildren().OfType<Sprite2D>().First(s => s.Visible).Texture.ResourcePath)
                .Distinct().Count() == 3, "all three dogs use their own skin textures");
            Check(card.Material is ShaderMaterial && card.Material != _table.GetNode<RoomPokerCardView>("Board/Card2").Material,
                "each card owns its directional flip parameters");

            // Contract checks use actual mirrored paw sprites and rotated/scaled table space.
            // Move each paw to either side of the card: hand ordinal must not pick direction.
            Reset(false);
            Vector2 dogPosition = _dogs[1].Position;
            foreach (Vector2 from in new[] { Vector2.Left, Vector2.Right, Vector2.Up, Vector2.Down, new Vector2(1, 1).Normalized() })
                for (int number = 1; number <= 2; number++)
                {
                    paws.Cancel(); card.ResetPose(); card.Present(4, true, false);
                    var root = _dogs[1].PokerPaw(number == 2);
                    var original = root.GetChildren().OfType<Sprite2D>().First(s => s.Visible);
                    var cardPose = card.GetParent<CanvasItem>().GetGlobalTransformWithCanvas() * card.PawPose(0);
                    _dogs[1].Position += cardPose.Origin + from * 350 - original.GlobalPosition;
                    Vector2 expected = -from;
                    paws.Play(new PokerFlip(1, 2, number, 0, false), card, false, 0,
                        direction => card.Present(4, false, true, direction));
                    var flying = paws.GetNode<Sprite2D>("Overlay/Paw1");
                    Vector2 toes = flying.Transform.Y.Normalized() * (flying.FlipV ? -1 : 1);
                    Check(toes.Dot(expected) > 0.999f && (flying.Position - cardPose.Origin).Dot(from) > 0,
                        $"paw {number} approaches from {from} with toes toward the card");
                    paws._Process(RoomPokerPawEffects.ApproachSeconds + .01);
                    Vector2 projected = (cardPose.X * card.FlipDirection.X + cardPose.Y * card.FlipDirection.Y).Normalized();
                    Check(projected.Dot(expected) > 0.999f && card.IsFlipping,
                        $"paw {number} and card share direction from {from}");
                    paws._Process(RoomPokerPawEffects.PushSeconds + RoomPokerPawEffects.ReturnTurnSeconds);
                    Check(flying.Visible && flying.Position.DistanceTo(original.GlobalPosition) > 10
                        && flying.Transform.Y.Normalized().Dot(original.GlobalTransform.Y.Normalized()) > 0.999f
                        && Mathf.Sign(flying.Transform.Determinant()) == Mathf.Sign(original.GlobalTransform.Determinant()),
                        $"paw {number} faces home early while still returning from {from}");
                    paws._Process(RoomPokerPawEffects.ReturnSeconds);
                    Check(paws.ActiveCount == 0 && root.Modulate.A == 1
                        && paws.LastReturnPosition.DistanceTo(original.GlobalPosition) < 0.1f,
                        $"paw {number} finishes at its original pose from {from}");
                }
            _dogs[1].Position = dogPosition;
            card.ResetPose();
            Check(card.FlipProgress == 0 && card.FlipDirection == Vector2.Zero,
                "cancelling a directional flip restores its undeformed face");
            GD.Print($"POKER_PAW_LAB_PASS: {checks}"); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("POKER_PAW_LAB_FAIL: " + error); GetTree().Quit(1); }
    }
}
#endif
