using System;
using System.Collections.Generic;
using System.Linq;
using L2Viewer.PackageCore;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using L2Viewer.SceneDomain.Services.CharacterServices;
using UnityEngine;

using QuaternionN = System.Numerics.Quaternion;
using Vector2N = System.Numerics.Vector2;
using Vector3N = System.Numerics.Vector3;

public static class L2SceneSkeletalAssetBridge
{
    public static SceneSkeletalAsset ToSceneAsset(L2SkeletalCharacterAsset asset)
    {
        if (asset == null)
        {
            throw new ArgumentNullException(nameof(asset));
        }

        return new SceneSkeletalAsset
        {
            PackagePath = asset.SourcePackagePath ?? string.Empty,
            MeshExportIndex = asset.MeshExportIndex,
            MeshObjectName = asset.MeshObjectName ?? asset.CharacterName ?? "SkeletalMesh",
            AnimationObjectName = asset.AnimationObjectName ?? string.Empty,
            Source = "Unity imported shared skeletal asset",
            Details = $"Mesh={asset.MeshObjectName}\r\nAnimation={asset.AnimationObjectName}",
            MaterialBindings = (asset.MaterialBindings ?? Array.Empty<L2SkeletalMaterialBindingData>())
                .Select(x => new SceneSkeletalMaterialBinding
                {
                    MaterialId = x.MaterialId,
                    PackageName = x.PackageName,
                    ObjectName = x.ObjectName,
                    TextureReference = x.TextureReference,
                    ResolvedPackagePath = x.ResolvedPackagePath
                })
                .ToArray(),
            PrimaryTextureReference = asset.PrimaryTextureReference,
            UsedTextures = (asset.UsedTextures ?? Array.Empty<L2SkeletalTextureRefData>())
                .Select(x => new MaterialTextureInfo(x.Reference ?? string.Empty, x.ResolvedPackagePath))
                .ToArray(),
            RoutingProfiles = Array.Empty<SceneSkeletalAnimationRoutingProfile>(),
            ConsumerWarnings = new[]
            {
                "Routing metadata is not embedded in Unity imported shared skeletal assets."
            },
            RequiresExplicitConsumerRouting = true,
            Skeleton = new SceneSkeletalSkeleton
            {
                Name = asset.MeshObjectName ?? asset.CharacterName ?? "Skeleton",
                Bones = (asset.Bones ?? Array.Empty<L2SkeletalBoneData>())
                    .Select((x, index) => new SceneSkeletalBone
                    {
                        Index = index,
                        Name = x.Name ?? $"Bone_{index}",
                        ParentIndex = x.ParentIndex,
                        RawBindPosition = ToNumerics(x.RawBindPosition),
                        RawBindRotation = ToNumerics(x.RawBindRotation),
                        StoredOrigLocation = ToNumerics(x.StoredOrigLocation),
                        StoredOrigQuaternion = ToNumerics(x.StoredOrigQuaternion),
                        PostQuaternion = ToNumerics(x.PostQuaternion),
                        IsRoot = x.IsRoot,
                        DontInvertRoot = x.DontInvertRoot
                    })
                    .ToArray()
            },
            Mesh = new SceneSkeletalGeometry
            {
                Name = asset.MeshObjectName ?? asset.CharacterName ?? "Mesh",
                Points = (asset.Points ?? Array.Empty<L2SkeletalPointData>())
                    .Select(x => new SceneSkeletalPoint
                    {
                        Position = ToNumerics(x.Position)
                    })
                    .ToArray(),
                Wedges = (asset.Wedges ?? Array.Empty<L2SkeletalWedgeData>())
                    .Select(x => new SceneSkeletalWedge
                    {
                        PointIndex = x.PointIndex,
                        UV = ToNumerics(x.UV),
                        MaterialIndex = x.MaterialIndex
                    })
                    .ToArray(),
                Faces = (asset.Faces ?? Array.Empty<L2SkeletalFaceData>())
                    .Select(x => new SceneSkeletalFace
                    {
                        WedgeIndex0 = x.WedgeIndex0,
                        WedgeIndex1 = x.WedgeIndex1,
                        WedgeIndex2 = x.WedgeIndex2,
                        MaterialIndex = x.MaterialIndex
                    })
                    .ToArray(),
                Weights = (asset.Weights ?? Array.Empty<L2SkeletalWeightData>())
                    .Select(x => new SceneSkeletalWeight
                    {
                        Weight = x.Weight,
                        PointIndex = x.PointIndex,
                        BoneIndex = x.BoneIndex
                    })
                    .ToArray(),
                SubMeshes = (asset.SubMeshes ?? Array.Empty<L2SkeletalSubMeshData>())
                    .Select(x => new SceneSkeletalSubMesh
                    {
                        MaterialId = x.MaterialId,
                        FaceCount = x.FaceCount
                    })
                    .ToArray(),
                BoundsMin = ToNumerics(asset.BoundsMin),
                BoundsMax = ToNumerics(asset.BoundsMax)
            },
            AnimationSet = new SceneSkeletalAnimationSet
            {
                Name = asset.AnimationObjectName ?? string.Empty,
                Bones = (asset.AnimationBones ?? Array.Empty<L2SkeletalAnimationBoneData>())
                    .Select((x, index) => new SceneSkeletalAnimationBone
                    {
                        Index = index,
                        Name = x.Name ?? $"AnimBone_{index}",
                        ParentIndex = x.ParentIndex
                    })
                    .ToArray(),
                Sequences = (asset.AnimationSequences ?? Array.Empty<L2SkeletalAnimationSequenceData>())
                    .Select(x => new SceneSkeletalAnimationSequence
                    {
                        Name = x.Name ?? string.Empty,
                        NormalizedName = NormalizeSequenceName(x.Name),
                        Category = ClassifySequenceCategory(x.Name),
                        TotalBones = x.TotalBones,
                        TrackTime = x.TrackTime,
                        AnimRate = x.AnimRate,
                        FirstRawFrame = x.FirstRawFrame,
                        NumRawFrames = x.NumRawFrames,
                        SuggestedLoop = IsSuggestedLoop(x.Name),
                        IsOneShot = IsOneShot(x.Name),
                        RequiresExplicitRouting = IsUnknownSequence(x.Name),
                        SuggestedNextSequenceNames = BuildSuggestedNextSequenceNames(x.Name, asset.AnimationSequences ?? Array.Empty<L2SkeletalAnimationSequenceData>()),
                        Notifies = Array.Empty<SceneSkeletalAnimationNotify>()
                    })
                    .ToArray(),
                Keys = (asset.AnimationKeys ?? Array.Empty<L2SkeletalAnimationKeyData>())
                    .Select(x => new SceneSkeletalAnimationKey
                    {
                        Position = ToNumerics(x.Position),
                        Orientation = ToNumerics(x.Orientation),
                        Time = x.Time
                    })
                    .ToArray()
            }
        };
    }

