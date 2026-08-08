using Godot;
using System.Collections.Generic;
using DataTables;

namespace LuckyDogRise;

public enum InteractionHintTriggerKind
{
    PassiveMistake,
    ProactiveIdle,
}

public interface IInteractionHintTarget
{
    bool CanPlayInteractionHint { get; }
    bool IsInteractionHintPlaying { get; }
    void PlayInteractionHint(InteractionHintTriggerKind triggerKind);
}

/// <summary>
/// 集中维护当前阶段可被提示的交互目标。
/// 业务层登记目标并切换可用列表；目标本身只负责播放自己的提示动画。
/// </summary>
public partial class InteractionHintController : Node
{
    private const double DefaultProactiveHintIdleSeconds = 6.0;
    private const double DefaultProactiveHintRepeatSeconds = 0.8;
    private readonly Dictionary<string, IInteractionHintTarget> _targets = new();
    private readonly List<string> _availableKeys = new();
    private readonly HashSet<string> _warnedConfigKeys = new();
    private bool _hasPendingClick;
    private bool _shouldResolvePendingClick;
    private bool _pendingClickWasHandled;
    private bool _proactiveHintsEnabled = true;
    private bool _proactiveHintContextActive;
    private bool _inputContextActive = true;
    private double _secondsSinceEffectiveInteraction;
    private bool _proactiveHintHasPlayed;
    private bool _proactiveHintAnimationWasPlaying;
    private double _proactiveHintRepeatDelayRemaining;
    private string _activeProactiveHintKey = "";

    public void RegisterTarget(string key, IInteractionHintTarget target)
    {
        _targets[key] = target;
        _ = LoadHintSettings(key);
    }

    public void SetAvailableKeys(params string[] keys)
    {
        _availableKeys.Clear();
        foreach (var key in keys)
        {
            if (!_availableKeys.Contains(key))
                _availableKeys.Add(key);
        }
    }

    public void SetProactiveHintsEnabled(bool enabled)
    {
        if (_proactiveHintsEnabled == enabled)
            return;

        _proactiveHintsEnabled = enabled;
        ResetProactiveHintIdlePeriod();
    }

    /// <summary>
    /// 仅在扑克模式且没有全屏覆盖交互时允许无操作后的主动提示。
    /// </summary>
    public void SetProactiveHintContextActive(bool active)
    {
        if (_proactiveHintContextActive == active)
            return;

        _proactiveHintContextActive = active;
        ResetProactiveHintIdlePeriod();
    }

    /// <summary>
    /// 全屏交互遮罩显示时暂停点击误点判定，避免遮罩自身的点击被当作扑克区域误点。
    /// </summary>
    public void SetInputContextActive(bool active)
    {
        if (_inputContextActive == active)
            return;

        _inputContextActive = active;
        _hasPendingClick = false;
        _shouldResolvePendingClick = false;
        _pendingClickWasHandled = false;
        ResetProactiveHintIdlePeriod();
    }

    /// <summary>
    /// 由实际完成当前阶段操作的交互回调调用，阻止本次点击触发新手提示。
    /// </summary>
    public void NotifyInteractionHandled()
    {
        _pendingClickWasHandled = true;
        ResetProactiveHintIdlePeriod();
    }

    /// <summary>
    /// 本次点击由交互物接收但不推进流程：不触发误点提示，也不重置主动提示计时。
    /// </summary>
    public void NotifyInteractionIgnored()
    {
        _pendingClickWasHandled = true;
    }

    public override void _Input(InputEvent @event)
    {
        if (!_inputContextActive)
            return;

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
        ResolveIncorrectClickHint();
        ProcessProactiveHint(delta);
    }

    private void ResolveIncorrectClickHint()
    {
        if (!_inputContextActive)
            return;

        if (!_hasPendingClick || !_shouldResolvePendingClick)
            return;

        // Button.Pressed 在鼠标松开时触发；等到松开后的 _Process 再结算，
        // 才能正确区分翻牌、下注等有效点击与真正的误点。
        _hasPendingClick = false;
        _shouldResolvePendingClick = false;
        if (!_pendingClickWasHandled)
            TryPlayBestAvailableHint(InteractionHintTriggerKind.PassiveMistake);
    }

