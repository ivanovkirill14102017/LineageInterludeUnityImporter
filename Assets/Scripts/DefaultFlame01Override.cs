using UnityEngine;

[System.Serializable]
public enum DefaultFlame01Preset
{
    Torch,
    Campfire
}

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class DefaultFlame01Override : MonoBehaviour
{
    private const string CoreName = "CoreFlame";
    private const string TonguesName = "FlameTongues";
    private const string EmbersName = "Embers";
    private const string SmokeName = "Smoke";
    private const string LightName = "FireLight";

    [Header("Preset")]
    public DefaultFlame01Preset Preset = DefaultFlame01Preset.Torch;
    public bool CastShadows = true;

    private static Material s_builtinParticleMaterial;
    private static Texture2D s_builtinParticleTexture;
    private Light _fireLight;
    private FlamePresetSettings _settings;

    private void OnEnable()
    {
        EnsureEffect();
        ApplyPreset();
    }

    private void OnValidate()
    {
        EnsureEffect();
        ApplyPreset();
    }

    private void Update()
    {
        if (_fireLight == null)
        {
            CacheLight();
        }

        if (_fireLight == null)
        {
            return;
        }

        var flicker = 1f;
        if (_settings.FlickerAmplitude > 0f)
        {
            var time = GetEffectTime();
            flicker += Mathf.Sin(time * _settings.FlickerSpeed) * _settings.FlickerAmplitude * 0.35f;
            flicker += Mathf.Sin(time * (_settings.FlickerSpeed * 1.73f)) * _settings.FlickerAmplitude * 0.18f;
        }

        _fireLight.intensity = Mathf.Max(0f, _settings.LightIntensity * flicker);
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
        ApplyPreset();
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
        _settings = BuildPresetSettings(Preset);

        var core = GetOrCreateChild(CoreName);
        var tongues = GetOrCreateChild(TonguesName);
        var embers = GetOrCreateChild(EmbersName);
        var smoke = GetOrCreateChild(SmokeName);
        var lightNode = GetOrCreateChild(LightName);

        ConfigureEffectNodeTransform(core, Vector3.zero);
        ConfigureEffectNodeTransform(tongues, Vector3.zero);
        ConfigureEffectNodeTransform(embers, new Vector3(0f, _settings.FlameHeight * 0.08f, 0f));
        ConfigureEffectNodeTransform(smoke, new Vector3(0f, _settings.FlameHeight * 0.22f, 0f));

        ConfigureCoreFlame(GetOrAddParticleSystem(core));
        ConfigureTongues(GetOrAddParticleSystem(tongues));
        ConfigureEmbers(GetOrAddParticleSystem(embers));
        ConfigureSmoke(GetOrAddParticleSystem(smoke));
        ConfigureLight(lightNode);
    }

    private void ConfigureCoreFlame(ParticleSystem particleSystem)
    {
        var sizeMultiplier = _settings.ParticleSizeMultiplier;
        var speedMultiplier = _settings.ParticleSpeedMultiplier;
        var rateMultiplier = _settings.EmissionMultiplier;
        ConfigureSharedDefaults(particleSystem, loop: true, duration: 1.2f, maxParticles: 80);

        var main = particleSystem.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.55f * speedMultiplier, 1.1f * speedMultiplier);
        main.startSize3D = false;
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f * sizeMultiplier, 0.18f * sizeMultiplier);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = particleSystem.emission;
        emission.rateOverTime = 42f * rateMultiplier;

        var shape = particleSystem.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 5f;
        shape.radius = _settings.FlameWidth * 0.1f;
        shape.radiusThickness = 1f;
        shape.length = Mathf.Max(0.01f, _settings.FlameHeight * 0.06f);
        shape.position = Vector3.zero;
        shape.scale = new Vector3(0.65f * sizeMultiplier, _settings.FlameHeight * 1.15f, 0.65f * sizeMultiplier);

        var velocity = particleSystem.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.05f * speedMultiplier, 0.05f * speedMultiplier);
        velocity.y = new ParticleSystem.MinMaxCurve(1.15f * speedMultiplier, 2.05f * speedMultiplier);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.05f * speedMultiplier, 0.05f * speedMultiplier);

        var limit = particleSystem.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.dampen = 0.2f;
        limit.limit = 2.2f * speedMultiplier;

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
        renderer.lengthScale = 0.8f * sizeMultiplier;
        renderer.velocityScale = 0.28f * speedMultiplier;
        renderer.cameraVelocityScale = 0f;
        renderer.minParticleSize = 0.0001f;
        renderer.maxParticleSize = 0.38f * sizeMultiplier;
    }

    private void ConfigureTongues(ParticleSystem particleSystem)
    {
        var sizeMultiplier = _settings.ParticleSizeMultiplier;
        var speedMultiplier = _settings.ParticleSpeedMultiplier;
        var rateMultiplier = _settings.EmissionMultiplier;
        ConfigureSharedDefaults(particleSystem, loop: true, duration: 1.4f, maxParticles: 36);

        var main = particleSystem.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.05f * speedMultiplier, 2.2f * speedMultiplier);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f * sizeMultiplier, 0.14f * sizeMultiplier);
        main.startRotation = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
        main.startRotation3D = false;

        var emission = particleSystem.emission;
        emission.rateOverTime = 20f * rateMultiplier;

        var shape = particleSystem.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 4f;
        shape.radius = Mathf.Max(0.01f, _settings.FlameWidth * 0.03f);
        shape.radiusThickness = 1f;
        shape.length = Mathf.Max(0.01f, _settings.FlameHeight * 0.03f);
        shape.position = new Vector3(0f, _settings.FlameHeight * 0.02f, 0f);
        shape.rotation = Vector3.zero;
        shape.scale = new Vector3(
            Mathf.Max(0.12f, _settings.FlameWidth * 0.32f),
            _settings.FlameHeight * 0.28f,
            Mathf.Max(0.12f, _settings.FlameWidth * 0.32f));

        var velocity = particleSystem.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.08f * speedMultiplier, 0.08f * speedMultiplier);
        velocity.y = new ParticleSystem.MinMaxCurve(1.8f * speedMultiplier, 3.1f * speedMultiplier);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.08f * speedMultiplier, 0.08f * speedMultiplier);

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
        trails.ratio = 0.75f;
        trails.lifetime = 0.1f;
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
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.lengthScale = 0.95f;
        renderer.velocityScale = 0.24f * speedMultiplier;
        renderer.cameraVelocityScale = 0f;
        renderer.normalDirection = 0f;
        renderer.minParticleSize = 0.0001f;
        renderer.maxParticleSize = 0.3f * Mathf.Max(1f, sizeMultiplier * 0.7f);
    }

    private void ConfigureEmbers(ParticleSystem particleSystem)
    {
        var sizeMultiplier = _settings.ParticleSizeMultiplier;
        var speedMultiplier = _settings.ParticleSpeedMultiplier;
        var rateMultiplier = _settings.EmissionMultiplier;
        ConfigureSharedDefaults(particleSystem, loop: true, duration: 1.8f, maxParticles: 24);

        var main = particleSystem.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f * speedMultiplier, 0.9f * speedMultiplier);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f * sizeMultiplier, 0.07f * sizeMultiplier);
        main.gravityModifier = -0.02f;

        var emission = particleSystem.emission;
        emission.rateOverTime = 6f * rateMultiplier;

        var shape = particleSystem.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = _settings.FlameWidth * 0.14f;

        var velocity = particleSystem.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.22f * speedMultiplier, 0.22f * speedMultiplier);
        velocity.y = new ParticleSystem.MinMaxCurve(0.9f * speedMultiplier, 1.8f * speedMultiplier);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.22f * speedMultiplier, 0.22f * speedMultiplier);

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
        var sizeMultiplier = _settings.ParticleSizeMultiplier;
        var speedMultiplier = _settings.ParticleSpeedMultiplier;
        var rateMultiplier = _settings.EmissionMultiplier;
        ConfigureSharedDefaults(particleSystem, loop: true, duration: 2.2f, maxParticles: 18);

        var main = particleSystem.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f * speedMultiplier, 0.45f * speedMultiplier);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f * sizeMultiplier, 0.45f * sizeMultiplier);

        var emission = particleSystem.emission;
        emission.rateOverTime = 3f * rateMultiplier;

        var shape = particleSystem.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 9f;
        shape.radius = _settings.FlameWidth * 0.12f;
        shape.radiusThickness = 1f;
        shape.length = Mathf.Max(0.02f, _settings.FlameHeight * 0.12f);

        var velocity = particleSystem.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.08f * speedMultiplier, 0.08f * speedMultiplier);
        velocity.y = new ParticleSystem.MinMaxCurve(0.5f * speedMultiplier, 0.95f * speedMultiplier);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.08f * speedMultiplier, 0.08f * speedMultiplier);

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
            new Keyframe(0f, 0.35f * sizeMultiplier),
            new Keyframe(1f, 1.65f * sizeMultiplier)));

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
        ApplyStaticLightProperties();
        _fireLight.renderMode = LightRenderMode.Auto;
    }

    private void ApplyPreset()
    {
        _settings = BuildPresetSettings(Preset);
        CacheLight();
        ApplyStaticLightProperties();
    }

    private void ApplyStaticLightProperties()
    {
        if (_fireLight == null)
        {
            return;
        }

        _fireLight.color = new Color(1f, 0.56f, 0.22f, 1f);
        _fireLight.range = _settings.LightRange;
        _fireLight.intensity = _settings.LightIntensity;
        _fireLight.shadows = CastShadows ? LightShadows.Soft : LightShadows.None;
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

    private static void ConfigureEffectNodeTransform(Transform target, Vector3 localPosition)
    {
        if (target == null)
        {
            return;
        }

        target.localPosition = localPosition;
        target.localRotation = Quaternion.identity;
        target.localScale = Vector3.one;
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

    private static FlamePresetSettings BuildPresetSettings(DefaultFlame01Preset preset)
    {
        switch (preset)
        {
            case DefaultFlame01Preset.Campfire:
                return new FlamePresetSettings(
                    flameHeight: 4.2f,
                    flameWidth: 1.24f,
                    lightIntensity: 25.6f,
                    lightRange: 23.2f,
                    flickerAmplitude: 0.52f,
                    flickerSpeed: 5.6f,
                    emissionMultiplier: 3.7f,
                    particleSizeMultiplier: 5.2f,
                    particleSpeedMultiplier: 1.55f);
            default:
                return new FlamePresetSettings(
                    flameHeight: 2.4f,
                    flameWidth: 0.56f,
                    lightIntensity: 11.2f,
                    lightRange: 10.4f,
                    flickerAmplitude: 0.38f,
                    flickerSpeed: 8.2f,
                    emissionMultiplier: 1.64f,
                    particleSizeMultiplier: 3.2f,
                    particleSpeedMultiplier: 1.25f);
        }
    }

    private readonly struct FlamePresetSettings
    {
        public FlamePresetSettings(
            float flameHeight,
            float flameWidth,
            float lightIntensity,
            float lightRange,
            float flickerAmplitude,
            float flickerSpeed,
            float emissionMultiplier,
            float particleSizeMultiplier,
            float particleSpeedMultiplier)
        {
            FlameHeight = flameHeight;
            FlameWidth = flameWidth;
            LightIntensity = lightIntensity;
            LightRange = lightRange;
            FlickerAmplitude = flickerAmplitude;
            FlickerSpeed = flickerSpeed;
            EmissionMultiplier = emissionMultiplier;
            ParticleSizeMultiplier = particleSizeMultiplier;
            ParticleSpeedMultiplier = particleSpeedMultiplier;
        }

        public float FlameHeight { get; }
        public float FlameWidth { get; }
        public float LightIntensity { get; }
        public float LightRange { get; }
        public float FlickerAmplitude { get; }
        public float FlickerSpeed { get; }
        public float EmissionMultiplier { get; }
        public float ParticleSizeMultiplier { get; }
        public float ParticleSpeedMultiplier { get; }
    }
}
