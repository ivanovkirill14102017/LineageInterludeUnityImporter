#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using L2Viewer.SceneDomain.Services.MaterialServices;
using L2Viewer.UnrFile;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEditor;
using UnityEngine;
using NumericsVector3 = System.Numerics.Vector3;

internal static class L2ParticleAssetBuilder
{
    private const float UnrealToUnityScale = L2WorldScale.BakeUnrealToUnityScale;
    private const float NeutralParticleStartSize = 1f;
    private const byte DefaultParticleDrawStyle = 3;
    private static readonly JsonSerializerSettings DiagnosticJsonSettings = new()
    {
        Formatting = Formatting.Indented,
        ContractResolver = new ParticleDiagnosticContractResolver()
    };

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
        var textureAssets = PrepareParticleTextureAssets(outputDir, resolvedTextures);
        var materialAssets = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

        var missingTextureReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var emitter in emitters.OrderBy(x => x.ExportIndex))
        {
            var emitterRoot = new GameObject(emitter.StableName);
            emitterRoot.transform.SetParent(parent.transform, false);
            ApplyEmitterTransform(emitterRoot.transform, emitter);
            AttachDiagnostic(emitterRoot, BuildEmitterDiagnosticJson(emitter));

            foreach (var layer in emitter.Layers.OrderBy(x => x.ExportIndex))
            {
                BuildSpriteLayer(layer, outputDir, resolvedTextures, textureAssets, materialAssets, emitterRoot.transform, missingTextureReferences);
            }

            foreach (var layer in emitter.MeshLayers.OrderBy(x => x.ExportIndex))
            {
                BuildMeshLayer(layer, outputDir, resolvedTextures, textureAssets, materialAssets, emitterRoot.transform, log);
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

    private static Dictionary<string, Texture2D> PrepareParticleTextureAssets(
        string outputDir,
        IReadOnlyDictionary<string, BspTextureManager.ResolvedTexture> resolvedTextures)
    {
        var textureAssets = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        var pendingTexturePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resolvedTexture in resolvedTextures)
        {
            if (string.IsNullOrWhiteSpace(resolvedTexture.Key) || resolvedTexture.Value?.Texture == null)
            {
                continue;
            }

            var needsRefresh = ImportedTextureAssetUtility.PrepareTextureAssetFile(
                resolvedTexture.Key,
                resolvedTexture.Value.Texture,
                L2AssetManager.SharedTexturesRoot,
                "ParticleTextures",
                traits: null,
                reuseExisting: true,
                out var texturePath,
                out var loadedTexture);
            if (loadedTexture != null)
            {
                textureAssets[resolvedTexture.Key] = loadedTexture;
                continue;
            }

            if (needsRefresh)
            {
                pendingTexturePaths[resolvedTexture.Key] = texturePath;
            }
        }

        if (pendingTexturePaths.Count > 0)
        {
            AssetDatabase.Refresh();
        }

        foreach (var pendingTexture in pendingTexturePaths)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(pendingTexture.Value);
            if (texture != null)
            {
                textureAssets[pendingTexture.Key] = texture;
            }
        }

        return textureAssets;
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
        var particleSystem = CreateParticleSystemObject(layer.StableName, parent, ResolveSpriteRenderMode(layer.UseDirectionAs));
        AttachDiagnostic(particleSystem.gameObject, BuildSpriteLayerDiagnosticJson(layer));
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
            particleMaterial: ResolveParticleMaterial(layer.TextureReference, layer.DrawStyle, outputDir, resolvedTextures, textureAssets, materialAssets, missingTextureReferences),
            alignment: ResolveSpriteAlignment(layer.UseDirectionAs),
            sizeScale: layer.UseSizeScale ? layer.SizeScale : Array.Empty<UnrParticleSizeScale>());
        ConfigureTextureSheetAnimation(particleSystem.textureSheetAnimation, layer);
        return true;
    }

    private static bool BuildMeshLayer(
        SceneMeshEmitterLayerData layer,
        string outputDir,
        IReadOnlyDictionary<string, BspTextureManager.ResolvedTexture> resolvedTextures,
        IDictionary<string, Texture2D> textureAssets,
        IDictionary<string, Material> materialAssets,
        Transform parent,
        Action<string> log)
    {
        var particleSystem = CreateParticleSystemObject(layer.StableName, parent, ParticleSystemRenderMode.Mesh);
        AttachDiagnostic(particleSystem.gameObject, BuildMeshLayerDiagnosticJson(layer));
        if (!TryResolveMeshEmitterAsset(layer.StaticMeshReference, out var mesh, out var material, out var resolvedAssetPath))
        {
            log($"[Particles/MeshEmitter] Missing mesh asset for '{layer.StableName}' ref='{layer.StaticMeshReference ?? "<null>"}'. Layer left as diagnostic-only placeholder.");
            return false;
        }

        material = CreateOrUpdateMeshEmitterMaterial(material, layer.StaticMeshReference!, layer.DrawStyle, outputDir);

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
            layer.StartSpinRange,
            layer.SpinParticles,
            layer.SpinsPerSecondRange,
            startLocationRange: null,
            sphereRadiusRange: null,
            particleMaterial: material,
            alignment: ParticleSystemRenderSpace.Local,
            sizeScale: layer.SizeScale,
            isMeshEmitter: true);

        var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
        renderer.mesh = mesh;
        renderer.sharedMaterial = material;
        if (layer.RenderTwoSided && material != null && material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        }

        if (!string.IsNullOrWhiteSpace(resolvedAssetPath))
        {
            AttachDiagnostic(particleSystem.gameObject, BuildMeshLayerDiagnosticJson(layer, resolvedAssetPath));
        }
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
        AttachDiagnostic(particleSystem.gameObject, BuildVertMeshLayerDiagnosticJson(layer));
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
            particleMaterial: ResolveParticleMaterial(null, layer.DrawStyle, outputDir, resolvedTextures, textureAssets, materialAssets),
            alignment: ParticleSystemRenderSpace.View);
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
        AttachDiagnostic(particleSystem.gameObject, BuildBeamLayerDiagnosticJson(layer));
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
            particleMaterial: ResolveParticleMaterial(layer.TextureReference, drawStyle: null, outputDir, resolvedTextures, textureAssets, materialAssets, missingTextureReferences),
            alignment: ParticleSystemRenderSpace.View);
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
        ParticleSystemRenderSpace alignment,
        UnrParticleSizeScale[]? sizeScale = null,
        bool isMeshEmitter = false)
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
        ConfigureStartSize(main, startSizeRange, isMeshEmitter);
        ConfigureStartRotation(main, startSpinRange, isMeshEmitter);


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
        ConfigureRotationOverLifetime(rotationOverLifetime, spinParticles, spinsPerSecondRange, isMeshEmitter);

        var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = particleMaterial;
        renderer.alignment = alignment;
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
        byte? drawStyle,
        string outputDir,
        IReadOnlyDictionary<string, BspTextureManager.ResolvedTexture> resolvedTextures,
        IDictionary<string, Texture2D> textureAssets,
        IDictionary<string, Material> materialAssets,
        ISet<string>? missingTextureReferences = null)
    {
        var resolvedDrawStyle = drawStyle ?? DefaultParticleDrawStyle;
        var materialKey = $"{(string.IsNullOrWhiteSpace(textureReference) ? "__default__" : textureReference)}|ds={resolvedDrawStyle}";
        if (materialAssets.TryGetValue(materialKey, out var cached))
        {
            return cached;
        }

        var materialPath = BuildParticleMaterialPath(textureReference, resolvedDrawStyle);
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            var shader = ResolveParticleShader();
            material = new Material(shader);
            material.enableInstancing = true;
        }

        ConfigureParticleDrawStyle(material, resolvedDrawStyle);

        var texture = ResolveParticleTexture(textureReference, outputDir, resolvedTextures, textureAssets, missingTextureReferences);
        if (texture != null)
        {
            L2MaterialUtility.AssignMainTexture(material, texture);
        }

        L2MaterialUtility.SetBaseColor(material, Color.white);
        material = UnityAssetDatabaseUtility.CreateOrReplaceAsset(material, materialPath);
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

        var texturePath = BuildParticleTexturePath(textureReference);
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

        textureAssets[textureReference] = texture;
        return texture;
    }

    private static string BuildParticleTexturePath(string textureReference)
    {
        return L2AssetManager.BuildClientPackageAssetPath(
            L2AssetManager.SharedTexturesRoot,
            textureReference,
            "TEX",
            "png",
            "ParticleTextures");
    }

    private static string BuildParticleMaterialPath(string? textureReference, byte drawStyle)
    {
        return L2AssetManager.BuildClientPackageAssetPath(
            L2AssetManager.ManagedParticleMaterialsRoot,
            $"{textureReference ?? "DefaultParticle"}_PTDS_{drawStyle}",
            "MAT",
            "mat",
            "ParticleMaterials");
    }

    private static Material CreateOrUpdateMeshEmitterMaterial(
        Material source,
        string staticMeshReference,
        byte? drawStyle,
        string outputDir)
    {
        var resolvedDrawStyle = drawStyle ?? DefaultParticleDrawStyle;
        var materialPath = BuildParticleMaterialPath($"{staticMeshReference}_MeshEmitter", resolvedDrawStyle);
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(source);
        }
        else
        {
            material.CopyPropertiesFromMaterial(source);
            material.shader = source.shader;
        }

        material.enableInstancing = true;
        ConfigureParticleDrawStyle(material, resolvedDrawStyle);
        return UnityAssetDatabaseUtility.CreateOrReplaceAsset(material, materialPath);
    }

    private static void ConfigureParticleDrawStyle(Material material, byte drawStyle)
    {
        switch (drawStyle)
        {
            case 0:
                L2MaterialUtility.ConfigureTransparent(material, UnityEngine.Rendering.BlendMode.One, UnityEngine.Rendering.BlendMode.Zero, false);
                break;
            case 1:
                L2MaterialUtility.ConfigureTransparent(material, UnityEngine.Rendering.BlendMode.SrcAlpha, UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha, false);
                break;
            case 2:
                L2MaterialUtility.ConfigureTransparent(material, UnityEngine.Rendering.BlendMode.DstColor, UnityEngine.Rendering.BlendMode.Zero, false, true);
                break;
            case 3:
                L2MaterialUtility.ConfigureTransparent(material, UnityEngine.Rendering.BlendMode.One, UnityEngine.Rendering.BlendMode.One, false);
                break;
            case 4:
                L2MaterialUtility.ConfigureTransparent(material, UnityEngine.Rendering.BlendMode.DstColor, UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha, false, true);
                break;
            case 5:
                L2MaterialUtility.ConfigureTransparent(material, UnityEngine.Rendering.BlendMode.Zero, UnityEngine.Rendering.BlendMode.OneMinusSrcColor, false);
                break;
            case 6:
                L2MaterialUtility.ConfigureTransparent(material, UnityEngine.Rendering.BlendMode.One, UnityEngine.Rendering.BlendMode.One, false);
                break;
            default:
                throw new InvalidOperationException($"Unsupported Unreal EParticleDrawStyle value {drawStyle}.");
        }
    }

    private static void ConfigureTextureSheetAnimation(
        ParticleSystem.TextureSheetAnimationModule textureSheet,
        SceneSpriteEmitterLayerData layer)
    {
        var tilesX = Math.Max(1, layer.TextureUSubdivisions ?? 1);
        var tilesY = Math.Max(1, layer.TextureVSubdivisions ?? 1);
        if (tilesX <= 1 && tilesY <= 1)
        {
            textureSheet.enabled = false;
            return;
        }

        var frameCount = Math.Max(1, tilesX * tilesY);
        var startFrame = Mathf.Clamp(layer.SubdivisionStart ?? 0, 0, frameCount - 1);
        var endFrame = Mathf.Clamp(layer.SubdivisionEnd ?? startFrame, startFrame, frameCount - 1);
        textureSheet.enabled = true;
        textureSheet.mode = ParticleSystemAnimationMode.Grid;
        textureSheet.numTilesX = tilesX;
        textureSheet.numTilesY = tilesY;
        textureSheet.animation = ParticleSystemAnimationType.WholeSheet;
        textureSheet.cycleCount = 1;
        if (layer.UseRandomSubdivision && endFrame > startFrame)
        {
            textureSheet.startFrame = new ParticleSystem.MinMaxCurve(startFrame, endFrame);
            textureSheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            return;
        }

        textureSheet.startFrame = new ParticleSystem.MinMaxCurve(0f);
        textureSheet.frameOverTime = endFrame > startFrame
            ? new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, startFrame, 1f, endFrame))
            : new ParticleSystem.MinMaxCurve(startFrame);
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
            Math.Max(0.001f, ComputeScalarRangeValue(range, useMax: false) * UnrealToUnityScale),
            Math.Max(0.001f, ComputeScalarRangeValue(range, useMax: true) * UnrealToUnityScale));
    }

    private static ParticleSystem.MinMaxCurve BuildRotationCurve(UnrRangeVector? range)
    {
        if (range == null)
        {
            return new ParticleSystem.MinMaxCurve(0f, 0f);
        }

        var min = ComputeDominantAxisValue(range, useMax: false) * Mathf.PI * 2f;
        var max = ComputeDominantAxisValue(range, useMax: true) * Mathf.PI * 2f;
        return new ParticleSystem.MinMaxCurve(min, max);
    }

    private static void ConfigureStartSize(
        ParticleSystem.MainModule main,
        UnrRangeVector? range,
        bool isMeshEmitter)
    {
        if (!isMeshEmitter)
        {
            main.startSize3D = false;
            main.startSize = BuildSizeCurve(range, NeutralParticleStartSize, NeutralParticleStartSize);
            return;
        }

        main.startSize3D = true;
        if (range == null)
        {
            main.startSizeX = new ParticleSystem.MinMaxCurve(NeutralParticleStartSize);
            main.startSizeY = new ParticleSystem.MinMaxCurve(NeutralParticleStartSize);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(NeutralParticleStartSize);
            return;
        }

        // MeshEmitter StartSizeRange is a dimensionless mesh multiplier. The mesh
        // vertices already contain the Unreal-to-Unity world scale.
        main.startSizeX = BuildMeshSizeCurve(range.X);
        main.startSizeY = BuildMeshSizeCurve(range.Z);
        main.startSizeZ = BuildMeshSizeCurve(range.Y);
    }

    private static ParticleSystem.MinMaxCurve BuildMeshSizeCurve(UnrFloatRange range)
    {
        var first = Math.Max(0.001f, Math.Abs(range.Min));
        var second = Math.Max(0.001f, Math.Abs(range.Max));
        return new ParticleSystem.MinMaxCurve(Math.Min(first, second), Math.Max(first, second));
    }

    private static void ConfigureStartRotation(
        ParticleSystem.MainModule main,
        UnrRangeVector? range,
        bool isMeshEmitter)
    {
        if (!isMeshEmitter)
        {
            main.startRotation3D = false;
            main.startRotation = BuildRotationCurve(range);
            return;
        }

        var spin = BuildRotationCurve(range);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f);
        main.startRotationY = spin;
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f);
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

        var normalizedColorKeys = NormalizeGradientColorKeys(colorKeys);
        var normalizedAlphaKeys = NormalizeGradientAlphaKeys(alphaKeys);
        gradient.SetKeys(
            normalizedColorKeys,
            normalizedAlphaKeys);
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
        UnrRangeVector? spinsPerSecondRange,
        bool isMeshEmitter)
    {
        if (!spinParticles || spinsPerSecondRange == null)
        {
            rotationOverLifetime.enabled = false;
            return;
        }

        var spin = new ParticleSystem.MinMaxCurve(
            ComputeDominantAxisValue(spinsPerSecondRange, useMax: false) * Mathf.PI * 2f,
            ComputeDominantAxisValue(spinsPerSecondRange, useMax: true) * Mathf.PI * 2f);

        rotationOverLifetime.enabled = true;
        rotationOverLifetime.separateAxes = isMeshEmitter;
        if (isMeshEmitter)
        {
            rotationOverLifetime.x = new ParticleSystem.MinMaxCurve(0f);
            rotationOverLifetime.y = spin;
            rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(0f);
        }
        else
        {
            rotationOverLifetime.z = spin;
        }
    }

    private static float ComputeScalarRangeValue(UnrRangeVector range, bool useMax)
    {
        var x = useMax ? range.X.Max : range.X.Min;
        var y = useMax ? range.Y.Max : range.Y.Min;
        var z = useMax ? range.Z.Max : range.Z.Min;
        return Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
    }

    private static float ComputeDominantAxisValue(UnrRangeVector range, bool useMax)
    {
        var x = useMax ? range.X.Max : range.X.Min;
        var y = useMax ? range.Y.Max : range.Y.Min;
        var z = useMax ? range.Z.Max : range.Z.Min;

        var absX = Math.Abs(x);
        var absY = Math.Abs(y);
        var absZ = Math.Abs(z);
        if (absX >= absY && absX >= absZ)
        {
            return x;
        }

        if (absY >= absX && absY >= absZ)
        {
            return y;
        }

        return z;
    }

    private static GradientColorKey[] NormalizeGradientColorKeys(IEnumerable<GradientColorKey> keys)
    {
        var ordered = keys
            .GroupBy(x => Mathf.Clamp01(x.time))
            .OrderBy(x => x.Key)
            .Select(x => new GradientColorKey(x.Last().color, x.Key))
            .ToList();
        if (ordered.Count == 0)
        {
            ordered.Add(new GradientColorKey(Color.white, 0f));
            ordered.Add(new GradientColorKey(Color.white, 1f));
        }

        return DownsampleGradientKeys(
            ordered,
            maxKeys: 8,
            firstFactory: key => new GradientColorKey(key.color, key.time),
            lastFactory: key => new GradientColorKey(key.color, key.time),
            middleFactory: key => new GradientColorKey(key.color, key.time));
    }

    private static GradientAlphaKey[] NormalizeGradientAlphaKeys(IEnumerable<GradientAlphaKey> keys)
    {
        var ordered = keys
            .GroupBy(x => Mathf.Clamp01(x.time))
            .OrderBy(x => x.Key)
            .Select(x => new GradientAlphaKey(x.Last().alpha, x.Key))
            .ToList();
        if (ordered.Count == 0)
        {
            ordered.Add(new GradientAlphaKey(1f, 0f));
            ordered.Add(new GradientAlphaKey(1f, 1f));
        }

        return DownsampleGradientKeys(
            ordered,
            maxKeys: 8,
            firstFactory: key => new GradientAlphaKey(key.alpha, key.time),
            lastFactory: key => new GradientAlphaKey(key.alpha, key.time),
            middleFactory: key => new GradientAlphaKey(key.alpha, key.time));
    }

    private static TKey[] DownsampleGradientKeys<TKey>(
        IReadOnlyList<TKey> ordered,
        int maxKeys,
        Func<TKey, TKey> firstFactory,
        Func<TKey, TKey> lastFactory,
        Func<TKey, TKey> middleFactory)
    {
        if (ordered.Count <= maxKeys)
        {
            return ordered.ToArray();
        }

        var result = new List<TKey>(maxKeys)
        {
            firstFactory(ordered[0])
        };

        var middleSlots = maxKeys - 2;
        for (var i = 0; i < middleSlots; i++)
        {
            var sampleIndex = 1 + (int)Math.Round((ordered.Count - 3) * (i / (double)Math.Max(1, middleSlots - 1)));
            result.Add(middleFactory(ordered[sampleIndex]));
        }

        result.Add(lastFactory(ordered[ordered.Count - 1]));
        return result.ToArray();
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

    private static bool TryResolveMeshEmitterAsset(string? staticMeshReference, out Mesh mesh, out Material material, out string resolvedAssetPath)
    {
        mesh = null;
        material = null;
        resolvedAssetPath = string.Empty;

        if (string.IsNullOrWhiteSpace(staticMeshReference))
        {
            return false;
        }

        var prefabPath = L2AssetManager.BuildClientPackageAssetPath(
            L2AssetManager.ManagedStaticMeshPrefabsRoot,
            staticMeshReference,
            "PF",
            "prefab",
            "StaticMeshPrefabs");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab != null)
        {
            var meshFilter = prefab.GetComponentInChildren<MeshFilter>();
            var meshRenderer = prefab.GetComponentInChildren<MeshRenderer>();
            if (meshFilter != null && meshFilter.sharedMesh != null && meshRenderer != null && meshRenderer.sharedMaterials.Length > 0)
            {
                mesh = meshFilter.sharedMesh;
                material = meshRenderer.sharedMaterials.FirstOrDefault(x => x != null);
                resolvedAssetPath = prefabPath;
                return mesh != null && material != null;
            }
        }

        var meshPath = L2AssetManager.BuildClientPackageAssetPath(
            L2AssetManager.SharedStaticMeshesRoot,
            staticMeshReference,
            "SM",
            "asset",
            "StaticMeshes");
        mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        resolvedAssetPath = meshPath;
        return false;
    }

    private static ParticleSystemRenderMode ResolveSpriteRenderMode(byte? useDirectionAs)
    {
        return useDirectionAs == 4
            ? ParticleSystemRenderMode.HorizontalBillboard
            : ParticleSystemRenderMode.Billboard;
    }

    private static ParticleSystemRenderSpace ResolveSpriteAlignment(byte? useDirectionAs)
    {
        return useDirectionAs == 4
            ? ParticleSystemRenderSpace.World
            : ParticleSystemRenderSpace.View;
    }

    private static void AttachDiagnostic(GameObject target, string jsonText)
    {
        if (target == null || string.IsNullOrWhiteSpace(jsonText))
        {
            return;
        }

        var diagnostic = target.GetComponent<L2JsonDiagnosticData>();
        if (diagnostic == null)
        {
            diagnostic = target.AddComponent<L2JsonDiagnosticData>();
        }

        diagnostic.JsonText = jsonText;
    }

    private static string BuildEmitterDiagnosticJson(SceneParticleEmitterData emitter)
    {
        return SerializeRawDiagnostic(emitter);
    }

    private static string BuildSpriteLayerDiagnosticJson(SceneSpriteEmitterLayerData layer)
    {
        return SerializeRawDiagnostic(layer);
    }

    private static string BuildMeshLayerDiagnosticJson(SceneMeshEmitterLayerData layer, string? resolvedAssetPath = null)
    {
        return SerializeRawDiagnostic(layer);
    }

    private static string BuildVertMeshLayerDiagnosticJson(SceneVertMeshEmitterLayerData layer)
    {
        return SerializeRawDiagnostic(layer);
    }

    private static string BuildBeamLayerDiagnosticJson(SceneBeamEmitterLayerData layer)
    {
        return SerializeRawDiagnostic(layer);
    }

    private static string SerializeRawDiagnostic<T>(T value)
    {
        return JsonConvert.SerializeObject(value, DiagnosticJsonSettings);
    }

    private sealed class ParticleDiagnosticContractResolver : DefaultContractResolver
    {
        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            var properties = base.CreateProperties(type, memberSerialization);
            if (type == typeof(UnrParticleColorScale))
            {
                return properties
                    .Where(x => !string.Equals(x.PropertyName, nameof(UnrParticleColorScale.RawHex), StringComparison.Ordinal))
                    .ToList();
            }

            if (type == typeof(UnrParticleSizeScale))
            {
                return properties
                    .Where(x => !string.Equals(x.PropertyName, nameof(UnrParticleSizeScale.RawHex), StringComparison.Ordinal))
                    .ToList();
            }

            return properties;
        }
    }
}
