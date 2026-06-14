using System.Collections.Generic;
using UnityEngine;

internal sealed class StaticMeshMaterialCatalog
{
    public Dictionary<string, Material[]> MaterialsByMeshReference { get; } = new Dictionary<string, Material[]>(System.StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Texture2D[][]> FlipbooksByMeshReference { get; } = new Dictionary<string, Texture2D[][]>(System.StringComparer.OrdinalIgnoreCase);
}
