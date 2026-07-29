using System;
using System.Collections.Generic;
using System.Linq;
using L2Viewer.PackageCore;
using L2Viewer.SceneDTO;
using L2Viewer.SceneDomain.Models;
using L2Viewer.UkxFile;
using QuaternionN = System.Numerics.Quaternion;
using Vector2N = System.Numerics.Vector2;
using Vector3N = System.Numerics.Vector3;

internal static class PlayerCharacterPartAssetBuilder
{
    private sealed class RuntimeInfluence
    {
        public RuntimeInfluence(int boneIndex, float weight)
        {
            BoneIndex = boneIndex;
            Weight = weight;
        }

        public int BoneIndex { get; }
        public float Weight { get; }
    }

    private sealed class RuntimeVertex
    {
        public RuntimeVertex(Vector3N position, Vector2N uv, int materialId, RuntimeInfluence[] influences, string pointKey)
        {
            Position = position;
            UV = uv;
            MaterialId = materialId;
            Influences = influences ?? Array.Empty<RuntimeInfluence>();
            PointKey = string.IsNullOrWhiteSpace(pointKey) ? Guid.NewGuid().ToString("N") : pointKey;
        }

        public Vector3N Position { get; }
        public Vector2N UV { get; }
        public int MaterialId { get; }
        public RuntimeInfluence[] Influences { get; }
        public string PointKey { get; }
    }

    public static SceneSkeletalAsset BuildMeshOnlyAsset(SceneResourceLocation location, SceneSkeletalAsset baseAsset)
    {
        if (location == null)
        {
            throw new ArgumentNullException(nameof(location));
        }

        if (baseAsset == null)
        {
            throw new ArgumentNullException(nameof(baseAsset));
        }

        var ukx = UkxFileReader.Read(location.PackagePath);
        var exportEntry = ukx.ExportObjects
            .FirstOrDefault(x => x.Object is UkxSkeletalMeshObject mesh && mesh.ObjectName.Is(location.ObjectName));
        var meshObject = exportEntry?.Object as UkxSkeletalMeshObject;
        if (meshObject == null)
        {
            throw new InvalidOperationException(
                $"Skeletal mesh '{location.ObjectName}' was not found in '{location.PackagePath}'.");
        }

        if (meshObject.RefSkeleton.Length == 0)
        {
            throw new InvalidOperationException(
                $"Skeletal mesh '{meshObject.ObjectName}' does not contain a reference skeleton.");
        }

        var meshSkeleton = BuildSkeleton(meshObject);
        var geometry = BuildGeometry(meshObject);
        var remappedGeometry = RemapGeometryWeights(geometry, meshSkeleton, baseAsset.Skeleton);
        var materialSource = SceneSkeletalMeshCodec.DecodeSkeletalMesh(meshObject, location.PackagePath);

        return new SceneSkeletalAsset
        {
            PackagePath = location.PackagePath,
            MeshExportIndex = exportEntry.Export.Index,
            MeshObjectName = meshObject.ObjectName,
            AnimationObjectName = baseAsset.AnimationObjectName,
            Source = "Mesh-only skeletal part asset reusing base player animation set",
            Details = $"Mesh={meshObject.ObjectName}\r\nAnimation={baseAsset.AnimationObjectName}\r\nMode=MeshOnlyPart",
            Skeleton = baseAsset.Skeleton,
            Mesh = remappedGeometry,
            AnimationSet = baseAsset.AnimationSet,
            MaterialBindings = BuildMaterialBindings(meshObject, location.PackagePath),
            PrimaryTextureReference = materialSource?.TextureRef,
            UsedTextures = materialSource?.UsedTextures ?? Array.Empty<MaterialTextureInfo>(),
            RoutingProfiles = baseAsset.RoutingProfiles,
            ConsumerWarnings = baseAsset.ConsumerWarnings,
            RequiresExplicitConsumerRouting = baseAsset.RequiresExplicitConsumerRouting
        };
    }

