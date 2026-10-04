#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

internal sealed class L2SkillVisualEditorPreview
{
    private readonly L2SkillVisualController _controller;
    private bool _active;
    private int _index;
    private double _nextStageTime;
    private bool _stopAfterCurrentStage;
    private int[] _phaseActions;
    private int _phaseActionIndex;
    private int _phaseEndIndex;
    private double _phaseStartTime;
    private float _phaseDuration;
    private string _visualReference;
    private double _visualStartTime;
    private bool _sawCasting;

    public L2SkillVisualEditorPreview(L2SkillVisualController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public void Play(int startIndex, bool stopAfterCurrentStage)
    {
        if (!stopAfterCurrentStage)
        {
            L2SkillVisualTimeline.ValidateStandaloneTimeline(_controller.Skill, _controller.StageBindings, startIndex);
        }

        _controller.Stop();
        var bindings = _controller.StageBindings ?? Array.Empty<L2SkillVisualStageBinding>();
        _controller.IsPlaying = true;
        _active = true;
        _index = Mathf.Clamp(startIndex, 0, bindings.Length);
        _nextStageTime = EditorApplication.timeSinceStartup;
        _stopAfterCurrentStage = stopAfterCurrentStage;
        _phaseActions = null;
        _visualReference = null;
        _sawCasting = false;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    public void Stop()
    {
        if (!_active)
        {
            return;
        }

        EditorApplication.update -= Tick;
        _active = false;
        _stopAfterCurrentStage = false;
        _phaseActions = null;
        _visualReference = null;
    }

    private void Tick()
    {
        if (!_active || Application.isPlaying)
        {
            Stop();
            return;
        }

        var bindings = _controller.StageBindings ?? Array.Empty<L2SkillVisualStageBinding>();
        if (_index >= bindings.Length)
        {
            Stop();
            _controller.IsPlaying = false;
            return;
        }

        var now = EditorApplication.timeSinceStartup;
        if (now < _nextStageTime)
        {
            return;
        }

        if (_phaseActions == null)
        {
            var placement = bindings[_index].Placement ?? throw new InvalidOperationException($"Stage '{bindings[_index].StageName}' has no placement.");
            if (!string.Equals(_visualReference, placement.VisualReference, StringComparison.OrdinalIgnoreCase))
            {
                _visualReference = placement.VisualReference;
                _visualStartTime = now;
                _sawCasting = false;
            }

            if (!_stopAfterCurrentStage)
            {
                L2SkillVisualTimeline.ValidateStandalonePhase(placement.Phase);
            }
            if (placement.Phase == L2SkillVisualPhase.Casting)
            {
                _sawCasting = true;
            }
            else if (placement.Phase == L2SkillVisualPhase.Shot && _sawCasting)
            {
                var shotTime = _visualStartTime + L2SkillVisualTimeline.ResolveHitTime(_controller.Skill, _visualReference);
                if (now < shotTime)
                {
                    _nextStageTime = shotTime;
                    return;
                }
            }

            if (placement.Phase == L2SkillVisualPhase.Shot)
            {
                _controller.StopCastingEffects();
            }
            else if (placement.Phase == L2SkillVisualPhase.Explosion)
            {
                _controller.StopShotEffects();
            }

            _phaseEndIndex = _stopAfterCurrentStage
                ? _index + 1
                : L2SkillVisualTimeline.FindPhaseEnd(bindings, _index);
            _phaseActions = L2SkillVisualTimeline.OrderPhaseActions(bindings, _index, _phaseEndIndex);
            _phaseActionIndex = 0;
            _phaseStartTime = now;
            _phaseDuration = 0f;
        }

        while (_phaseActionIndex < _phaseActions.Length)
        {
            var binding = bindings[_phaseActions[_phaseActionIndex]];
            var delay = Mathf.Max(0f, binding.Placement.SpawnDelay);
            if (now < _phaseStartTime + delay)
            {
                _nextStageTime = _phaseStartTime + delay;
                return;
            }

            _phaseDuration = Mathf.Max(_phaseDuration, delay + _controller.PlayStageBinding(binding));
            _phaseActionIndex++;
        }

        _nextStageTime = _phaseStartTime + _phaseDuration;
        if (now >= _nextStageTime)
        {
            _index = _stopAfterCurrentStage ? bindings.Length : _phaseEndIndex;
            _phaseActions = null;
            _nextStageTime = now;
        }

        SceneView.RepaintAll();
    }
}
#endif
