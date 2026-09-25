internal interface IResourceImportProgress
{
    void ReportItem(string phase, object resource, int index, int count, float start, float end);
}
