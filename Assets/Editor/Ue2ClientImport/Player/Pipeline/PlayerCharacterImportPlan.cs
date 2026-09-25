using L2Viewer.SceneDomain.Models;

internal sealed class PlayerCharacterImportPlan
{
    public PlayerCharacterImportPlan(
        SceneCharacterAppearanceData appearance,
        PlayerCharacterVariantPlan[] variants)
    {
        Appearance = appearance;
        Variants = variants;
    }

    public SceneCharacterAppearanceData Appearance { get; }
    public PlayerCharacterVariantPlan[] Variants { get; }
}

internal sealed class PlayerCharacterVariantPlan
{
    public PlayerCharacterVariantPlan(
        string slotName,
        string displayName,
        string key,
        int id,
        int auxiliaryId,
        SceneCharacterPartBinding binding,
        SceneResourceReference[] meshReferences,
        SceneResourceReference[] surfaceReferences)
    {
        SlotName = slotName;
        DisplayName = displayName;
        Key = key;
        Id = id;
        AuxiliaryId = auxiliaryId;
        Binding = binding;
        MeshReferences = meshReferences;
        SurfaceReferences = surfaceReferences;
    }

    public string SlotName { get; }
    public string DisplayName { get; }
    public string Key { get; }
    public int Id { get; }
    public int AuxiliaryId { get; }
    public SceneCharacterPartBinding Binding { get; }
    public SceneResourceReference[] MeshReferences { get; }
    public SceneResourceReference[] SurfaceReferences { get; }
}
