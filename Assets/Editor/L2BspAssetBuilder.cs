using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using UnityEditor;
using UnityEngine;

internal static class L2BspAssetBuilder
{
    private const float UnrealToUnityScale = L2WorldScale.BakeUnrealToUnityScale;
    private const uint UnknownPolyFlag02000 = 0x00002000;
    private const uint UnknownPolyFlag08000 = 0x00008000;

    private sealed class BspSectionEntry
    {
        public string ModelName;
        public string ChunkName;
        public string SectionName;
        public string MeshAssetPath;
        public L2Viewer.SceneDomain.Models.SceneBspMeshSection Section;
        public L2Viewer.UtxFile.ResolvedMaterialGraph ResolvedMaterial;
    }

    public static void BuildBsp(
        L2Viewer.SceneDomain.Models.SceneBspScene bspScene,
        GameObject parent,
        string mapKey,
        string outputDir,
        Action<string> log,
        string assetSubdirName = "Bsp",
        bool includePortalLike = false,
        bool includeInvisibleLike = false,
        bool reuseExistingMaterialTextureAssets = true)
    {
        var rootDir = $"{outputDir}/{assetSubdirName}";
        var meshDir = $"{rootDir}/Meshes";
        var materialDir = L2AssetManager.SharedMaterialsRoot;
        var textureDir = L2AssetManager.SharedTexturesRoot;

        L2AssetManager.EnsureFolderExists(rootDir);
        L2AssetManager.EnsureFolderExists(meshDir);
        L2AssetManager.EnsureFolderExists(materialDir);
        L2AssetManager.EnsureFolderExists(textureDir);

        var textureCache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        var materialCache = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

        var shader = L2MaterialUtility.FindBestLitShader();

        var clientPath = ConstInfo.L2GameClientPath;
        var textureManager = new L2Viewer.SceneDomain.Services.BspTextureManager(clientPath);
        var materialResolver = new L2Viewer.SceneDomain.Services.SceneMaterialResolver(clientPath, textureManager);

        log($"Building {assetSubdirName} BSP with {bspScene.Models.Length} models...");

        log("[BSP] START Material graph resolve");
        var materialResolveStopwatch = Stopwatch.StartNew();
        var materialRefs = CollectMaterialRequests(bspScene);
        log($"BSP material requests: {materialRefs.Count}");
        var resolvedMaterialsBatch = materialResolver.ResolveMany(mapKey, materialRefs);
        materialResolveStopwatch.Stop();
        log($"BSP material graph resolve took {materialResolveStopwatch.Elapsed.TotalSeconds:F2}s");
        log("[BSP] DONE Material graph resolve");

        log("[BSP] START Texture resolve");
        var textureResolveStopwatch = Stopwatch.StartNew();
        var textureRefs = CollectTextureRequests(resolvedMaterialsBatch);
        log($"BSP texture requests: {textureRefs.Count}");
        var resolvedTexturesBatch = textureManager.ResolveMany(textureRefs);
        textureResolveStopwatch.Stop();
        log($"BSP texture resolve took {textureResolveStopwatch.Elapsed.TotalSeconds:F2}s");
        log("[BSP] DONE Texture resolve");

        log("[BSP] START Section preparation");
        var sectionPreparationStopwatch = Stopwatch.StartNew();
        var sectionEntries = CollectSectionEntries(
            bspScene,
            resolvedMaterialsBatch,
            meshDir,
            includePortalLike,
            includeInvisibleLike);
        sectionPreparationStopwatch.Stop();
        log($"BSP sections to import: {sectionEntries.Count}");
        log($"BSP section preparation took {sectionPreparationStopwatch.Elapsed.TotalSeconds:F2}s");
        log("[BSP] DONE Section preparation");

        log("[BSP] START Geometry asset build");
        var geometryStopwatch = Stopwatch.StartNew();
        var meshAssets = BuildMeshAssets(sectionEntries, resolvedTexturesBatch);
        geometryStopwatch.Stop();
        log($"BSP geometry asset build took {geometryStopwatch.Elapsed.TotalSeconds:F2}s");
        log("[BSP] DONE Geometry asset build");

        log("[BSP] START Material asset build");
        var materialAssetStopwatch = Stopwatch.StartNew();
        var materialAssets = BuildMaterialAssets(
            sectionEntries,
            resolvedTexturesBatch,
            mapKey,
            materialDir,
            textureDir,
            textureCache,
            materialCache,
            shader,
            log,
            reuseExistingMaterialTextureAssets);
        materialAssetStopwatch.Stop();
        log($"BSP material asset build took {materialAssetStopwatch.Elapsed.TotalSeconds:F2}s");
        log("[BSP] DONE Material asset build");

        log("[BSP] START Object placement");
        var placementStopwatch = Stopwatch.StartNew();
        BuildSceneHierarchy(
            bspScene,
            parent,
            meshDir,
            meshAssets,
            materialAssets,
            includePortalLike,
            includeInvisibleLike);
        placementStopwatch.Stop();
        log($"BSP object placement took {placementStopwatch.Elapsed.TotalSeconds:F2}s");
        log("[BSP] DONE Object placement");
    }

