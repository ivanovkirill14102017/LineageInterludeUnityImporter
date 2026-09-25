internal sealed class CreatureImportProgress : IResourceImportProgress
{
    private readonly MapImportExecutionContext _context;

    public CreatureImportProgress(MapImportExecutionContext context)
    {
        _context = context;
    }

    public void Report(string phase, string detail, float progress)
    {
        _context?.Report($"Creatures / {phase}", detail, progress);
    }

    public void ReportItem(
        string phase,
        object resource,
        int index,
        int count,
        float start,
        float end)
    {
        var progress = count > 0
            ? start + ((end - start) * index / count)
            : end;
        Report(phase, $"{index + 1}/{count}  {resource}", progress);
    }
}
