using System;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class L2PlayerAnimationStateController : MonoBehaviour
{
    private enum Phase
    {
        None,
        Idle,
        CombatIdle,
        Running,
        SittingDown,
        Sitting,
        StandingUp,
        CastStart,
        Casting,
        CastEnd,
        Attacking,
        Custom
    }

    public L2PlayerCharacterArchetypeAsset Archetype;
    public L2PlayerEquipmentVisual Equipment;
    public Animator Animator;

    public bool IsSitting;
    public bool IsInCombat;
    public bool IsRunning;
    public bool IsCasting;
    public bool IsAttacking;
    public bool UseCustomAnimation;
    public int CustomAnimationIndex;
    public bool LoopCustomAnimation;

    private L2SkeletalAnimationSequenceData _currentSequence;
    private Phase _phase;
    private L2WeaponAnimationClass _lastWeaponClass;
    private int _attackIndex;
    private int _lastCustomIndex = -1;

    public string[] GetAnimationNames()
    {
        return (Archetype?.BaseAsset?.AnimationSequences ?? Array.Empty<L2SkeletalAnimationSequenceData>())
            .Select(x => x?.Name ?? "<missing>")
            .ToArray();
    }

    public void SetRunning(bool value)
    {
        if (IsRunning == value)
        {
            return;
        }

        IsRunning = value;
        Refresh();
    }

    public void Refresh()
    {
        if (!Application.isPlaying || Archetype?.BaseAsset == null || Animator == null || Equipment == null)
        {
            return;
        }

        var weaponClass = Equipment.AnimationClass;
        if (weaponClass != _lastWeaponClass)
        {
            _lastWeaponClass = weaponClass;
            _phase = Phase.None;
            if (weaponClass == L2WeaponAnimationClass.Fishing)
            {
                IsAttacking = false;
            }
        }

        if (UseCustomAnimation)
        {
            ApplyCustomAnimation();
            return;
        }

        if (_phase == Phase.Custom)
        {
            _phase = Phase.None;
        }

        if (IsSitting)
        {
            ApplySitting();
            return;
        }

        if (_phase == Phase.SittingDown || _phase == Phase.Sitting)
        {
            PlayPhase(Phase.StandingUp, RequireAction("Stand"));
            return;
        }

        if (_phase == Phase.StandingUp)
        {
            if (!CurrentFinished())
            {
                return;
            }

            _phase = Phase.None;
        }

        if (IsCasting)
        {
            ApplyCasting();
            return;
        }

        if (_phase == Phase.CastStart || _phase == Phase.Casting)
        {
            PlayPhase(Phase.CastEnd, RequireAction("castEnd"));
            return;
        }

        if (_phase == Phase.CastEnd)
        {
            if (!CurrentFinished())
            {
                return;
            }

            _phase = Phase.None;
        }

        if (IsAttacking && weaponClass != L2WeaponAnimationClass.Fishing)
        {
            ApplyAttack(weaponClass);
            return;
        }

        var desired = IsRunning ? Phase.Running : IsInCombat ? Phase.CombatIdle : Phase.Idle;
        if (_phase != desired)
        {
            var action = weaponClass == L2WeaponAnimationClass.Fishing && desired != Phase.Running
                ? "Fishing_wait"
                : desired switch
            {
                Phase.Running => $"Run_{AnimationSuffix(weaponClass)}",
                Phase.CombatIdle => $"AtkWait_{AnimationSuffix(weaponClass)}",
                _ => $"Wait_{AnimationSuffix(weaponClass)}"
            };
            PlayPhase(desired, RequireAction(action));
        }
        else
        {
            RepeatHoldIfNeeded();
        }
    }

    private void ApplySitting()
    {
        if (_phase != Phase.SittingDown && _phase != Phase.Sitting)
        {
            PlayPhase(Phase.SittingDown, RequireAction("Sit"));
            return;
        }

        if (_phase == Phase.SittingDown && CurrentFinished())
        {
            PlayPhase(Phase.Sitting, RequireAction("SitWait"));
        }
        else if (_phase == Phase.Sitting)
        {
            RepeatHoldIfNeeded();
        }
    }

    private void ApplyCasting()
    {
        if (_phase != Phase.CastStart && _phase != Phase.Casting)
        {
            PlayPhase(Phase.CastStart, RequireAction("castLong"));
            return;
        }

        if (_phase == Phase.CastStart && CurrentFinished())
        {
            PlayPhase(Phase.Casting, RequireAction("castMid"));
        }
        else if (_phase == Phase.Casting)
        {
            RepeatHoldIfNeeded();
        }
    }

    private void ApplyAttack(L2WeaponAnimationClass weaponClass)
    {
        var attacks = FindAttacks(weaponClass);
        if (attacks.Length == 0)
        {
            throw new InvalidOperationException(
                $"Character '{Archetype.ArchetypeName}' has no attack sequence for '{weaponClass}'.");
        }

        if (_phase != Phase.Attacking)
        {
            _attackIndex = 0;
            PlayPhase(Phase.Attacking, attacks[_attackIndex]);
        }
        else if (CurrentFinished())
        {
            _attackIndex = (_attackIndex + 1) % attacks.Length;
            PlayPhase(Phase.Attacking, attacks[_attackIndex]);
        }
    }

    private void ApplyCustomAnimation()
    {
        var sequences = Archetype.BaseAsset.AnimationSequences ?? Array.Empty<L2SkeletalAnimationSequenceData>();
        if (sequences.Length == 0)
        {
            return;
        }

        var index = Mathf.Clamp(CustomAnimationIndex, 0, sequences.Length - 1);
        if (_phase != Phase.Custom || _lastCustomIndex != index)
        {
            _lastCustomIndex = index;
            PlayPhase(Phase.Custom, sequences[index]);
        }
        else if (LoopCustomAnimation && CurrentFinished() && !_currentSequence.SuggestedLoop)
        {
            PlayPhase(Phase.Custom, _currentSequence);
        }
    }

    private L2SkeletalAnimationSequenceData[] FindAttacks(L2WeaponAnimationClass weaponClass)
    {
        if (weaponClass == L2WeaponAnimationClass.Fishing)
        {
            return Array.Empty<L2SkeletalAnimationSequenceData>();
        }

        var suffix = AnimationSuffix(weaponClass);
        return new[] { $"Atk01_{suffix}", $"Atk02_{suffix}", $"Atk03_{suffix}" }
            .Select(FindAction)
            .Where(x => x != null)
            .ToArray();
    }

    private L2SkeletalAnimationSequenceData RequireAction(string action)
    {
        return FindAction(action) ?? throw new InvalidOperationException(
            $"Character '{Archetype.ArchetypeName}' has no sequence for action '{action}'.");
    }

    private L2SkeletalAnimationSequenceData FindAction(string action)
    {
        var prefix = action + "_";
        var matches = (Archetype.BaseAsset.AnimationSequences ?? Array.Empty<L2SkeletalAnimationSequenceData>())
            .Where(x => x != null && x.Name != null &&
                        x.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"Character '{Archetype.ArchetypeName}' has ambiguous sequence for action '{action}'.");
        }

        return matches.FirstOrDefault();
    }

    private static string AnimationSuffix(L2WeaponAnimationClass weaponClass)
    {
        return weaponClass switch
        {
            L2WeaponAnimationClass.Hand => "Hand",
            L2WeaponAnimationClass.OneHanded => "1HS",
            L2WeaponAnimationClass.TwoHanded => "2HS",
            L2WeaponAnimationClass.Bow => "Bow",
            L2WeaponAnimationClass.Dual => "Dual",
            L2WeaponAnimationClass.Pole => "Pole",
            L2WeaponAnimationClass.Fishing => "Hand",
            _ => throw new ArgumentOutOfRangeException(nameof(weaponClass), weaponClass, null)
        };
    }

    private void PlayPhase(Phase phase, L2SkeletalAnimationSequenceData sequence)
    {
        if (sequence == null)
        {
            throw new InvalidOperationException($"Character '{Archetype.ArchetypeName}' has a null animation sequence.");
        }

        L2CharacterAnimationPlayback.Play(Archetype, Animator, sequence.Name);
        _currentSequence = sequence;
        _phase = phase;
    }

    private bool CurrentFinished()
    {
        return L2CharacterAnimationPlayback.IsFinished(Animator);
    }

    private void RepeatHoldIfNeeded()
    {
        if (_currentSequence != null && !_currentSequence.SuggestedLoop && CurrentFinished())
        {
            PlayPhase(_phase, _currentSequence);
        }
    }

    private void OnEnable()
    {
        Refresh();
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            Refresh();
        }
    }

    private void Update()
    {
        if (Application.isPlaying)
        {
            Refresh();
        }
    }
}