    private void ProcessProactiveHint(double delta)
    {
        if (!_inputContextActive
            || !_proactiveHintsEnabled
            || !_proactiveHintContextActive)
            return;

        var candidate = GetBestAvailableCandidate(proactiveOnly: true);
        if (candidate == null)
        {
            if (_activeProactiveHintKey.Length > 0)
            {
                _activeProactiveHintKey = "";
                ResetProactiveHintIdlePeriod();
            }
            return;
        }

        if (_activeProactiveHintKey != candidate.Value.Key)
        {
            _activeProactiveHintKey = candidate.Value.Key;
            ResetProactiveHintIdlePeriod();
        }

        if (!_proactiveHintHasPlayed)
        {
            _secondsSinceEffectiveInteraction += delta;
            if (_secondsSinceEffectiveInteraction < candidate.Value.Settings.IdleSeconds)
                return;

            if (TryPlayCandidate(candidate.Value, InteractionHintTriggerKind.ProactiveIdle))
            {
                _proactiveHintHasPlayed = true;
                _proactiveHintAnimationWasPlaying = true;
            }
            return;
        }

        if (candidate.Value.Target.IsInteractionHintPlaying)
        {
            _proactiveHintAnimationWasPlaying = true;
            return;
        }

        if (_proactiveHintAnimationWasPlaying)
        {
            _proactiveHintAnimationWasPlaying = false;
            _proactiveHintRepeatDelayRemaining = candidate.Value.Settings.RepeatSeconds;
            return;
        }

        if (candidate.Value.Settings.RepeatSeconds <= 0.0)
            return;

        if (_proactiveHintRepeatDelayRemaining > 0.0)
        {
            _proactiveHintRepeatDelayRemaining -= delta;
            return;
        }

        if (TryPlayCandidate(candidate.Value, InteractionHintTriggerKind.ProactiveIdle))
            _proactiveHintAnimationWasPlaying = true;
    }

    private bool TryPlayBestAvailableHint(InteractionHintTriggerKind triggerKind)
    {
        if (IsAvailableHintAnimationPlaying())
            return false;

        var candidate = GetBestAvailableCandidate(proactiveOnly: false);
        return candidate != null && TryPlayCandidate(candidate.Value, triggerKind);
    }

    private bool TryPlayCandidate(HintCandidate candidate, InteractionHintTriggerKind triggerKind)
    {
        if (!candidate.Target.CanPlayInteractionHint
            || candidate.Target.IsInteractionHintPlaying
            || IsAvailableHintAnimationPlaying())
            return false;

        candidate.Target.PlayInteractionHint(triggerKind);
        return true;
    }

    private HintCandidate? GetBestAvailableCandidate(bool proactiveOnly)
    {
        HintCandidate? best = null;
        for (var index = 0; index < _availableKeys.Count; index++)
        {
            var key = _availableKeys[index];
            if (!_targets.TryGetValue(key, out var target) || !target.CanPlayInteractionHint)
                continue;

            var settings = LoadHintSettings(key);
            if (proactiveOnly && !settings.ProactiveEnabled)
                continue;

            var candidate = new HintCandidate(key, target, settings, index);
            if (best == null
                || candidate.Settings.Priority > best.Value.Settings.Priority
                || (candidate.Settings.Priority == best.Value.Settings.Priority
                    && candidate.Order < best.Value.Order))
            {
                best = candidate;
            }
        }

        return best;
    }

    private bool IsAvailableHintAnimationPlaying()
    {
        foreach (var key in _availableKeys)
        {
            if (_targets.TryGetValue(key, out var target) && target.IsInteractionHintPlaying)
                return true;
        }

        return false;
    }

    private void ResetProactiveHintIdlePeriod()
    {
        _secondsSinceEffectiveInteraction = 0.0;
        _proactiveHintHasPlayed = false;
        _proactiveHintAnimationWasPlaying = false;
        _proactiveHintRepeatDelayRemaining = 0.0;
    }

    private HintSettings LoadHintSettings(string key)
    {
        var config = LubanData.Tables.TbInteractionHintConfig.GetOrDefault(key);
        if (config == null)
        {
            WarnConfigOnce(
                $"missing:{key}",
                $"[InteractionHint] Missing InteractionHintConfig key '{key}'; using " +
                $"{DefaultProactiveHintIdleSeconds:0.##}s idle and {DefaultProactiveHintRepeatSeconds:0.##}s repeat fallbacks.");
            return new HintSettings(true, DefaultProactiveHintIdleSeconds, DefaultProactiveHintRepeatSeconds, 0);
        }

        var idleSeconds = config.ProactiveIdleSeconds;
        if (idleSeconds <= 0f)
        {
            WarnConfigOnce(
                $"idle:{key}",
                $"[InteractionHint] InteractionHintConfig '{key}' has non-positive ProactiveIdleSeconds; " +
                $"using {DefaultProactiveHintIdleSeconds:0.##}s fallback.");
            idleSeconds = (float)DefaultProactiveHintIdleSeconds;
        }

        var repeatSeconds = config.ProactiveRepeatSeconds;
        if (repeatSeconds < 0f)
        {
            WarnConfigOnce(
                $"repeat:{key}",
                $"[InteractionHint] InteractionHintConfig '{key}' has negative ProactiveRepeatSeconds; treating it as 0 (no repeat).");
            repeatSeconds = 0f;
        }

        return new HintSettings(config.ProactiveEnabled, idleSeconds, repeatSeconds, config.Priority);
    }

    private void WarnConfigOnce(string warningKey, string message)
    {
        if (_warnedConfigKeys.Add(warningKey))
            GD.PushWarning(message);
    }

    private readonly record struct HintSettings(
        bool ProactiveEnabled,
        double IdleSeconds,
        double RepeatSeconds,
        int Priority);

    private readonly record struct HintCandidate(
        string Key,
        IInteractionHintTarget Target,
        HintSettings Settings,
        int Order);
}
