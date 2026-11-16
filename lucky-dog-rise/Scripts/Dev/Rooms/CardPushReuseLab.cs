#if DEBUG
using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace LuckyDogRise;

// Actual solo card prefab + shared effects only: no Room/PokerMatch/Steam/GameData dependency.
public partial class CardPushReuseLab : Control
{
    [Export] private PokerPawEffects _paws;
    [Export] private DogVisual _dog;
    [Export] private Godot.Collections.Array<CardController> _cards = new();
    [Export] private Button _left, _right, _straighten, _reset, _close;
    private readonly bool[] _faceUp = { true, true };
    private bool _testing;
    private int _order;

    public override void _Ready()
    {
        GetViewport().TransparentBg = false;
        GetWindow().Title = "小狗来啦 · 单机卡牌复用推牌动画";
        GetWindow().Size = new Vector2I(960, 720);
        GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        _paws.Bind(GetViewport());
        _dog.SetPreviewAppearance(DogAppearanceSpec.FromDogSkin(LubanData.Tables.TbDogSkin.Get(1001)));
        for (int i = 0; i < _cards.Count; i++) _cards[i].Clicked += index => Push(index);
        _left.Pressed += () => Push(0);
        _right.Pressed += () => Push(1);
        _straighten.Pressed += () =>
        {
            _paws.Cancel();
            for (int i = 0; i < _cards.Count; i++) _cards[i].PushAnimation.Straighten(i);
            AudioManager.Instance?.PlaySfxOncePerFrame("Card_PokerBoardStraighten", "CardPushLab");
        };
        _reset.Pressed += ResetCards;
        _close.Pressed += () => GetTree().Quit();
        ResetCards();
        if (OS.GetCmdlineUserArgs().Contains("--card-push-reuse-smoke"))
        {
            _testing = true;
            AudioManager.Instance?.SetSfxVolume(0);
            Callable.From(RunChecks).CallDeferred();
        }
    }

    private void ResetCards()
    {
        _paws.Cancel(); _order = 0;
        for (int i = 0; i < _cards.Count; i++)
        {
            _faceUp[i] = true;
            _cards[i].SetCard(i == 0 ? 12 : 27, i);
            _cards[i].ResetPushPresentation(true);
        }
    }

    private void Push(int index)
    {
        var card = _cards[index];
        bool faceUp = _faceUp[index] = !_faceUp[index];
        float angle = card.PushAnimation.ChooseTiltAngle();
        int order = ++_order;
        _paws.Play(_dog.PokerPaw(right: index == 1), card.PushAnimation, angle,
            direction => card.AnimatePushedFlip(faceUp, direction, order, angle));
    }

    public override void _Process(double delta)
    {
        if (_testing && OperatingSystem.IsWindows() && DisplayServer.GetName() != "headless")
            WindowNative.ShowWindow((IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle), WindowNative.SW_HIDE);
    }

