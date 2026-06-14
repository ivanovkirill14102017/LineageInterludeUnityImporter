using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using L2Viewer.PackageCore;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using L2Viewer.UtxFile;
using UnityEditor;
using UnityEngine;

internal static class AngelNpcImportBuilder
{
    public const string OutputRoot = "Assets/L2Imported/Npcs";

    internal readonly struct ImportOptions
    {
        public ImportOptions(string actorXPskPath, string actorXPsaPath)
        {
            ActorXPskPath = actorXPskPath ?? string.Empty;
            ActorXPsaPath = actorXPsaPath ?? string.Empty;
        }

        public string ActorXPskPath { get; }
        public string ActorXPsaPath { get; }
    }

    internal readonly struct ImportResult
    {
        public ImportResult(string prefabPath)
        {
            PrefabPath = prefabPath;
        }

        public string PrefabPath { get; }
    }

    public static ImportResult Import(string clientRoot, string packageRelativePath, string meshName, ImportOptions options, Action<string> log)
    {
        var packagePath = Path.Combine(clientRoot ?? string.Empty, packageRelativePath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
        {
            throw new FileNotFoundException("UKX package was not found.", packagePath);
        }

        if (string.IsNullOrWhiteSpace(meshName))
        {
            throw new InvalidOperationException("Mesh name is required.");
        }

        log($"Reading shared skeletal asset from package: {packagePath}");
        var resolver = new SceneSkeletalMeshResolver();
        var sharedAsset = resolver.ResolveAssetNamed(packagePath, meshName);
        var preview = resolver.ResolveNamed(packagePath, meshName);
        var bindPoseMesh = preview.Session.BuildBindPoseMesh();
        log($"Shared asset loaded. Bones={sharedAsset.Skeleton.Bones.Count}, Points={sharedAsset.Mesh.Points.Count}, Faces={sharedAsset.Mesh.Faces.Count}, Sequences={sharedAsset.AnimationSet.Sequences.Count}.");

        var characterName = string.IsNullOrWhiteSpace(meshName) ? sharedAsset.MeshObjectName : meshName;
        var characterAsset = BuildCharacterAsset(characterName, sharedAsset);
        var outputDir = $"{OutputRoot}/{AssetNameUtility.SanitizeName(characterName)}";
        L2AssetManager.EnsureFolderExists(OutputRoot);
        L2AssetManager.EnsureFolderExists(outputDir);

        var assetPath = $"{outputDir}/NPC_{AssetNameUtility.SanitizeName(characterName)}.asset";
        var meshAsset = UnityAssetDatabaseUtility.CreateOrReplaceAsset(characterAsset, assetPath);
        log($"Character asset updated: {assetPath}");

        var sourceMeshPath = $"{outputDir}/SM_{AssetNameUtility.SanitizeName(characterName)}_diagnostic.asset";
        var sourceMesh = UnityAssetDatabaseUtility.CreateOrReplaceAsset(
            L2SceneSkeletalAssetBridge.BuildUnityMesh(bindPoseMesh, $"SM_{AssetNameUtility.SanitizeName(characterName)}_diagnostic"),
            sourceMeshPath);
        log($"Diagnostic mesh asset updated: {sourceMeshPath}");

        var textures = ImportTextures(meshAsset, clientRoot, characterName, log);
        var materials = CreateMaterials(meshAsset, outputDir, textures, log);
        var prefabPath = $"{outputDir}/PF_{AssetNameUtility.SanitizeName(characterName)}.prefab";
        CreatePrefab(meshAsset, sourceMesh, materials, prefabPath);
        log($"Prefab updated: {prefabPath}");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab != null)
        {
            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance != null)
            {
                instance.name = prefab.name;
                Selection.activeObject = instance;
                log("Prefab instantiated into the current scene and selected.");
            }
        }

