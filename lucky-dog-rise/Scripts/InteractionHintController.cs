using Godot;
using System.Collections.Generic;

namespace LuckyDogRise;

public enum InteractionHintTargetId
{
    BetStack,
    RewardStack,
}

public interface IInteractionHintTarget
{
    bool CanPlayInteractionHint { get; }
    void PlayInteractionHint();
}

/// <summary>
/// 集中维护当前阶段可被提示的交互目标。
/// 业务层登记目标并切换可用列表；目标本身只负责播放自己的提示动画。
/// </summary>
public partial class InteractionHintController : Node
{
    private readonly Dictionary<InteractionHintTargetId, IInteractionHintTarget> _targets = new();
    private readonly HashSet<InteractionHintTargetId> _availableTargets = new();
    private bool _hasPendingClick;
    private bool _pendingClickWasHandled;

    public void RegisterTarget(InteractionHintTargetId id, IInteractionHintTarget target)
    {
        _targets[id] = target;
    }

    public void SetAvailableTargets(params InteractionHintTargetId[] targetIds)
    {
        _availableTargets.Clear();
        foreach (var id in targetIds)
            _availableTargets.Add(id);
    }

    /// <summary>
    /// 由实际完成当前阶段操作的交互回调调用，阻止本次点击触发新手提示。
    /// </summary>
    public void NotifyInteractionHandled()
    {
        _pendingClickWasHandled = true;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.Left,
            })
            return;

        _pendingClickWasHandled = false;
        _hasPendingClick = true;
    }

    public override void _Process(double delta)
    {
        if (!_hasPendingClick)
            return;

        // _Input 先于按钮回调触发；等到本帧 _Process 时，正确目标已可调用
        // NotifyInteractionHandled() 标记本次点击，未标记才播放提示。
        _hasPendingClick = false;
        if (_pendingClickWasHandled)
            return;

        foreach (var id in _availableTargets)
        {
            if (_targets.TryGetValue(id, out var target) && target.CanPlayInteractionHint)
            {
                GD.Print($"[InteractionHint] Dispatch hint: {id}");
                target.PlayInteractionHint();
            }
        }
    }
}
