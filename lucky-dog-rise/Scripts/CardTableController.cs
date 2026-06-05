using Godot;

namespace LuckyDogRise;

public partial class CardTableController : Node2D
{
    private static readonly Color DimColor = new(0.6f, 0.6f, 0.6f, 1f);
    private const float FadeInDuration = 0.3f;

    private TextureRect[] _cards = new TextureRect[5];

    public override void _Ready()
    {
        for (int i = 0; i < 5; i++)
            _cards[i] = GetNode<TextureRect>($"Card{i}");
    }

    public void SetCards(int[] hand)
    {
        for (int i = 0; i < 5; i++)
            SetCardTexture(i, hand[i]);
    }

    public void DimAll()
    {
        for (int i = 0; i < 5; i++)
            _cards[i].Modulate = DimColor;
    }

    public void BrightenAll()
    {
        for (int i = 0; i < 5; i++)
            _cards[i].Modulate = Colors.White;
    }

    public void SetHeld(int index, bool held)
    {
        _cards[index].Modulate = held ? Colors.White : DimColor;
    }

    public void AnimateFadeIn(int index)
    {
        _cards[index].Modulate = new Color(1, 1, 1, 0);
        var tween = CreateTween();
        tween.TweenProperty(_cards[index], "modulate:a", 1f, FadeInDuration)
            .SetEase(Tween.EaseType.Out);
    }

    public void ConnectCardInput(GodotObject target, string method)
    {
        for (int i = 0; i < 5; i++)
        {
            int index = i;
            _cards[i].GuiInput += (e) => target.Call(method, e, index);
        }
    }

    private void SetCardTexture(int index, int card)
    {
        var path = DeckManager.CardToAssetPath(card);
        var tex = GD.Load<Texture2D>(path);
        if (tex != null)
            _cards[index].Texture = tex;
        else
            GD.PrintErr($"[Card] FAILED to load: {path}");
    }
}
