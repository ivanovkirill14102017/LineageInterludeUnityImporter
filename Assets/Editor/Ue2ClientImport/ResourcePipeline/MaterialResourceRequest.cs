using System;
using L2Viewer.SceneDomain.Models;

internal sealed class MaterialResourceId : IEquatable<MaterialResourceId>
{
    public MaterialResourceId(SceneSurfaceResourceReference surface)
    {
        Surface = surface;
    }

    public SceneSurfaceResourceReference Surface { get; }

    public bool Equals(MaterialResourceId other)
    {
        return other != null && Surface.Equals(other.Surface);
    }

    public override bool Equals(object obj)
    {
        return obj is MaterialResourceId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Surface.GetHashCode();
    }

    public override string ToString() => Surface.ToString();
}

internal sealed class MaterialResourceRequest
{
    public MaterialResourceRequest(
        MaterialResourceId id,
        SceneSurfaceResourceReference textureReference,
        SceneMaterialBlendMode blendMode)
    {
        Id = id;
        TextureReference = textureReference;
        BlendMode = blendMode;
    }

    public MaterialResourceId Id { get; }
    public SceneSurfaceResourceReference TextureReference { get; }
    public SceneMaterialBlendMode BlendMode { get; }

    public MaterialResourceRequest WithTextureReference(SceneSurfaceResourceReference textureReference)
    {
        return new MaterialResourceRequest(Id, textureReference, BlendMode);
    }

    public static MaterialResourceRequest Opaque(SceneSurfaceResourceReference textureReference)
    {
        return new MaterialResourceRequest(
            new MaterialResourceId(textureReference),
            textureReference,
            SceneMaterialBlendMode.Opaque);
    }

    public static MaterialResourceRequest MissingTexture(SceneSurfaceResourceReference surfaceReference)
    {
        return new MaterialResourceRequest(
            new MaterialResourceId(surfaceReference),
            null,
            SceneMaterialBlendMode.Opaque);
    }

}
