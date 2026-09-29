#if DEBUG && !RECORDING_BUILD
using System;
using Godot;
using DataTables;

namespace LuckyDogRise.Rooms;

public partial class RoomDogView : Node2D
{
    [Export] public DogVisual Dog { get; private set; } = null!;
    [Export] private PanelContainer _nameBar = null!;
    [Export] private Label _name = null!;
    [Export] private PanelContainer _bubble = null!;
    [Export] private Label _bubbleText = null!;
    [Export] private Button _chatButton = null!;
    [Export] private PanelContainer _inputPanel = null!;
    [Export] private LineEdit _input = null!;
    [Export] private Button _send = null!;
    [Export] private Button _cancel = null!;
    [Export] private Godot.Collections.Array<Button> _dogHits = new();
    public event Action<string> SendRequested;
    private RoomMember _member;
    private bool _isLocal;
    private double _hoverGrace;
    private Sprite2D _tongue;
    public bool Editing => _inputPanel.Visible;
    public int MemberId => _member?.Id ?? 0;

    public override void _Ready()
    {
        _tongue = Dog.GetNode<Sprite2D>("HeadRoot/Tonghe");
        _chatButton.Pressed += OpenChat;
        _send.Pressed += Send;
        _cancel.Pressed += CloseChat;
        _input.TextSubmitted += _ => Send();
        _input.MaxLength = RoomRules.MaxChatCharacters;
        _bubble.Hide();
        _inputPanel.Hide();
        _chatButton.Hide();
    }

    public void Display(RoomMember member, bool local, bool owner, bool showName, string bubble)
    {
        if (_member?.Id != member.Id || _isLocal != local) CloseChat();
        _isLocal = local;
        if (_member?.SkinId != member.SkinId)
            Dog.SetPreviewAppearance(DogAppearanceSpec.FromDogSkin(LubanData.Tables.TbDogSkin.Get(member.SkinId)));
        if (_member?.HeadwearId != member.HeadwearId || _member?.SkinId != member.SkinId)
            Dog.SetPreviewHeadwear(LubanData.Tables.TbItem.GetOrDefault(member.HeadwearId));
        if (_member?.Reaction != member.Reaction || _member?.SkinId != member.SkinId)
            Dog.ApplyReaction((EDogReactionTrigger)member.Reaction);
        _member = member;
        _name.Text = (owner ? "♛ " : "") + member.Name + (local ? " · 本机" : "");
        _nameBar.Visible = showName;
        _bubble.Visible = !string.IsNullOrEmpty(bubble);
        _bubbleText.Text = bubble ?? "";
        if (!local) _chatButton.Hide();
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
        if (!Visible || !_isLocal) return;
        if (IsPointerOverDog() || Contains(_chatButton)) _hoverGrace = 0.4;
        else _hoverGrace -= delta;
        _chatButton.Visible = !Editing && _hoverGrace > 0;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (Editing && @event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            CloseChat();
            GetViewport().SetInputAsHandled();
        }
    }

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
        || Contains(_chatButton) || Contains(_inputPanel));

    public void MakePassivePreview()
    {
        foreach (var hit in _dogHits)
        {
            hit.MouseFilter = Control.MouseFilterEnum.Ignore;
            hit.FocusMode = Control.FocusModeEnum.None;
        }
    }

    public void OpenChat()
    {
        if (!_isLocal || !Visible) return;
        _inputPanel.Show();
        _chatButton.Hide();
        _input.GrabFocus();
    }
    private void Send() => SendRequested?.Invoke(_input.Text);
    public void AcceptSend() { _input.Clear(); CloseChat(); }
    public void SubmitChatForSmoke(string text)
    {
        OpenChat();
        _input.Text = text;
        _send.EmitSignal(Button.SignalName.Pressed);
    }
    public void CloseChat()
    {
        _inputPanel.Hide();
        _input.ReleaseFocus();
    }
}
#endif
