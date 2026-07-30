#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
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
using Object = UnityEngine.Object;

internal static class SkillVisualImportBuilder
{
    public const string AssetOutputRoot = "Assets/L2Imported/ClientPackages/Skills";
    public const string PrefabOutputRoot = "Assets/L2Imported/Managed/SkillVisualPrefabs";
    public const string TextureOutputRoot = "Assets/L2Imported/ClientPackages/Skills/Textures";
    public const string MaterialOutputRoot = "Assets/L2Imported/ClientPackages/Skills/Materials";

    private const float UnrealToUnityScale = L2WorldScale.BakeUnrealToUnityScale;
    private const float NeutralParticleStartSize = 1f;
    private const string DefaultMaterialKey = "__default__";

    private static readonly JsonSerializerSettings DiagnosticJsonSettings = new()
    {
        Formatting = Formatting.Indented,
        ContractResolver = new SkillDiagnosticContractResolver()
    };

    public sealed class ImportResult
    {
        public string AssetPath = string.Empty;
        public string PrefabPath = string.Empty;
        public L2SkillVisualAsset? Asset;
        public int NameCount;
        public int LevelCount;
        public int SoundCount;
        public int StageCount;
        public int LayerCount;
        public int MobTriggerCount;
        public int MobVisualCount;
        public int WarningCount;
    }

    public static ImportResult ImportBySkillId(
        string clientRoot,
        int skillId,
        Action<string>? log = null,
        bool buildPrefab = true,
        bool reuseExistingAssets = true,
        MapImportExecutionContext? context = null)
    {
        if (skillId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(skillId), "Skill id must be positive.");
        }

        if (string.IsNullOrWhiteSpace(clientRoot))
        {
            throw new ArgumentException("Client root is required.", nameof(clientRoot));
        }

        var fullClientRoot = Path.GetFullPath(clientRoot);
        if (!Directory.Exists(fullClientRoot))
        {
            throw new DirectoryNotFoundException($"Client root was not found: '{fullClientRoot}'.");
        }

        context?.Report("Skill Visual", "Read SceneDomain skill data", 0.12f);
        log?.Invoke($"[Skill] Reading skill visual data for id={skillId}.");
        var sceneData = new SceneSkillVisualBuilder().Build(fullClientRoot, skillId);

        context?.ThrowIfCancellationRequested();
        context?.Report("Skill Visual", "Create Unity asset", 0.28f);
        var asset = CreateSkillVisualAsset(sceneData);
        var assetPath = BuildSkillAssetPath(sceneData);
        asset = UnityAssetDatabaseUtility.CreateOrReplaceAsset(asset, assetPath);

        var dependencies = SkillVisualDependencyContext.Empty;
        var prefabPath = string.Empty;
        if (buildPrefab)
        {
            context?.ThrowIfCancellationRequested();
            context?.Report("Skill Visual", "Import prefab dependencies", 0.48f);
            dependencies = ImportDependencies(fullClientRoot, sceneData, reuseExistingAssets, log, context);

            context?.ThrowIfCancellationRequested();
            context?.Report("Skill Visual", "Build preview prefab", 0.78f);
            prefabPath = BuildSkillVisualPrefab(sceneData, asset, dependencies, log);
            asset.PreviewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            EditorUtility.SetDirty(asset);
        }

