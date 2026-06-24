#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using L2Viewer.UnrFile;
using UnityEditor;
using UnityEngine;
using NumericsVector3 = System.Numerics.Vector3;

internal static class L2ParticleAssetBuilder
{
    private const float UnrealToUnityScale = L2WorldScale.BakeUnrealToUnityScale;
    private const float NeutralParticleStartSize = 1f;

    public static void BuildParticles(
        SceneParticleEmitterData[] emitters,
        string clientPath,
        string outputDir,
        GameObject parent,
        Action<string> log)
    {
        if (emitters == null || emitters.Length == 0)
        {
            log("[Particles] No emitter roots were supplied.");
            return;
        }

        var textureManager = new BspTextureManager(clientPath);
        var resolvedTextures = ResolveTextures(emitters, textureManager);
        var textureAssets = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        var materialAssets = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

        var missingTextureReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var emitter in emitters.OrderBy(x => x.ExportIndex))
        {
            var emitterRoot = new GameObject(emitter.StableName);
            emitterRoot.transform.SetParent(parent.transform, false);
            ApplyEmitterTransform(emitterRoot.transform, emitter);

            foreach (var layer in emitter.Layers.OrderBy(x => x.ExportIndex))
            {
               BuildSpriteLayer(layer, outputDir, resolvedTextures, textureAssets, materialAssets, emitterRoot.transform, missingTextureReferences);               
            }

            foreach (var layer in emitter.MeshLayers.OrderBy(x => x.ExportIndex))
            {
                BuildMeshLayer(layer, outputDir, resolvedTextures, textureAssets, materialAssets, emitterRoot.transform);
            }

            foreach (var layer in emitter.VertMeshLayers.OrderBy(x => x.ExportIndex))
            {
                BuildVertMeshLayer(layer, outputDir, resolvedTextures, textureAssets, materialAssets, emitterRoot.transform);
            }

            foreach (var layer in emitter.BeamLayers.OrderBy(x => x.ExportIndex))
            {
                BuildBeamLayer(layer, outputDir, resolvedTextures, textureAssets, materialAssets, emitterRoot.transform, missingTextureReferences);

                if (layer.UnknownProperties.Any(x => x.Name.Equals("BeamEndPoints", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new Exception("BeamEndPoints should be, return SceneDomain to complete parsing!");
                }
            }
        }
  }

    private static bool BuildSpriteLayer(
        SceneSpriteEmitterLayerData layer,
        string outputDir,
        IReadOnlyDictionary<string, BspTextureManager.ResolvedTexture> resolvedTextures,
        IDictionary<string, Texture2D> textureAssets,
        IDictionary<string, Material> materialAssets,
        Transform parent,
        ISet<string> missingTextureReferences)
    {
        var particleSystem = CreateParticleSystemObject(layer.StableName, parent, ParticleSystemRenderMode.Billboard);
        ConfigureCommonParticleSystem(
            particleSystem,
            layer.MaxParticles,
            layer.LifetimeRange,
            layer.Opacity,
            layer.ColorScale,
            fadeInEndTime: null,
            layer.FadeOut ? layer.FadeOutStartTime : null,
            layer.StartVelocityRange,
            layer.Acceleration,
            layer.StartSizeRange,
            layer.StartSpinRange,
            layer.SpinParticles,
            layer.SpinsPerSecondRange,
            startLocationRange: null,
            sphereRadiusRange: null,
            particleMaterial: ResolveParticleMaterial(layer.TextureReference, outputDir, resolvedTextures, textureAssets, materialAssets, missingTextureReferences),
            sizeScale: layer.UseSizeScale ? layer.SizeScale : Array.Empty<UnrParticleSizeScale>());
        return true;
    }

    private static bool BuildMeshLayer(
        SceneMeshEmitterLayerData layer,
        string outputDir,
        IReadOnlyDictionary<string, BspTextureManager.ResolvedTexture> resolvedTextures,
        IDictionary<string, Texture2D> textureAssets,
        IDictionary<string, Material> materialAssets,
        Transform parent)
    {
        var particleSystem = CreateParticleSystemObject(layer.StableName, parent, ParticleSystemRenderMode.Billboard);
        ConfigureCommonParticleSystem(
            particleSystem,
            layer.MaxParticles,
            layer.LifetimeRange,
            layer.Opacity,
            layer.ColorScale,
            layer.FadeIn ? layer.FadeInEndTime : null,
            layer.FadeOut ? layer.FadeOutStartTime : null,
            layer.StartVelocityRange,
            acceleration: null,
            layer.StartSizeRange,
            startSpinRange: null,
            layer.SpinParticles,
            layer.SpinsPerSecondRange,
            startLocationRange: null,
            sphereRadiusRange: null,
            particleMaterial: ResolveParticleMaterial(null, outputDir, resolvedTextures, textureAssets, materialAssets));
        return true;
    }

    private static bool BuildVertMeshLayer(
        SceneVertMeshEmitterLayerData layer,
        string outputDir,
        IReadOnlyDictionary<string, BspTextureManager.ResolvedTexture> resolvedTextures,
        IDictionary<string, Texture2D> textureAssets,
        IDictionary<string, Material> materialAssets,
        Transform parent)
    {
        var particleSystem = CreateParticleSystemObject( layer.StableName, parent, ParticleSystemRenderMode.Billboard);
        ConfigureCommonParticleSystem(
            particleSystem,
            layer.MaxParticles,
            layer.LifetimeRange,
            layer.Opacity,
            layer.UseColorScale ? layer.ColorScale : Array.Empty<UnrParticleColorScale>(),
            layer.FadeIn ? layer.FadeInEndTime : null,
            layer.FadeOut ? layer.FadeOutStartTime : null,
            layer.StartVelocityRange,
            layer.Acceleration,
            layer.StartSizeRange,
            layer.StartSpinRange,
            layer.SpinParticles,
            layer.RevolutionsPerSecondRange,
            layer.StartLocationRange,
            sphereRadiusRange: null,
            particleMaterial: ResolveParticleMaterial(null, outputDir, resolvedTextures, textureAssets, materialAssets));
        return true;
    }

    private static bool BuildBeamLayer(
        SceneBeamEmitterLayerData layer,
        string outputDir,
        IReadOnlyDictionary<string, BspTextureManager.ResolvedTexture> resolvedTextures,
        IDictionary<string, Texture2D> textureAssets,
        IDictionary<string, Material> materialAssets,
        Transform parent,
        ISet<string> missingTextureReferences)
    {
        var particleSystem = CreateParticleSystemObject(layer.StableName, parent, ParticleSystemRenderMode.Stretch);
        ConfigureCommonParticleSystem(
            particleSystem,
            layer.MaxParticles,
            layer.LifetimeRange,
            layer.Opacity,
            layer.ColorScale,
            layer.FadeIn ? layer.FadeInEndTime : null,
            layer.FadeOut ? layer.FadeOutStartTime : null,
            startVelocityRange: null,
            acceleration: null,
            layer.StartSizeRange,
            startSpinRange: null,
            spinParticles: false,
            spinsPerSecondRange: null,
            layer.StartLocationRange,
            layer.SphereRadiusRange,
            particleMaterial: ResolveParticleMaterial(layer.TextureReference, outputDir, resolvedTextures, textureAssets, materialAssets, missingTextureReferences));
        return true;
    }

    private static void ConfigureCommonParticleSystem(
        ParticleSystem particleSystem,
        int? maxParticles,
        UnrFloatRange? lifetimeRange,
        float? opacity,
        UnrParticleColorScale[] colorScale,
        float? fadeInEndTime,
        float? fadeOutStartTime,
        UnrRangeVector? startVelocityRange,
        NumericsVector3? acceleration,
        UnrRangeVector? startSizeRange,
        UnrRangeVector? startSpinRange,
        bool spinParticles,
        UnrRangeVector? spinsPerSecondRange,
        UnrRangeVector? startLocationRange,
        UnrFloatRange? sphereRadiusRange,
        Material particleMaterial,
        UnrParticleSizeScale[]? sizeScale = null)
    {
        var lifetimeMin = lifetimeRange?.Min ?? 1f;
        var lifetimeMax = Math.Max(lifetimeMin, lifetimeRange?.Max ?? lifetimeMin);
        var main = particleSystem.main;
        main.loop = true;
        main.playOnAwake = true;
        main.duration = Math.Max(0.1f, lifetimeMax);
        main.maxParticles = Math.Max(1, maxParticles ?? 32);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(Math.Max(0.01f, lifetimeMin), Math.Max(0.01f, lifetimeMax));
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0f);
        main.startSize3D = false;
        main.startSize = BuildSizeCurve(startSizeRange, NeutralParticleStartSize, NeutralParticleStartSize);
        main.startRotation = BuildRotationCurve(startSpinRange);
        main.startColor = BuildStartColor(opacity, colorScale, applyOpacity: !ShouldDriveAlphaOverLifetime(colorScale, fadeInEndTime, fadeOutStartTime));

        var emission = particleSystem.emission;
        emission.enabled = true;
        emission.rateOverTime = Math.Max(1f, main.maxParticles / Math.Max(0.1f, (lifetimeMin + lifetimeMax) * 0.5f));

        var shape = particleSystem.shape;
        ConfigureShape(shape, startLocationRange, sphereRadiusRange);

        var velocity = particleSystem.velocityOverLifetime;
        ConfigureVelocityOverLifetime(velocity, startVelocityRange);

        var force = particleSystem.forceOverLifetime;
        ConfigureForceOverLifetime(force, acceleration);

        var colorOverLifetime = particleSystem.colorOverLifetime;
        ConfigureColorOverLifetime(colorOverLifetime, opacity, colorScale, fadeInEndTime, fadeOutStartTime, lifetimeMax);

        var sizeOverLifetime = particleSystem.sizeOverLifetime;
        ConfigureSizeOverLifetime(sizeOverLifetime, sizeScale);

        var rotationOverLifetime = particleSystem.rotationOverLifetime;
        ConfigureRotationOverLifetime(rotationOverLifetime, spinParticles, spinsPerSecondRange);

        var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = particleMaterial;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.minParticleSize = 0.0001f;
        renderer.maxParticleSize = 0.5f;
    }