    public static SceneSkeletalSkeleton LoadMeshSkeleton(SceneResourceLocation location)
    {
        if (location == null)
        {
            throw new ArgumentNullException(nameof(location));
        }

        var ukx = UkxFileReader.Read(location.PackagePath);
        var exportEntry = ukx.ExportObjects
            .FirstOrDefault(x => x.Object is UkxSkeletalMeshObject mesh && mesh.ObjectName.Is(location.ObjectName));
        var meshObject = exportEntry?.Object as UkxSkeletalMeshObject;
        if (meshObject == null)
        {
            throw new InvalidOperationException(
                $"Skeletal mesh '{location.ObjectName}' was not found in '{location.PackagePath}'.");
        }

        if (meshObject.RefSkeleton.Length == 0)
        {
            throw new InvalidOperationException(
                $"Skeletal mesh '{meshObject.ObjectName}' does not contain a reference skeleton.");
        }

        return BuildSkeleton(meshObject);
    }

    private static SceneSkeletalSkeleton BuildSkeleton(UkxSkeletalMeshObject mesh)
    {
        var bones = new SceneSkeletalBone[mesh.RefSkeleton.Length];
        for (var i = 0; i < mesh.RefSkeleton.Length; i++)
        {
            var source = mesh.RefSkeleton[i];
            var parentIndex = source.ParentIndex >= 0 && source.ParentIndex < mesh.RefSkeleton.Length && source.ParentIndex != i
                ? source.ParentIndex
                : -1;
            var isRoot = parentIndex < 0;
            var rawBindPosition = ToActorXBonePosition(source.JointPosition.Position);
            var rawBindRotation = ToActorXOrientation(source.JointPosition.Orientation);
            var storedOrigQuaternion = isRoot ? QuaternionN.Conjugate(rawBindRotation) : rawBindRotation;
            var postQuaternion = QuaternionN.Conjugate(storedOrigQuaternion);

            bones[i] = new SceneSkeletalBone
            {
                Index = i,
                Name = source.Name,
                ParentIndex = parentIndex,
                RawBindPosition = rawBindPosition,
                RawBindRotation = rawBindRotation,
                StoredOrigLocation = rawBindPosition,
                StoredOrigQuaternion = storedOrigQuaternion,
                PostQuaternion = postQuaternion,
                IsRoot = isRoot,
                DontInvertRoot = true
            };
        }

        return new SceneSkeletalSkeleton
        {
            Name = mesh.ObjectName,
            Bones = bones
        };
    }

    private static SceneSkeletalGeometry BuildGeometry(UkxSkeletalMeshObject mesh)
    {
        var runtimeVertices = BuildVertices(mesh);
        var points = new List<SceneSkeletalPoint>(runtimeVertices.Length);
        var wedges = new SceneSkeletalWedge[runtimeVertices.Length];
        var weights = new List<SceneSkeletalWeight>(runtimeVertices.Sum(x => x.Influences.Length));
        var pointIndexByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var pointWeightsWritten = new HashSet<int>();

        for (var i = 0; i < runtimeVertices.Length; i++)
        {
            var pointIndex = GetOrCreatePointIndex(runtimeVertices[i]);
            wedges[i] = new SceneSkeletalWedge
            {
                PointIndex = pointIndex,
                UV = runtimeVertices[i].UV,
                MaterialIndex = runtimeVertices[i].MaterialId
            };

            if (!pointWeightsWritten.Add(pointIndex))
            {
                continue;
            }

            foreach (var influence in runtimeVertices[i].Influences)
            {
                if (influence.Weight <= 0f || influence.BoneIndex < 0)
                {
                    continue;
                }

                weights.Add(new SceneSkeletalWeight
                {
                    Weight = influence.Weight,
                    PointIndex = pointIndex,
                    BoneIndex = influence.BoneIndex
                });
            }
        }
        
        var faces = new SceneSkeletalFace[runtimeVertices.Length / 3];
        for (var faceIndex = 0; faceIndex < faces.Length; faceIndex++)
        {
            var baseIndex = faceIndex * 3;
            faces[faceIndex] = new SceneSkeletalFace
            {
                WedgeIndex0 = baseIndex + 0,
                WedgeIndex1 = baseIndex + 1,
                WedgeIndex2 = baseIndex + 2,
                MaterialIndex = wedges[baseIndex].MaterialIndex
            };
        }

        var (boundsMin, boundsMax) = ComputeBounds(points.Select(x => x.Position));

        return new SceneSkeletalGeometry
        {
            Name = mesh.ObjectName,
            Points = points.ToArray(),
            Wedges = wedges,
            Faces = faces,
            Weights = weights.ToArray(),
            SubMeshes = faces
                .GroupBy(x => x.MaterialIndex)
                .OrderBy(x => x.Key)
                .Select(x => new SceneSkeletalSubMesh
                {
                    MaterialId = x.Key,
                    FaceCount = x.Count()
                })
                .ToArray(),
            BoundsMin = boundsMin,
            BoundsMax = boundsMax
        };

        int GetOrCreatePointIndex(RuntimeVertex vertex)
        {
            if (pointIndexByKey.TryGetValue(vertex.PointKey, out var existingIndex))
            {
                return existingIndex;
            }

            var pointIndex = points.Count;
            pointIndexByKey[vertex.PointKey] = pointIndex;
            points.Add(new SceneSkeletalPoint
            {
                Position = ToActorXPosition(vertex.Position)
            });
            return pointIndex;
        }
    }

