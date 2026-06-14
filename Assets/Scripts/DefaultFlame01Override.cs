using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class DefaultFlame01Override : MonoBehaviour
{
    private const string CoreName = "CoreFlame";
    private const string TonguesName = "FlameTongues";
    private const string EmbersName = "Embers";
    private const string SmokeName = "Smoke";
    private const string LightName = "FireLight";

    [Header("Shape")]
    public float FlameHeight = 0.95f;
    public float FlameWidth = 0.22f;

    [Header("Light")]
    public float LightIntensity = 3.6f;
    public float LightRange = 3.2f;
    public float FlickerAmplitude = 0.45f;
    public float FlickerSpeed = 7.5f;

    private static Material s_builtinParticleMaterial;
    private static Texture2D s_builtinParticleTexture;
    private Light _fireLight;

    private void OnEnable()
    {
        CacheLight();
    }

    private void OnValidate()
    {
        CacheLight();
    }

    private void Update()
    {
        CacheLight();

        if (_fireLight == null)
        {
            return;
        }

        _fireLight.color = new Color(1f, 0.56f, 0.22f, 1f);
        _fireLight.range = LightRange;

        var flicker = 1f;
        if (FlickerAmplitude > 0f)
        {
            var time = GetEffectTime();
            flicker += Mathf.Sin(time * FlickerSpeed) * FlickerAmplitude * 0.35f;
            flicker += Mathf.Sin(time * (FlickerSpeed * 1.73f)) * FlickerAmplitude * 0.18f;
        }

        _fireLight.intensity = Mathf.Max(0f, LightIntensity * flicker);
    }

    private void CacheLight()
    {
        if (_fireLight != null)
        {
            return;
        }

        var lightTransform = transform.Find(LightName);
        if (lightTransform != null)
        {
            _fireLight = lightTransform.GetComponent<Light>();
        }
    }

    [ContextMenu("Rebuild Effect Children")]
    private void RebuildEffectChildren()
    {
        EnsureEffect();
        CacheLight();
    }

    private static float GetEffectTime()
    {
        if (Application.isPlaying)
        {
            return Time.time;
        }

#if UNITY_EDITOR
        return (float)UnityEditor.EditorApplication.timeSinceStartup;
#else
        return 0f;
#endif
    }

    private void EnsureEffect()
    {
        var core = GetOrCreateChild(CoreName);
        var tongues = GetOrCreateChild(TonguesName);
        var embers = GetOrCreateChild(EmbersName);
        var smoke = GetOrCreateChild(SmokeName);
        var lightNode = GetOrCreateChild(LightName);

        ConfigureCoreFlame(GetOrAddParticleSystem(core));
        ConfigureTongues(GetOrAddParticleSystem(tongues));
        ConfigureEmbers(GetOrAddParticleSystem(embers));
        ConfigureSmoke(GetOrAddParticleSystem(smoke));
        ConfigureLight(lightNode);
    }

    private void ConfigureCoreFlame(ParticleSystem particleSystem)
    {
        ConfigureSharedDefaults(particleSystem, loop: true, duration: 1.2f, maxParticles: 80);

        var main = particleSystem.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.55f, 1.1f);
        main.startSize3D = false;
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = particleSystem.emission;
        emission.rateOverTime = 42f;

        var shape = particleSystem.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 5f;
        shape.radius = FlameWidth * 0.1f;
        shape.position = Vector3.zero;
        shape.scale = new Vector3(0.65f, FlameHeight * 1.15f, 0.65f);

        var velocity = particleSystem.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
        velocity.y = new ParticleSystem.MinMaxCurve(1.15f, 2.05f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

        var limit = particleSystem.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.dampen = 0.2f;
        limit.limit = 2.2f;

        var noise = particleSystem.noise;
        noise.enabled = true;
        noise.strengthX = 0.07f;
        noise.strengthY = 0.16f;
        noise.strengthZ = 0.07f;
        noise.frequency = 0.38f;
        noise.scrollSpeed = 0.34f;
        noise.octaveCount = 1;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        var color = particleSystem.colorOverLifetime;
        color.enabled = true;
        color.color = BuildFlameGradient();

        var size = particleSystem.sizeOverLifetime;
        size.enabled = true;
        size.separateAxes = false;
        size.size = new ParticleSystem.MinMaxCurve(1f, BuildCurve(
            new Keyframe(0f, 0.22f),
            new Keyframe(0.18f, 0.82f),
            new Keyframe(0.55f, 0.42f),
            new Keyframe(1f, 0.06f)));

        var renderer = GetOrAddRenderer(particleSystem);
        renderer.sharedMaterial = GetBuiltinParticleMaterialOrThrow();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.lengthScale = 0.8f;
        renderer.velocityScale = 0.28f;
        renderer.cameraVelocityScale = 0f;
        renderer.minParticleSize = 0.0001f;
        renderer.maxParticleSize = 0.38f;
    }

    private void ConfigureTongues(ParticleSystem particleSystem)
    {
        ConfigureSharedDefaults(particleSystem, loop: true, duration: 1.4f, maxParticles: 36);

        var main = particleSystem.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.05f, 2.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.startRotation = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
        main.startRotation3D = false;

        var emission = particleSystem.emission;
        emission.rateOverTime = 20f;

        var shape = particleSystem.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 4f;
        shape.radius = FlameWidth * 0.07f;

        var velocity = particleSystem.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
        velocity.y = new ParticleSystem.MinMaxCurve(1.8f, 3.1f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);

        var noise = particleSystem.noise;
        noise.enabled = true;
        noise.strengthX = 0.16f;
        noise.strengthY = 0.08f;
        noise.strengthZ = 0.16f;
        noise.frequency = 0.45f;
        noise.scrollSpeed = 0.28f;

        var color = particleSystem.colorOverLifetime;
        color.enabled = true;
        color.color = BuildTongueGradient();

        var size = particleSystem.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, BuildCurve(
            new Keyframe(0f, 0.18f),
            new Keyframe(0.14f, 0.72f),
            new Keyframe(0.5f, 1f),
            new Keyframe(1f, 0.12f)));

        var trails = particleSystem.trails;
        trails.enabled = true;
        trails.mode = ParticleSystemTrailMode.PerParticle;
        trails.ratio = 1f;
        trails.lifetime = 0.16f;
        trails.dieWithParticles = true;
        trails.sizeAffectsWidth = true;
        trails.sizeAffectsLifetime = true;
        trails.inheritParticleColor = true;
        trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, BuildCurve(
            new Keyframe(0f, 0.22f),
            new Keyframe(1f, 0f)));
        trails.colorOverTrail = BuildTongueGradient();

        var renderer = GetOrAddRenderer(particleSystem);
        renderer.sharedMaterial = GetBuiltinParticleMaterialOrThrow();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 1.35f;
        renderer.velocityScale = 0.62f;
        renderer.cameraVelocityScale = 0f;
        renderer.normalDirection = 0f;
    }

    private void ConfigureEmbers(ParticleSystem particleSystem)
    {
        ConfigureSharedDefaults(particleSystem, loop: true, duration: 1.8f, maxParticles: 24);

        var main = particleSystem.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.07f);
        main.gravityModifier = -0.02f;

        var emission = particleSystem.emission;
        emission.rateOverTime = 6f;

        var shape = particleSystem.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = FlameWidth * 0.14f;

        var velocity = particleSystem.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.22f, 0.22f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.22f, 0.22f);

        var noise = particleSystem.noise;
        noise.enabled = true;
        noise.strengthX = 0.12f;
        noise.strengthY = 0.12f;
        noise.strengthZ = 0.12f;
        noise.frequency = 0.75f;

        var color = particleSystem.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(BuildGradient(
            new[]
            {
                new GradientColorKey(new Color(8f, 3f, 0.65f), 0f),
                new GradientColorKey(new Color(5f, 1.4f, 0.25f), 0.45f),
                new GradientColorKey(new Color(0.7f, 0.18f, 0.05f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.95f, 0.18f),
                new GradientAlphaKey(0.5f, 0.72f),
                new GradientAlphaKey(0f, 1f)
            }));

        var renderer = GetOrAddRenderer(particleSystem);
        renderer.sharedMaterial = GetBuiltinParticleMaterialOrThrow();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
    }

    private void ConfigureSmoke(ParticleSystem particleSystem)
    {
        ConfigureSharedDefaults(particleSystem, loop: true, duration: 2.2f, maxParticles: 18);

        var main = particleSystem.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.45f);

        var emission = particleSystem.emission;
        emission.rateOverTime = 3f;

        var shape = particleSystem.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 9f;
        shape.radius = FlameWidth * 0.12f;

        var velocity = particleSystem.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.5f, 0.95f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);

        var noise = particleSystem.noise;
        noise.enabled = true;
        noise.strengthX = 0.15f;
        noise.strengthY = 0.1f;
        noise.strengthZ = 0.15f;
        noise.frequency = 0.42f;
        noise.scrollSpeed = 0.25f;

        var color = particleSystem.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(BuildGradient(
            new[]
            {
                new GradientColorKey(new Color(0.11f, 0.1f, 0.1f), 0f),
                new GradientColorKey(new Color(0.19f, 0.18f, 0.18f), 0.55f),
                new GradientColorKey(new Color(0.26f, 0.25f, 0.25f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.18f, 0.12f),
                new GradientAlphaKey(0.09f, 0.65f),
                new GradientAlphaKey(0f, 1f)
            }));

        var size = particleSystem.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, BuildCurve(
            new Keyframe(0f, 0.35f),
            new Keyframe(1f, 1.65f)));

        var renderer = GetOrAddRenderer(particleSystem);
        renderer.sharedMaterial = GetBuiltinParticleMaterialOrThrow();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
    }

    private void ConfigureLight(Transform lightTransform)
    {
        lightTransform.localPosition = new Vector3(0f, 0.54f, 0f);
        lightTransform.localRotation = Quaternion.identity;
        lightTransform.localScale = Vector3.one;

        _fireLight = lightTransform.GetComponent<Light>();
        if (_fireLight == null)
        {
            _fireLight = lightTransform.gameObject.AddComponent<Light>();
        }

        _fireLight.type = LightType.Point;
        _fireLight.color = new Color(1f, 0.56f, 0.22f, 1f);
        _fireLight.intensity = LightIntensity;
        _fireLight.range = LightRange;
        _fireLight.shadows = LightShadows.Soft;
        _fireLight.renderMode = LightRenderMode.Auto;
    }

    private static void ConfigureSharedDefaults(ParticleSystem particleSystem, bool loop, float duration, int maxParticles)
    {
        particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particleSystem.main;
        main.loop = loop;
        main.duration = duration;
        main.maxParticles = maxParticles;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Local;

        var emission = particleSystem.emission;
        emission.enabled = true;

        var renderer = GetOrAddRenderer(particleSystem);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        particleSystem.Play();
    }

    private static ParticleSystem GetOrAddParticleSystem(Transform target)
    {
        var particleSystem = target.GetComponent<ParticleSystem>();
        if (particleSystem == null)
        {
            particleSystem = target.gameObject.AddComponent<ParticleSystem>();
        }

        GetOrAddRenderer(particleSystem);
        return particleSystem;
    }

    private static ParticleSystemRenderer GetOrAddRenderer(ParticleSystem particleSystem)
    {
        var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
        if (renderer == null)
        {
            renderer = particleSystem.gameObject.AddComponent<ParticleSystemRenderer>();
        }

        return renderer;
    }

    private Transform GetOrCreateChild(string childName)
    {
        var child = transform.Find(childName);
        if (child != null)
        {
            return child;
        }

        var go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    private static Material GetBuiltinParticleMaterialOrThrow()
    {
        if (s_builtinParticleMaterial != null)
        {
            return s_builtinParticleMaterial;
        }

        s_builtinParticleMaterial = LoadBuiltinParticleMaterial();
        if (s_builtinParticleMaterial != null)
        {
            ApplyBuiltinParticleTextureOrThrow(s_builtinParticleMaterial);
            return s_builtinParticleMaterial;
        }

        throw new System.InvalidOperationException(
            "Built-in Unity particle material for URP was not found. DefaultFlame01Override does not create custom particle materials.");
    }

    private static void ApplyBuiltinParticleTextureOrThrow(Material material)
    {
        if (material == null)
        {
            return;
        }

        var texture = GetBuiltinParticleTextureOrThrow();

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
        }
        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", texture);
        }
    }

    private static Texture2D GetBuiltinParticleTextureOrThrow()
    {
        if (s_builtinParticleTexture != null)
        {
            return s_builtinParticleTexture;
        }

        s_builtinParticleTexture = LoadBuiltinParticleTexture();
        if (s_builtinParticleTexture != null)
        {
            return s_builtinParticleTexture;
        }

        throw new System.InvalidOperationException(
            "Built-in Unity texture 'Default-Particle.psd' was not found. DefaultFlame01Override does not use fallback textures.");
    }

    private static Texture2D LoadBuiltinParticleTexture()
    {
#if UNITY_EDITOR
        var editorTexture = UnityEditor.AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
        if (editorTexture != null)
        {
            return editorTexture;
        }
#endif
        return Resources.GetBuiltinResource<Texture2D>("Default-Particle.psd");
    }

    private static Material LoadBuiltinParticleMaterial()
    {
#if UNITY_EDITOR
        var editorMaterial =
            UnityEditor.AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat") ??
            UnityEditor.AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
        if (editorMaterial != null)
        {
            return editorMaterial;
        }
#endif

        return Resources.GetBuiltinResource<Material>("Default-ParticleSystem.mat") ??
               Resources.GetBuiltinResource<Material>("Default-Particle.mat");
    }

    private static ParticleSystem.MinMaxGradient BuildFlameGradient()
    {
        return new ParticleSystem.MinMaxGradient(BuildGradient(
            new GradientColorKey(new Color(9.2f, 6.1f, 2.5f), 0f),
            new GradientColorKey(new Color(8f, 2.4f, 0.36f), 0.24f),
            new GradientColorKey(new Color(1.8f, 0.22f, 0.03f), 0.68f),
            new GradientColorKey(new Color(0.18f, 0.02f, 0.01f), 1f),
            new GradientAlphaKey(0f, 0f),
            new GradientAlphaKey(0.98f, 0.06f),
            new GradientAlphaKey(0.55f, 0.54f),
            new GradientAlphaKey(0f, 1f)));
    }

    private static ParticleSystem.MinMaxGradient BuildTongueGradient()
    {
        return new ParticleSystem.MinMaxGradient(BuildGradient(
            new GradientColorKey(new Color(8.4f, 4.4f, 1.7f), 0f),
            new GradientColorKey(new Color(5.6f, 1.15f, 0.14f), 0.35f),
            new GradientColorKey(new Color(0.95f, 0.09f, 0.02f), 1f),
            new GradientAlphaKey(0f, 0f),
            new GradientAlphaKey(0.92f, 0.12f),
            new GradientAlphaKey(0.08f, 1f)));
    }

    private static Gradient BuildGradient(
        GradientColorKey color0,
        GradientColorKey color1,
        GradientColorKey color2,
        GradientColorKey color3,
        GradientAlphaKey alpha0,
        GradientAlphaKey alpha1,
        GradientAlphaKey alpha2,
        GradientAlphaKey alpha3)
    {
        return BuildGradient(
            new[] { color0, color1, color2, color3 },
            new[] { alpha0, alpha1, alpha2, alpha3 });
    }

    private static Gradient BuildGradient(
        GradientColorKey color0,
        GradientColorKey color1,
        GradientColorKey color2,
        GradientAlphaKey alpha0,
        GradientAlphaKey alpha1,
        GradientAlphaKey alpha2)
    {
        return BuildGradient(
            new[] { color0, color1, color2 },
            new[] { alpha0, alpha1, alpha2 });
    }

    private static Gradient BuildGradient(GradientColorKey[] colors, GradientAlphaKey[] alphas)
    {
        var gradient = new Gradient();
        gradient.SetKeys(colors, alphas);
        return gradient;
    }

    private static AnimationCurve BuildCurve(params Keyframe[] keys)
    {
        return new AnimationCurve(keys);
    }
}
