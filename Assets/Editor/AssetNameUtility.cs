using System.Linq;

internal static class AssetNameUtility
{
    public static string SanitizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "ImportedTerrain";
        }

        var chars = value
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray();

        var sanitized = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "ImportedTerrain" : sanitized;
    }
}
