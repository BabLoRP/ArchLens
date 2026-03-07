using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    public static Task<ProjectChanges> GetProjectChangesAsync(
        ParserOptions parserOptions,
        ProjectDependencyGraph? lastSavedGraph,
        CancellationToken ct = default)
    {
        var projectRoot = string.IsNullOrEmpty(parserOptions.BaseOptions.FullRootPath)
            ? Path.GetFullPath(parserOptions.BaseOptions.ProjectRoot)
            : parserOptions.BaseOptions.FullRootPath;

        var rules = CompileExclusions(parserOptions.Exclusions);

        var current = ScanCurrentProjectFileStructure(
            projectRoot,
            parserOptions.FileExtensions,
            rules,
            ct);

        var changedByDir = lastSavedGraph is null
            ? BuildFullStructure(current)
            : BuildDeltaStructure(current, lastSavedGraph, ct);

        var (deletedFiles, deletedDirs) = DiscoverDeletedPaths(
            lastSavedGraph,
            current.Files,
            current.DirRels,
            ct);

        var collapsedDeletedDirs = CollapseDeletedDirectories(deletedDirs);

        deletedFiles.RemoveAll(file => IsUnderAnyDeletedDirectory(file, collapsedDeletedDirs));

        return Task.FromResult(new ProjectChanges(
            ChangedFilesByDirectory: FreezeChanged(changedByDir),
            DeletedFiles: [.. deletedFiles],
            DeletedDirectories: [.. collapsedDeletedDirs]
        ));
    }

    private static Dictionary<RelativePath, List<RelativePath>> BuildFullStructure(ProjectFileStructure current)
    {
        return current.ChildrenByDir.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.OrderBy(p => p.Value, StringComparer.OrdinalIgnoreCase).ToList()
        );
    }

    private static Dictionary<RelativePath, List<RelativePath>> BuildDeltaStructure(
    ProjectFileStructure current,
    ProjectDependencyGraph lastSavedGraph,
    CancellationToken ct)
    {
        HashSet<RelativePath> changedFiles = [];

        foreach (var (fileRel, meta) in current.Files)
        {
            ct.ThrowIfCancellationRequested();

            var lastItem = lastSavedGraph.GetProjectItem(fileRel);
            if (lastItem is null)
            {
                changedFiles.Add(fileRel);
                continue;
            }

            if (TrimMilliseconds(meta.LastWriteUtc) > TrimMilliseconds(lastItem.LastWriteTime))
                changedFiles.Add(fileRel);
        }

        HashSet<RelativePath> neededDirs = [];

        foreach (var file in changedFiles)
        {
            var parent = current.Files[file].ParentDirRel;
            AddDirAndAncestors(parent, neededDirs, lastSavedGraph, ct);
        }

        foreach (var dir in current.DirRels)
        {
            ct.ThrowIfCancellationRequested();
            if (!lastSavedGraph.ContainsProjectItem(dir))
                AddDirAndAncestors(dir, neededDirs, lastSavedGraph, ct);
        }

        var delta = new Dictionary<RelativePath, List<RelativePath>>();

        foreach (var dir in neededDirs)
        {
            ct.ThrowIfCancellationRequested();

            if (!current.ChildrenByDir.TryGetValue(dir, out var children))
                continue;

            var list = new List<RelativePath>();

            foreach (var child in children)
            {
                var childItemIsDir = current.DirRels.Contains(child);

                if (childItemIsDir)
                {
                    if (neededDirs.Contains(child) || !lastSavedGraph.ContainsProjectItem(child))
                        list.Add(child);
                }
                else
                {
                    if (changedFiles.Contains(child))
                        list.Add(child);
                }
            }

            if (list.Count > 0)
                delta[dir] = list;
        }

        return delta;
    }

    private static void AddDirAndAncestors(
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

            var parent = lastSavedGraph.ParentOf(current);
            if (parent is null)
                break;

            current = parent.Value;
        }
    }

    private static ExclusionRule CompileExclusions(IReadOnlyList<string> exclusions)
    {
        List<string> dirPrefixes = [];
        List<string> segments = [];
        List<string> suffixes = [];

        foreach (var entry in exclusions)
        {
            var exclusion = (entry ?? string.Empty).Trim();
            if (exclusion.Length == 0) continue;

            if (exclusion.StartsWith("**/", StringComparison.Ordinal)) exclusion = exclusion[3..];

            var norm = exclusion.Replace('\\', '/');
            if (norm.EndsWith('.')) norm = norm[..^1];

            // relative path with trailing '/' -> dir
            if (norm.EndsWith('/'))
            {
                var p = norm;
                if (p.StartsWith("./", StringComparison.Ordinal)) p = p[2..];
                if (!p.EndsWith('/')) p += "/";
                dirPrefixes.Add(p);
                continue;
            }

            // a relative path without trailing '/' -> dir
            if (norm.Contains('/'))
            {
                var p = norm;
                if (!p.EndsWith('/')) p += "/";
                dirPrefixes.Add(p);
                continue;
            }

            // Filename wildcard like "*.dev.cs" -> suffix on filename
            if (norm.StartsWith("*.", StringComparison.Ordinal))
            {
                suffixes.Add(norm[1..]);
                continue;
            }

            segments.Add(norm.TrimStart('.'));
        }

        return new ExclusionRule(
            DirPrefixes: [.. dirPrefixes],
            Segments: [.. segments],
            FileSuffixes: [.. suffixes]
        );
    }

    private static ProjectFileStructure ScanCurrentProjectFileStructure(
    string projectRoot,
    IReadOnlyList<string> extensions,
    ExclusionRule rules,
    CancellationToken ct)
    {
        var extensionsSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

        var files = new Dictionary<RelativePath, ProjectItemMeta>();
        var dirs = new HashSet<RelativePath>();
        var childrenByDir = new Dictionary<RelativePath, HashSet<RelativePath>>();

        void AddChild(RelativePath parent, RelativePath child)
        {
            if (!childrenByDir.TryGetValue(parent, out var set))
            {
                set = [];
                childrenByDir[parent] = set;
            }
            set.Add(child);
        }

        var stack = new Stack<string>();
        stack.Push(projectRoot);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var dirAbs = stack.Pop();
            var dirRel = RelativePath.Directory(projectRoot, dirAbs);
            dirs.Add(dirRel);

            IEnumerable<string> subdirs = [];
            try { subdirs = Directory.EnumerateDirectories(dirAbs); } catch { }

            foreach (var subAbs in subdirs)
            {
                ct.ThrowIfCancellationRequested();
                if (IsExcluded(projectRoot, subAbs, rules))
                    continue;

                var subRel = RelativePath.Directory(projectRoot, subAbs);
                dirs.Add(subRel);
                AddChild(dirRel, subRel);

                stack.Push(subAbs);
            }

            IEnumerable<string> fileAbsList = [];
            try { fileAbsList = Directory.EnumerateFiles(dirAbs); } catch { }

            foreach (var fileAbs in fileAbsList)
            {
                ct.ThrowIfCancellationRequested();

                if (IsExcluded(projectRoot, fileAbs, rules))
                    continue;

                var ext = Path.GetExtension(fileAbs);
                if (!extensionsSet.Contains(ext))
                    continue;

                var fileRel = RelativePath.File(projectRoot, fileAbs);
                var writeUtc = File.GetLastWriteTimeUtc(fileAbs);

                files[fileRel] = new ProjectItemMeta(dirRel, writeUtc);
                AddChild(dirRel, fileRel);
            }
        }

        return new ProjectFileStructure(files, dirs, childrenByDir);
    }

    private static (List<RelativePath> deletedFilesRel, List<RelativePath> deletedDirsRel) DiscoverDeletedPaths(
        ProjectDependencyGraph? lastSavedGraph,
        IReadOnlyDictionary<RelativePath, ProjectItemMeta> currentFiles,
        IReadOnlySet<RelativePath> currentDirs,
        CancellationToken ct)
    {
        List<RelativePath> deletedFiles = [];
        List<RelativePath> deletedDirs = [];

        if (lastSavedGraph is null  || lastSavedGraph.ProjectItems is null)
            return (deletedFiles, deletedDirs);

        foreach (var item in lastSavedGraph.ProjectItems.Values)
        {
            ct.ThrowIfCancellationRequested();

            if (IsProjectRoot(item.Path))
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
            if (kept.Any(parent => d.Value.StartsWith(parent.Value, StringComparison.OrdinalIgnoreCase)))
                continue;

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

        if (rules.DirPrefixes.Any(rule => (path + '/').StartsWith(rule, StringComparison.OrdinalIgnoreCase)))
            return true;

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
            return string.Equals(value, pattern, StringComparison.Ordinal);

        var suffix = pattern.TrimStart('*');
        return value.EndsWith(suffix, StringComparison.Ordinal);
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