    private async void RunChecks()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++; GD.Print("[CardPushReuse] " + message);
        }
        async Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        try
        {
            var card = _cards[0];
            var body = card.GetNode<Node2D>("CardBody");
            var front = card.GetNode<Sprite2D>("CardBody/Front");
            var back = card.GetNode<Sprite2D>("CardBody/Back");
            var paw = _dog.PokerPaw(false);
            int clicks = 0;
            card.Clicked += _ => clicks++;
            card.SetHeld(true);
            await Wait(.22);
            int value = card.CardValue;
            Push(0);
            Check(card.PushAnimation.IsFlipping && paw.Modulate.A == 0 && _dog.PokerPaw(true).Modulate.A == 1,
                "shared effect immediately pushes the real solo card with the chosen paw");
            var original = paw.GetChildren().OfType<Sprite2D>().First(s => s.Visible);
            var flying = _paws.GetNode<Sprite2D>("Overlay/Paw1");
            Check(Mathf.Abs(flying.Transform.X.Length() - original.GetGlobalTransformWithCanvas().X.Length()) < .001f,
                "solo paw retains authored size");
            var pose = card.GetGlobalTransformWithCanvas() * card.PushAnimation.PawPose(0);
            Check(pose.Origin.DistanceTo(body.GlobalPosition) < .01f,
                "centered Sprite2D target has no half-card offset");
            await Wait(.25);
            Check(!front.Visible && back.Visible && card.IsHeld && card.CardValue == value && clicks == 0,
                "tutorial flip only changes presentation, not held state, card value or clicks");
            Check(body.Rotation != 0 && card.ZIndex > _cards[1].ZIndex,
                "same shared tilt raises the whole solo card above its neighbors");
            Check(front.Material == back.Material && front.Material != _cards[1].GetNode<Sprite2D>("CardBody/Front").Material
                && ((ShaderMaterial)front.Material).GetShaderParameter("card_center").AsVector2() == Vector2.Zero,
                "solo faces share one centered material, separate cards remain independent");
            await Wait(.4);
            Check(_paws.ActiveCount == 0 && paw.Modulate.A == 1, "solo paw returns and restores original visibility");
            card.PushAnimation.Straighten(0);
            await Wait(.4);
            Check(body.Position.IsZeroApprox() && Mathf.IsZeroApprox(body.Rotation) && card.ZIndex == 0,
                "shared straighten restores solo card home and draw order");

            bool touched = false;
            _paws.Play(paw, card.PushAnimation, .1f, _ => touched = true, immediate: false);
            card.ResetPushPresentation(true);
            await Wait(.25);
            Check(!touched && _paws.ActiveCount == 0 && paw.Modulate.A == 1 && front.Visible,
                "reset during approach cancels stale contact and restores the paw");
            _paws.Play(paw, card.PushAnimation, .1f, _ => touched = true, immediate: false);
            card.ResetPushPresentation(true);
            bool replacementTouched = false;
            _paws.Play(paw, card.PushAnimation, .1f, _ => replacementTouched = true);
            Check(!touched && replacementTouched, "same-frame reset and immediate replay never flush a stale callback");
            _paws.Cancel();
            _paws.Play(paw, card.PushAnimation, .1f, _ => _paws.Cancel());
            Check(_paws.ActiveCount == 0 && paw.Modulate.A == 1, "a contact callback may cancel its own presentation safely");
            _paws.Play(paw, card.PushAnimation, .1f, _ => touched = true, immediate: false);
            card.Hide();
            await Wait(.25);
            Check(!touched && _paws.ActiveCount == 0 && paw.Modulate.A == 1, "hidden target cancels pending push safely");
            card.Show();
            ResetCards();
            card.AnimateDeal(.2f, 1, 0);
            Push(0);
            await Wait(.6);
            Check(back.Visible && !front.Visible && !card.PushAnimation.IsFlipping,
                "cancelled solo deal timer cannot overwrite the scripted push");
            card.SetHeld(true);
            await Wait(.22);
            Check(card.IsHeld && front.Visible && body.Position.IsZeroApprox() && Mathf.IsZeroApprox(body.Rotation),
                "normal solo rehold cancels preview and restores standard presentation");
            card.SetHeld(false);
            await Wait(.22);
            Check(!card.IsHeld && back.Visible && body.Scale.IsEqualApprox(Vector2.One), "normal solo discard still flips to back");
            card.SetHeld(true); card.Hide();
            await Wait(.22); card.Show();
            Check(card.IsHeld && front.Visible && body.Scale.IsEqualApprox(Vector2.One),
                "hiding mid-rehold resumes at the intended front face");
            card.SetHeld(false); card.Hide();
            await Wait(.22); card.Show();
            Check(!card.IsHeld && back.Visible, "hiding mid-discard resumes at the intended back face");
            card.AnimateReplace(1, 0);
            await Wait(.22);
            Check(front.Visible && !card.GetNode<Sprite2D>("Shadow").Visible, "normal solo replacement still reveals and clears shadow");
            card.AnimateDeal(0, 1, 0);
            await Wait(.5);
            Check(front.Visible && body.Scale.IsEqualApprox(Vector2.One) && body.Position.IsZeroApprox(),
                "normal solo deal retains its landing and reveal");
            card.AnimateDeal(.2f, 1, 0); card.Hide();
            await Wait(.5); card.Show();
            Check(front.Visible && body.Scale.IsEqualApprox(Vector2.One),
                "hiding before a delayed deal completes at the intended face without stale timers");
            card.PlayInteractionHint(0, 8, .04f);
            await Wait(.4);
            Check(front.Visible && body.Position.IsZeroApprox() && Mathf.IsZeroApprox(body.Rotation),
                "existing passive hint remains visual-only and returns home");
            ResetCards(); Push(1);
            Check(_dog.PokerPaw(true).Modulate.A == 0 && paw.Modulate.A == 1, "right-paw selection is explicit, without player IDs or flip numbers");
            await Wait(.65);
            Check(_dog.PokerPaw(true).Modulate.A == 1, "mirrored right paw returns normally");
            bool fallback = false;
            _paws.Play(null, card.PushAnimation, 0, direction => fallback = direction == Vector2.Zero);
            Check(fallback && _paws.ActiveCount == 0, "missing source falls back to the caller's ordinary flip");
            ResetCards(); Push(0); _paws.Cancel(); card.ResetPushPresentation(true);
            await Wait(.25);
            Check(front.Visible && !card.PushAnimation.IsFlipping && card.PushAnimation.FlipProgress == 0
                && _paws.ActiveCount == 0 && paw.Modulate.A == 1, "explicit cancellation clears all old tweens and shader projection");
            GD.Print($"CARD_PUSH_REUSE_PASS:{checks}"); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("CARD_PUSH_REUSE_FAIL:" + error); GetTree().Quit(1); }
    }
}
#endif
