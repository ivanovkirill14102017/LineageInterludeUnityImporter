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
    private L2PlayerAnimationStateController _animation;

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
        _animation = GetComponent<L2PlayerAnimationStateController>();
        _animation?.SetRunning(player.IsMoving);
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
        _animation?.SetRunning(Player.IsMoving);
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

}
