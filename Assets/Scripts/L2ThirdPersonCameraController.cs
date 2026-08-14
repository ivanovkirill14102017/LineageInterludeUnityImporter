using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class L2ThirdPersonCameraController : MonoBehaviour
{
    public Transform Target;
    public Vector3 TargetOffset = new Vector3(0f, 1.35f, 0f);
    public float Distance = 4.5f;
    public float MinDistance = 1.75f;
    public float MaxDistance = 12f;
    public float OrbitSensitivity = 160f;
    public float ZoomSensitivity = 3f;
    public float Pitch = 24f;
    public float Yaw;

    public void Attach(Transform target)
    {
        Target = target;
        if (target != null)
        {
            var euler = transform.rotation.eulerAngles;
            Yaw = euler.y;
            Pitch = NormalizePitch(euler.x);
        }
    }

    private void LateUpdate()
    {
        if (Target == null)
        {
            return;
        }

        var mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        if (mouse.rightButton.isPressed)
        {
            var delta = mouse.delta.ReadValue();
            Yaw += delta.x * OrbitSensitivity * Time.deltaTime * 0.1f;
            Pitch -= delta.y * OrbitSensitivity * Time.deltaTime * 0.1f;
            Pitch = Mathf.Clamp(Pitch, -15f, 70f);
        }

        var scroll = NormalizeScroll(mouse.scroll.ReadValue().y);
        if (Mathf.Abs(scroll) > 0.001f)
        {
            Distance = Mathf.Clamp(Distance - scroll * ZoomSensitivity, MinDistance, MaxDistance);
        }

        var rotation = Quaternion.Euler(Pitch, Yaw, 0f);
        var focus = Target.position + TargetOffset;
        transform.position = focus - rotation * Vector3.forward * Distance;
        transform.rotation = rotation;
    }

    private static float NormalizePitch(float pitch)
    {
        return pitch > 180f ? pitch - 360f : pitch;
    }

    private static float NormalizeScroll(float scrollY)
    {
        return Mathf.Abs(scrollY) > 10f ? scrollY / 120f : scrollY;
    }
}
