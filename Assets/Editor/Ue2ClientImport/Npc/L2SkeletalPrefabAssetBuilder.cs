using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using L2Viewer.PackageCore;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using L2Viewer.UtxFile;
using UnityEditor;
using UnityEngine;

internal static class L2SkeletalPrefabAssetBuilder
{
    internal sealed class BuildContext
    {
        public BuildContext(string clientRoot)
        {
            ClientRoot = clientRoot ?? string.Empty;
            TextureManager = new BspTextureManager(ClientRoot);
            MaterialResolver = new SceneMaterialResolver(ClientRoot, TextureManager);
        }

        public string ClientRoot { get; }
        public BspTextureManager TextureManager { get; }
        public SceneMaterialResolver MaterialResolver { get; }
    }

    internal readonly struct BuildResult
    {
        public BuildResult(string prefabPath, GameObject prefab, L2SkeletalCharacterAsset characterAsset, Mesh sourceMesh)
        {
            PrefabPath = prefabPath;
            Prefab = prefab;
            CharacterAsset = characterAsset;
            SourceMesh = sourceMesh;
        }

        public string PrefabPath { get; }
        public GameObject Prefab { get; }
        public L2SkeletalCharacterAsset CharacterAsset { get; }
        public Mesh SourceMesh { get; }
    }

    public static BuildContext CreateBuildContext(string clientRoot)
    {
        return new BuildContext(clientRoot);
    }

    public static BuildResult BuildFromResolvedAsset(
        string clientRoot,
        SceneSkeletalAsset sharedAsset,
        string prefabRoot,
        string assetRoot,
        string referenceText,
        string prefabNameSuffix,
        string displayLabel,
        Action<string> log,
        BuildContext context = null,
        bool finalizeAssets = true)
    {
        if (sharedAsset == null)
        {
            throw new ArgumentNullException(nameof(sharedAsset));
        }

        var characterName = string.IsNullOrWhiteSpace(sharedAsset.MeshObjectName)
            ? "L2SkeletalCharacter"
            : sharedAsset.MeshObjectName;
        var labelText = string.IsNullOrWhiteSpace(displayLabel) ? characterName : displayLabel.Trim();
        var activeContext = context ?? CreateBuildContext(clientRoot);

        L2AssetManager.EnsureFolderExists(assetRoot);
        L2AssetManager.EnsureFolderExists(prefabRoot);

        var characterAsset = BuildCharacterAsset(characterName, sharedAsset);
        var assetPath = L2AssetManager.BuildClientPackageAssetPath(
            assetRoot,
            referenceText,
            "NPC",
            "asset",
            "SkeletalCharacters");
        var meshAsset = UnityAssetDatabaseUtility.CreateOrReplaceAsset(characterAsset, assetPath);
        log?.Invoke($"[SkeletalPrefab] Character asset updated: {assetPath}");

        var bindPoseMesh = ActorXSkeletalAnimationPreviewSession.Create(sharedAsset).BuildBindPoseMesh();
        var sourceMeshPath = L2AssetManager.BuildClientPackageAssetPath(
            assetRoot,
            referenceText,
            "SM",
            "asset",
            "SkeletalCharacters",
            "diagnostic");
        var sourceMesh = UnityAssetDatabaseUtility.CreateOrReplaceAsset(
            L2SceneSkeletalAssetBridge.BuildUnityMesh(bindPoseMesh, $"SM_{characterName}_diagnostic"),
            sourceMeshPath);
        log?.Invoke($"[SkeletalPrefab] Diagnostic mesh updated: {sourceMeshPath}");

        var materials = CreateMaterials(meshAsset, referenceText, assetRoot, log, activeContext);
        var prefabPath = L2AssetManager.BuildClientPackageAssetPath(
            prefabRoot,
            referenceText,
            "PF",
            "prefab",
            "CreaturePrefabs",
            prefabNameSuffix);
        CreatePrefab(meshAsset, sourceMesh, materials, prefabPath, labelText);
        if (finalizeAssets)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(sourceMeshPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }

        var prefab = finalizeAssets ? AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) : null;
        return new BuildResult(prefabPath, prefab, meshAsset, sourceMesh);
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

