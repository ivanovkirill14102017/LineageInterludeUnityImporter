using System;
using System.Collections.Generic;
using L2Viewer.SceneDomain.Services;
using UnityEngine;

internal sealed class StaticMeshTextureCatalog
{
    public Dictionary<string, Texture2D> TexturesByReference { get; } = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Texture2D[]> FlipbooksByBindingKey { get; } = new Dictionary<string, Texture2D[]>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> PrimaryTextureReferenceByBindingKey { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, MaterialKnownTraits> TraitsByBindingKey { get; } = new Dictionary<string, MaterialKnownTraits>(StringComparer.OrdinalIgnoreCase);
}
