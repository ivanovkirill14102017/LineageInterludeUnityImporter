using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using L2Viewer.DatFile;
using L2Viewer.DbFile.DbJson;
using L2Viewer.DbFile.DbJson.Models;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services.CharacterServices;
using L2Viewer.SceneDomain.Services.Utility;
using UnityEditor;
using UnityEngine;

public sealed class NonPermanentVisualImporterWindow : EditorWindow
{
    private NonPermanentVisualImporterPanel _panel;

    [MenuItem("L2/Import Non-Permanent Visuals")]
    private static void OpenWindow()
    {
        var window = GetWindow<NonPermanentVisualImporterWindow>("Non-Permanent Visuals");
        window.minSize = new Vector2(720f, 420f);
        window.Show();
    }

    private void OnEnable()
    {
        _panel = new NonPermanentVisualImporterPanel(Repaint);
    }

    private void OnGUI()
    {
        _panel ??= new NonPermanentVisualImporterPanel(Repaint);
        _panel.OnGUI();
    }
}

internal sealed class NonPermanentVisualImporterPanel
{
    private readonly Action _repaint;
    private string _status = "Ready to import non-permanent creature visuals.";
    private Vector2 _scroll;

    public NonPermanentVisualImporterPanel(Action repaint)
    {
        _repaint = repaint;
    }

    public void OnGUI()
    {
        EditorGUILayout.LabelField("Import non-permanent visuals", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Client", ConstInfo.L2GameClientPath);
            EditorGUILayout.LabelField("DB", ConstInfo.L2DbRootPath);
            EditorGUILayout.LabelField("Creature Output", CreatureNpcImportBuilder.PrefabOutputRoot);
            EditorGUILayout.LabelField("Mode", "Build prefab assets only. Nothing is placed into the current scene.");
        }

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(EditorApplication.isCompiling))
        {
            if (GUILayout.Button("Import Non-Permanent Objects", GUILayout.Height(34f)))
            {
                Import();
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
        using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll, GUILayout.MinHeight(150f)))
        {
            _scroll = scroll.scrollPosition;
            EditorGUILayout.TextArea(_status, GUILayout.ExpandHeight(true));
        }
    }

    private void Import()
    {
        try
        {
            _status = "Import started...";
            var result = NonPermanentVisualImportBuilder.Import(
                ConstInfo.L2GameClientPath,
                ConstInfo.L2DbRootPath,
                AppendStatus);
            _status +=
                "\nDone." +
                $"\nCandidates: {result.CandidateCount}" +
                $"\nCreated: {result.CreatedCount}" +
                $"\nReused/skipped existing: {result.ReusedCount}" +
                $"\nFailed: {result.FailedCount}";
        }
        catch (Exception exception)
        {
            _status = exception.ToString();
            UnityEngine.Debug.LogException(exception);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            _repaint?.Invoke();
        }
    }

    private void AppendStatus(string message)
    {
        _status = $"{_status}\n{message}";
        _repaint?.Invoke();
    }
}

internal static class NonPermanentVisualImportBuilder
{
    internal readonly struct ImportResult
    {
        public ImportResult(int candidateCount, int createdCount, int reusedCount, int failedCount)
        {
            CandidateCount = candidateCount;
            CreatedCount = createdCount;
            ReusedCount = reusedCount;
            FailedCount = failedCount;
        }

        public int CandidateCount { get; }
        public int CreatedCount { get; }
        public int ReusedCount { get; }
        public int FailedCount { get; }
    }

    private sealed class VisualCandidate
    {
        public int NpcId { get; set; }
        public string DisplayName { get; set; }
        public string DbClassName { get; set; }
        public NpcGrpDatEntry Visual { get; set; }
        public string SourceTags { get; set; }
    }

    private sealed class RandomSpawnRow
    {
        public int groupId { get; set; }
        public int npcId { get; set; }
    }

    private sealed class RandomSpawnLocationRow
    {
        public int groupId { get; set; }
    }

    private sealed class RaidBossSpawnlistRow
    {
        public int boss_id { get; set; }
    }

    private sealed class PetStatsRow
    {
        public int typeID { get; set; }
    }