    private static ParticleSystem CreateParticleSystemObject(string name, Transform parent, ParticleSystemRenderMode renderMode)
    {
        var gameObject = new GameObject(name);
        gameObject.transform.SetParent(parent, false);
        var particleSystem = gameObject.AddComponent<ParticleSystem>();
        var renderer = gameObject.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = renderMode;
        return particleSystem;
    }

    private static void ApplyEmitterTransform(Transform target, SceneParticleEmitterData emitter)
    {
        target.localPosition = ConvertPosition(emitter.WorldLocation ?? NumericsVector3.Zero);
        if (emitter.WorldRotationEulerDegrees is NumericsVector3 rotationDegrees)
        {
            target.localRotation = ConvertEulerAngles(rotationDegrees);
        }

        var scale = emitter.DrawScale3D;
        target.localScale = new Vector3(
            Math.Max(0.0001f, Math.Abs(scale.X * emitter.DrawScale)),
            Math.Max(0.0001f, Math.Abs(scale.Z * emitter.DrawScale)),
            Math.Max(0.0001f, Math.Abs(scale.Y * emitter.DrawScale)));
    }

    private static IReadOnlyDictionary<string, BspTextureManager.ResolvedTexture> ResolveTextures(
        IEnumerable<SceneParticleEmitterData> emitters,
        BspTextureManager textureManager)
    {
        var requests = emitters
            .SelectMany(x => x.Layers.Select(layer => layer.TextureReference)
                .Concat(x.BeamLayers.Select(layer => layer.TextureReference)))
            .Select(ParseTextureRequest)
            .Where(x => x is not null)
            .Cast<SceneTextureRequest>()
            .Distinct()
            .ToArray();

        return textureManager.ResolveMany(requests);
    }

