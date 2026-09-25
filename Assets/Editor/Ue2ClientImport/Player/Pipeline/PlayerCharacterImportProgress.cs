internal sealed class PlayerCharacterImportProgress : IResourceImportProgress
{
    private readonly MapImportExecutionContext _context;

    public PlayerCharacterImportProgress(MapImportExecutionContext context)
    {
        _context = context;
    }

    public void Report(string phase, object resource, float progress)
    {
        _context?.Report("Player Archetype / " + phase, resource?.ToString(), progress);
    }

    public void ReportItem(string phase, object resource, int index, int count, float start, float end)
    {
        var value = count == 0 ? end : start + ((end - start) * index / count);
        Report(phase, $"{index + 1}/{count}  {resource}", value);
    }
}
