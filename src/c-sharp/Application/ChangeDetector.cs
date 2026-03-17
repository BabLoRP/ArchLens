using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using Archlens.Domain.Utils;
using ProjectChanges = Archlens.Domain.Models.Records.ProjectChanges;
using ProjectDependencyGraph = Archlens.Domain.Models.ProjectDependencyGraph;

namespace Archlens.Application;

public sealed class ChangeDetector
{
    private readonly record struct ProjectItemMeta(RelativePath ParentDirRel, DateTime LastWriteUtc);

    private sealed record ProjectFileStructure(
        Dictionary<RelativePath, ProjectItemMeta> Files, // fileRel -> (parentDirRel, lastWriteUtc)
        HashSet<RelativePath> DirRels,
        Dictionary<RelativePath, HashSet<RelativePath>> ChildrenByDir
    );

    private sealed record ExclusionRule(
        string[] DirPrefixes,
        string[] Segments,
        string[] FileSuffixes
    );

    public static async Task<ProjectChanges> GetProjectChangesAsync(
        ParserOptions parserOptions,
        ProjectDependencyGraph? lastSavedGraph,
        CancellationToken ct = default)
    {
        var projectRoot = string.IsNullOrEmpty(parserOptions.BaseOptions.FullRootPath)
            ? Path.GetFullPath(parserOptions.BaseOptions.ProjectRoot)
            : parserOptions.BaseOptions.FullRootPath;

        var rules = CompileExclusions(parserOptions.Exclusions);

        var current = await Task.Run(
            () => ScanCurrentProjectFileStructure(projectRoot, parserOptions.FileExtensions, rules, ct),
            ct);

        var changedByDir = lastSavedGraph is null
            ? BuildFullStructure(current)
            : BuildDeltaStructure(projectRoot, current, lastSavedGraph, ct);

        var (deletedFiles, deletedDirs) = DiscoverDeletedPaths(
            projectRoot,
            lastSavedGraph,
            current.Files,
            current.DirRels,
            rules,
            ct);

        var collapsedDeletedDirs = CollapseDeletedDirectories(deletedDirs);

        deletedFiles.RemoveAll(file => IsUnderAnyDeletedDirectory(file, collapsedDeletedDirs));

        return new ProjectChanges(
            ChangedFilesByDirectory: FreezeChanged(changedByDir),
            DeletedFiles: [.. deletedFiles],
            DeletedDirectories: [.. collapsedDeletedDirs]
        );
    }