    private static Material[] CreateMaterials(L2SkeletalCharacterAsset asset, string referenceText, string assetRoot, Action<string> log, BuildContext context)
    {
        var textures = ImportTextures(asset, referenceText, log, context);
        var shader = StaticMeshImportUtility.FindDefaultShader() ?? Shader.Find("Standard");
        var materialCount = asset.SubMeshes == null || asset.SubMeshes.Length == 0 ? 1 : asset.SubMeshes.Length;
        var materials = new Material[materialCount];
        for (var i = 0; i < materialCount; i++)
        {
            var materialId = asset.SubMeshes != null && i < asset.SubMeshes.Length ? asset.SubMeshes[i].MaterialId : i;
            var materialPath = L2AssetManager.BuildClientPackageAssetPath(
                assetRoot,
                $"{referenceText}.Material{materialId:00}",
                "MAT",
                "mat",
                "SkeletalMaterials");
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
                log?.Invoke($"[SkeletalPrefab] No exact texture resolved for material {materialId} on {asset.CharacterName}.");
            }

            materials[i] = UnityAssetDatabaseUtility.CreateOrReplaceAsset(material, materialPath);
        }

        return materials;
    }

    private static Dictionary<string, Texture2D> ImportTextures(L2SkeletalCharacterAsset asset, string referenceText, Action<string> log, BuildContext context)
    {
        var result = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        if (asset == null || context == null || string.IsNullOrWhiteSpace(context.ClientRoot))
        {
            return result;
        }

        var textureDir = L2AssetManager.SharedTexturesRoot;
        L2AssetManager.EnsureFolderExists(textureDir);

        var bindings = asset.MaterialBindings ?? Array.Empty<L2SkeletalMaterialBindingData>();
        if (bindings.Length == 0)
        {
            log?.Invoke($"[SkeletalPrefab] No skeletal material bindings were found for {referenceText}.");
            return result;
        }

        foreach (var binding in bindings.Where(x => x != null))
        {
            if (string.IsNullOrWhiteSpace(binding.PackageName) || string.IsNullOrWhiteSpace(binding.ObjectName))
            {
                continue;
            }

            var resolvedTexture = ResolveExactTextureBinding(asset.SourcePackagePath, binding, context.MaterialResolver, context.TextureManager);
            if (resolvedTexture == null || resolvedTexture.Texture == null || string.IsNullOrWhiteSpace(resolvedTexture.Reference))
            {
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
                "SkeletalTextures");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath) ??
                          L2AssetManager.CreateTextureAsset(
                              resolvedTexture.Texture,
                              texturePath,
                              false,
                              StaticMeshImportUtility.NeedsAlpha(resolvedTexture.Traits));
            if (texture != null)
            {
                result[resolvedTexture.Reference] = texture;
            }
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

    private static void CreatePrefab(L2SkeletalCharacterAsset asset, Mesh sourceMesh, Material[] materials, string prefabPath, string displayLabel)
    {
        var root = new GameObject($"NPC_{asset.CharacterName}");
        try
        {
            root.transform.localScale = Vector3.one;

            var geometry = new GameObject("Geometry");
            geometry.transform.SetParent(root.transform, false);
            geometry.transform.localScale = Vector3.one;

            var meshFilter = geometry.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = sourceMesh;
            var meshRenderer = geometry.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterials = materials;

            var player = geometry.AddComponent<L2SkeletalAnimationPlayer>();
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

            CreateLabel(root.transform, sourceMesh, displayLabel);

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

    private static void CreateLabel(Transform root, Mesh sourceMesh, string displayLabel)
    {
        var labelObject = new GameObject("NameLabel");
        labelObject.transform.SetParent(root, false);

        var text = labelObject.AddComponent<TextMesh>();
        text.text = string.IsNullOrWhiteSpace(displayLabel) ? root.name : displayLabel;
        text.anchor = TextAnchor.LowerCenter;
        text.alignment = TextAlignment.Center;
        text.fontSize = 48;
        text.characterSize = 0.05f;
        text.color = new Color(1f, 0.95f, 0.65f, 1f);

        var height = sourceMesh != null ? sourceMesh.bounds.max.y : 0f;
        var offset = sourceMesh != null ? Mathf.Max(0.18f, sourceMesh.bounds.size.y * 0.1f) : 0.18f;
        labelObject.transform.localPosition = new Vector3(0f, height + offset, 0f);
        labelObject.transform.localRotation = Quaternion.identity;
        labelObject.transform.localScale = Vector3.one;
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
            if (binding != null && textures.TryGetValue(binding.TextureReference, out var boundTexture))
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