    public static ActorXSkeletalAnimationPreviewSession CreateSession(L2SkeletalCharacterAsset asset)
    {
        return ActorXSkeletalAnimationPreviewSession.Create(ToSceneAsset(asset));
    }

    public static MeshData BuildRawMeshData(L2SkeletalCharacterAsset asset)
    {
        var sceneAsset = ToSceneAsset(asset);
        var points = sceneAsset.Mesh.Points.Select(x => x.Position).ToArray();
        var wedges = sceneAsset.Mesh.Wedges.ToArray();
        var faces = sceneAsset.Mesh.Faces.ToArray();
        var triangles = new List<Triangle>(faces.Length);
        var min = new Vector3N(float.MaxValue);
        var max = new Vector3N(float.MinValue);

        foreach (var face in faces)
        {
            if ((uint)face.WedgeIndex0 >= wedges.Length ||
                (uint)face.WedgeIndex1 >= wedges.Length ||
                (uint)face.WedgeIndex2 >= wedges.Length)
            {
                continue;
            }

            var w0 = wedges[face.WedgeIndex0];
            var w1 = wedges[face.WedgeIndex1];
            var w2 = wedges[face.WedgeIndex2];
            if ((uint)w0.PointIndex >= points.Length ||
                (uint)w1.PointIndex >= points.Length ||
                (uint)w2.PointIndex >= points.Length)
            {
                continue;
            }

            var a = points[w1.PointIndex];
            var b = points[w0.PointIndex];
            var c = points[w2.PointIndex];
            var normal = ComputeFaceNormal(a, b, c);
            triangles.Add(new Triangle(
                new TriangleVertex(a, w1.UV, normal),
                new TriangleVertex(b, w0.UV, normal),
                new TriangleVertex(c, w2.UV, normal),
                face.MaterialIndex));
            min = Vector3N.Min(min, a);
            min = Vector3N.Min(min, b);
            min = Vector3N.Min(min, c);
            max = Vector3N.Max(max, a);
            max = Vector3N.Max(max, b);
            max = Vector3N.Max(max, c);
        }

        if (triangles.Count == 0)
        {
            min = Vector3N.Zero;
            max = Vector3N.Zero;
        }

        return new MeshData(
            sceneAsset.Mesh.Name,
            triangles,
            min,
            max,
            "Unity raw shared skeletal mesh",
            sceneAsset.PrimaryTextureReference,
            sceneAsset.UsedTextures);
    }