    public static ImportResult Import(string clientRoot, string dbRootPath, Action<string> log)
    {
        var stopwatch = Stopwatch.StartNew();
        var normalizedClientRoot = NormalizeClientRoot(clientRoot);
        var normalizedDbRoot = NormalizeDbRoot(dbRootPath);
        var systemRoot = Path.Combine(normalizedClientRoot, "system");
        var npcGrpPath = Path.Combine(systemRoot, "npcgrp.dat");
        var npcNamePath = Path.Combine(systemRoot, "npcname-e.dat");
        if (!File.Exists(npcGrpPath))
        {
            throw new FileNotFoundException("npcgrp.dat was not found.", npcGrpPath);
        }

        if (!File.Exists(npcNamePath))
        {
            throw new FileNotFoundException("npcname-e.dat was not found.", npcNamePath);
        }

        log?.Invoke("[NonPermanentVisuals] Reading dat/json catalogs.");
        var npcVisuals = DatFileReader.ReadDocument<NpcGrpDatDocument>(npcGrpPath).Entries
            .Where(x => x != null && x.Tag <= int.MaxValue)
            .GroupBy(x => checked((int)x.Tag))
            .ToDictionary(x => x.Key, x => x.First());
        var npcNames = DatFileReader.ReadDocument<NpcNameDatDocument>(npcNamePath).Entries
            .Where(x => x != null && x.Id <= int.MaxValue)
            .GroupBy(x => checked((int)x.Id))
            .ToDictionary(x => x.Key, x => x.First());
        var npcRows = ReadTable<NpcRow>(Path.Combine(normalizedDbRoot, "npc.json"), log)
            .GroupBy(x => decimal.ToInt32(decimal.Truncate(x.id)))
            .ToDictionary(x => x.Key, x => x.First());
        var positionedIds = CollectPositionedNpcIds(normalizedDbRoot, log);
        var petIds = ReadTable<PetStatsRow>(Path.Combine(normalizedDbRoot, "pets_stats.json"), log)
            .Select(x => x.typeID)
            .ToHashSet();
        var summonItemIds = CollectSummonItemNpcIds(normalizedDbRoot, log);
        var staticObjectCount = CountStaticObjectDatEntries(systemRoot, log);

        log?.Invoke(
            $"[NonPermanentVisuals] npcgrp={npcVisuals.Count}, positioned={positionedIds.Count}, pets={petIds.Count}, summon-items={summonItemIds.Count}, staticobject.dat={staticObjectCount}.");

        var candidates = npcVisuals
            .Where(x => !positionedIds.Contains(x.Key))
            .Where(x => HasUsableCreatureVisual(x.Value))
            .Select(x => BuildCandidate(x.Key, x.Value, npcNames, npcRows, petIds, summonItemIds))
            .OrderBy(x => x.NpcId)
            .ToArray();

        log?.Invoke($"[NonPermanentVisuals] Importing {candidates.Length} non-positioned creature visual candidates.");
        if (staticObjectCount > 0)
        {
            log?.Invoke("[NonPermanentVisuals] staticobject-e.dat was read for diagnostics only; this dat contains ids/names, not Package.StaticMesh references, so no static mesh prefab can be resolved from it yet.");
        }

        var packageIndex = ScenePackageIndexer.BuildResourcePackageIndex(normalizedClientRoot);
        var resolver = new SceneSkeletalMeshResolver();
        var buildContext = L2SkeletalAnimatorPrefabBuilder.CreateBuildContext(normalizedClientRoot);
        var created = 0;
        var reused = 0;
        var failed = 0;

        for (var i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            if (EditorUtility.DisplayCancelableProgressBar(
                    "Import Non-Permanent Visuals",
                    $"{i + 1}/{candidates.Length}: {candidate.DisplayName}",
                    candidates.Length == 0 ? 1f : (float)i / candidates.Length))
            {
                log?.Invoke("[NonPermanentVisuals] Import cancelled by user.");
                break;
            }

            try
            {
                if (!TryBuildResourceLocation(normalizedClientRoot, packageIndex, candidate.Visual.Mesh, "SkeletalMesh", out var meshResource))
                {
                    failed++;
                    log?.Invoke($"[NonPermanentVisuals] Missing mesh package for npc={candidate.NpcId}, mesh='{candidate.Visual.Mesh}'.");
                    continue;
                }

                var variantKey = $"npc_{candidate.NpcId:D6}";
                var referenceText = $"{meshResource.Reference}.{variantKey}";
                var prefabPath = L2AssetManager.BuildClientPackageAssetPath(
                    L2AssetManager.ManagedCreaturePrefabsRoot,
                    referenceText,
                    "PF",
                    "prefab",
                    "CreaturePrefabs");
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                {
                    reused++;
                    continue;
                }

                var sharedAsset = resolver.ResolveAssetNamed(meshResource.PackagePath, meshResource.ObjectName);
                var prepared = L2SkeletalAnimatorPrefabBuilder.PrepareFromResolvedAsset(
                    normalizedClientRoot,
                    sharedAsset,
                    L2AssetManager.ManagedCreaturePrefabsRoot,
                    L2AssetManager.SharedSkeletalCharactersRoot,
                    referenceText,
                    log,
                    buildContext,
                    includeMaterials: false,
                    assetObjectRootSuffix: variantKey);
                ApplyTextureOverrides(prepared.CharacterAsset, candidate.Visual, normalizedClientRoot, packageIndex);
                var preparedWithMaterials = L2SkeletalAnimatorPrefabBuilder.MaterializePreparedBuildMaterials(prepared, log);
                L2SkeletalAnimatorPrefabBuilder.CompletePreparedBuild(
                    preparedWithMaterials,
                    prefabNameSuffix: null,
                    displayLabel: BuildDisplayLabel(candidate),
                    log,
                    finalizeAssets: false,
                    archetypeNameSuffix: variantKey);
                created++;
            }
            catch (Exception ex)
            {
                failed++;
                log?.Invoke($"[NonPermanentVisuals] Failed npc={candidate.NpcId} '{candidate.DisplayName}': {ex.Message}");
            }
        }

        AssetDatabase.SaveAssets();
        EditorUtility.ClearProgressBar();
        stopwatch.Stop();
        log?.Invoke($"[NonPermanentVisuals] Finished in {stopwatch.Elapsed.TotalSeconds:F2}s. Created={created}, reused={reused}, failed={failed}.");
        return new ImportResult(candidates.Length, created, reused, failed);
    }

