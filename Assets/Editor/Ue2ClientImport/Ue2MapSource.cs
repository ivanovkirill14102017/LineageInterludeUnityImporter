using L2Viewer.UnrFile;

internal sealed class Ue2MapSource
{
    public Ue2MapSource(string clientPath, string mapFullPath, UnrFile unrFile)
    {
        ClientPath = clientPath;
        MapFullPath = mapFullPath;
        UnrFile = unrFile;
    }

    public string ClientPath { get; }
    public string MapFullPath { get; }
    public UnrFile UnrFile { get; }
}
