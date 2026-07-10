using Godot;
using System;
using System.Collections.Generic;

namespace LuckyDogRise;

public enum InteractionHintTargetId
{
    BetStack,
    RewardStack,
    HandConfirm,
    CardSelection,
    DogAdvice,
}

public interface IInteractionHintTarget
{
    bool CanPlayInteractionHint { get; }
    bool IsInteractionHintPlaying { get; }
    void PlayInteractionHint();
}

/// <summary>
/// 集中维护当前阶段可被提示的交互目标。
/// 业务层登记目标并切换可用列表；目标本身只负责播放自己的提示动画。
/// </summary>
public partial class InteractionHintController : Node
{
    private readonly Dictionary<InteractionHintTargetId, IInteractionHintTarget> _targets = new();
    private readonly Dictionary<InteractionHintTargetId, Action> _hintActions = new();
    private readonly HashSet<InteractionHintTargetId> _availableTargets = new();
    private bool _hasPendingClick;
    private bool _shouldResolvePendingClick;
    private bool _pendingClickWasHandled;

    public void RegisterTarget(InteractionHintTargetId id, IInteractionHintTarget target)
    {
        _targets[id] = target;
    }

    /// <summary>
    /// 在目标的提示动画尚未实现时，也可先登记一项可替换的提示行为，例如诊断输出。
    /// </summary>
    public void RegisterHintAction(InteractionHintTargetId id, Action hintAction)
    {
        _hintActions[id] = hintAction;
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
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mouseButton)
            return;

        if (mouseButton.Pressed)
        {
            _pendingClickWasHandled = false;
            _hasPendingClick = true;
            _shouldResolvePendingClick = false;
            return;
        }

        if (_hasPendingClick)
            _shouldResolvePendingClick = true;
    }

    public override void _Process(double delta)
    {
        if (!_hasPendingClick || !_shouldResolvePendingClick)
            return;

        // Button.Pressed 在鼠标松开时触发；等到松开后的 _Process 再结算，
        // 才能正确区分翻牌、下注等有效点击与真正的误点。
        // NotifyInteractionHandled() 标记本次点击，未标记才播放提示。
        _hasPendingClick = false;
        _shouldResolvePendingClick = false;
        if (_pendingClickWasHandled)
            return;

        foreach (var id in _availableTargets)
        {
            if (_targets.TryGetValue(id, out var target)
                && target.CanPlayInteractionHint
                && !target.IsInteractionHintPlaying)
            {
                target.PlayInteractionHint();
            }
            else if (_hintActions.TryGetValue(id, out var hintAction))
            {
                hintAction();
            }
        }
    }
}
