using UnityEditor;

[CustomEditor(typeof(L2CameraAtmosphereProbe))]
public sealed class L2CameraAtmosphereProbeEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
    }

    public override bool RequiresConstantRepaint()
    {
        return true;
    }
}