        context?.Report("Skill Visual", "Save assets", 0.95f);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return new ImportResult
        {
            AssetPath = assetPath,
            PrefabPath = prefabPath,
            Asset = asset,
            NameCount = sceneData.Names.Count,
            LevelCount = sceneData.Levels.Count,
            SoundCount = sceneData.Sounds.Count,
            StageCount = sceneData.Stages.Count,
            LayerCount = sceneData.Stages.Sum(x => x.Layers.Count),
            MobTriggerCount = sceneData.MobTriggers.Count,
            MobVisualCount = sceneData.MobVisuals.Count,
            WarningCount = sceneData.Warnings.Count
        };
    }

    private static SkillVisualDependencyContext ImportDependencies(
        string clientRoot,
        SceneSkillVisualData sceneData,
        bool reuseExistingAssets,
        Action<string>? log,
        MapImportExecutionContext? context)
    {
        var textures = ImportLayerTextures(clientRoot, sceneData, reuseExistingAssets, log);
        var materials = BuildSkillMaterials(textures);
        var staticMeshPrefabs = ImportStaticMeshDependencies(clientRoot, sceneData, reuseExistingAssets, log, context);

        return new SkillVisualDependencyContext(textures, materials, staticMeshPrefabs);
    }

    private static IReadOnlyDictionary<string, Texture2D> ImportLayerTextures(
        string clientRoot,
        SceneSkillVisualData sceneData,
        bool reuseExistingAssets,
        Action<string>? log)
    {
        var requests = sceneData.Stages
            .SelectMany(x => x.Layers)
            .Select(BuildTextureRequest)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .GroupBy(x => x.Reference, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();
        if (requests.Length == 0)
        {
            log?.Invoke("[Skill/Textures] No texture references were found in skill layers.");
            return new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        }

        var textureManager = new BspTextureManager(clientRoot);
        var resolved = textureManager.ResolveMany(requests.Select(x => x.Request).Distinct().ToArray());
        var textureAssets = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        var pendingTexturePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var request in requests)
        {
            var lookupKey = $"{request.Request.PackageName}.{request.Request.ObjectName}";
            if (!resolved.TryGetValue(lookupKey, out var resolvedTexture) || resolvedTexture?.Texture == null)
            {
                log?.Invoke($"[Skill/Textures] Texture was not resolved: {lookupKey}");
                continue;
            }

            var needsRefresh = ImportedTextureAssetUtility.PrepareTextureAssetFile(
                request.Reference,
                resolvedTexture.Texture,
                TextureOutputRoot,
                "Skills/Textures",
                traits: null,
                reuseExisting: reuseExistingAssets,
                out var texturePath,
                out var loadedTexture);
            if (loadedTexture != null)
            {
                textureAssets[request.Reference] = loadedTexture;
                continue;
            }

            if (needsRefresh)
            {
                pendingTexturePaths[request.Reference] = texturePath;
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
            else
            {
                log?.Invoke($"[Skill/Textures] Failed to load texture asset after refresh: {pendingTexture.Key} -> {pendingTexture.Value}");
            }
        }

        log?.Invoke($"[Skill/Textures] Imported/loaded {textureAssets.Count} texture assets from {requests.Length} references.");
        return textureAssets;
    }

    private static IReadOnlyDictionary<string, Material> BuildSkillMaterials(IReadOnlyDictionary<string, Texture2D> textures)
    {
        var materials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultMaterialKey] = CreateOrUpdateSkillMaterial(DefaultMaterialKey, null)
        };

        foreach (var texture in textures)
        {
            if (texture.Value == null)
            {
                continue;
            }

            materials[texture.Key] = CreateOrUpdateSkillMaterial(texture.Key, texture.Value);
        }

        return materials;
    }

    private static Material CreateOrUpdateSkillMaterial(string reference, Texture2D? texture)
    {
        var materialPath = BuildSkillMaterialPath(reference);
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(ResolveParticleShader());
        }

        L2MaterialUtility.ConfigureTransparent(
            material,
            UnityEngine.Rendering.BlendMode.SrcAlpha,
            UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha,
            premultiplyKeyword: false);
        material.enableInstancing = true;
        L2MaterialUtility.SetBaseColor(material, Color.white);
        if (texture != null)
        {
            L2MaterialUtility.AssignMainTexture(material, texture);
        }

        return UnityAssetDatabaseUtility.CreateOrReplaceAsset(material, materialPath);
    }

    private static IReadOnlyDictionary<string, GameObject> ImportStaticMeshDependencies(
        string clientRoot,
        SceneSkillVisualData sceneData,
        bool reuseExistingAssets,
        Action<string>? log,
        MapImportExecutionContext? context)
    {
        var meshReferences = sceneData.Stages
            .SelectMany(x => x.Layers)
            .Select(BuildStaticMeshObjectReference)
            .Where(x => x != null)
            .Cast<UnrFileObjectReference>()
            .GroupBy(x => $"{x.PackageName}.{x.ObjectName}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();
        if (meshReferences.Length == 0)
        {
            log?.Invoke("[Skill/Mesh] No static mesh references were found in skill layers.");
            return new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var lineageEffectPath = Path.Combine(clientRoot, "system", "LineageEffect.u");
            var textureManager = new BspTextureManager(clientRoot);
            var meshResolver = new SceneStaticMeshResolver(clientRoot, textureManager);
            var definitions = meshResolver.ResolveMany(lineageEffectPath, meshReferences);
            log?.Invoke($"[Skill/Mesh] Resolved {definitions.Count} static mesh dependencies.");
            context?.ThrowIfCancellationRequested();
            return L2StaticMeshAssetBuilder.EnsureStaticMeshPrefabs(
                definitions,
                clientRoot,
                "Skills",
                log ?? (_ => { }),
                reuseExistingAssets,
                context);
        }
        catch (Exception ex)
        {
            log?.Invoke($"[Skill/Mesh] Static mesh dependency import failed: {ex.Message}");
            return new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string BuildSkillVisualPrefab(
        SceneSkillVisualData sceneData,
        L2SkillVisualAsset asset,
        SkillVisualDependencyContext dependencies,
        Action<string>? log)
    {
        var prefabPath = BuildSkillPrefabPath(sceneData);
        var prefabRoot = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
        try
        {
            var controller = prefabRoot.AddComponent<L2SkillVisualController>();
            var castPoint = new GameObject("CastPoint");
            castPoint.transform.SetParent(prefabRoot.transform, false);

            var targetPoint = new GameObject("TargetPoint");
            targetPoint.transform.SetParent(prefabRoot.transform, false);
            targetPoint.transform.localPosition = ComputeDefaultTargetOffset(sceneData);

            var stageContainer = new GameObject("Stages");
            stageContainer.transform.SetParent(prefabRoot.transform, false);

            var runtimeContainer = new GameObject("Runtime");
            runtimeContainer.transform.SetParent(prefabRoot.transform, false);

            AttachDiagnostic(prefabRoot, SerializeRawDiagnostic(new
            {
                sceneData.SkillId,
                DisplayName = ResolveDisplayName(sceneData),
                sceneData.ResolvedEffectStem,
                sceneData.ResolvedEffectStems,
                sceneData.Warnings
            }));

            var stageBindings = new List<L2SkillVisualStageBinding>();
            foreach (var stage in sceneData.Stages.OrderBy(x => x.StageOrder).ThenBy(x => x.ObjectName, StringComparer.OrdinalIgnoreCase))
            {
                var stageObject = new GameObject(BuildStageObjectName(stage));
                stageObject.transform.SetParent(stageContainer.transform, false);
                AttachDiagnostic(stageObject, SerializeRawDiagnostic(stage));

                foreach (var layer in stage.Layers.OrderBy(x => x.ExportIndex).ThenBy(x => x.ObjectName, StringComparer.OrdinalIgnoreCase))
                {
                    BuildLayerObject(layer, stageObject.transform, dependencies, log);
                }

                var playbackRole = InferPlaybackRole(stage);
                stageObject.SetActive(false);
                stageBindings.Add(new L2SkillVisualStageBinding
                {
                    StageKey = stage.StageKey ?? string.Empty,
                    StageOrder = stage.StageOrder,
                    StageName = stage.ObjectName ?? stageObject.name,
                    Role = playbackRole,
                    StageRoot = stageObject
                });
            }

            controller.Skill = asset;
            controller.CastPoint = castPoint.transform;
            controller.TargetPoint = targetPoint.transform;
            controller.StageContainer = stageContainer.transform;
            controller.RuntimeContainer = runtimeContainer.transform;
            controller.StageBindings = stageBindings.ToArray();
            controller.SelectedStageIndex = 0;
            controller.ProjectileSpeed = 8f;
            controller.StageIntervalSeconds = 0.45f;
            controller.RuntimeInstanceLifetime = 3f;

            L2AssetManager.EnsureParentFolderExists(prefabPath);
            var prefab = PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException($"Failed to save skill visual prefab: {prefabPath}");
            }

            log?.Invoke($"[Skill/Prefab] Saved preview prefab: {prefabPath}");
            return prefabPath;
        }
        finally
        {
            Object.DestroyImmediate(prefabRoot);
        }
    }

    private static void BuildLayerObject(
        SceneSkillVisualLayerData layer,
        Transform parent,
        SkillVisualDependencyContext dependencies,
        Action<string>? log)
    {
        var particleSystem = CreateParticleSystemObject(BuildLayerObjectName(layer), parent, ResolveLayerRenderMode(layer));
        AttachDiagnostic(particleSystem.gameObject, SerializeRawDiagnostic(layer));

        var material = ResolveLayerMaterial(layer, dependencies);
        ConfigureLayerParticleSystem(particleSystem, layer, material, ResolveLayerAlignment(layer));

        if (!string.IsNullOrWhiteSpace(layer.StaticMeshReference))
        {
            var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            if (TryResolveMeshEmitterAsset(layer.StaticMeshReference, out var mesh, out var meshMaterial, out _))
            {
                renderer.mesh = mesh;
                renderer.sharedMaterial = meshMaterial != null ? meshMaterial : material;
            }
            else
            {
                renderer.sharedMaterial = material;
                log?.Invoke($"[Skill/Prefab] Missing mesh asset for layer '{layer.ObjectName}' ref='{layer.StaticMeshReference}'.");
            }
        }
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

    private static void ConfigureLayerParticleSystem(
        ParticleSystem particleSystem,
        SceneSkillVisualLayerData layer,
        Material material,
        ParticleSystemRenderSpace alignment)
    {
        var lifetimeMin = layer.LifetimeRange?.Min ?? 1f;
        var lifetimeMax = Math.Max(lifetimeMin, layer.LifetimeRange?.Max ?? lifetimeMin);
        var maxParticles = Math.Max(1, layer.MaxParticles ?? 32);

        var main = particleSystem.main;
        main.loop = true;
        main.playOnAwake = true;
        main.duration = Math.Max(0.1f, lifetimeMax);
        main.maxParticles = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(Math.Max(0.01f, lifetimeMin), Math.Max(0.01f, lifetimeMax));
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0f);
        main.startSize3D = false;
        main.startSize = BuildSizeCurve(layer.StartSizeRange, NeutralParticleStartSize, NeutralParticleStartSize);
        main.startRotation = BuildRotationCurve(layer.StartSpinRange);
        main.startColor = BuildStartColor(layer.Opacity, layer.ColorScale, applyOpacity: !ShouldDriveAlphaOverLifetime(layer));

        var emission = particleSystem.emission;
        emission.enabled = true;
        emission.rateOverTime = Math.Max(1f, maxParticles / Math.Max(0.1f, (lifetimeMin + lifetimeMax) * 0.5f));

        ConfigureShape(particleSystem.shape, layer.StartLocationRange);
        ConfigureVelocityOverLifetime(particleSystem.velocityOverLifetime, layer.StartVelocityRange);
        ConfigureForceOverLifetime(particleSystem.forceOverLifetime, layer.Acceleration);
        ConfigureColorOverLifetime(
            particleSystem.colorOverLifetime,
            layer.Opacity,
            layer.ColorScale,
            layer.FadeIn ? layer.FadeInEndTime : null,
            layer.FadeOut ? layer.FadeOutStartTime : null,
            lifetimeMax);
        ConfigureSizeOverLifetime(particleSystem.sizeOverLifetime, layer.SizeScale);
        ConfigureRotationOverLifetime(particleSystem.rotationOverLifetime, layer.SpinsPerSecondRange);

        var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.alignment = alignment;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.minParticleSize = 0.0001f;
        renderer.maxParticleSize = 0.5f;
    }

    private static void ConfigureShape(ParticleSystem.ShapeModule shape, UnrRangeVector? startLocationRange)
    {
        if (startLocationRange == null)
        {
            shape.enabled = false;
            return;
        }

        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.position = ConvertUnrealPosition(new NumericsVector3(
            (startLocationRange.X.Min + startLocationRange.X.Max) * 0.5f,
            (startLocationRange.Y.Min + startLocationRange.Y.Max) * 0.5f,
            (startLocationRange.Z.Min + startLocationRange.Z.Max) * 0.5f));
        shape.scale = new Vector3(
            Math.Abs(startLocationRange.X.Max - startLocationRange.X.Min) * UnrealToUnityScale,
            Math.Abs(startLocationRange.Z.Max - startLocationRange.Z.Min) * UnrealToUnityScale,
            Math.Abs(startLocationRange.Y.Max - startLocationRange.Y.Min) * UnrealToUnityScale);
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
        if (acceleration == null)
        {
            force.enabled = false;
            return;
        }

        var converted = ConvertUnrealPosition(acceleration.Value);
        force.enabled = true;
        force.space = ParticleSystemSimulationSpace.Local;
        force.x = converted.x;
        force.y = converted.y;
        force.z = converted.z;
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

        gradient.SetKeys(NormalizeGradientColorKeys(colorKeys), NormalizeGradientAlphaKeys(alphaKeys));
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

    private static void ConfigureRotationOverLifetime(ParticleSystem.RotationOverLifetimeModule rotationOverLifetime, UnrRangeVector? spinsPerSecondRange)
    {
        if (spinsPerSecondRange == null)
        {
            rotationOverLifetime.enabled = false;
            return;
        }

        rotationOverLifetime.enabled = true;
        rotationOverLifetime.separateAxes = false;
        rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(
            ComputeDominantAxisValue(spinsPerSecondRange, useMax: false) * Mathf.PI * 2f,
            ComputeDominantAxisValue(spinsPerSecondRange, useMax: true) * Mathf.PI * 2f);
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

    private static Color BuildStartColor(float? opacity, UnrParticleColorScale[] colorScale, bool applyOpacity)
    {
        var firstColor = colorScale != null && colorScale.Length > 0 ? colorScale[0].Color : null;
        if (firstColor != null)
        {
            var color = ToUnityColor(firstColor);
            if (applyOpacity)
            {
                color.a *= opacity ?? 1f;
            }

            return color;
        }

        return new Color(1f, 1f, 1f, applyOpacity ? Mathf.Clamp01(opacity ?? 1f) : 1f);
    }

    private static bool ShouldDriveAlphaOverLifetime(SceneSkillVisualLayerData layer)
    {
        return (layer.ColorScale != null && layer.ColorScale.Length > 0) || layer.FadeInEndTime.HasValue || layer.FadeOutStartTime.HasValue;
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

    private static Material ResolveLayerMaterial(SceneSkillVisualLayerData layer, SkillVisualDependencyContext dependencies)
    {
        var textureReference = ResolveTextureReference(layer);
        if (!string.IsNullOrWhiteSpace(textureReference) &&
            dependencies.Materials.TryGetValue(textureReference, out var material) &&
            material != null)
        {
            return material;
        }

        return dependencies.Materials.TryGetValue(DefaultMaterialKey, out var defaultMaterial) && defaultMaterial != null
            ? defaultMaterial
            : CreateOrUpdateSkillMaterial(DefaultMaterialKey, null);
    }

    private static bool TryResolveMeshEmitterAsset(string? staticMeshReference, out Mesh? mesh, out Material? material, out string resolvedAssetPath)
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
                return mesh != null;
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
        return mesh != null;
    }

    private static ParticleSystemRenderMode ResolveLayerRenderMode(SceneSkillVisualLayerData layer)
    {
        if (!string.IsNullOrWhiteSpace(layer.StaticMeshReference))
        {
            return ParticleSystemRenderMode.Mesh;
        }

        return layer.ClassName != null && layer.ClassName.IndexOf("Beam", StringComparison.OrdinalIgnoreCase) >= 0
            ? ParticleSystemRenderMode.Stretch
            : ParticleSystemRenderMode.Billboard;
    }

    private static ParticleSystemRenderSpace ResolveLayerAlignment(SceneSkillVisualLayerData layer)
    {
        return layer.ClassName != null && layer.ClassName.IndexOf("Beam", StringComparison.OrdinalIgnoreCase) >= 0
            ? ParticleSystemRenderSpace.View
            : ParticleSystemRenderSpace.Local;
    }

    private static SkillTextureRequest? BuildTextureRequest(SceneSkillVisualLayerData layer)
    {
        var reference = ResolveTextureReference(layer);
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        if (layer.TextureResourceReference != null &&
            !string.IsNullOrWhiteSpace(layer.TextureResourceReference.PackageName) &&
            !string.IsNullOrWhiteSpace(layer.TextureResourceReference.ObjectName))
        {
            return new SkillTextureRequest(
                reference,
                new SceneTextureRequest(layer.TextureResourceReference.PackageName, layer.TextureResourceReference.ObjectName));
        }

        if (!TrySplitReference(reference, out var packageName, out var objectName))
        {
            return null;
        }

        return new SkillTextureRequest(reference, new SceneTextureRequest(packageName, objectName));
    }

    private static UnrFileObjectReference? BuildStaticMeshObjectReference(SceneSkillVisualLayerData layer)
    {
        if (layer.StaticMeshResourceReference != null &&
            !string.IsNullOrWhiteSpace(layer.StaticMeshResourceReference.PackageName) &&
            !string.IsNullOrWhiteSpace(layer.StaticMeshResourceReference.ObjectName))
        {
            return new UnrFileObjectReference
            {
                RawReference = 0,
                Kind = UnrFileReferenceKind.Import,
                ClassName = string.IsNullOrWhiteSpace(layer.StaticMeshResourceReference.ClassName)
                    ? "StaticMesh"
                    : layer.StaticMeshResourceReference.ClassName,
                PackageName = layer.StaticMeshResourceReference.PackageName,
                ObjectName = layer.StaticMeshResourceReference.ObjectName
            };
        }

        if (!TrySplitReference(layer.StaticMeshReference, out var packageName, out var objectName))
        {
            return null;
        }

        return new UnrFileObjectReference
        {
            RawReference = 0,
            Kind = UnrFileReferenceKind.Import,
            ClassName = "StaticMesh",
            PackageName = packageName,
            ObjectName = objectName
        };
    }

    private static string ResolveTextureReference(SceneSkillVisualLayerData layer)
    {
        return layer.TextureResourceReference?.Reference
               ?? layer.TextureReference
               ?? string.Empty;
    }

    private static bool TrySplitReference(string? reference, out string packageName, out string objectName)
    {
        packageName = string.Empty;
        objectName = string.Empty;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var normalized = reference!.Trim();
        var separatorIndex = normalized.LastIndexOf('.');
        if (separatorIndex <= 0 || separatorIndex >= normalized.Length - 1)
        {
            return false;
        }

        packageName = normalized.Substring(0, separatorIndex);
        objectName = normalized.Substring(separatorIndex + 1);
        return true;
    }

    private static L2SkillVisualAsset CreateSkillVisualAsset(SceneSkillVisualData data)
    {
        return new L2SkillVisualAsset
        {
            SkillId = data.SkillId,
            DisplayName = ResolveDisplayName(data),
            ResolvedEffectStem = data.ResolvedEffectStem ?? string.Empty,
            ResolvedEffectStems = data.ResolvedEffectStems?.ToArray() ?? Array.Empty<string>(),
            Warnings = data.Warnings?.ToArray() ?? Array.Empty<string>(),
            Names = data.Names?.Select(ConvertNameEntry).ToArray() ?? Array.Empty<L2SkillNameEntryData>(),
            Levels = data.Levels?.Select(ConvertLevel).ToArray() ?? Array.Empty<L2SkillLevelData>(),
            Sounds = data.Sounds?.Select(ConvertSound).ToArray() ?? Array.Empty<L2SkillSoundData>(),
            MobTriggers = data.MobTriggers?.Select(ConvertMobTrigger).ToArray() ?? Array.Empty<L2MobSkillTriggerData>(),
            Effects = data.Effects?.Select(ConvertEffect).ToArray() ?? Array.Empty<L2SkillVisualEffectData>(),
            MobVisuals = data.MobVisuals?.Select(ConvertMobVisual).ToArray() ?? Array.Empty<L2MobSkillVisualData>(),
            Stages = data.Stages?.Select(ConvertStage).ToArray() ?? Array.Empty<L2SkillVisualStageData>()
        };
    }

    private static L2SkillNameEntryData ConvertNameEntry(SceneSkillNameEntryData value)
    {
        return new L2SkillNameEntryData
        {
            SkillLevel = value.SkillLevel,
            Name = value.Name ?? string.Empty,
            Description = value.Description ?? string.Empty,
            DescriptionAdd1 = value.DescriptionAdd1 ?? string.Empty,
            DescriptionAdd2 = value.DescriptionAdd2 ?? string.Empty
        };
    }

    private static L2SkillLevelData ConvertLevel(SceneSkillLevelData value)
    {
        return new L2SkillLevelData
        {
            SkillLevel = value.SkillLevel,
            OperType = value.OperType,
            MpConsume = value.MpConsume,
            CastRange = value.CastRange,
            CastStyle = value.CastStyle,
            HitTime = value.HitTime,
            IsMagic = value.IsMagic,
            AnimationCharacter = value.AnimationCharacter ?? string.Empty,
            DescriptionToken = value.DescriptionToken ?? string.Empty,
            IconName = value.IconName ?? string.Empty,
            IconName2 = value.IconName2 ?? string.Empty,
            IsEnchanted = value.IsEnchanted,
            EnchantedSkillId = value.EnchantedSkillId,
            HpConsume = value.HpConsume
        };
    }

    private static L2SkillSoundData ConvertSound(SceneSkillSoundData value)
    {
        return new L2SkillSoundData
        {
            SkillLevel = value.SkillLevel,
            SpellEffectSounds = value.SpellEffectSounds?.ToArray() ?? Array.Empty<string>(),
            ShotEffectSounds = value.ShotEffectSounds?.ToArray() ?? Array.Empty<string>(),
            ExpEffectSounds = value.ExpEffectSounds?.ToArray() ?? Array.Empty<string>(),
            CharacterSubSounds = value.CharacterSubSounds?.ToArray() ?? Array.Empty<string>(),
            CharacterThrowSounds = value.CharacterThrowSounds?.ToArray() ?? Array.Empty<string>(),
            SoundVolume = value.SoundVolume,
            SoundRadius = value.SoundRadius
        };
    }

    private static L2MobSkillTriggerData ConvertMobTrigger(SceneMobSkillTriggerData value)
    {
        return new L2MobSkillTriggerData
        {
            NpcId = value.NpcId,
            SkillId = value.SkillId,
            SequenceName = value.SequenceName ?? string.Empty,
            SkillName = value.SkillName ?? string.Empty,
            NpcName = value.NpcName ?? string.Empty,
            NpcClass = value.NpcClass ?? string.Empty
        };
    }

    private static L2MobSkillVisualData ConvertMobVisual(SceneMobSkillVisualData value)
    {
        return new L2MobSkillVisualData
        {
            NpcId = value.NpcId,
            NpcClass = value.NpcClass ?? string.Empty,
            MeshReference = value.MeshReference ?? string.Empty,
            MeshPackagePath = value.MeshPackagePath ?? string.Empty,
            SequenceName = value.SequenceName ?? string.Empty,
            SequenceCategory = value.SequenceCategory ?? string.Empty,
            ActorEffectReference = value.ActorEffectReference ?? string.Empty,
            ActorEffectPackagePath = value.ActorEffectPackagePath ?? string.Empty
        };
    }

    private static L2SkillVisualEffectData ConvertEffect(SceneSkillVisualEffectData value)
    {
        return new L2SkillVisualEffectData
        {
            Stem = value.Stem ?? string.Empty,
            Source = value.Source ?? string.Empty,
            Stages = value.Stages?.Select(ConvertStage).ToArray() ?? Array.Empty<L2SkillVisualStageData>()
        };
    }

    private static L2SkillVisualStageData ConvertStage(SceneSkillVisualStageData value)
    {
        return new L2SkillVisualStageData
        {
            StageKey = value.StageKey ?? string.Empty,
            StageOrder = value.StageOrder,
            PlaybackRole = InferPlaybackRole(value),
            ObjectName = value.ObjectName ?? string.Empty,
            SuperClassName = value.SuperClassName ?? string.Empty,
            StageReference = ConvertResourceReference(value.StageReference),
            StageResource = ConvertResourceLocation(value.StageResource),
            EmitterReferences = value.EmitterReferences?.Select(ConvertResourceReference).ToArray() ?? Array.Empty<L2ResourceReferenceData>(),
            EmitterResources = value.EmitterResources?.Select(ConvertResourceLocation).ToArray() ?? Array.Empty<L2ResourceLocationData>(),
            Layers = value.Layers?.Select(ConvertLayer).ToArray() ?? Array.Empty<L2SkillVisualLayerData>()
        };
    }

    private static L2SkillVisualLayerData ConvertLayer(SceneSkillVisualLayerData value)
    {
        return new L2SkillVisualLayerData
        {
            ExportIndex = value.ExportIndex,
            ObjectName = value.ObjectName ?? string.Empty,
            ClassName = value.ClassName ?? string.Empty,
            LayerName = value.LayerName ?? string.Empty,
            LayerReference = ConvertResourceReference(value.LayerReference),
            LayerResource = ConvertResourceLocation(value.LayerResource),
            StaticMeshReference = value.StaticMeshReference ?? string.Empty,
            StaticMeshResourceReference = ConvertResourceReference(value.StaticMeshResourceReference),
            StaticMeshResource = ConvertResourceLocation(value.StaticMeshResource),
            TextureReference = value.TextureReference ?? string.Empty,
            TextureResourceReference = ConvertResourceReference(value.TextureResourceReference),
            TextureResource = ConvertResourceLocation(value.TextureResource),
            HasOpacity = value.Opacity.HasValue,
            Opacity = value.Opacity ?? 1f,
            HasFadeOutStartTime = value.FadeOutStartTime.HasValue,
            FadeOutStartTime = value.FadeOutStartTime ?? 0f,
            FadeOut = value.FadeOut,
            HasFadeInEndTime = value.FadeInEndTime.HasValue,
            FadeInEndTime = value.FadeInEndTime ?? 0f,
            FadeIn = value.FadeIn,
            HasMaxParticles = value.MaxParticles.HasValue,
            MaxParticles = value.MaxParticles ?? 0,
            HasLifetimeRange = value.LifetimeRange != null,
            LifetimeRange = ConvertFloatRange(value.LifetimeRange),
            HasAcceleration = value.Acceleration.HasValue,
            Acceleration = value.Acceleration.HasValue ? ConvertRawVector(value.Acceleration.Value) : Vector3.zero,
            HasStartLocationRange = value.StartLocationRange != null,
            StartLocationRange = ConvertRangeVector(value.StartLocationRange),
            HasStartSizeRange = value.StartSizeRange != null,
            StartSizeRange = ConvertRangeVector(value.StartSizeRange),
            HasStartVelocityRange = value.StartVelocityRange != null,
            StartVelocityRange = ConvertRangeVector(value.StartVelocityRange),
            HasStartSpinRange = value.StartSpinRange != null,
            StartSpinRange = ConvertRangeVector(value.StartSpinRange),
            HasSpinsPerSecondRange = value.SpinsPerSecondRange != null,
            SpinsPerSecondRange = ConvertRangeVector(value.SpinsPerSecondRange),
            ColorScale = value.ColorScale?.Select(ConvertColorScale).ToArray() ?? Array.Empty<L2ParticleColorScaleData>(),
            SizeScale = value.SizeScale?.Select(ConvertSizeScale).ToArray() ?? Array.Empty<L2ParticleSizeScaleData>()
        };
    }

    private static L2ResourceReferenceData? ConvertResourceReference(SceneResourceReference? value)
    {
        if (value == null)
        {
            return null;
        }

        return new L2ResourceReferenceData
        {
            Reference = value.Reference ?? string.Empty,
            ClassName = value.ClassName ?? string.Empty,
            PackageName = value.PackageName ?? string.Empty,
            ObjectName = value.ObjectName ?? string.Empty
        };
    }

    private static L2ResourceLocationData? ConvertResourceLocation(SceneResourceLocation? value)
    {
        if (value == null)
        {
            return null;
        }

        return new L2ResourceLocationData
        {
            Reference = value.Reference ?? string.Empty,
            ClassName = value.ClassName ?? string.Empty,
            PackageName = value.PackageName ?? string.Empty,
            ObjectName = value.ObjectName ?? string.Empty,
            PackagePath = value.PackagePath ?? string.Empty,
            ClientRelativePath = value.ClientRelativePath ?? string.Empty,
            Uri = value.Uri ?? string.Empty
        };
    }

    private static L2FloatRangeData? ConvertFloatRange(UnrFloatRange? value)
    {
        if (value == null)
        {
            return null;
        }

        return new L2FloatRangeData
        {
            Min = value.Min,
            Max = value.Max
        };
    }

    private static L2RangeVectorData? ConvertRangeVector(UnrRangeVector? value)
    {
        if (value == null)
        {
            return null;
        }

        return new L2RangeVectorData
        {
            X = ConvertFloatRange(value.X),
            Y = ConvertFloatRange(value.Y),
            Z = ConvertFloatRange(value.Z)
        };
    }

    private static L2ParticleColorScaleData ConvertColorScale(UnrParticleColorScale value)
    {
        return new L2ParticleColorScaleData
        {
            ArrayIndex = value.ArrayIndex,
            Type = value.Type,
            StructName = value.StructName ?? string.Empty,
            HasRelativeTime = value.RelativeTime.HasValue,
            RelativeTime = value.RelativeTime ?? 0f,
            HasColor = value.Color != null,
            Color = value.Color == null ? default : new Color32(value.Color.R, value.Color.G, value.Color.B, value.Color.A)
        };
    }

    private static L2ParticleSizeScaleData ConvertSizeScale(UnrParticleSizeScale value)
    {
        return new L2ParticleSizeScaleData
        {
            ArrayIndex = value.ArrayIndex,
            Type = value.Type,
            StructName = value.StructName ?? string.Empty,
            HasRelativeTime = value.RelativeTime.HasValue,
            RelativeTime = value.RelativeTime ?? 0f,
            HasRelativeSize = value.RelativeSize.HasValue,
            RelativeSize = value.RelativeSize ?? 0f
        };
    }

    private static string BuildSkillAssetPath(SceneSkillVisualData data)
    {
        L2AssetManager.EnsureFolderExists(AssetOutputRoot);
        return $"{AssetOutputRoot}/SKILL_{data.SkillId:D5}.asset";
    }

    private static string BuildSkillPrefabPath(SceneSkillVisualData data)
    {
        L2AssetManager.EnsureFolderExists(PrefabOutputRoot);
        return $"{PrefabOutputRoot}/{BuildSkillPrefabName(data)}.prefab";
    }

    private static string BuildSkillMaterialPath(string reference)
    {
        return L2AssetManager.BuildClientPackageAssetPath(
            MaterialOutputRoot,
            string.Equals(reference, DefaultMaterialKey, StringComparison.OrdinalIgnoreCase) ? "DefaultSkillParticle" : reference,
            "MAT",
            "mat",
            "Skills/Materials");
    }

    private static string BuildSkillPrefabName(SceneSkillVisualData data)
    {
        var name = ResolveDisplayName(data);
        return string.IsNullOrWhiteSpace(name)
            ? $"PF_Skill_{data.SkillId:D5}"
            : $"PF_Skill_{data.SkillId:D5}_{SanitizeObjectName(name)}";
    }

    private static string BuildStageObjectName(SceneSkillVisualStageData stage)
    {
        return $"Stage_{stage.StageOrder:D2}_{SanitizeObjectName(stage.ObjectName)}";
    }

    private static string BuildLayerObjectName(SceneSkillVisualLayerData layer)
    {
        var label = string.IsNullOrWhiteSpace(layer.LayerName) ? layer.ObjectName : layer.LayerName;
        return $"Layer_{layer.ExportIndex:D4}_{SanitizeObjectName(label)}";
    }

    private static Vector3 ComputeDefaultTargetOffset(SceneSkillVisualData data)
    {
        var castRange = data.Levels?
            .Where(x => x.CastRange > 0)
            .OrderBy(x => x.SkillLevel)
            .Select(x => x.CastRange)
            .FirstOrDefault() ?? 0;
        var distance = castRange > 0
            ? Mathf.Clamp(castRange * L2WorldScale.BakeUnrealToUnityScale, 1.5f, 12f)
            : 4f;
        return Vector3.forward * distance;
    }

    private static L2SkillVisualStagePlaybackRole InferPlaybackRole(SceneSkillVisualStageData stage)
    {
        var stageKey = stage.StageKey ?? string.Empty;
        var objectName = stage.ObjectName ?? string.Empty;
        var hasBeamLayer = stage.Layers?.Any(x => string.Equals(x.ClassName, "BeamEmitter", StringComparison.OrdinalIgnoreCase)) == true;
        var hasMeshProjectileToken = stage.Layers?.Any(x =>
        {
            var reference = x.StaticMeshReference ?? string.Empty;
            return reference.IndexOf("arrow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   reference.IndexOf("bolt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   reference.IndexOf("shot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   reference.IndexOf("projectile", StringComparison.OrdinalIgnoreCase) >= 0;
        }) == true;

        if (stageKey.Equals("pr", StringComparison.OrdinalIgnoreCase) ||
            stageKey.Equals("fl", StringComparison.OrdinalIgnoreCase) ||
            hasBeamLayer ||
            hasMeshProjectileToken ||
            objectName.IndexOf("projectile", StringComparison.OrdinalIgnoreCase) >= 0 ||
            objectName.IndexOf("arrow", StringComparison.OrdinalIgnoreCase) >= 0 ||
            objectName.IndexOf("bolt", StringComparison.OrdinalIgnoreCase) >= 0 ||
            objectName.IndexOf("shot", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return L2SkillVisualStagePlaybackRole.Projectile;
        }

        if (stageKey.Equals("ta", StringComparison.OrdinalIgnoreCase) ||
            stageKey.Equals("to", StringComparison.OrdinalIgnoreCase) ||
            objectName.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return L2SkillVisualStagePlaybackRole.Target;
        }

        if (objectName.IndexOf("hit", StringComparison.OrdinalIgnoreCase) >= 0 ||
            objectName.IndexOf("impact", StringComparison.OrdinalIgnoreCase) >= 0 ||
            objectName.IndexOf("explosion", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return L2SkillVisualStagePlaybackRole.Impact;
        }

        return L2SkillVisualStagePlaybackRole.Caster;
    }

    private static string ResolveDisplayName(SceneSkillVisualData data)
    {
        return data.Names?
                   .OrderBy(x => x.SkillLevel)
                   .Select(x => x.Name)
                   .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
               ?? data.ResolvedEffectStem
               ?? $"Skill_{data.SkillId:D5}";
    }

    private static string SanitizeObjectName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unnamed";
        }

        var sanitized = (value ?? string.Empty).Trim();
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalidChar, '_');
        }

        return sanitized.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
    }

    private static Shader ResolveParticleShader()
    {
        return Shader.Find("Universal Render Pipeline/Particles/Unlit")
               ?? Shader.Find("Particles/Standard Unlit")
               ?? L2MaterialUtility.FindBestUnlitShader()
               ?? throw new InvalidOperationException("Compatible particle shader was not found.");
    }

    private static Vector3 ConvertRawVector(NumericsVector3 value)
    {
        return new Vector3(value.X, value.Y, value.Z);
    }

    private static Vector3 ConvertUnrealPosition(NumericsVector3 raw)
    {
        return new Vector3(raw.X * UnrealToUnityScale, raw.Z * UnrealToUnityScale, raw.Y * UnrealToUnityScale);
    }

    private static Color ToUnityColor(UnrFileColor color)
    {
        return new Color32(color.R, color.G, color.B, color.A);
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

    private static string SerializeRawDiagnostic<T>(T value)
    {
        return JsonConvert.SerializeObject(value, DiagnosticJsonSettings);
    }

    private readonly struct SkillTextureRequest
    {
        public SkillTextureRequest(string reference, SceneTextureRequest request)
        {
            Reference = reference;
            Request = request;
        }

        public string Reference { get; }
        public SceneTextureRequest Request { get; }
    }

    private sealed class SkillVisualDependencyContext
    {
        public static readonly SkillVisualDependencyContext Empty = new(
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase));

        public SkillVisualDependencyContext(
            IReadOnlyDictionary<string, Texture2D> textures,
            IReadOnlyDictionary<string, Material> materials,
            IReadOnlyDictionary<string, GameObject> staticMeshPrefabs)
        {
            Textures = textures;
            Materials = materials;
            StaticMeshPrefabs = staticMeshPrefabs;
        }

        public IReadOnlyDictionary<string, Texture2D> Textures { get; }
        public IReadOnlyDictionary<string, Material> Materials { get; }
        public IReadOnlyDictionary<string, GameObject> StaticMeshPrefabs { get; }
    }

    private sealed class SkillDiagnosticContractResolver : DefaultContractResolver
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
