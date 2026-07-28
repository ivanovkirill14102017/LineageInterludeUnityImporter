using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal static class CreatureSkinnedMeshBuilder
{
    public static Mesh Build(L2SkeletalCharacterAsset asset, Material[] materials, Action<string> log, out string notes)
    {
        var materialIds = CreatureSkeletalImportUtility.GetMaterialIds(asset);
        var materialIdToSubmesh = materialIds
            .Select((id, index) => new KeyValuePair<int, int>(id, index))
            .ToDictionary(x => x.Key, x => x.Value);

        var weightsByPoint = CreatureSkeletalImportUtility.BuildWeightsByPoint(asset);
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var boneWeights = new List<BoneWeight>();
        var trianglesBySubmesh = new List<int>[Math.Max(1, materials.Length)];
        var wedgeToVertexIndex = new Dictionary<int, int>();
        for (var i = 0; i < trianglesBySubmesh.Length; i++)
        {
            trianglesBySubmesh[i] = new List<int>();
        }

        foreach (var face in asset.Faces ?? Array.Empty<L2SkeletalFaceData>())
        {
            if (!CreatureSkeletalImportUtility.TryGetWedge(asset, face.WedgeIndex1, out var aWedge) ||
                !CreatureSkeletalImportUtility.TryGetWedge(asset, face.WedgeIndex0, out var bWedge) ||
                !CreatureSkeletalImportUtility.TryGetWedge(asset, face.WedgeIndex2, out var cWedge) ||
                !CreatureSkeletalImportUtility.TryGetPoint(asset, aWedge.PointIndex, out var aPoint) ||
                !CreatureSkeletalImportUtility.TryGetPoint(asset, bWedge.PointIndex, out var bPoint) ||
                !CreatureSkeletalImportUtility.TryGetPoint(asset, cWedge.PointIndex, out var cPoint))
            {
                continue;
            }

            var vertexIndex0 = GetOrCreateVertexIndex(face.WedgeIndex1, aWedge, aPoint);
            var vertexIndex1 = GetOrCreateVertexIndex(face.WedgeIndex2, cWedge, cPoint);
            var vertexIndex2 = GetOrCreateVertexIndex(face.WedgeIndex0, bWedge, bPoint);

            var materialId = face.MaterialIndex;
            if (!materialIdToSubmesh.TryGetValue(materialId, out var submeshIndex))
            {
                submeshIndex = 0;
            }

            trianglesBySubmesh[submeshIndex].Add(vertexIndex0);
            trianglesBySubmesh[submeshIndex].Add(vertexIndex1);
            trianglesBySubmesh[submeshIndex].Add(vertexIndex2);
        }

        var mesh = new Mesh
        {
            name = $"SM_{asset.CharacterName}_skinned_poc",
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.boneWeights = boneWeights.ToArray();
        mesh.subMeshCount = trianglesBySubmesh.Length;
        for (var i = 0; i < trianglesBySubmesh.Length; i++)
        {
            mesh.SetTriangles(trianglesBySubmesh[i], i, true);
        }

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.bindposes = BuildBindPoses(asset, out var emptyWeightCount);
        notes = emptyWeightCount > 0
            ? $"skinning mesh built; {emptyWeightCount} point(s) had no usable weights and were left unbound on Unity side."
            : "skinning mesh built from asset points/wedges/faces with bindposes from SceneDomain bind pose debug frame.";
        if (mesh.bindposes.Length == 0)
        {
            log?.Invoke("[SkinnedPOC] No bindposes were produced.");
        }

        return mesh;

        int GetOrCreateVertexIndex(int wedgeIndex, L2SkeletalWedgeData wedge, L2SkeletalPointData point)
        {
            if (wedgeToVertexIndex.TryGetValue(wedgeIndex, out var existingIndex))
            {
                return existingIndex;
            }

            var vertexIndex = vertices.Count;
            wedgeToVertexIndex[wedgeIndex] = vertexIndex;
            vertices.Add(CreatureSkeletalImportUtility.ToUnityPosition(point.Position));
            uvs.Add(new Vector2(wedge.UV.x, 1f - wedge.UV.y));
            boneWeights.Add(CreatureSkeletalImportUtility.GetBoneWeight(weightsByPoint, wedge.PointIndex));
            return vertexIndex;
        }
    }

    private static Matrix4x4[] BuildBindPoses(L2SkeletalCharacterAsset asset, out int emptyWeightCount)
    {
        var session = L2SceneSkeletalAssetBridge.CreateSession(asset);
        var bindFrame = session.CaptureBindPoseDebugFrame(Mathf.Max(1, asset.Points?.Length ?? 1));
        var bonePoses = CreatureSkeletalImportUtility.BuildBonePoses(bindFrame.Bones, asset.Bones);
        var bindposes = new Matrix4x4[bonePoses.Length];
        for (var i = 0; i < bonePoses.Length; i++)
        {
            var boneMatrix = Matrix4x4.TRS(bonePoses[i].WorldPosition, bonePoses[i].WorldRotation, Vector3.one);
            bindposes[i] = boneMatrix.inverse;
        }

        emptyWeightCount = 0;
        var weightsByPoint = CreatureSkeletalImportUtility.BuildWeightsByPoint(asset);
        for (var i = 0; i < weightsByPoint.Length; i++)
        {
            if (weightsByPoint[i] == null || weightsByPoint[i].Count == 0)
            {
                emptyWeightCount++;
            }
        }

        return bindposes;
    }
}
