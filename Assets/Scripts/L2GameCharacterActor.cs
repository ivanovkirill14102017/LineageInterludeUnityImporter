using L2Viewer.GameServer.gameserver.model;
using L2Viewer.GameServer.gameserver.model.actor.instance;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class L2GameCharacterActor : MonoBehaviour
{
    private const float GroundProbeStartOffset = 0.08f;
    private const float GroundProbeDistance = 8f;
    private const float GroundSnapTolerance = 0.03f;
    private const float FallSpeed = 12f;

    private CharacterController _controller;
    private L2PlayerCharacterWardrobe _wardrobe;
    private int _idleAnimationIndex;
    private int _runAnimationIndex;
    private bool _lastMoving;

    public L2PcInstance Player { get; private set; }

    public void Bind(L2PcInstance player)
    {
        Player = player;
        _controller = GetComponent<CharacterController>();
        if (_controller == null)
        {
            _controller = gameObject.AddComponent<CharacterController>();
        }

        if (_controller == null)
        {
            Debug.LogError($"[L2Game] Failed to create CharacterController for '{name}'.", this);
            return;
        }

        _controller.radius = player.CollisionRadius;
        _controller.height = player.CollisionHeight;
        _controller.center = new Vector3(0f, player.CollisionHeight * 0.5f, 0f);
        _wardrobe = GetComponent<L2PlayerCharacterWardrobe>();
        ResolveAnimationIndices();
        ApplyAnimationState(force: true);
    }

    public void MoveTo(Vector3 worldPosition)
    {
        if (Player == null)
        {
            return;
        }

        Player.MoveTo(new Location(worldPosition.x, transform.position.y, worldPosition.z));
    }

    public void SyncFromServer(float deltaSeconds)
    {
        if (Player == null)
        {
            return;
        }

        var target = new Vector3(Player.Location.X, transform.position.y, Player.Location.Z);
        var horizontalDelta = target - transform.position;
        if (_controller != null && _controller.enabled)
        {
            var motion = horizontalDelta + Vector3.up * ResolveVerticalMotion(deltaSeconds);
            _controller.Move(motion);
        }
        else
        {
            transform.position = target;
        }

        if (Player.IsMoving)
        {
            transform.rotation = Quaternion.Euler(0f, Player.Location.Heading * Mathf.Rad2Deg, 0f);
        }

        Player.Location = new Location(transform.position.x, transform.position.y, transform.position.z, Player.Location.Heading);
        ApplyAnimationState(force: false);
    }

    private float ResolveVerticalMotion(float deltaSeconds)
    {
        if (!TryFindGroundBelow(out var groundY))
        {
            return 0f;
        }

        var bottomY = transform.position.y;
        var distanceToGround = bottomY - groundY;
        if (distanceToGround <= GroundSnapTolerance)
        {
            return 0f;
        }

        return -Mathf.Min(distanceToGround, FallSpeed * deltaSeconds);
    }

    private bool TryFindGroundBelow(out float groundY)
    {
        groundY = transform.position.y;
        var radius = _controller != null ? Mathf.Max(0.01f, _controller.radius * 0.85f) : 0.25f;
        var origin = transform.position + Vector3.up * GroundProbeStartOffset;
        if (Physics.SphereCast(
                origin,
                radius,
                Vector3.down,
                out var hit,
                GroundProbeStartOffset + GroundProbeDistance,
                ~0,
                QueryTriggerInteraction.Ignore))
        {
            groundY = hit.point.y;
            return true;
        }

        return false;
    }

    private void ResolveAnimationIndices()
    {
        _idleAnimationIndex = FindAnimationIndex(new[] { "idle", "wait", "stand", "wait01", "wait_01" });
        _runAnimationIndex = FindAnimationIndex(new[] { "run", "walk", "move" });
    }

    private int FindAnimationIndex(string[] tokens)
    {
        var names = _wardrobe?.GetAnimationNames() ?? System.Array.Empty<string>();
        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i]?.ToLowerInvariant() ?? string.Empty;
            if (IsDeadLikeAnimationName(name))
            {
                continue;
            }

            foreach (var token in tokens)
            {
                if (name.Contains(token))
                {
                    return i;
                }
            }
        }

        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i]?.ToLowerInvariant() ?? string.Empty;
            if (!IsDeadLikeAnimationName(name))
            {
                return i;
            }
        }

        return 0;
    }

    private static bool IsDeadLikeAnimationName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return name.Contains("dead") ||
               name.Contains("death") ||
               name.Contains("die") ||
               name.Contains("died");
    }

    private void ApplyAnimationState(bool force)
    {
        if (_wardrobe == null)
        {
            return;
        }

        var moving = Player?.IsMoving == true;
        if (!force && moving == _lastMoving)
        {
            return;
        }

        _lastMoving = moving;
        _wardrobe.SetSelectedAnimation(moving ? _runAnimationIndex : _idleAnimationIndex);
    }
}