    public static Mesh BuildUnityMesh(MeshData meshData, string meshName)
    {
        var mesh = new Mesh
        {
            name = string.IsNullOrWhiteSpace(meshName) ? meshData.Name : meshName,
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };

        ApplyMeshData(mesh, meshData);
        return mesh;
    }

    private static string NormalizeSequenceName(string name)
    {
        return string.IsNullOrWhiteSpace(name)
            ? string.Empty
            : name.Trim().ToLowerInvariant();
    }

    private static string ClassifySequenceCategory(string name)
    {
        var normalized = NormalizeSequenceName(name);
        if (normalized.Length == 0)
        {
            return "unknown";
        }

        if (normalized.Contains("deathwait", StringComparison.OrdinalIgnoreCase))
        {
            return "death_hold";
        }

        if (normalized.Contains("death", StringComparison.OrdinalIgnoreCase))
        {
            return "death";
        }

        if (normalized.StartsWith("social", StringComparison.OrdinalIgnoreCase))
        {
            return "social";
        }

        if (normalized.Contains("spwait", StringComparison.OrdinalIgnoreCase))
        {
            return "combat_skill_idle";
        }

        if (normalized.Contains("atkwait", StringComparison.OrdinalIgnoreCase))
        {
            return "combat_idle";
        }

        if (normalized == "wait" || normalized.EndsWith("wait", StringComparison.OrdinalIgnoreCase))
        {
            return "idle";
        }

        if (normalized.Contains("run", StringComparison.OrdinalIgnoreCase))
        {
            return "run";
        }

        if (normalized.Contains("walk", StringComparison.OrdinalIgnoreCase))
        {
            return "walk";
        }

        if (normalized.StartsWith("spatk", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("cast", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("spell", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("magic", StringComparison.OrdinalIgnoreCase))
        {
            return "skill";
        }

        if (normalized.StartsWith("atk", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("attack", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("strike", StringComparison.OrdinalIgnoreCase))
        {
            return "attack";
        }

        return "unknown";
    }

    private static bool IsSuggestedLoop(string name)
    {
        switch (ClassifySequenceCategory(name))
        {
            case "idle":
            case "walk":
            case "run":
            case "combat_idle":
            case "combat_skill_idle":
            case "death_hold":
                return true;
            default:
                return false;
        }
    }

    private static bool IsOneShot(string name)
    {
        switch (ClassifySequenceCategory(name))
        {
            case "attack":
            case "skill":
            case "social":
            case "death":
                return true;
            default:
                return false;
        }
    }

    private static bool IsUnknownSequence(string name)
    {
        return string.Equals(ClassifySequenceCategory(name), "unknown", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> BuildSuggestedNextSequenceNames(string name, IReadOnlyList<L2SkeletalAnimationSequenceData> sequences)
    {
        var sequenceNames = sequences
            .Select(x => x?.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        switch (ClassifySequenceCategory(name))
        {
            case "attack":
                return PreferSequences(sequenceNames, "atkwait", "wait");
            case "skill":
                return PreferSequences(sequenceNames, "spwait01", "atkwait", "wait");
            case "social":
                return PreferSequences(sequenceNames, "wait");
            case "death":
                return PreferSequences(sequenceNames, "deathwait");
            default:
                return Array.Empty<string>();
        }
    }

    private static string[] PreferSequences(IReadOnlyList<string> sequenceNames, params string[] candidates)
    {
        var result = new List<string>(candidates.Length);
        foreach (var candidate in candidates)
        {
            var match = sequenceNames.FirstOrDefault(x => string.Equals(x, candidate, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match))
            {
                result.Add(match);
            }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static void ApplyMeshData(Mesh mesh, MeshData meshData)
    {
        if (mesh == null)
        {
            throw new ArgumentNullException(nameof(mesh));
        }

        var triangles = meshData?.Triangles ?? Array.Empty<Triangle>();
        var vertexCount = triangles.Count * 3;
        var vertices = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var uvs = new Vector2[vertexCount];
        var indicesByMaterial = new Dictionary<int, List<int>>();

        for (var triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
        {
            var triangle = triangles[triangleIndex];
            var baseIndex = triangleIndex * 3;

            WriteVertex(triangle.A, vertices, normals, uvs, baseIndex + 0);
            WriteVertex(triangle.C, vertices, normals, uvs, baseIndex + 1);
            WriteVertex(triangle.B, vertices, normals, uvs, baseIndex + 2);

            if (!indicesByMaterial.TryGetValue(triangle.MaterialId, out var bucket))
            {
                bucket = new List<int>();
                indicesByMaterial[triangle.MaterialId] = bucket;
            }

            bucket.Add(baseIndex + 0);
            bucket.Add(baseIndex + 1);
            bucket.Add(baseIndex + 2);
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.subMeshCount = Math.Max(1, indicesByMaterial.Count);

        if (indicesByMaterial.Count == 0)
        {
            mesh.SetTriangles(Array.Empty<int>(), 0, true);
        }
        else
        {
            var orderedMaterials = indicesByMaterial.Keys.OrderBy(x => x).ToArray();
            for (var i = 0; i < orderedMaterials.Length; i++)
            {
                mesh.SetTriangles(indicesByMaterial[orderedMaterials[i]].ToArray(), i, true);
            }
        }

        mesh.RecalculateBounds();
    }

    private static void WriteVertex(TriangleVertex source, Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int index)
    {
        vertices[index] = ToUnityPosition(source.Position);
        normals[index] = ToUnityDirection(source.Normal);
        uvs[index] = new Vector2(source.UV.X, 1f - source.UV.Y);
    }

    private static Vector3N ComputeFaceNormal(Vector3N a, Vector3N b, Vector3N c)
    {
        var normal = Vector3N.Cross(b - a, c - a);
        return normal.LengthSquared() > 0.000001f ? Vector3N.Normalize(normal) : Vector3N.UnitZ;
    }

    private static Vector3 ToUnityPosition(Vector3N value)
    {
        return new Vector3(value.X, value.Z, -value.Y);
    }

    private static Vector3 ToUnityDirection(Vector3N value)
    {
        var converted = new Vector3(value.X, value.Z, -value.Y);
        return converted.sqrMagnitude > 0.000001f ? converted.normalized : Vector3.up;
    }

    private static Vector3N ToNumerics(Vector3 value) => new Vector3N(value.x, value.y, value.z);
    private static Vector2N ToNumerics(Vector2 value) => new Vector2N(value.x, value.y);
    private static QuaternionN ToNumerics(Quaternion value) => new QuaternionN(value.x, value.y, value.z, value.w);
}