    private static Dictionary<string, Mesh> BuildMeshAssets(
        IReadOnlyList<BspSectionEntry> sectionEntries,
        IReadOnlyDictionary<string, L2Viewer.SceneDomain.Services.BspTextureManager.ResolvedTexture> resolvedTexturesBatch)
    {
        var meshAssets = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);

        UnityAssetDatabaseUtility.RunAssetEditingBatch(() =>
        {
            foreach (var entry in sectionEntries)
            {
                var mesh = BuildSectionMesh(entry.Section, entry.ResolvedMaterial, resolvedTexturesBatch, entry.SectionName);
                mesh = UnityAssetDatabaseUtility.CreateOrReplaceAsset(mesh, entry.MeshAssetPath);
                meshAssets[entry.MeshAssetPath] = mesh;
            }
        });

        return meshAssets;
    }

    private static Dictionary<string, Material> BuildMaterialAssets(
        IReadOnlyList<BspSectionEntry> sectionEntries,
        IReadOnlyDictionary<string, L2Viewer.SceneDomain.Services.BspTextureManager.ResolvedTexture> resolvedTexturesBatch,
        string mapKey,
        string materialDir,
        string textureDir,
        Dictionary<string, Texture2D> textureCache,
        Dictionary<string, Material> materialCache,
        Shader shader,
        Action<string> log,
        bool reuseExistingMaterialTextureAssets)
    {
        var materialAssets = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in sectionEntries)
        {
            materialAssets[entry.MeshAssetPath] = BuildSectionMaterial(
                entry.Section,
                entry.ResolvedMaterial,
                resolvedTexturesBatch,
                mapKey,
                materialDir,
                textureDir,
                textureCache,
                materialCache,
                shader,
                log,
                reuseExistingMaterialTextureAssets);
        }

        return materialAssets;
    }

    private static void BuildSceneHierarchy(
        L2Viewer.SceneDomain.Models.SceneBspScene bspScene,
        GameObject parent,
        string meshDir,
        IReadOnlyDictionary<string, Mesh> meshAssets,
        IReadOnlyDictionary<string, Material> materialAssets,
        bool includePortalLike,
        bool includeInvisibleLike)
    {
        foreach (var model in bspScene.Models)
        {
            if (model.Chunks == null || model.Chunks.Length == 0)
            {
                continue;
            }

            var modelGo = new GameObject(model.StableName);
            modelGo.isStatic = true;
            modelGo.transform.SetParent(parent.transform, false);

            foreach (var chunk in model.Chunks)
            {
                if ((!includeInvisibleLike && chunk.IsInvisibleLike) ||
                    (!includePortalLike && chunk.IsPortalLike) ||
                    chunk.MeshSections == null ||
                    chunk.MeshSections.Length == 0)
                {
                    continue;
                }

                var chunkGo = new GameObject(chunk.StableName);
                chunkGo.isStatic = true;
                chunkGo.transform.SetParent(modelGo.transform, false);

                for (var sectionIndex = 0; sectionIndex < chunk.MeshSections.Length; sectionIndex++)
                {
                    var section = chunk.MeshSections[sectionIndex];
                    if (ShouldSkipSection(section) ||
                        section.Indices == null ||
                        section.Indices.Length == 0 ||
                        section.Positions == null ||
                        section.Positions.Length == 0)
                    {
                        continue;
                    }

                    var sectionName = section.StableName;
                    var sectionAssetPath = BuildSectionAssetPath(meshDir, model.StableName, chunk.StableName, section.StableName);
                    if (!meshAssets.TryGetValue(sectionAssetPath, out var mesh) ||
                        !materialAssets.TryGetValue(sectionAssetPath, out var material))
                    {
                        continue;
                    }

                    var sectionGo = new GameObject(sectionName);
                    sectionGo.isStatic = true;
                    sectionGo.transform.SetParent(chunkGo.transform, false);

                    var filter = sectionGo.AddComponent<MeshFilter>();
                    filter.sharedMesh = mesh;

                    var renderer = sectionGo.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                }
            }
        }
    }

    private static List<BspSectionEntry> CollectSectionEntries(
        L2Viewer.SceneDomain.Models.SceneBspScene bspScene,
        IReadOnlyDictionary<string, L2Viewer.UtxFile.ResolvedMaterialGraph> resolvedMaterialsBatch,
        string meshDir,
        bool includePortalLike,
        bool includeInvisibleLike)
    {
        var entries = new List<BspSectionEntry>();

        foreach (var model in bspScene.Models)
        {
            if (model.Chunks == null || model.Chunks.Length == 0)
            {
                continue;
            }

            foreach (var chunk in model.Chunks)
            {
                if ((!includeInvisibleLike && chunk.IsInvisibleLike) ||
                    (!includePortalLike && chunk.IsPortalLike) ||
                    chunk.MeshSections == null ||
                    chunk.MeshSections.Length == 0)
                {
                    continue;
                }

                for (var sectionIndex = 0; sectionIndex < chunk.MeshSections.Length; sectionIndex++)
                {
                    var section = chunk.MeshSections[sectionIndex];
                    if (ShouldSkipSection(section) ||
                        section.Indices == null ||
                        section.Indices.Length == 0 ||
                        section.Positions == null ||
                        section.Positions.Length == 0)
                    {
                        continue;
                    }

                    L2Viewer.UtxFile.ResolvedMaterialGraph resolvedMaterial = null;
                    var materialLookupKey = $"{section.MaterialPackageName}.{section.MaterialObjectName}";
                    resolvedMaterialsBatch?.TryGetValue(materialLookupKey, out resolvedMaterial);

                    entries.Add(new BspSectionEntry
                    {
                        ModelName = model.StableName,
                        ChunkName = chunk.StableName,
                        Section = section,
                        ResolvedMaterial = resolvedMaterial,
                        SectionName = section.StableName,
                        MeshAssetPath = BuildSectionAssetPath(meshDir, model.StableName, chunk.StableName, section.StableName)
                    });
                }
            }
        }

        return entries;
    }

    private static List<L2Viewer.SceneDomain.Services.SceneMaterialRequest> CollectMaterialRequests(L2Viewer.SceneDomain.Models.SceneBspScene bspScene)
    {
        var materialRefs = new List<L2Viewer.SceneDomain.Services.SceneMaterialRequest>();

        foreach (var model in bspScene.Models)
        {
            if (model.Chunks == null)
            {
                continue;
            }

            foreach (var chunk in model.Chunks)
            {
                if (chunk.MeshSections == null)
                {
                    continue;
                }

                foreach (var section in chunk.MeshSections)
                {
                    if (!string.IsNullOrEmpty(section.MaterialPackageName) || !string.IsNullOrEmpty(section.MaterialObjectName))
                    {
                        materialRefs.Add(new L2Viewer.SceneDomain.Services.SceneMaterialRequest(section.MaterialPackageName, section.MaterialObjectName));
                    }
                }
            }
        }

        return materialRefs;
    }

    private static List<L2Viewer.SceneDomain.Services.SceneTextureRequest> CollectTextureRequests(
        IReadOnlyDictionary<string, L2Viewer.UtxFile.ResolvedMaterialGraph> resolvedMaterialsBatch)
    {
        var textureRefs = new List<L2Viewer.SceneDomain.Services.SceneTextureRequest>();
        if (resolvedMaterialsBatch == null)
        {
            return textureRefs;
        }

        foreach (var resolvedMaterial in resolvedMaterialsBatch.Values)
        {
            if (resolvedMaterial?.TextureSlots == null)
            {
                continue;
            }

            foreach (var slot in resolvedMaterial.TextureSlots)
            {
                if (!string.IsNullOrEmpty(slot.PackageName) || !string.IsNullOrEmpty(slot.ObjectName))
                {
                    textureRefs.Add(new L2Viewer.SceneDomain.Services.SceneTextureRequest(slot.PackageName, slot.ObjectName));
                }
            }
        }

        return textureRefs;
    }

    private static string BuildSectionAssetPath(string meshDir, string modelName, string chunkName, string sectionName)
    {
        return $"{meshDir}/{modelName}_{chunkName}_{sectionName}.asset";
    }

    private static Mesh BuildSectionMesh(L2Viewer.SceneDomain.Models.SceneBspMeshSection section, L2Viewer.UtxFile.ResolvedMaterialGraph resolvedMaterial, System.Collections.Generic.IReadOnlyDictionary<string, L2Viewer.SceneDomain.Services.BspTextureManager.ResolvedTexture> resolvedTexturesBatch, string sectionName)
    {
        var mesh = new Mesh
        {
            name = sectionName,
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };

        mesh.vertices = section.Positions.Select(ConvertPosition).ToArray();
        mesh.normals = section.Normals.Select(ConvertNormal).ToArray();
        mesh.uv = BuildSectionUvs(section, resolvedMaterial, resolvedTexturesBatch);
        mesh.triangles = ReverseTriangleWinding(section.Indices);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    private static Vector2[] BuildSectionUvs(L2Viewer.SceneDomain.Models.SceneBspMeshSection section, L2Viewer.UtxFile.ResolvedMaterialGraph resolvedMaterial, System.Collections.Generic.IReadOnlyDictionary<string, L2Viewer.SceneDomain.Services.BspTextureManager.ResolvedTexture> resolvedTexturesBatch)
    {
        if (section.TextureCoordinates == null || section.TextureCoordinates.Length == 0)
        {
            return Array.Empty<Vector2>();
        }

        L2Viewer.PackageCore.TextureData slotTexture = null;
        var firstSlot = resolvedMaterial?.TextureSlots?.FirstOrDefault();
        if (firstSlot != null) {
            if (firstSlot.Texture != null) {
                slotTexture = firstSlot.Texture;
            } else if (resolvedTexturesBatch != null) {
                var texKeyLookup = $"{firstSlot.PackageName}.{firstSlot.ObjectName}";
                if (resolvedTexturesBatch.ContainsKey(texKeyLookup)) {
                    var resolvedTex = resolvedTexturesBatch[texKeyLookup];
                    if (resolvedTex != null) slotTexture = resolvedTex.Texture;
                }
            }
        }

        if (slotTexture == null || slotTexture.Width <= 0 || slotTexture.Height <= 0)
        {
            return section.TextureCoordinates
                .Select(x => new Vector2(x.X, x.Y))
                .ToArray();
        }

        return section.TextureCoordinates
            .Select(x => new Vector2(x.X / slotTexture.Width, 1f - (x.Y / slotTexture.Height)))
            .ToArray();
    }

    private static int[] ReverseTriangleWinding(int[] source)
    {
        var result = new int[source.Length];
        for (var i = 0; i < source.Length; i += 3)
        {
            result[i] = source[i];
            result[i + 1] = source[i + 2];
            result[i + 2] = source[i + 1];
        }

        return result;
    }

    private static Material BuildSectionMaterial(
        L2Viewer.SceneDomain.Models.SceneBspMeshSection section,
        L2Viewer.UtxFile.ResolvedMaterialGraph resolvedMaterial,
        System.Collections.Generic.IReadOnlyDictionary<string, L2Viewer.SceneDomain.Services.BspTextureManager.ResolvedTexture> resolvedTexturesBatch,
        string mapKey,
        string materialDir,
        string textureDir,
        Dictionary<string, Texture2D> textureCache,
        Dictionary<string, Material> materialCache,
        Shader shader,
        Action<string> log,
        bool reuseExistingMaterialTextureAssets)
    {
        var traits = resolvedMaterial == null
            ? null
            : L2Viewer.SceneDomain.Services.MaterialHeuristics.GetKnownTraits(resolvedMaterial);
        var blendHint = traits?.BlendModeHint.ToString() ?? "Opaque";
        var materialKey = $"{section.MaterialReference ?? section.StableName}_{blendHint}";
        if (materialCache.TryGetValue(materialKey, out var cached))
        {
            return cached;
        }

        var materialReference = L2AssetManager.BuildReferenceText(
            section.MaterialPackageName,
            section.MaterialObjectName,
            section.MaterialReference ?? section.StableName);
        var materialPath = L2AssetManager.BuildClientPackageAssetPath(
            materialDir,
            materialReference,
            "MAT",
            "mat",
            $"{mapKey}/BspMaterials",
            blendHint);
        if (reuseExistingMaterialTextureAssets)
        {
            var existingMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (existingMaterial != null)
            {
                materialCache[materialKey] = existingMaterial;
                return existingMaterial;
            }
        }

        var material = new Material(shader);
        material = UnityAssetDatabaseUtility.CreateOrReplaceAsset(material, materialPath);

        var textureChoice = ResolveDirectGraphTexture(section, resolvedMaterial, resolvedTexturesBatch, mapKey, textureDir, textureCache, traits, log, reuseExistingMaterialTextureAssets);
        if (textureChoice.Texture != null)
        {
            L2MaterialUtility.AssignMainTexture(material, textureChoice.Texture);
        }
        else if (section.ColorArgb != 0)
        {
            var bytes = BitConverter.GetBytes(section.ColorArgb);
            var color = new Color32(bytes[2], bytes[1], bytes[0], bytes[3]);
            L2MaterialUtility.SetBaseColor(material, color);
        }

        if (traits != null)
        {
            L2AssetManager.ApplyMaterialTraits(material, traits, L2MaterialUtility.IsHdrp(shader));
        }

        EditorUtility.SetDirty(material);
        materialCache[materialKey] = material;
        return material;
    }

    private static (Texture2D Texture, string Reference) ResolveDirectGraphTexture(
        L2Viewer.SceneDomain.Models.SceneBspMeshSection section,
        L2Viewer.UtxFile.ResolvedMaterialGraph resolvedMaterial,
        System.Collections.Generic.IReadOnlyDictionary<string, L2Viewer.SceneDomain.Services.BspTextureManager.ResolvedTexture> resolvedTexturesBatch,
        string mapKey,
        string textureDir,
        Dictionary<string, Texture2D> textureCache,
        L2Viewer.SceneDomain.Services.MaterialKnownTraits traits,
        Action<string> log,
        bool reuseExistingMaterialTextureAssets)
    {
        if (resolvedMaterial?.TextureSlots == null || resolvedMaterial.TextureSlots.Count == 0)
        {
            log($"BSP texture missing: section={section.StableName} mat={section.MaterialReference ?? "<null>"} primary={section.PrimaryTextureReference ?? "<null>"} slots=[<no material graph>]");
            return (null, null);
        }

        var firstSlotWithTexture = resolvedMaterial.TextureSlots.FirstOrDefault();
        if (firstSlotWithTexture == null)
        {
            return (null, null);
        }

        var reference = firstSlotWithTexture.Reference ?? BuildReference(firstSlotWithTexture.PackageName, firstSlotWithTexture.ObjectName);
        if (!textureCache.TryGetValue(reference, out var texture))
        {
            var texturePath = L2AssetManager.BuildClientPackageAssetPath(
                textureDir,
                reference,
                "TEX",
                "png",
                $"{mapKey}/BspTextures");
            if (reuseExistingMaterialTextureAssets)
            {
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (texture != null)
                {
                    textureCache[reference] = texture;
                    log($"BSP texture reused: section={section.StableName} ref={reference}");
                    return (texture, reference);
                }
            }

            L2Viewer.PackageCore.TextureData textureData = null;
            if (firstSlotWithTexture.Texture != null) {
                 textureData = firstSlotWithTexture.Texture;
            } else if (resolvedTexturesBatch != null) {
                var texKeyLookup = $"{firstSlotWithTexture.PackageName}.{firstSlotWithTexture.ObjectName}";
                if (resolvedTexturesBatch.ContainsKey(texKeyLookup)) {
                    var resolvedTex = resolvedTexturesBatch[texKeyLookup];
                    if (resolvedTex != null) textureData = resolvedTex.Texture;
                }
            }

            if (textureData != null)
            {
                var needsAlpha = NeedsAlpha(traits);
                texture = L2AssetManager.CreateTextureAsset(textureData, texturePath, false, needsAlpha);
                if (texture != null)
                {
                    textureCache[reference] = texture;
                }
            }
        }

        if (texture == null)
        {
            log($"BSP texture asset load failed: section={section.StableName} source=MaterialSlot:{firstSlotWithTexture.SlotName} ref={reference}");
            return (null, reference);
        }

        log($"BSP texture: section={section.StableName} source=MaterialSlot:{firstSlotWithTexture.SlotName} ref={reference}");
        return (texture, reference);
    }

    private static bool NeedsAlpha(L2Viewer.SceneDomain.Services.MaterialKnownTraits traits)
    {
        if (traits == null)
        {
            return false;
        }

        return traits.BlendModeHint == L2Viewer.SceneDomain.Services.MaterialBlendModeHint.Translucent ||
               traits.BlendModeHint == L2Viewer.SceneDomain.Services.MaterialBlendModeHint.Additive ||
               traits.BlendModeHint == L2Viewer.SceneDomain.Services.MaterialBlendModeHint.Modulated ||
               traits.HasOpacityInput ||
               traits.HasMaskInput;
    }

    private static string BuildReference(string packageName, string objectName)
    {
        if (string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(objectName))
        {
            return objectName ?? packageName ?? "Texture";
        }

        return $"{packageName}.{objectName}";
    }

    private static bool ShouldSkipSection(SceneBspMeshSection section)
    {
        if ((section.KnownPolyFlags & L2Viewer.UnrFile.UnrPolyFlags.FakeBackdrop) != 0)
        {
            return true;
        }

        var unknownMask = section.UnknownPolyFlagsMask;
        return (unknownMask & UnknownPolyFlag02000) != 0 &&
               (unknownMask & UnknownPolyFlag08000) != 0;
    }

    private static Vector3 ConvertPosition(System.Numerics.Vector3 raw)
    {
        return new Vector3(raw.X * UnrealToUnityScale, raw.Z * UnrealToUnityScale, raw.Y * UnrealToUnityScale);
    }

    private static Vector3 ConvertNormal(System.Numerics.Vector3 raw)
    {
        return new Vector3(raw.X, raw.Z, raw.Y);
    }
}