        return new ImportResult(prefabPath);
    }

    private static L2SkeletalCharacterAsset BuildCharacterAsset(string characterName, SceneSkeletalAsset source)
    {
        var asset = ScriptableObject.CreateInstance<L2SkeletalCharacterAsset>();
        asset.CharacterName = characterName;
        asset.SourcePackagePath = source.PackagePath ?? string.Empty;
        asset.MeshExportIndex = source.MeshExportIndex;
        asset.MeshObjectName = source.MeshObjectName ?? characterName;
        asset.AnimationObjectName = source.AnimationObjectName ?? string.Empty;
        asset.PrimaryTextureReference = source.PrimaryTextureReference ?? string.Empty;
        asset.BoundsMin = ToUnityRaw(source.Mesh.BoundsMin);
        asset.BoundsMax = ToUnityRaw(source.Mesh.BoundsMax);
        asset.UsedTextures = source.UsedTextures
            .Select(x => new L2SkeletalTextureRefData
            {
                Reference = x.Reference,
                ResolvedPackagePath = x.ResolvedPackagePath
            })
            .ToArray();
        asset.MaterialBindings = source.MaterialBindings
            .Select(x => new L2SkeletalMaterialBindingData
            {
                MaterialId = x.MaterialId,
                PackageName = x.PackageName,
                ObjectName = x.ObjectName,
                TextureReference = x.TextureReference,
                ResolvedPackagePath = x.ResolvedPackagePath
            })
            .ToArray();
        asset.Bones = source.Skeleton.Bones
            .OrderBy(x => x.Index)
            .Select(x => new L2SkeletalBoneData
            {
                Name = x.Name,
                ParentIndex = x.ParentIndex,
                RawBindPosition = ToUnityRaw(x.RawBindPosition),
                RawBindRotation = ToUnityRaw(x.RawBindRotation),
                StoredOrigLocation = ToUnityRaw(x.StoredOrigLocation),
                StoredOrigQuaternion = ToUnityRaw(x.StoredOrigQuaternion),
                PostQuaternion = ToUnityRaw(x.PostQuaternion),
                IsRoot = x.IsRoot,
                DontInvertRoot = x.DontInvertRoot
            })
            .ToArray();
        asset.Points = source.Mesh.Points
            .Select(x => new L2SkeletalPointData
            {
                Position = ToUnityRaw(x.Position)
            })
            .ToArray();
        asset.Wedges = source.Mesh.Wedges
            .Select(x => new L2SkeletalWedgeData
            {
                PointIndex = x.PointIndex,
                UV = new Vector2(x.UV.X, x.UV.Y),
                MaterialIndex = x.MaterialIndex
            })
            .ToArray();
        asset.Faces = source.Mesh.Faces
            .Select(x => new L2SkeletalFaceData
            {
                WedgeIndex0 = x.WedgeIndex0,
                WedgeIndex1 = x.WedgeIndex1,
                WedgeIndex2 = x.WedgeIndex2,
                MaterialIndex = x.MaterialIndex
            })
            .ToArray();
        asset.Weights = source.Mesh.Weights
            .Select(x => new L2SkeletalWeightData
            {
                Weight = x.Weight,
                PointIndex = x.PointIndex,
                BoneIndex = x.BoneIndex
            })
            .ToArray();
        asset.SubMeshes = source.Mesh.SubMeshes
            .Select(x => new L2SkeletalSubMeshData
            {
                MaterialId = x.MaterialId,
                FaceCount = x.FaceCount
            })
            .ToArray();
        asset.AnimationBones = source.AnimationSet.Bones
            .OrderBy(x => x.Index)
            .Select(x => new L2SkeletalAnimationBoneData
            {
                Name = x.Name,
                ParentIndex = x.ParentIndex
            })
            .ToArray();
        asset.AnimationSequences = source.AnimationSet.Sequences
            .Select(x => new L2SkeletalAnimationSequenceData
            {
                Name = x.Name,
                TotalBones = x.TotalBones,
                TrackTime = x.TrackTime,
                AnimRate = x.AnimRate,
                FirstRawFrame = x.FirstRawFrame,
                NumRawFrames = x.NumRawFrames
            })
            .ToArray();
        asset.AnimationKeys = source.AnimationSet.Keys
            .Select(x => new L2SkeletalAnimationKeyData
            {
                Position = ToUnityRaw(x.Position),
                Orientation = ToUnityRaw(x.Orientation),
                Time = x.Time
            })
            .ToArray();
        return asset;
    }

    private static Dictionary<string, Texture2D> ImportTextures(L2SkeletalCharacterAsset asset, string clientRoot, string characterName, Action<string> log)
    {
        var result = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        if (asset == null || string.IsNullOrWhiteSpace(clientRoot))
        {
            return result;
        }

        var textureDir = L2AssetManager.SharedTexturesRoot;
        L2AssetManager.EnsureFolderExists(textureDir);

        var bindings = asset.MaterialBindings ?? Array.Empty<L2SkeletalMaterialBindingData>();
        if (bindings.Length == 0)
        {
            log($"No skeletal material bindings were found for {characterName}.");
            return result;
        }

        var textureManager = new BspTextureManager(clientRoot);
        var materialResolver = new SceneMaterialResolver(clientRoot, textureManager);
        var hasAnyMaterialRoot = false;

        foreach (var binding in bindings.Where(x => x != null))
        {
            if (string.IsNullOrWhiteSpace(binding.PackageName) || string.IsNullOrWhiteSpace(binding.ObjectName))
            {
                continue;
            }

            hasAnyMaterialRoot = true;
            var resolvedTexture = ResolveExactTextureBinding(asset.SourcePackagePath, binding, materialResolver, textureManager);
            if (resolvedTexture == null || resolvedTexture.Texture == null || string.IsNullOrWhiteSpace(resolvedTexture.Reference))
            {
                log($"No exact texture could be resolved from material root {binding.PackageName}.{binding.ObjectName} for material {binding.MaterialId} on {characterName}.");
                continue;
            }

            binding.TextureReference = resolvedTexture.Reference;
            binding.ResolvedPackagePath = resolvedTexture.ResolvedPackagePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(asset.PrimaryTextureReference))
            {
                asset.PrimaryTextureReference = resolvedTexture.Reference;
            }

            if (result.ContainsKey(resolvedTexture.Reference))
            {
                continue;
            }

            var texturePath = L2AssetManager.BuildClientPackageAssetPath(
                textureDir,
                resolvedTexture.Reference,
                "TEX",
                "png",
                $"{AssetNameUtility.SanitizeName(characterName)}/SkeletalTextures");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath) ??
                          L2AssetManager.CreateTextureAsset(
                              resolvedTexture.Texture,
                              texturePath,
                              false,
                              StaticMeshImportUtility.NeedsAlpha(resolvedTexture.Traits));
            if (texture != null)
            {
                result[resolvedTexture.Reference] = texture;
                log($"Texture imported: {resolvedTexture.Reference}");
            }
        }

        if (!hasAnyMaterialRoot)
        {
            log($"No exact skeletal material bindings with material roots were found for {characterName}.");
        }

        return result;
    }

    private static ResolvedSkeletalTextureBinding ResolveExactTextureBinding(
        string sourcePackagePath,
        L2SkeletalMaterialBindingData binding,
        SceneMaterialResolver materialResolver,
        BspTextureManager textureManager)
    {
        if (binding == null || string.IsNullOrWhiteSpace(binding.PackageName) || string.IsNullOrWhiteSpace(binding.ObjectName))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(binding.TextureReference))
        {
            var resolvedDirectTexture = textureManager.ResolveMany(new[]
            {
                new SceneTextureRequest(binding.PackageName, binding.ObjectName)
            });
            var directKey = $"{binding.PackageName}.{binding.ObjectName}";
            if (resolvedDirectTexture.TryGetValue(directKey, out var directTexture) && directTexture?.Texture != null)
            {
                return new ResolvedSkeletalTextureBinding(
                    binding.TextureReference,
                    binding.ResolvedPackagePath,
                    directTexture.Texture,
                    traits: null);
            }
        }

        var graph = materialResolver.ResolveMany(
                sourcePackagePath,
                new[]
                {
                    new SceneMaterialRequest(binding.PackageName, binding.ObjectName)
                })
            .Values
            .FirstOrDefault();
        if (graph == null)
        {
            return null;
        }

        var preferredSlot = MaterialTextureSlotOrdering.GetPreferredTextureSlot(graph.TextureSlots);
        if (preferredSlot == null)
        {
            return null;
        }

        var texture = preferredSlot.Texture;
        if (texture == null)
        {
            var resolvedTexture = textureManager.ResolveMany(new[]
            {
                new SceneTextureRequest(preferredSlot.PackageName, preferredSlot.ObjectName)
            });
            resolvedTexture.TryGetValue(preferredSlot.Reference, out var resolved);
            texture = resolved?.Texture;
        }

        return new ResolvedSkeletalTextureBinding(
            preferredSlot.Reference,
            preferredSlot.PackagePath,
            texture,
            MaterialHeuristics.GetKnownTraits(graph));
    }

    private static Material[] CreateMaterials(L2SkeletalCharacterAsset asset, string outputDir, Dictionary<string, Texture2D> textures, Action<string> log)
    {
        var shader = StaticMeshImportUtility.FindDefaultShader() ?? Shader.Find("Standard");
        var materialCount = asset.SubMeshes == null || asset.SubMeshes.Length == 0 ? 1 : asset.SubMeshes.Length;
        var materials = new Material[materialCount];
        for (var i = 0; i < materialCount; i++)
        {
            var materialId = asset.SubMeshes != null && i < asset.SubMeshes.Length ? asset.SubMeshes[i].MaterialId : i;
            var materialPath = $"{outputDir}/MAT_{AssetNameUtility.SanitizeName(asset.CharacterName)}_{materialId:00}.mat";
            var material = new Material(shader)
            {
                name = Path.GetFileNameWithoutExtension(materialPath),
                color = Color.HSVToRGB((i * 0.17f) % 1f, 0.35f, 0.92f)
            };

            var subMeshTexture = ResolveTextureForMaterial(asset, materialId, textures);
            if (subMeshTexture != null)
            {
                AssignMainTexture(material, subMeshTexture);
                SetMaterialBaseColor(material, Color.white);
            }
            else
            {
                log($"No exact texture resolved for material {materialId} on {asset.CharacterName}.");
            }

            materials[i] = UnityAssetDatabaseUtility.CreateOrReplaceAsset(material, materialPath);
        }

        return materials;
    }

    private static void CreatePrefab(L2SkeletalCharacterAsset asset, Mesh sourceMesh, Material[] materials, string prefabPath)
    {
        var root = new GameObject($"NPC_{AssetNameUtility.SanitizeName(asset.CharacterName)}");
        try
        {
            var meshFilter = root.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = sourceMesh;
            var meshRenderer = root.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterials = materials;

            var player = root.AddComponent<L2SkeletalAnimationPlayer>();
            player.Asset = asset;
            player.SourceMesh = sourceMesh;
            player.PoseMode = asset.AnimationSequences != null && asset.AnimationSequences.Length > 0
                ? L2SkeletalAnimationPlayer.DiagnosticPoseMode.Animated
                : L2SkeletalAnimationPlayer.DiagnosticPoseMode.BindPose;
            player.PlayOnEnable = false;
            player.Loop = asset.AnimationSequences != null && asset.AnimationSequences.Length > 0;
            player.PreviewInEditMode = true;
            player.PlaybackSpeed = 1f;
            player.IsPlaying = false;
            player.PreviewTimeSeconds = 0f;
            player.DrawBoneLabels = true;
            player.BoneMarkerSize = 0.02f;
            player.BoneAxisLength = 0.04f;
            player.ShowMesh = true;
            player.ShowSkeleton = true;
            EditorUtility.SetDirty(meshFilter);
            EditorUtility.SetDirty(meshRenderer);
            EditorUtility.SetDirty(player);
            EditorUtility.SetDirty(root);

            L2AssetManager.EnsureParentFolderExists(prefabPath);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static Vector3 ToUnityRaw(System.Numerics.Vector3 value)
    {
        return new Vector3(value.X, value.Y, value.Z);
    }

    private static Quaternion ToUnityRaw(System.Numerics.Quaternion value)
    {
        return new Quaternion(value.X, value.Y, value.Z, value.W);
    }

    private static Texture2D ResolveTextureForMaterial(L2SkeletalCharacterAsset asset, int materialId, Dictionary<string, Texture2D> textures)
    {
        if (asset == null || textures == null || textures.Count == 0)
        {
            return null;
        }

        if (asset.MaterialBindings != null)
        {
            var binding = asset.MaterialBindings.FirstOrDefault(x => x != null && x.MaterialId == materialId && !string.IsNullOrWhiteSpace(x.TextureReference));
            if (binding != null &&
                textures.TryGetValue(binding.TextureReference, out var boundTexture))
            {
                return boundTexture;
            }
        }

        return null;
    }

    private static void AssignMainTexture(Material material, Texture2D texture)
    {
        L2MaterialUtility.AssignMainTexture(material, texture);
    }

    private static void SetMaterialBaseColor(Material material, Color color)
    {
        L2MaterialUtility.SetBaseColor(material, color);
    }

    private sealed class ResolvedSkeletalTextureBinding
    {
        public ResolvedSkeletalTextureBinding(string reference, string resolvedPackagePath, TextureData texture, MaterialKnownTraits traits)
        {
            Reference = reference;
            ResolvedPackagePath = resolvedPackagePath;
            Texture = texture;
            Traits = traits;
        }

        public string Reference { get; }
        public string ResolvedPackagePath { get; }
        public TextureData Texture { get; }
        public MaterialKnownTraits Traits { get; }
    }
}