    private static Material ResolveParticleMaterial(
        string? textureReference,
        string outputDir,
        IReadOnlyDictionary<string, BspTextureManager.ResolvedTexture> resolvedTextures,
        IDictionary<string, Texture2D> textureAssets,
        IDictionary<string, Material> materialAssets,
        ISet<string>? missingTextureReferences = null)
    {
        var materialKey = string.IsNullOrWhiteSpace(textureReference) ? "__default__" : textureReference;
        if (materialAssets.TryGetValue(materialKey, out var cached))
        {
            return cached;
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(BuildParticleMaterialPath(outputDir, textureReference));
        if (material == null)
        {
            var shader = ResolveParticleShader();
            material = new Material(shader);
            L2MaterialUtility.ConfigureTransparent(
                material,
                UnityEngine.Rendering.BlendMode.SrcAlpha,
                UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha,
                premultiplyKeyword: false);
            material.enableInstancing = true;
        }

        var texture = ResolveParticleTexture(textureReference, outputDir, resolvedTextures, textureAssets, missingTextureReferences);
        if (texture != null)
        {
            L2MaterialUtility.AssignMainTexture(material, texture);
        }

        L2MaterialUtility.SetBaseColor(material, Color.white);
        material = UnityAssetDatabaseUtility.CreateOrReplaceAsset(material, BuildParticleMaterialPath(outputDir, textureReference));
        materialAssets[materialKey] = material;
        return material;
    }

    private static Texture2D? ResolveParticleTexture(
        string? textureReference,
        string outputDir,
        IReadOnlyDictionary<string, BspTextureManager.ResolvedTexture> resolvedTextures,
        IDictionary<string, Texture2D> textureAssets,
        ISet<string>? missingTextureReferences)
    {
        if (string.IsNullOrWhiteSpace(textureReference))
        {
            return null;
        }

        if (textureAssets.TryGetValue(textureReference, out var cached))
        {
            return cached;
        }

        if (!resolvedTextures.TryGetValue(textureReference, out var resolved) || resolved.Texture == null)
        {
            missingTextureReferences?.Add(textureReference);
            return null;
        }

        var texturePath = BuildParticleTexturePath(outputDir, textureReference);
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null)
        {
            texture = L2AssetManager.CreateTextureAsset(resolved.Texture, texturePath, false, true);
        }

        textureAssets[textureReference] = texture;
        return texture;
    }