    internal static SceneSkeletalGeometry RemapGeometryWeights(
        SceneSkeletalGeometry geometry,
        SceneSkeletalSkeleton sourceSkeleton,
        SceneSkeletalSkeleton targetSkeleton)
    {
        var sourceBonesByIndex = sourceSkeleton.Bones
            .OrderBy(x => x.Index)
            .ToArray();
        var targetBoneIndexByName = targetSkeleton.Bones
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Index, StringComparer.OrdinalIgnoreCase);
        var remapCache = new Dictionary<int, int>();

        var remappedWeights = geometry.Weights
            .Select(weight =>
            {
                if ((uint)weight.BoneIndex >= sourceBonesByIndex.Length)
                {
                    throw new InvalidOperationException(
                        $"Mesh weight references missing source bone index {weight.BoneIndex} on '{geometry.Name}'.");
                }

                if (!TryResolveTargetBoneIndex(weight.BoneIndex, sourceBonesByIndex, targetBoneIndexByName, remapCache, out var targetBoneIndex))
                {
                    var boneName = sourceBonesByIndex[weight.BoneIndex].Name;
                    throw new InvalidOperationException(
                        $"Mesh '{geometry.Name}' references bone '{boneName}' that does not exist in the base player skeleton.");
                }

                return new SceneSkeletalWeight
                {
                    Weight = weight.Weight,
                    PointIndex = weight.PointIndex,
                    BoneIndex = targetBoneIndex
                };
            })
            .ToArray();

