using System;
using System.Collections.Generic;
using System.Linq;
using L2Viewer.SceneDomain.Models;

internal sealed class EquipmentSourceResourceSet
{
    public EquipmentSourceResourceSet(
        SceneCharacterEquipmentCatalogItemData[] items,
        SceneSkeletalMeshResourceReference[] meshReferences,
        SceneSurfaceResourceReference[] textureReferences,
        MaterialResourceRequest[] materialRequests)
    {
        Items = items;
        MeshReferences = meshReferences;
        TextureReferences = textureReferences;
        MaterialRequests = materialRequests;
    }

    public SceneCharacterEquipmentCatalogItemData[] Items { get; }
    public SceneSkeletalMeshResourceReference[] MeshReferences { get; }
    public SceneSurfaceResourceReference[] TextureReferences { get; }
    public MaterialResourceRequest[] MaterialRequests { get; }
}

internal static class EquipmentSourceResourceResolver
{
    public static EquipmentSourceResourceSet Resolve(
        IEnumerable<int> itemIds,
        IEnumerable<SceneCharacterEquipmentCatalogData> catalogs)
    {
        var requestedIds = itemIds.Where(x => x > 0).Distinct().OrderBy(x => x).ToArray();
        if (requestedIds.Length == 0)
        {
            return new EquipmentSourceResourceSet(
                Array.Empty<SceneCharacterEquipmentCatalogItemData>(),
                Array.Empty<SceneSkeletalMeshResourceReference>(),
                Array.Empty<SceneSurfaceResourceReference>(),
                Array.Empty<MaterialResourceRequest>());
        }

        var requested = requestedIds.ToHashSet();
        var items = catalogs
            .SelectMany(x => x.Slots ?? Array.Empty<SceneCharacterEquipmentCatalogSlotData>())
            .SelectMany(x => x.Items ?? Array.Empty<SceneCharacterEquipmentCatalogItemData>())
            .Where(x => requested.Contains(x.ItemId))
            .GroupBy(x => x.ItemId)
            .Select(x => x.First())
            .OrderBy(x => x.ItemId)
            .ToArray();
        var meshReferences = items
            .SelectMany(x => x.MeshResources ?? Array.Empty<SceneResourceReference>())
            .Select(x => new SceneSkeletalMeshResourceReference(x.ResourceId))
            .Distinct()
            .ToArray();
        var textureReferences = items
            .SelectMany(x => x.TextureResources ?? Array.Empty<SceneResourceReference>())
            .Select(x => new SceneSurfaceResourceReference(x.ResourceId))
            .Distinct()
            .ToArray();
        return new EquipmentSourceResourceSet(
            items,
            meshReferences,
            textureReferences,
            textureReferences.Select(MaterialResourceRequest.Opaque).ToArray());
    }
}