    private static string BuildParticleTexturePath(string outputDir, string textureReference)
    {
        return L2AssetManager.BuildClientPackageAssetPath(
            $"{outputDir}/Particles/Textures",
            textureReference,
            "TEX",
            "png",
            "Particles/Textures");
    }

    private static string BuildParticleMaterialPath(string outputDir, string? textureReference)
    {
        return L2AssetManager.BuildClientPackageAssetPath(
            $"{outputDir}/Particles/Materials",
            textureReference ?? "DefaultParticle",
            "MAT",
            "mat",
            "Particles/Materials");
    }

    private static Shader ResolveParticleShader()
    {
        return Shader.Find("Universal Render Pipeline/Particles/Unlit")
               ?? Shader.Find("Particles/Standard Unlit")
               ?? L2MaterialUtility.FindBestUnlitShader()
               ?? throw new InvalidOperationException("Compatible particle shader was not found.");
    }

    private static SceneTextureRequest? ParseTextureRequest(string? referenceText)
    {
        if (string.IsNullOrWhiteSpace(referenceText))
        {
            return null;
        }

        var split = referenceText.Split('.', 2, StringSplitOptions.RemoveEmptyEntries);
        return split.Length == 2
            ? new SceneTextureRequest(split[0], split[1])
            : null;
    }

    private static ParticleSystem.MinMaxCurve BuildSizeCurve(UnrRangeVector? range, float fallbackMin, float fallbackMax)
    {
        if (range == null)
        {
            return new ParticleSystem.MinMaxCurve(fallbackMin, fallbackMax);
        }

        return new ParticleSystem.MinMaxCurve(
            Math.Max(0.001f, ComputeScalarRangeValue(range, useMax: false)),//* UnrealToUnityScale),
            Math.Max(0.001f, ComputeScalarRangeValue(range, useMax: true)));// * UnrealToUnityScale));
    }

    private static ParticleSystem.MinMaxCurve BuildRotationCurve(UnrRangeVector? range)
    {
        if (range == null)
        {
            return new ParticleSystem.MinMaxCurve(0f, 0f);
        }

        var min = ComputeZRangeValue(range, useMax: false) * Mathf.PI * 2f;
        var max = ComputeZRangeValue(range, useMax: true) * Mathf.PI * 2f;
        return new ParticleSystem.MinMaxCurve(min, max);
    }