        return new SceneSkeletalGeometry
        {
            Name = geometry.Name,
            Points = geometry.Points,
            Wedges = geometry.Wedges,
            Faces = geometry.Faces,
            Weights = remappedWeights,
            SubMeshes = geometry.SubMeshes,
            BoundsMin = geometry.BoundsMin,
            BoundsMax = geometry.BoundsMax
        };
    }

    private static bool TryResolveTargetBoneIndex(
        int sourceBoneIndex,
        SceneSkeletalBone[] sourceBonesByIndex,
        IReadOnlyDictionary<string, int> targetBoneIndexByName,
        IDictionary<int, int> remapCache,
        out int targetBoneIndex)
    {
        if (remapCache.TryGetValue(sourceBoneIndex, out targetBoneIndex))
        {
            return true;
        }

        var currentIndex = sourceBoneIndex;
        while ((uint)currentIndex < sourceBonesByIndex.Length)
        {
            var currentBone = sourceBonesByIndex[currentIndex];
            if (!string.IsNullOrWhiteSpace(currentBone.Name) &&
                targetBoneIndexByName.TryGetValue(currentBone.Name, out targetBoneIndex))
            {
                remapCache[sourceBoneIndex] = targetBoneIndex;
                return true;
            }

            if (currentBone.ParentIndex < 0 || currentBone.ParentIndex == currentIndex)
            {
                break;
            }

            currentIndex = currentBone.ParentIndex;
        }

        targetBoneIndex = -1;
        return false;
    }

    private static RuntimeVertex[] BuildVertices(UkxSkeletalMeshObject mesh)
    {
        var lod = mesh.LodModels.Length > 0 ? mesh.LodModels[0] : null;
        if (lod != null && HasModernSectionVertices(lod))
        {
            var modernVertices = new List<RuntimeVertex>();
            BuildLineageSoftVertices(lod, modernVertices);
            BuildRigidVertices(lod, modernVertices);
            return modernVertices.ToArray();
        }

        if (lod != null && HasStandardLodVertices(lod))
        {
            var standardVertices = new List<RuntimeVertex>();
            BuildStandardVertices(lod.Points, lod.Wedges, lod.Faces, lod.VertInfluences, standardVertices);
            return standardVertices.ToArray();
        }

        if (HasBaseLodVertices(mesh))
        {
            var baseVertices = new List<RuntimeVertex>();
            BuildStandardBaseVertices(mesh, baseVertices);
            return baseVertices.ToArray();
        }

        return Array.Empty<RuntimeVertex>();
    }

    private static void BuildLineageSoftVertices(UkxSkeletalLodModel lod, List<RuntimeVertex> vertices)
    {
        if (lod.LineageWedges.Length == 0 || lod.SoftIndices.Indices.Length == 0)
        {
            return;
        }

        foreach (var section in lod.SoftSections)
        {
            for (var faceIndex = 0; faceIndex < section.NumFaces; faceIndex++)
            {
                var baseIndex = (section.FirstFace + faceIndex) * 3;
                if (baseIndex + 2 >= lod.SoftIndices.Indices.Length)
                {
                    continue;
                }

                var wedge0 = lod.SoftIndices.Indices[baseIndex];
                var wedge1 = lod.SoftIndices.Indices[baseIndex + 1];
                var wedge2 = lod.SoftIndices.Indices[baseIndex + 2];
                if ((uint)wedge0 >= lod.LineageWedges.Length || (uint)wedge1 >= lod.LineageWedges.Length || (uint)wedge2 >= lod.LineageWedges.Length)
                {
                    continue;
                }

                vertices.Add(CreateLineageVertex(lod.LineageWedges[wedge0], section));
                vertices.Add(CreateLineageVertex(lod.LineageWedges[wedge1], section));
                vertices.Add(CreateLineageVertex(lod.LineageWedges[wedge2], section));
            }
        }
    }

    private static void BuildRigidVertices(UkxSkeletalLodModel lod, List<RuntimeVertex> vertices)
    {
        if (lod.VertexStream.Vertices.Length == 0 || lod.RigidIndices.Indices.Length == 0)
        {
            return;
        }

        foreach (var section in lod.RigidSections)
        {
            for (var faceIndex = 0; faceIndex < section.NumFaces; faceIndex++)
            {
                var baseIndex = (section.FirstFace + faceIndex) * 3;
                if (baseIndex + 2 >= lod.RigidIndices.Indices.Length)
                {
                    continue;
                }

                var wedge0 = lod.RigidIndices.Indices[baseIndex];
                var wedge1 = lod.RigidIndices.Indices[baseIndex + 1];
                var wedge2 = lod.RigidIndices.Indices[baseIndex + 2];
                if ((uint)wedge0 >= lod.VertexStream.Vertices.Length || (uint)wedge1 >= lod.VertexStream.Vertices.Length || (uint)wedge2 >= lod.VertexStream.Vertices.Length)
                {
                    continue;
                }

                vertices.Add(CreateRigidVertex(lod.VertexStream.Vertices[wedge0], section.MaterialIndex, section.BoneIndex));
                vertices.Add(CreateRigidVertex(lod.VertexStream.Vertices[wedge1], section.MaterialIndex, section.BoneIndex));
                vertices.Add(CreateRigidVertex(lod.VertexStream.Vertices[wedge2], section.MaterialIndex, section.BoneIndex));
            }
        }
    }

    private static void BuildStandardVertices(
        Vector3N[] points,
        UkxMeshWedge[] wedges,
        UkxMeshFace[] faces,
        UkxVertInfluence[] influences,
        List<RuntimeVertex> vertices)
    {
        if (points.Length == 0 || wedges.Length == 0 || faces.Length == 0)
        {
            return;
        }

        var influencesByPoint = BuildInfluenceMap(points.Length, influences);
        foreach (var face in faces)
        {
            if ((uint)face.WedgeIndex0 >= wedges.Length || (uint)face.WedgeIndex1 >= wedges.Length || (uint)face.WedgeIndex2 >= wedges.Length)
            {
                continue;
            }

            var wedge0 = wedges[face.WedgeIndex0];
            var wedge1 = wedges[face.WedgeIndex1];
            var wedge2 = wedges[face.WedgeIndex2];
            if ((uint)wedge0.VertexIndex >= points.Length || (uint)wedge1.VertexIndex >= points.Length || (uint)wedge2.VertexIndex >= points.Length)
            {
                continue;
            }

            vertices.Add(CreateStandardVertex(points[wedge0.VertexIndex], wedge0.UV, face.MaterialIndex, influencesByPoint, wedge0.VertexIndex));
            vertices.Add(CreateStandardVertex(points[wedge1.VertexIndex], wedge1.UV, face.MaterialIndex, influencesByPoint, wedge1.VertexIndex));
            vertices.Add(CreateStandardVertex(points[wedge2.VertexIndex], wedge2.UV, face.MaterialIndex, influencesByPoint, wedge2.VertexIndex));
        }
    }

    private static void BuildStandardBaseVertices(UkxSkeletalMeshObject mesh, List<RuntimeVertex> vertices)
    {
        if (mesh.BaseLodPoints.Length > 0 &&
            mesh.BaseLodWedges.Length > 0 &&
            mesh.BaseLodTriangles.Length > 0)
        {
            BuildStandardTriangles(mesh.BaseLodPoints, mesh.BaseLodWedges, mesh.BaseLodTriangles, mesh.BaseLodVertInfluences, vertices);
            return;
        }

        if (mesh.BasePoints.Length == 0 ||
            mesh.BaseLodWedges.Length == 0 ||
            mesh.BaseLodTriangles.Length == 0 ||
            mesh.WeightIndices.Length == 0 ||
            mesh.BoneInfluences.Length == 0)
        {
            return;
        }

        BuildStandardTriangles(
            mesh.BasePoints,
            mesh.BaseLodWedges,
            mesh.BaseLodTriangles,
            ConvertLegacyInfluences(mesh.WeightIndices, mesh.BoneInfluences),
            vertices);
    }

    private static void BuildStandardTriangles(
        Vector3N[] points,
        UkxMeshWedge[] wedges,
        UkxTriangle[] triangles,
        UkxVertInfluence[] influences,
        List<RuntimeVertex> vertices)
    {
        if (points.Length == 0 || wedges.Length == 0 || triangles.Length == 0)
        {
            return;
        }

        var influencesByPoint = BuildInfluenceMap(points.Length, influences);
        foreach (var triangle in triangles)
        {
            if ((uint)triangle.WedgeIndex0 >= wedges.Length || (uint)triangle.WedgeIndex1 >= wedges.Length || (uint)triangle.WedgeIndex2 >= wedges.Length)
            {
                continue;
            }

            var wedge0 = wedges[triangle.WedgeIndex0];
            var wedge1 = wedges[triangle.WedgeIndex1];
            var wedge2 = wedges[triangle.WedgeIndex2];
            if ((uint)wedge0.VertexIndex >= points.Length || (uint)wedge1.VertexIndex >= points.Length || (uint)wedge2.VertexIndex >= points.Length)
            {
                continue;
            }

            vertices.Add(CreateStandardVertex(points[wedge0.VertexIndex], wedge0.UV, triangle.MaterialIndex, influencesByPoint, wedge0.VertexIndex));
            vertices.Add(CreateStandardVertex(points[wedge1.VertexIndex], wedge1.UV, triangle.MaterialIndex, influencesByPoint, wedge1.VertexIndex));
            vertices.Add(CreateStandardVertex(points[wedge2.VertexIndex], wedge2.UV, triangle.MaterialIndex, influencesByPoint, wedge2.VertexIndex));
        }
    }

    private static RuntimeVertex CreateLineageVertex(UkxLineageWedge wedge, UkxSkelMeshSection section)
    {
        var influences = new List<RuntimeInfluence>(4);
        AppendLineageInfluence(influences, section.LineageBoneMap, wedge.Bone0, wedge.Weight0);
        AppendLineageInfluence(influences, section.LineageBoneMap, wedge.Bone1, wedge.Weight1);
        AppendLineageInfluence(influences, section.LineageBoneMap, wedge.Bone2, wedge.Weight2);
        AppendLineageInfluence(influences, section.LineageBoneMap, wedge.Bone3, wedge.Weight3);
        var normalized = NormalizeWeights(influences);
        return new RuntimeVertex(
            wedge.Position,
            wedge.UV,
            section.MaterialIndex,
            normalized,
            BuildSyntheticPointKey(wedge.Position, normalized));
    }

    private static RuntimeVertex CreateRigidVertex(UkxAnimMeshVertex vertex, int materialIndex, int boneIndex)
    {
        var influences = new[] { new RuntimeInfluence(boneIndex, 1f) };
        return new RuntimeVertex(
            vertex.Position,
            vertex.UV,
            materialIndex,
            influences,
            BuildSyntheticPointKey(vertex.Position, influences));
    }

    private static RuntimeVertex CreateStandardVertex(
        Vector3N position,
        Vector2N uv,
        int materialIndex,
        RuntimeInfluence[][] influencesByPoint,
        int pointIndex)
    {
        var influences = (uint)pointIndex < influencesByPoint.Length ? influencesByPoint[pointIndex] : Array.Empty<RuntimeInfluence>();
        return new RuntimeVertex(position, uv, materialIndex, influences, $"src:{pointIndex}");
    }

    private static RuntimeInfluence[][] BuildInfluenceMap(int pointCount, UkxVertInfluence[] influences)
    {
        var lists = new List<RuntimeInfluence>[pointCount];
        foreach (var influence in influences)
        {
            if (influence.Weight <= 0f || influence.PointIndex >= pointCount)
            {
                continue;
            }

            lists[influence.PointIndex] ??= new List<RuntimeInfluence>();
            lists[influence.PointIndex].Add(new RuntimeInfluence(influence.BoneIndex, influence.Weight));
        }

        var result = new RuntimeInfluence[pointCount][];
        for (var i = 0; i < pointCount; i++)
        {
            result[i] = lists[i] == null ? Array.Empty<RuntimeInfluence>() : NormalizeWeights(lists[i]);
        }

        return result;
    }

    private static UkxVertInfluence[] ConvertLegacyInfluences(UkxWeightIndex[] weightIndices, UkxBoneInfluence[] boneInfluences)
    {
        var result = new List<UkxVertInfluence>();
        for (var influenceCountMinusOne = 0; influenceCountMinusOne < weightIndices.Length; influenceCountMinusOne++)
        {
            var weightIndex = weightIndices[influenceCountMinusOne];
            var sourceIndex = weightIndex.StartBoneInfluence;
            foreach (var pointIndex in weightIndex.BoneInfluenceIndices)
            {
                for (var influenceOffset = 0; influenceOffset <= influenceCountMinusOne; influenceOffset++)
                {
                    if ((uint)sourceIndex >= boneInfluences.Length)
                    {
                        break;
                    }

                    var boneInfluence = boneInfluences[sourceIndex++];
                    result.Add(new UkxVertInfluence(boneInfluence.BoneWeight / 65535.0f, pointIndex, boneInfluence.BoneIndex));
                }
            }
        }

        return result.ToArray();
    }

    private static IReadOnlyList<SceneSkeletalMaterialBinding> BuildMaterialBindings(UkxSkeletalMeshObject mesh, string packagePath)
    {
        var bindings = new List<SceneSkeletalMaterialBinding>(mesh.Materials.Length);
        for (var materialId = 0; materialId < mesh.Materials.Length; materialId++)
        {
            var material = mesh.Materials[materialId];
            string packageName = null;
            string objectName = null;
            string textureReference = null;
            string resolvedPackagePath = null;

            if (material.TextureIndex >= 0 && material.TextureIndex < mesh.TextureReferences.Length)
            {
                var reference = mesh.TextureReferences[material.TextureIndex];
                if (reference != null)
                {
                    packageName = reference.PackageName ?? System.IO.Path.GetFileNameWithoutExtension(packagePath);
                    objectName = reference.ObjectName;
                    resolvedPackagePath = reference.ExportIndex != null ? packagePath : null;

                    if (reference.ClassName.EndsWith(UnrealClassNames.Texture, StringComparison.OrdinalIgnoreCase) ||
                        reference.ClassName.Is(UnrealClassNames.Texture))
                    {
                        textureReference = $"{packageName}.{objectName}";
                    }
                }
            }

            bindings.Add(new SceneSkeletalMaterialBinding
            {
                MaterialId = materialId,
                PackageName = packageName,
                ObjectName = objectName,
                TextureReference = textureReference,
                ResolvedPackagePath = resolvedPackagePath
            });
        }

        return bindings;
    }

    private static bool HasModernSectionVertices(UkxSkeletalLodModel lod)
    {
        return (lod.SoftSections.Length > 0 && lod.LineageWedges.Length > 0 && lod.SoftIndices.Indices.Length > 0) ||
               (lod.RigidSections.Length > 0 && lod.VertexStream.Vertices.Length > 0 && lod.RigidIndices.Indices.Length > 0);
    }

    private static bool HasStandardLodVertices(UkxSkeletalLodModel lod)
    {
        return lod.Points.Length > 0 && lod.Wedges.Length > 0 && lod.Faces.Length > 0;
    }

    private static bool HasBaseLodVertices(UkxSkeletalMeshObject mesh)
    {
        return (mesh.BaseLodPoints.Length > 0 && mesh.BaseLodWedges.Length > 0 && mesh.BaseLodTriangles.Length > 0) ||
               (mesh.BasePoints.Length > 0 &&
                mesh.BaseLodWedges.Length > 0 &&
                mesh.BaseLodTriangles.Length > 0 &&
                mesh.WeightIndices.Length > 0 &&
                mesh.BoneInfluences.Length > 0);
    }

    private static RuntimeInfluence[] NormalizeWeights(List<RuntimeInfluence> influences)
    {
        if (influences.Count == 0)
        {
            return Array.Empty<RuntimeInfluence>();
        }

        var totalWeight = influences.Sum(x => x.Weight);
        if (totalWeight <= 0.000001f)
        {
            return Array.Empty<RuntimeInfluence>();
        }

        return influences
            .Where(x => x.Weight > 0f)
            .Select(x => new RuntimeInfluence(x.BoneIndex, x.Weight / totalWeight))
            .ToArray();
    }

    private static void AppendLineageInfluence(List<RuntimeInfluence> influences, int[] lineageBoneMap, byte localBoneIndex, float weight)
    {
        if (localBoneIndex == byte.MaxValue || weight <= 0f || localBoneIndex >= lineageBoneMap.Length)
        {
            return;
        }

        influences.Add(new RuntimeInfluence(lineageBoneMap[localBoneIndex], weight));
    }

    private static string BuildSyntheticPointKey(Vector3N position, IReadOnlyList<RuntimeInfluence> influences)
    {
        var key = $"{Quantize(position.X)}|{Quantize(position.Y)}|{Quantize(position.Z)}";
        for (var i = 0; i < influences.Count; i++)
        {
            var influence = influences[i];
            key += $"|{influence.BoneIndex}:{Quantize(influence.Weight)}";
        }

        return key;
    }

    private static string Quantize(float value)
    {
        return MathF.Round(value, 5).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static (Vector3N Min, Vector3N Max) ComputeBounds(IEnumerable<Vector3N> points)
    {
        var min = new Vector3N(float.MaxValue);
        var max = new Vector3N(float.MinValue);
        var hasAny = false;
        foreach (var point in points)
        {
            hasAny = true;
            min = Vector3N.Min(min, point);
            max = Vector3N.Max(max, point);
        }

        return hasAny ? (min, max) : (Vector3N.Zero, Vector3N.Zero);
    }

    private static Vector3N ToActorXPosition(Vector3N value)
    {
        return new Vector3N(value.X, -value.Y, value.Z) * 0.01f;
    }

    private static Vector3N ToActorXBonePosition(Vector3N value)
    {
        return new Vector3N(value.X, -value.Y, value.Z) * 0.01f;
    }

    private static QuaternionN ToActorXOrientation(QuaternionN value)
    {
        var converted = new QuaternionN(value.X, -value.Y, value.Z, -value.W);
        return converted.LengthSquared() > 0.000001f ? QuaternionN.Normalize(converted) : QuaternionN.Identity;
    }
}
