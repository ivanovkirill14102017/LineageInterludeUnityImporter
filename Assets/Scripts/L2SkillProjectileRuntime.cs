using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class L2SkillProjectileRuntime : MonoBehaviour
{
    public Vector3 Origin;
    public Vector3 Target;
    public float Speed = 8f;
    public float ArcHeight;
    public bool FaceTravelDirection = true;
    public bool DestroyOnArrival = true;
    private float _duration = 0.1f;
    private float _elapsed;
    private bool _initialized;
#if UNITY_EDITOR
    private double _lastEditorUpdateTime;
#endif

    public void Initialize(
        Vector3 origin,
        Vector3 target,
        float speed,
        float arcHeight)
    {
        Origin = origin;
        Target = target;
        Speed = Mathf.Max(0.01f, speed);
        ArcHeight = Mathf.Max(0f, arcHeight);
        _duration = Mathf.Max(0.01f, Vector3.Distance(Origin, Target) / Speed);
        _elapsed = 0f;
        _initialized = true;
#if UNITY_EDITOR
        _lastEditorUpdateTime = EditorApplication.timeSinceStartup;
#endif
        transform.position = Origin;
        FaceTowards(Target - Origin);
    }

    private void Update()
    {
        if (!_initialized)
        {
            return;
        }

        var deltaTime = ResolveDeltaTime();
        _elapsed += deltaTime;
        var t = Mathf.Clamp01(_elapsed / _duration);
        var position = Vector3.Lerp(Origin, Target, t);
        if (ArcHeight > 0f)
        {
            position.y += Mathf.Sin(t * Mathf.PI) * ArcHeight;
        }

        if (FaceTravelDirection)
        {
            var nextT = Mathf.Clamp01((_elapsed + Time.deltaTime) / _duration);
            var next = Vector3.Lerp(Origin, Target, nextT);
            if (ArcHeight > 0f)
            {
                next.y += Mathf.Sin(nextT * Mathf.PI) * ArcHeight;
            }

            FaceTowards(next - transform.position);
        }

        transform.position = position;

        if (t >= 1f)
        {
            _initialized = false;
            if (DestroyOnArrival)
            {
                DestroyRuntimeObject(gameObject);
            }
        }
    }

    private float ResolveDeltaTime()
    {
        if (Application.isPlaying)
        {
            return Time.deltaTime;
        }

#if UNITY_EDITOR
        var now = EditorApplication.timeSinceStartup;
        var delta = Mathf.Clamp((float)(now - _lastEditorUpdateTime), 0.001f, 0.1f);
        _lastEditorUpdateTime = now;
        return delta;
#else
        return Time.deltaTime;
#endif
    }

    private static void DestroyRuntimeObject(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private void FaceTowards(Vector3 direction)
    {
        if (direction.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }
    }
}