    private static Color BuildStartColor(float? opacity, UnrParticleColorScale[] colorScale, bool applyOpacity)
    {
        if (colorScale != null && colorScale.Length > 0 && colorScale[0].Color != null)
        {
            var color = ToUnityColor(colorScale[0].Color);
            if (applyOpacity)
            {
                color.a *= opacity ?? 1f;
            }
            return color;
        }

        return new Color(1f, 1f, 1f, applyOpacity ? Mathf.Clamp01(opacity ?? 1f) : 1f);
    }

    private static bool ShouldDriveAlphaOverLifetime(
        UnrParticleColorScale[] colorScale,
        float? fadeInEndTime,
        float? fadeOutStartTime)
    {
        return (colorScale != null && colorScale.Length > 0) || fadeInEndTime.HasValue || fadeOutStartTime.HasValue;
    }

    private static void ConfigureShape(
        ParticleSystem.ShapeModule shape,
        UnrRangeVector? startLocationRange,
        UnrFloatRange? sphereRadiusRange)
    {
        if (startLocationRange != null)
        {
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = ConvertPosition(new NumericsVector3(
                (startLocationRange.X.Min + startLocationRange.X.Max) * 0.5f,
                (startLocationRange.Y.Min + startLocationRange.Y.Max) * 0.5f,
                (startLocationRange.Z.Min + startLocationRange.Z.Max) * 0.5f));
            shape.scale = new Vector3(
                Math.Abs(startLocationRange.X.Max - startLocationRange.X.Min) * UnrealToUnityScale,
                Math.Abs(startLocationRange.Z.Max - startLocationRange.Z.Min) * UnrealToUnityScale,
                Math.Abs(startLocationRange.Y.Max - startLocationRange.Y.Min) * UnrealToUnityScale);
            return;
        }

        if (sphereRadiusRange != null)
        {
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Math.Max(0.01f, sphereRadiusRange.Max * UnrealToUnityScale);
            return;
        }

        shape.enabled = false;
    }

    private static void ConfigureVelocityOverLifetime(ParticleSystem.VelocityOverLifetimeModule velocity, UnrRangeVector? startVelocityRange)
    {
        if (startVelocityRange == null)
        {
            velocity.enabled = false;
            return;
        }

        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(
            startVelocityRange.X.Min * UnrealToUnityScale,
            startVelocityRange.X.Max * UnrealToUnityScale);
        velocity.y = new ParticleSystem.MinMaxCurve(
            startVelocityRange.Z.Min * UnrealToUnityScale,
            startVelocityRange.Z.Max * UnrealToUnityScale);
        velocity.z = new ParticleSystem.MinMaxCurve(
            startVelocityRange.Y.Min * UnrealToUnityScale,
            startVelocityRange.Y.Max * UnrealToUnityScale);
    }

    private static void ConfigureForceOverLifetime(ParticleSystem.ForceOverLifetimeModule force, NumericsVector3? acceleration)
    {
        if (acceleration is null)
        {
            force.enabled = false;
            return;
        }

        force.enabled = true;
        force.space = ParticleSystemSimulationSpace.Local;
        force.x = acceleration.Value.X * UnrealToUnityScale;
        force.y = acceleration.Value.Z * UnrealToUnityScale;
        force.z = acceleration.Value.Y * UnrealToUnityScale;
    }