    private static VisualCandidate BuildCandidate(
        int npcId,
        NpcGrpDatEntry visual,
        IReadOnlyDictionary<int, NpcNameDatEntry> npcNames,
        IReadOnlyDictionary<int, NpcRow> npcRows,
        IReadOnlyCollection<int> petIds,
        IReadOnlyCollection<int> summonItemIds)
    {
        npcNames.TryGetValue(npcId, out var npcName);
        npcRows.TryGetValue(npcId, out var npcRow);
        var tags = new List<string>();
        if (petIds.Contains(npcId))
        {
            tags.Add("pet");
        }

        if (summonItemIds.Contains(npcId))
        {
            tags.Add("summon_item");
        }

        if (tags.Count == 0)
        {
            tags.Add("template_only");
        }

        return new VisualCandidate
        {
            NpcId = npcId,
            Visual = visual,
            DisplayName = FirstNonEmpty(npcName?.Name, npcRow?.name, $"NPC {npcId}"),
            DbClassName = npcRow?.@class ?? string.Empty,
            SourceTags = string.Join(",", tags)
        };
    }

    private static string BuildDisplayLabel(VisualCandidate candidate)
    {
        var suffix = string.IsNullOrWhiteSpace(candidate.SourceTags) ? string.Empty : $" [{candidate.SourceTags}]";
        return $"{candidate.DisplayName} ({candidate.NpcId}){suffix}";
    }

    private static bool HasUsableCreatureVisual(NpcGrpDatEntry visual)
    {
        return visual != null &&
               !string.IsNullOrWhiteSpace(visual.Mesh);
    }

    private static HashSet<int> CollectPositionedNpcIds(string dbRootPath, Action<string> log)
    {
        var result = new HashSet<int>();
        foreach (var row in ReadTable<SpawnlistRow>(Path.Combine(dbRootPath, "spawnlist.json"), log))
        {
            result.Add(row.npc_templateid);
        }

        foreach (var row in ReadTable<RaidBossSpawnlistRow>(Path.Combine(dbRootPath, "raidboss_spawnlist.json"), log))
        {
            result.Add(row.boss_id);
        }

        var randomLocationGroups = ReadTable<RandomSpawnLocationRow>(Path.Combine(dbRootPath, "random_spawn_loc.json"), log)
            .Select(x => x.groupId)
            .ToHashSet();
        foreach (var row in ReadTable<RandomSpawnRow>(Path.Combine(dbRootPath, "random_spawn.json"), log))
        {
            if (randomLocationGroups.Contains(row.groupId))
            {
                result.Add(row.npcId);
            }
        }

        return result;
    }

