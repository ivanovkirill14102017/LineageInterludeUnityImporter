using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using L2Viewer.SceneDomain.Services.MaterialServices;
using L2Viewer.UnrFile;
using L2Viewer.UtxFile;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

internal static class SharedSkySceneImporter
{
    private const string ScenePath = "Assets/Scenes/SharedSky.unity";

    public static void Build(string clientRoot, SceneSkyTimeEnvironmentData sourceTime, SkyTimeProfileData timeProfiles)
    {
        var mapPath = Path.Combine(clientRoot, "maps", "skylevel.unr");
        if (!File.Exists(mapPath))
            throw new FileNotFoundException("Client sky level was not found.", mapPath);
        var sky = new SceneSkyEnvironmentBuilder().Build(UnrFileReader.Read(mapPath));
        if (sky.Layers == null || sky.Layers.Length == 0)
            throw new InvalidDataException("SkyLevel has no sky materials.");

        var sourceTracks = sourceTime.Profiles[0].Tracks
            .Where(track => track.Role != SceneSkyTimeLayerRole.SkyBox)
            .ToArray();
        if (sourceTracks.Length != 4)
            throw new InvalidDataException("Expected haze, cloud and two star layers in Env.int.");
        var sourceLayers = sourceTracks.Select(track => sky.Layers.First(layer =>
            string.Equals(layer.Geometry.MaterialObjectName, track.MaterialName,
                StringComparison.OrdinalIgnoreCase))).ToArray();

        var sunReference = sky.Suns.SelectMany(sun => sun.SkinReferences).Single();
        var moonReference = sky.Moons.Where(moon => !moon.EnvironmentType.HasValue)
            .SelectMany(moon => moon.SkinReferences).Single();

        var textureManager = new BspTextureManager(clientRoot);
        var resolver = new SceneMaterialResolver(clientRoot, textureManager);
        var requests = sourceLayers.Select(layer => new SceneMaterialRequest(
                layer.Geometry.MaterialPackageName, layer.Geometry.MaterialObjectName))
            .Concat(new[] { MaterialRequest(sunReference), MaterialRequest(moonReference) });
        var materials = resolver.ResolveMany(mapPath, requests);
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            throw new InvalidOperationException("Universal Render Pipeline/Unlit shader is missing.");
        L2AssetManager.EnsureFolderExists(L2AssetManager.SharedTexturesRoot);
        L2AssetManager.EnsureFolderExists(L2AssetManager.UnrealMeshesRoot);
        var materialDir = $"{MapImportPaths.OutputRoot}/Managed/SharedSkyMaterials";
        L2AssetManager.EnsureFolderExists(materialDir);
        var meshDir = L2AssetManager.BuildClientPackageObjectRoot(
            L2AssetManager.UnrealMeshesRoot, "SkyLevel", "Sky", "Sky");

        var imported = new (SkyLayerRole Role, Mesh Mesh, Material Material, float Radius)[sourceTracks.Length];
        var meshPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var materialPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var firstProfile = sourceTime.Profiles[0];
        for (var i = 0; i < sourceTracks.Length; i++)
        {
            var layer = sourceLayers[i];
            var sourceTrack = sourceTracks[i];
            var role = ToUnityRole(sourceTrack.Role);
            if (!materials.TryGetValue(layer.MaterialReference, out var graph) ||
                graph?.TextureSlots == null || graph.TextureSlots.Count == 0)
                throw new InvalidDataException($"Sky material has no resolved texture: {layer.MaterialReference}");
            var slot = graph.TextureSlots[0];
            var isStarLayer = role == SkyLayerRole.StarsLarge || role == SkyLayerRole.StarsSmall;
            if (isStarLayer)
            {
                var outputBlending = graph.Nodes
                    .Where(node => string.Equals(node.ClassName, "Shader", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(node => node.NumericParameters)
                    .Single(parameter => string.Equals(parameter.Name, "OutputBlending", StringComparison.OrdinalIgnoreCase));
                if (outputBlending.Value != 3d)
                    throw new InvalidDataException($"Unexpected star blending in {layer.MaterialReference}.");
            }
            if (slot.Texture == null || slot.Texture.Width <= 0 || slot.Texture.Height <= 0)
                throw new InvalidDataException($"Sky texture could not be decoded: {slot.Reference}");
            var texture = ImportedTextureAssetUtility.LoadOrCreateTextureAsset(
                slot.Reference, slot.Texture, L2AssetManager.SharedTexturesRoot,
                "SkyLevel", null, true);
            if (texture == null)
                throw new InvalidDataException($"Sky texture asset could not be imported: {slot.Reference}");

            var mesh = BuildDome(role, 100f, out var radius);
            var meshPath = L2AssetManager.BuildAssetPathInFolder(meshDir, "MESH", $"Sky_{i:00}", "asset");
            mesh = UnityAssetDatabaseUtility.CreateOrReplaceAsset(mesh, meshPath);
            meshPaths.Add(meshPath);

            var material = new Material(shader);
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", timeProfiles.GetProfile(firstProfile.EnvironmentType)
                .Evaluate(role, 12f));
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", isStarLayer ? 2f : 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetFloat("_SrcBlend", (float)(isStarLayer ? BlendMode.One : BlendMode.SrcAlpha));
            material.SetFloat("_DstBlend", (float)(isStarLayer ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.renderQueue = RenderQueueFor(role);
            material.SetShaderPassEnabled("DepthOnly", false);
            material.SetShaderPassEnabled("DepthNormalsOnly", false);
            material.SetShaderPassEnabled("GBuffer", false);
            var materialPath = L2AssetManager.BuildAssetPathInFolder(
                materialDir, "MAT", $"Sky_{i:00}", "mat");
            material = UnityAssetDatabaseUtility.CreateOrReplaceAsset(material, materialPath);
            materialPaths.Add(materialPath);
            imported[i] = (role, mesh, material, radius);
        }

        var sunMaterial = ImportCelestialMaterial(sunReference, materials, shader, materialDir,
            "Sun", BlendMode.SrcAlpha, BlendMode.One, 3015, materialPaths);
        var moonMaterial = ImportCelestialMaterial(moonReference, materials, shader, materialDir,
            "Moon", BlendMode.One, BlendMode.One, 3016, materialPaths);
        var diskMeshPath = L2AssetManager.BuildAssetPathInFolder(meshDir, "MESH", "Sky_CelestialDisk", "asset");
        var diskMesh = UnityAssetDatabaseUtility.CreateOrReplaceAsset(BuildCelestialDisk(), diskMeshPath);
        meshPaths.Add(diskMeshPath);

        var cloudGraph = materials[sourceLayers[Array.FindIndex(sourceTracks,
            track => track.Role == SceneSkyTimeLayerRole.Cloud1)].MaterialReference];
        var cloudPanner = cloudGraph.Nodes.Single(node =>
            string.Equals(node.ClassName, "TexPanner", StringComparison.OrdinalIgnoreCase));
        var cloudPanRate = (float)cloudPanner.NumericParameters.Single(parameter =>
            string.Equals(parameter.Name, "PanRate", StringComparison.OrdinalIgnoreCase)).Value;

        var scene = SceneManager.GetSceneByPath(ScenePath);
        var openedHere = !scene.isLoaded;
        if (openedHere)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            var controller = scene.GetRootGameObjects()
                .Select(root => root.GetComponent<SharedSkyController>())
                .Single(component => component != null);
            var previous = controller.transform.Find("SkyLevelGeometry");
            if (previous != null)
                UnityEngine.Object.DestroyImmediate(previous.gameObject);
            var geometry = new GameObject("SkyLevelGeometry").transform;
            SceneManager.MoveGameObjectToScene(geometry.gameObject, scene);
            geometry.SetParent(controller.transform, false);
            var layers = new SharedSkyController.Layer[imported.Length];
            var maxRadius = 0f;
            for (var i = 0; i < imported.Length; i++)
            {
                var item = imported[i];
                var child = new GameObject($"{item.Role}_{i:00}");
                SceneManager.MoveGameObjectToScene(child, scene);
                child.transform.SetParent(geometry, false);
                child.AddComponent<MeshFilter>().sharedMesh = item.Mesh;
                var renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = item.Material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                layers[i] = new SharedSkyController.Layer { Role = item.Role, Renderer = renderer };
                maxRadius = Mathf.Max(maxRadius, item.Radius);
            }
            var sun = CreateCelestialBody("Sun", geometry, scene, diskMesh, sunMaterial, 24f);
            var moon = CreateCelestialBody("Moon", geometry, scene, diskMesh, moonMaterial, 30f);
            var initialSunDirection = new Vector3(0f, Mathf.Sin(55f * Mathf.Deg2Rad),
                -Mathf.Cos(55f * Mathf.Deg2Rad));
            sun.Transform.localPosition = initialSunDirection * 97f;
            sun.Transform.localRotation = Quaternion.LookRotation(-initialSunDirection);
            moon.Transform.localPosition = -initialSunDirection * 97.2f;
            moon.Transform.localRotation = Quaternion.LookRotation(initialSunDirection);
            moon.Renderer.enabled = false;
            var lightController = controller.GetComponent<SharedSkySunController>();
            if (lightController == null)
                throw new InvalidDataException("SharedSky scene has no light controller.");
            lightController.SunLight = GetOrCreateDirectionalLight("SunLight", controller.transform, scene);
            lightController.MoonLight = GetOrCreateDirectionalLight("MoonLight", controller.transform, scene);
            controller.GeometryRoot = geometry;
            controller.MaxLayerRadius = maxRadius;
            controller.Layers = layers;
            controller.Sun = sun;
            controller.Moon = moon;
            controller.CloudPanRate = cloudPanRate;
            controller.TimeProfiles = timeProfiles;
            geometry.localScale = Vector3.one * (100f / maxRadius);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(lightController);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            DeleteUnusedGeneratedAssets(meshDir, meshPaths, "MESH_Sky_");
            DeleteUnusedGeneratedAssets(materialDir, materialPaths, "MAT_Sky_");
        }
        finally
        {
            if (openedHere)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static SceneMaterialRequest MaterialRequest(string reference)
    {
        var separator = reference.IndexOf('.');
        if (separator <= 0 || separator == reference.Length - 1)
            throw new InvalidDataException($"Invalid sky material reference: {reference}");
        return new SceneMaterialRequest(reference.Substring(0, separator),
            reference.Substring(separator + 1));
    }

    private static Material ImportCelestialMaterial(string reference,
        IReadOnlyDictionary<string, ResolvedMaterialGraph?> materials, Shader shader,
        string materialDir, string name, BlendMode sourceBlend, BlendMode destinationBlend,
        int queue, HashSet<string> paths)
    {
        if (!materials.TryGetValue(reference, out var graph) ||
            graph?.TextureSlots == null || graph.TextureSlots.Count == 0)
            throw new InvalidDataException($"Sky material has no resolved texture: {reference}");
        var slot = graph.TextureSlots[0];
        if (slot.Texture == null || slot.Texture.Width <= 0 || slot.Texture.Height <= 0)
            throw new InvalidDataException($"Sky texture could not be decoded: {slot.Reference}");
        var texture = ImportedTextureAssetUtility.LoadOrCreateTextureAsset(slot.Reference, slot.Texture,
            L2AssetManager.SharedTexturesRoot, "SkyLevel", null, true);
        if (texture == null)
            throw new InvalidDataException($"Sky texture asset could not be imported: {slot.Reference}");
        var material = new Material(shader);
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", Color.white);
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 2f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetFloat("_SrcBlend", (float)sourceBlend);
        material.SetFloat("_DstBlend", (float)destinationBlend);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.renderQueue = queue;
        material.SetShaderPassEnabled("DepthOnly", false);
        material.SetShaderPassEnabled("DepthNormalsOnly", false);
        material.SetShaderPassEnabled("GBuffer", false);
        var path = L2AssetManager.BuildAssetPathInFolder(materialDir, "MAT", $"Sky_{name}", "mat");
        paths.Add(path);
        return UnityAssetDatabaseUtility.CreateOrReplaceAsset(material, path);
    }

    private static Mesh BuildCelestialDisk()
    {
        var mesh = new Mesh();
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
        };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0f, 1f), new Vector2(1f, 1f) };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static SharedSkyController.CelestialBody CreateCelestialBody(string name,
        Transform parent, Scene scene, Mesh mesh, Material material, float diameter)
    {
        var body = new GameObject(name);
        SceneManager.MoveGameObjectToScene(body, scene);
        body.transform.SetParent(parent, false);
        body.transform.localScale = Vector3.one * diameter;
        body.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = body.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return new SharedSkyController.CelestialBody { Transform = body.transform, Renderer = renderer };
    }

    private static Light GetOrCreateDirectionalLight(string name, Transform parent, Scene scene)
    {
        var child = parent.Find(name);
        if (child == null)
        {
            var lightObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            child = lightObject.transform;
            child.SetParent(parent, false);
        }
        var light = child.GetComponent<Light>();
        if (light == null)
            light = child.gameObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.shadows = name == "SunLight" ? LightShadows.Soft : LightShadows.None;
        light.enabled = false;
        return light;
    }

    private static SkyLayerRole ToUnityRole(SceneSkyTimeLayerRole role) => role switch
    {
        SceneSkyTimeLayerRole.SkyBox => SkyLayerRole.SkyBox,
        SceneSkyTimeLayerRole.HazeRing => SkyLayerRole.HazeRing,
        SceneSkyTimeLayerRole.Cloud1 => SkyLayerRole.Cloud1,
        SceneSkyTimeLayerRole.Cloud2 => SkyLayerRole.StarsLarge,
        SceneSkyTimeLayerRole.Cloud3 => SkyLayerRole.StarsSmall,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
    };

    private static void DeleteUnusedGeneratedAssets(string folder, HashSet<string> usedPaths, string prefix)
    {
        foreach (var guid in AssetDatabase.FindAssets("", new[] { folder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path == folder || usedPaths.Contains(path) ||
                !string.Equals(Path.GetDirectoryName(path)?.Replace('\\', '/'), folder,
                    StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                AssetDatabase.DeleteAsset(path);
        }
    }

    private static int RenderQueueFor(SkyLayerRole role) => role switch
    {
        SkyLayerRole.StarsLarge => 3000,
        SkyLayerRole.StarsSmall => 3010,
        SkyLayerRole.Cloud1 => 3020,
        SkyLayerRole.HazeRing => 3030,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
    };

    private static Mesh BuildDome(SkyLayerRole role, float baseRadius, out float radius)
    {
        const int segments = 48;
        const int rows = 16;
        var isHaze = role == SkyLayerRole.HazeRing;
        var minElevation = isHaze ? -10f : 0f;
        var maxElevation = isHaze ? 20f : 90f;
        radius = role switch
        {
            SkyLayerRole.HazeRing => baseRadius * 0.94f,
            SkyLayerRole.Cloud1 => baseRadius * 0.96f,
            SkyLayerRole.StarsLarge => baseRadius * 0.98f,
            SkyLayerRole.StarsSmall => baseRadius,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
        };
        var vertices = new Vector3[(rows + 1) * (segments + 1)];
        var uvs = new Vector2[vertices.Length];
        var indices = new int[rows * segments * 6];
        for (var row = 0; row <= rows; row++)
        {
            var v = row / (float)rows;
            var elevation = Mathf.Lerp(minElevation, maxElevation, v) * Mathf.Deg2Rad;
            var horizontal = Mathf.Cos(elevation) * radius;
            var height = Mathf.Sin(elevation) * radius;
            for (var column = 0; column <= segments; column++)
            {
                var u = column / (float)segments;
                var azimuth = u * Mathf.PI * 2f;
                var index = row * (segments + 1) + column;
                vertices[index] = new Vector3(
                    Mathf.Cos(azimuth) * horizontal, height,
                    Mathf.Sin(azimuth) * horizontal);
                uvs[index] = new Vector2(u, v);
            }
        }
        var triangle = 0;
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < segments; column++)
        {
            var lower = row * (segments + 1) + column;
            var upper = lower + segments + 1;
            indices[triangle++] = lower;
            indices[triangle++] = upper;
            indices[triangle++] = lower + 1;
            indices[triangle++] = lower + 1;
            indices[triangle++] = upper;
            indices[triangle++] = upper + 1;
        }
        var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = indices;
        mesh.RecalculateBounds();
        return mesh;
    }
}
