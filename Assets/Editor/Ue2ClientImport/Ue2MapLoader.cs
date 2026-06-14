using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using L2Viewer.UnrFile;

internal static class Ue2MapLoader
{
    public static Task<Ue2MapSource> LoadAsync(MapImportRequest request, System.Action<string> log)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var clientPath = ConstInfo.L2GameClientPath;
            var mapFullPath = Path.Combine(clientPath, request.MapRelativePath);

            ValidateInputs(clientPath, mapFullPath);
            log($"Loading map: {mapFullPath}");

            var unrFile = UnrFileReader.Read(mapFullPath);
            return Task.FromResult(new Ue2MapSource(clientPath, mapFullPath, unrFile));
        }
        finally
        {
            stopwatch.Stop();
            log($"Map load took {stopwatch.Elapsed.TotalSeconds:F2}s");
        }
    }

    private static void ValidateInputs(string clientPath, string mapFullPath)
    {
        if (!Directory.Exists(clientPath))
        {
            throw new DirectoryNotFoundException($"Client folder was not found: {clientPath}");
        }

        if (!File.Exists(mapFullPath))
        {
            throw new FileNotFoundException($"Map file was not found: {mapFullPath}", mapFullPath);
        }
    }
}
