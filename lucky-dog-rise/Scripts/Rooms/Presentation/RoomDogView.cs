#if !DEMO_BUILD && !RECORDING_BUILD
using System;
using Godot;
using DataTables;

namespace LuckyDogRise.Rooms;

public partial class RoomDogView : Node2D
{
    [Export] public DogVisual Dog { get; private set; } = null!;
    [Export] private PanelContainer _nameBar = null!;
    [Export] private Label _name = null!;
    [Export] public RoomChatView Chat { get; private set; } = null!;
    [Export] private Godot.Collections.Array<Button> _dogHits = new();
    public event Action<string> SendRequested;
    private RoomMember _member;
    private bool _isLocal;
    private Sprite2D _tongue;
    public bool Editing => Chat.Editing;
    public int MemberId => _member?.Id ?? 0;

    public override void _Ready()
    {
        _tongue = Dog.GetNode<Sprite2D>("HeadRoot/Tonghe");
        Chat.SendRequested += text => SendRequested?.Invoke(text);
    }

    public void Display(RoomMember member, bool local, bool owner, bool showName, string bubble,
        bool tongueActive = false)
    {
        if (_member?.Id != member.Id || _isLocal != local) CloseChat();
        _isLocal = local;
        if (_member?.SkinId != member.SkinId)
            Dog.SetPreviewAppearance(DogAppearanceSpec.FromDogSkin(LubanData.Tables.TbDogSkin.Get(member.SkinId)));
        if (_member?.HeadwearId != member.HeadwearId || _member?.SkinId != member.SkinId)
            Dog.SetPreviewHeadwear(LubanData.Tables.TbItem.GetOrDefault(member.HeadwearId));
        if (_member?.Reaction != member.Reaction || _member?.SkinId != member.SkinId)
            Dog.ApplyReaction((EDogReactionTrigger)member.Reaction);
        Dog.SetRoomTongueActivity(tongueActive);
        _member = member;
        _name.Text = (owner ? "♛ " : "") + member.Name + (local ? " · 本机" : "");
        _nameBar.Visible = showName;
        _currentBubble = bubble;
        Chat.Present(local, IsPointerOverDog(), bubble);
    }

    public void ApplyNameBarLayout(Rect2 idleRect, Font font, StyleBox style,
        bool centerOnTaskbar, float taskbarHeight, float taskbarAnchorY, float tongueClearance)
    {
        // Use the real counter's cached font/style, including its scale-specific
        // oversampling and antialiasing. No per-frame resource duplication.
        if (_name.GetThemeFont("font") != font) _name.AddThemeFontOverride("font", font);
        if (_nameBar.GetThemeStylebox("panel") != style) _nameBar.AddThemeStyleboxOverride("panel", style);
        var position = idleRect.Position;
        if (centerOnTaskbar && taskbarHeight > 0)
        {
            position.Y = taskbarAnchorY + (taskbarHeight - idleRect.Size.Y) / 2;
            if (_tongue.Texture != null)
            {
                float halfHeight = _tongue.Texture.GetHeight() * Mathf.Abs(_tongue.Scale.Y) / 2;
                float tongueBottom = Dog.Position.Y + (_tongue.Position.Y + halfHeight) * Mathf.Abs(Dog.Scale.Y);
                position.Y = Mathf.Max(position.Y, tongueBottom + tongueClearance);
            }
        }
        _nameBar.Position = position;
        _nameBar.Size = idleRect.Size;
    }

    public override void _Process(double delta)
    {
        if (Visible && _isLocal && IsPointerOverDog()) Chat.Present(true, true, _currentBubble);
    }

    private string _currentBubble = "";
    private bool Contains(Control control) => control.IsVisibleInTree()
        && new Rect2(Vector2.Zero, control.Size).HasPoint(control.GetLocalMousePosition());
    public bool IsPointerOverDog()
    {
        foreach (var hit in _dogHits) if (Contains(hit)) return true;
        return false;
    }
    public bool ContainsDogPoint(Vector2 windowPoint)
    {
        foreach (var hit in _dogHits) if (ContainsWindowPoint(hit, windowPoint)) return true;
        return false;
    }
    internal static bool ContainsWindowPoint(Control control, Vector2 windowPoint) => control.IsVisibleInTree()
        && new Rect2(Vector2.Zero, control.Size).HasPoint(
            control.GetGlobalTransformWithCanvas().AffineInverse() * windowPoint);
    public bool IsPointerOverContent() => Visible && (IsPointerOverDog()
        || Chat.ContainsPoint(GetGlobalMousePosition()));

    public void MakePassivePreview()
    {
        foreach (var hit in _dogHits)
        {
            hit.MouseFilter = Control.MouseFilterEnum.Ignore;
            hit.FocusMode = Control.FocusModeEnum.None;
        }
    }

    public void OpenChat() => Chat.OpenChat();
    public void AcceptSend() => Chat.SetSendResult("");
#if DEBUG
    public void SubmitChatForSmoke(string text) => Chat.SubmitForSmoke(text);
#endif
    public void CloseChat() => Chat.Reset();
}
#endif
