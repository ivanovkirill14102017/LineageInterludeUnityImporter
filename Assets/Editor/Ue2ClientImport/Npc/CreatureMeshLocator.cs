using System;
using System.IO;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using L2Viewer.SceneDomain.Services.CharacterServices;

internal static class CreatureMeshLocator
{
    internal readonly struct ResolvedCreaturePackage
    {
        public ResolvedCreaturePackage(string packagePath, SceneSkeletalAsset sharedAsset)
        {
            PackagePath = packagePath;
            SharedAsset = sharedAsset;
        }

        public string PackagePath { get; }
        public SceneSkeletalAsset SharedAsset { get; }
    }

    public static ResolvedCreaturePackage ResolveByIdentifier(string clientRoot, string creatureIdentifier, Action<string> log)
    {
        if (string.IsNullOrWhiteSpace(clientRoot) || !Directory.Exists(clientRoot))
        {
            throw new DirectoryNotFoundException($"Client root was not found: {clientRoot}");
        }

        var normalizedMeshName = CreatureIdentifierUtility.NormalizeCreatureIdentifier(creatureIdentifier);
        if (string.IsNullOrWhiteSpace(normalizedMeshName))
        {
            throw new InvalidOperationException("Creature identifier is required.");
        }

        log?.Invoke($"Searching client packages for skeletal mesh '{normalizedMeshName}'.");

        var animationsRoot = Path.Combine(clientRoot, "animations");
        var searchRoot = Directory.Exists(animationsRoot) ? animationsRoot : clientRoot;
        var packagePaths = Directory
            .EnumerateFiles(searchRoot, "*.ukx", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (packagePaths.Length == 0)
        {
            throw new FileNotFoundException($"No .ukx packages were found under '{searchRoot}'.");
        }

        var resolver = new SceneSkeletalMeshResolver();
        var candidateNames = CreatureIdentifierUtility.BuildCandidateMeshNames(normalizedMeshName);
        foreach (var packagePath in packagePaths)
        {
            foreach (var candidateName in candidateNames)
            {
                try
                {
                    var sharedAsset = resolver.ResolveAssetNamed(packagePath, candidateName);
                    if (sharedAsset != null)
                    {
                        return new ResolvedCreaturePackage(packagePath, sharedAsset);
                    }
                }
                catch
                {
                    // Ignore package misses and keep scanning for an exact mesh match.
                }
            }
        }

        throw new InvalidOperationException(
            $"Could not resolve skeletal mesh '{normalizedMeshName}' in client packages under '{searchRoot}'.");
    }
}