    private static void ConfigureColorOverLifetime(
        ParticleSystem.ColorOverLifetimeModule colorOverLifetime,
        float? opacity,
        UnrParticleColorScale[] colorScale,
        float? fadeInEndTime,
        float? fadeOutStartTime,
        float lifetimeMax)
    {
        if ((colorScale == null || colorScale.Length == 0) && !fadeInEndTime.HasValue && !fadeOutStartTime.HasValue)
        {
            colorOverLifetime.enabled = false;
            return;
        }

        var gradient = new Gradient();
        var sortedScale = (colorScale ?? Array.Empty<UnrParticleColorScale>())
            .Where(x => x.Color != null && x.RelativeTime.HasValue)
            .OrderBy(x => x.RelativeTime!.Value)
            .ToArray();

        var colorKeys = new List<GradientColorKey>();
        var alphaKeys = new List<GradientAlphaKey>();
        if (sortedScale.Length == 0)
        {
            colorKeys.Add(new GradientColorKey(Color.white, 0f));
            colorKeys.Add(new GradientColorKey(Color.white, 1f));
        }
        else
        {
            foreach (var entry in sortedScale)
            {
                var color = ToUnityColor(entry.Color!);
                colorKeys.Add(new GradientColorKey(new Color(color.r, color.g, color.b, 1f), Mathf.Clamp01(entry.RelativeTime!.Value)));
                alphaKeys.Add(new GradientAlphaKey(color.a * Mathf.Clamp01(opacity ?? 1f), Mathf.Clamp01(entry.RelativeTime!.Value)));
            }
        }

        if (alphaKeys.Count == 0)
        {
            alphaKeys.Add(new GradientAlphaKey(Mathf.Clamp01(opacity ?? 1f), 0f));
            alphaKeys.Add(new GradientAlphaKey(Mathf.Clamp01(opacity ?? 1f), 1f));
        }

        if (fadeInEndTime.HasValue && lifetimeMax > 0f)
        {
            var fadeInTime = Mathf.Clamp01(fadeInEndTime.Value / lifetimeMax);
            alphaKeys.Add(new GradientAlphaKey(0f, 0f));
            alphaKeys.Add(new GradientAlphaKey(Mathf.Clamp01(opacity ?? 1f), fadeInTime));
        }

        if (fadeOutStartTime.HasValue && lifetimeMax > 0f)
        {
            var fadeOutTime = Mathf.Clamp01(fadeOutStartTime.Value / lifetimeMax);
            alphaKeys.Add(new GradientAlphaKey(Mathf.Clamp01(opacity ?? 1f), fadeOutTime));
            alphaKeys.Add(new GradientAlphaKey(0f, 1f));
        }

        gradient.SetKeys(
            colorKeys.OrderBy(x => x.time).ToArray(),
            alphaKeys.OrderBy(x => x.time).ToArray());
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
    }

    private static void ConfigureSizeOverLifetime(ParticleSystem.SizeOverLifetimeModule sizeOverLifetime, UnrParticleSizeScale[]? sizeScale)
    {
        var entries = (sizeScale ?? Array.Empty<UnrParticleSizeScale>())
            .Where(x => x.RelativeTime.HasValue && x.RelativeSize.HasValue)
            .OrderBy(x => x.RelativeTime!.Value)
            .ToArray();
        if (entries.Length == 0)
        {
            sizeOverLifetime.enabled = false;
            return;
        }

        var curve = new AnimationCurve(entries
            .Select(x => new Keyframe(Mathf.Clamp01(x.RelativeTime!.Value), Math.Max(0f, x.RelativeSize!.Value)))
            .ToArray());
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, curve);
    }

    private static void ConfigureRotationOverLifetime(
        ParticleSystem.RotationOverLifetimeModule rotationOverLifetime,
        bool spinParticles,
        UnrRangeVector? spinsPerSecondRange)
    {
        if (!spinParticles || spinsPerSecondRange == null)
        {
            rotationOverLifetime.enabled = false;
            return;
        }

        rotationOverLifetime.enabled = true;
        rotationOverLifetime.separateAxes = false;
        rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(
            ComputeZRangeValue(spinsPerSecondRange, useMax: false) * Mathf.PI * 2f,
            ComputeZRangeValue(spinsPerSecondRange, useMax: true) * Mathf.PI * 2f);
    }

    private static float ComputeScalarRangeValue(UnrRangeVector range, bool useMax)
    {
        var x = useMax ? range.X.Max : range.X.Min;
        var y = useMax ? range.Y.Max : range.Y.Min;
        var z = useMax ? range.Z.Max : range.Z.Min;
        return Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
    }

    private static float ComputeZRangeValue(UnrRangeVector range, bool useMax)
    {
        return useMax ? range.Z.Max : range.Z.Min;
    }

    private static Vector3 ConvertPosition(NumericsVector3 raw)
    {
        return new Vector3(raw.X * UnrealToUnityScale, raw.Z * UnrealToUnityScale, raw.Y * UnrealToUnityScale);
    }

    private static Quaternion ConvertEulerAngles(NumericsVector3 rotDegrees)
    {
        return Quaternion.Euler(rotDegrees.X, -rotDegrees.Y, -rotDegrees.Z);
    }

    private static Color ToUnityColor(UnrFileColor color)
    {
        return new Color32(color.R, color.G, color.B, color.A);
    }
}
