using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class CreatureIdentifierUtility
{
    public static string NormalizeCreatureIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        normalized = Path.GetFileNameWithoutExtension(normalized);
        var dotIndex = normalized.LastIndexOf('.');
        if (dotIndex >= 0 && dotIndex < normalized.Length - 1)
        {
            normalized = normalized.Substring(dotIndex + 1);
        }

        normalized = StripKnownPrefix(normalized, "PF_");
        normalized = StripKnownPrefix(normalized, "NPC_");
        normalized = StripKnownPrefix(normalized, "SM_");
        return normalized.Trim();
    }

    public static string[] BuildCandidateMeshNames(string meshName)
    {
        var names = new List<string>();

        void AddCandidate(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !names.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(value);
            }
        }

        AddCandidate(meshName);
        AddCandidate(StripKnownPrefix(meshName, "PF_"));
        AddCandidate(StripKnownPrefix(meshName, "NPC_"));
        AddCandidate(StripKnownPrefix(meshName, "SM_"));
        return names.ToArray();
    }

    public static string StripKnownPrefix(string value, string prefix)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(prefix))
        {
            return value ?? string.Empty;
        }

        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? value.Substring(prefix.Length)
            : value;
    }
}