    private static HashSet<int> CollectSummonItemNpcIds(string dbRootPath, Action<string> log)
    {
        var result = new HashSet<int>();
        var summonItemsPath = ResolveSiblingDataFile(dbRootPath, "summon_items.csv");
        if (string.IsNullOrWhiteSpace(summonItemsPath) || !File.Exists(summonItemsPath))
        {
            return result;
        }

        foreach (var line in File.ReadLines(summonItemsPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = trimmed.Split(';');
            if (parts.Length < 2)
            {
                continue;
            }

            if (int.TryParse(parts[1], out var npcId))
            {
                result.Add(npcId);
            }
        }

        log?.Invoke($"[NonPermanentVisuals] Read summon item visuals: {result.Count} ids from {summonItemsPath}");
        return result;
    }

    private static int CountStaticObjectDatEntries(string systemRoot, Action<string> log)
    {
        var path = Path.Combine(systemRoot, "staticobject-e.dat");
        if (!File.Exists(path))
        {
            return 0;
        }

        try
        {
            return DatFileReader.ReadDocument<StaticObjectDatDocument>(path).Entries.Count;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[NonPermanentVisuals] Failed to read staticobject-e.dat: {ex.Message}");
            return 0;
        }
    }

    private static void ApplyTextureOverrides(
        L2SkeletalCharacterAsset asset,
        NpcGrpDatEntry visual,
        string clientRoot,
        IReadOnlyDictionary<string, string> packageIndex)
    {
        if (asset == null || visual == null)
        {
            return;
        }

        var textureResources = (visual.Textures1 ?? Array.Empty<string>())
            .Concat(visual.Textures2 ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(x => TryBuildResourceLocation(clientRoot, packageIndex, x, "Texture", out var location) ? location : null)
            .Where(x => x != null)
            .ToArray();
        if (textureResources.Length == 0)
        {
            return;
        }

        asset.UsedTextures = textureResources
            .Select(x => new L2SkeletalTextureRefData
            {
                Reference = x.Reference,
                ResolvedPackagePath = x.PackagePath
            })
            .ToArray();
        asset.PrimaryTextureReference = textureResources[0].Reference;

        var bindings = asset.MaterialBindings ?? Array.Empty<L2SkeletalMaterialBindingData>();
        for (var i = 0; i < bindings.Length && i < textureResources.Length; i++)
        {
            var binding = bindings[i];
            if (binding == null)
            {
                continue;
            }

            var resource = textureResources[i];
            binding.PackageName = resource.PackageName;
            binding.ObjectName = resource.ObjectName;
            binding.TextureReference = resource.Reference;
            binding.ResolvedPackagePath = resource.PackagePath;
        }
    }

    private static bool TryBuildResourceLocation(
        string clientRoot,
        IReadOnlyDictionary<string, string> packageIndex,
        string reference,
        string className,
        out SceneResourceLocation location)
    {
        location = null;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        try
        {
            var parsed = SceneReferenceUtilities.ParseFromDbResourceReference(reference);
            if (!packageIndex.TryGetValue(parsed.PackageName, out var packagePath))
            {
                return false;
            }

            location = SceneReferenceUtilities.BuildResourceLocation(
                clientRoot,
                packagePath,
                parsed.PackageName,
                parsed.ObjectName,
                className);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static List<T> ReadTable<T>(string path, Action<string> log) where T : new()
    {
        if (!File.Exists(path))
        {
            return new List<T>();
        }

        try
        {
            return TableJsonMapper.Read<T>(path);
        }
        catch (Exception ex)
        {
            log?.Invoke($"[NonPermanentVisuals] Failed to read {Path.GetFileName(path)}: {ex.Message}");
            return new List<T>();
        }
    }

    private static string ResolveSiblingDataFile(string dbRootPath, string fileName)
    {
        var current = new DirectoryInfo(dbRootPath);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string NormalizeDbRoot(string dbRootPath)
    {
        var normalizedPath = Path.GetFullPath(dbRootPath ?? string.Empty);
        if (File.Exists(normalizedPath))
        {
            return Path.GetDirectoryName(normalizedPath) ?? normalizedPath;
        }

        if (Directory.Exists(Path.Combine(normalizedPath, "InterludeDb")))
        {
            return Path.Combine(normalizedPath, "InterludeDb");
        }

        return normalizedPath;
    }

    private static string NormalizeClientRoot(string clientRoot)
    {
        var normalizedPath = Path.GetFullPath(clientRoot ?? string.Empty);
        return File.Exists(normalizedPath)
            ? Path.GetDirectoryName(normalizedPath) ?? normalizedPath
            : normalizedPath;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}