    private static Dictionary<RelativePath, List<RelativePath>> BuildFullStructure(ProjectFileStructure current)
    {
        return current.ChildrenByDir.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.OrderBy(p => p.Value, StringComparer.OrdinalIgnoreCase).ToList()
        );
    }

    private static Dictionary<RelativePath, List<RelativePath>> BuildDeltaStructure(
        string projectRoot,
        ProjectFileStructure current,
        ProjectDependencyGraph lastSavedGraph,
        CancellationToken ct)
    {
        var changedFiles = CollectChangedFiles(current, lastSavedGraph, ct);
        var neededDirs = CollectNeededDirs(projectRoot, current, changedFiles, lastSavedGraph, ct);
        return BuildDelta(current, neededDirs, changedFiles, lastSavedGraph, ct);
    }

    private static HashSet<RelativePath> CollectChangedFiles(
        ProjectFileStructure current,
        ProjectDependencyGraph lastSavedGraph,
        CancellationToken ct)
    {
        HashSet<RelativePath> changedFiles = [];

        foreach (var (fileRel, meta) in current.Files)
        {
            ct.ThrowIfCancellationRequested();

            var lastItem = lastSavedGraph.GetProjectItem(fileRel);
            var isNew = lastItem is null;
            var isModified = !isNew && TrimMilliseconds(meta.LastWriteUtc) > TrimMilliseconds(lastItem!.LastWriteTime);

            if (isNew || isModified)
                changedFiles.Add(fileRel);
        }

        return changedFiles;
    }

    private static HashSet<RelativePath> CollectNeededDirs(
        string projectRoot,
        ProjectFileStructure current,
        HashSet<RelativePath> changedFiles,
        ProjectDependencyGraph lastSavedGraph,
        CancellationToken ct)
    {
        HashSet<RelativePath> neededDirs = [];

        foreach (var file in changedFiles)
            AddDirAndAncestors(projectRoot, current.Files[file].ParentDirRel, neededDirs, lastSavedGraph, ct);

        foreach (var dir in current.DirRels)
        {
            ct.ThrowIfCancellationRequested();
            if (!lastSavedGraph.ContainsProjectItem(dir))
                AddDirAndAncestors(projectRoot, dir, neededDirs, lastSavedGraph, ct);
        }

        return neededDirs;
    }

    private static Dictionary<RelativePath, List<RelativePath>> BuildDelta(
        ProjectFileStructure current,
        HashSet<RelativePath> neededDirs,
        HashSet<RelativePath> changedFiles,
        ProjectDependencyGraph lastSavedGraph,
        CancellationToken ct)
    {
        var delta = new Dictionary<RelativePath, List<RelativePath>>();

        foreach (var dir in neededDirs)
        {
            ct.ThrowIfCancellationRequested();

            if (!current.ChildrenByDir.TryGetValue(dir, out var children))
            {
                if (!lastSavedGraph.ContainsProjectItem(dir))
                    delta[dir] = [];
                continue;
            }

            var relevantChildren = children
                .Where(child => IsChildRelevant(child, current.DirRels, neededDirs, changedFiles, lastSavedGraph))
                .ToList();

            if (relevantChildren.Count > 0)
                delta[dir] = relevantChildren;
        }

        return delta;
    }

    private static bool IsChildRelevant(
        RelativePath child,
        HashSet<RelativePath> currentDirRels,
        HashSet<RelativePath> neededDirs,
        HashSet<RelativePath> changedFiles,
        ProjectDependencyGraph lastSavedGraph)
    {
        return currentDirRels.Contains(child)
            ? neededDirs.Contains(child) || !lastSavedGraph.ContainsProjectItem(child)
            : changedFiles.Contains(child);
    }

    private static void AddDirAndAncestors(
        string projectRoot,
        RelativePath dir,
        HashSet<RelativePath> neededDirs,
        ProjectDependencyGraph lastSavedGraph,
        CancellationToken ct)
    {
        var current = dir;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (!neededDirs.Add(current))
                break;

            var parent = lastSavedGraph.ParentOf(current)
                ?? GetPathDerivedParent(current, projectRoot);
            if (parent is null)
                break;

            current = parent.Value;
        }
    }

    private static RelativePath? GetPathDerivedParent(RelativePath dir, string projectRoot)
    {
        var trimmed = dir.Value.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        if (lastSlash <= 0)
            return null;

        var parentSlice = trimmed[..(lastSlash + 1)];
        if (string.Equals(parentSlice, "./", StringComparison.Ordinal))
            return null;

        return RelativePath.Directory(projectRoot, parentSlice);
    }

    private static ExclusionRule CompileExclusions(IReadOnlyList<string> exclusions)
    {
        List<string> dirPrefixes = [];
        List<string> segments = [];
        List<string> suffixes = [];

        foreach (var entry in exclusions)
        {
            var norm = NormaliseExclusionEntry(entry);
            if (norm is null) continue;

            if (ToDirPrefix(norm) is { } prefix) { dirPrefixes.Add(prefix); continue; }
            if (norm.StartsWith("*.", StringComparison.Ordinal)) { suffixes.Add(norm[1..]); continue; }
            segments.Add(norm);
        }

        return new ExclusionRule(
            DirPrefixes: [.. dirPrefixes],
            Segments: [.. segments],
            FileSuffixes: [.. suffixes]
        );
    }

    // Returns null for blank/empty entries; otherwise strips **/  prefix, normalises slashes, and trims trailing dot.
    private static string? NormaliseExclusionEntry(string? entry)
    {
        var exclusion = (entry ?? string.Empty).Trim();
        if (exclusion.Length == 0) return null;

        if (exclusion.StartsWith("**/", StringComparison.Ordinal)) exclusion = exclusion[3..];

        var norm = exclusion.Replace('\\', '/');
        return norm.EndsWith('.') ? norm[..^1] : norm;
    }

    // Returns the canonical dir-prefix form when norm represents a directory pattern, otherwise null.
    private static string? ToDirPrefix(string norm)
    {
        // relative path with trailing '/' -> dir
        if (norm.EndsWith('/'))
        {
            var p = norm.StartsWith("./", StringComparison.Ordinal) ? norm[2..] : norm;
            return p.EndsWith('/') ? p : p + "/";
        }

        // relative path containing '/' but no trailing slash -> dir
        if (norm.Contains('/'))
            return norm + "/";

        return null;
    }

    private static ProjectFileStructure ScanCurrentProjectFileStructure(
        string projectRoot,
        IReadOnlyList<string> extensions,
        ExclusionRule rules,
        CancellationToken ct)
    {
        var extensionSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        var files = new ConcurrentDictionary<RelativePath, ProjectItemMeta>();
        var dirs = new ConcurrentDictionary<RelativePath, byte>();
        var childrenByDir = new ConcurrentDictionary<RelativePath, ConcurrentBag<RelativePath>>();

        var parallelOpts = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };

        void AddChild(RelativePath parent, RelativePath child)
            => childrenByDir.GetOrAdd(parent, _ => []).Add(child);

        void ScanDirectory(string dirAbs)
        {
            ct.ThrowIfCancellationRequested();

            var dirRel = RelativePath.Directory(projectRoot, dirAbs);
            dirs.TryAdd(dirRel, 0);

            string[] subdirs = [];
            string[] fileAbsList = [];
            try { subdirs = Directory.GetDirectories(dirAbs); } catch { }
            try { fileAbsList = Directory.GetFiles(dirAbs); } catch { }

            foreach (var fileAbs in fileAbsList)
            {
                ct.ThrowIfCancellationRequested();
                if (IsExcluded(projectRoot, fileAbs, rules)) continue;
                if (!extensionSet.Contains(Path.GetExtension(fileAbs))) continue;

                var fileRel = RelativePath.File(projectRoot, fileAbs);
                files[fileRel] = new ProjectItemMeta(dirRel, File.GetLastWriteTimeUtc(fileAbs));
                AddChild(dirRel, fileRel);
            }

            var includedSubdirs = subdirs
                .Where(s => !IsExcluded(projectRoot, s, rules))
                .ToArray();

            foreach (var subAbs in includedSubdirs)
            {
                var subRel = RelativePath.Directory(projectRoot, subAbs);
                dirs.TryAdd(subRel, 0);
                AddChild(dirRel, subRel);
            }

            Parallel.ForEach(includedSubdirs, parallelOpts, ScanDirectory);
        }

        ScanDirectory(projectRoot);

        return new ProjectFileStructure(
            Files: new Dictionary<RelativePath, ProjectItemMeta>(files),
            DirRels: [.. dirs.Keys],
            ChildrenByDir: childrenByDir.ToDictionary(
                kv => kv.Key,
                kv => new HashSet<RelativePath>(kv.Value))
        );
    }

    private static (List<RelativePath> deletedFilesRel, List<RelativePath> deletedDirsRel) DiscoverDeletedPaths(
        string projectRoot,
        ProjectDependencyGraph? lastSavedGraph,
        IReadOnlyDictionary<RelativePath, ProjectItemMeta> currentFiles,
        IReadOnlySet<RelativePath> currentDirs,
        ExclusionRule rules,
        CancellationToken ct)
    {
        List<RelativePath> deletedFiles = [];
        List<RelativePath> deletedDirs = [];

        if (lastSavedGraph is null || lastSavedGraph.ProjectItems is null)
            return (deletedFiles, deletedDirs);

        foreach (var item in lastSavedGraph.ProjectItems.Values)
        {
            ct.ThrowIfCancellationRequested();

            if (IsProjectRoot(item.Path))
                continue;

            var absPath = PathNormaliser.GetAbsolutePath(projectRoot, item.Path.Value);
            if (IsExcluded(projectRoot, absPath, rules))
                continue;

            if (item.Type == ProjectItemType.File)
            {
                if (!currentFiles.ContainsKey(item.Path))
                    deletedFiles.Add(item.Path);
            }
            else
            {
                if (!currentDirs.Contains(item.Path))
                    deletedDirs.Add(item.Path);
            }
        }
        return (deletedFiles, deletedDirs);
    }

    private static List<RelativePath> CollapseDeletedDirectories(IEnumerable<RelativePath> deletedDirsRel)
    {
        var ordered = deletedDirsRel
            .Where(d => !IsProjectRoot(d))
            .Distinct()
            .OrderBy(d => d.Value.Length)
            .ToList();

        List<RelativePath> kept = [];

        foreach (var d in ordered)
        {
            bool dominated = false;
            foreach (var parent in kept)
            {
                if (d.Value.StartsWith(parent.Value, StringComparison.OrdinalIgnoreCase))
                {
                    dominated = true;
                    break;
                }
            }
            if (!dominated)
                kept.Add(d);
        }
        return kept;
    }

    private static bool IsUnderAnyDeletedDirectory(RelativePath fileRel, IReadOnlyList<RelativePath> deletedDirsRel)
    {
        foreach (var deletedDir in deletedDirsRel)
        {
            if (fileRel.Value.StartsWith(deletedDir.Value, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static IReadOnlyDictionary<RelativePath, IReadOnlyList<RelativePath>> FreezeChanged(
        Dictionary<RelativePath, List<RelativePath>> changed)
    {
        return changed.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<RelativePath>)[.. kvp.Value.Distinct()]
        );
    }

    private static bool IsExcluded(string projectRoot, string content, ExclusionRule rules)
    {
        var path = GetRelative(projectRoot, content);

        // Plain loops — this is called for every file and directory during the scan,
        // so avoiding LINQ enumerator allocations per call matters.
        var pathWithSlash = path + '/';
        var pathWithBothSlashes = '/' + path + '/';
        foreach (var rule in rules.DirPrefixes)
        {
            if (pathWithSlash.StartsWith(rule, StringComparison.OrdinalIgnoreCase)
                || pathWithBothSlashes.Contains('/' + rule, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            foreach (var ban in rules.Segments)
            {
                if (MatchesSuffixPattern(segment, ban))
                    return true;
            }
        }

        var fileName = Path.GetFileName(path);
        foreach (var suf in rules.FileSuffixes)
        {
            if (fileName.EndsWith(suf, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool MatchesSuffixPattern(string value, string pattern)
    {
        if (!pattern.Contains('*'))
            return string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);

        var suffix = pattern.TrimStart('*');
        return value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProjectRoot(RelativePath path) =>
        string.Equals(path.Value, "./", StringComparison.Ordinal) ||
        string.Equals(path.Value, ".", StringComparison.Ordinal);

    private static DateTime TrimMilliseconds(DateTime dt) =>
        new(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, 0, dt.Kind);

    private static string GetRelative(string root, string path)
    {
        var rel = Path.GetRelativePath(root, path);
        return rel.Replace('\\', '/');
    }
}