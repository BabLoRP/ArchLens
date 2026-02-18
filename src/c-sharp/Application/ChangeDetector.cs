using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using Archlens.Domain.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Archlens.Application;

public sealed class ChangeDetector
{
    private readonly record struct ProjectItemMeta(string ParentDirRel, DateTime LastWriteUtc);

    private sealed record ProjectFileStructure(
        Dictionary<string, ProjectItemMeta> Files,    // fileRel -> (parentDirRel, lastWriteUtc)
        HashSet<string> DirRels                // dirRel set
    );

    private sealed record ExclusionRule(
        string[] DirPrefixes,      // strings that end with a '/', to indicate that it is a dir, like: "src/legacy/", "Tests/"
        string[] Segments,         // folders that are often mid-path, like: "bin", "obj", ".git"
        string[] FileSuffixes      // specific file postfixes, like: "*.dev.cs", ".g.cs"
    );

    public static Task<ProjectChanges> GetProjectChangesAsync(
    ParserOptions parserOptions,
    DependencyGraph? lastSavedGraph,
    CancellationToken ct = default)
    {
        var projectRoot = string.IsNullOrEmpty(parserOptions.BaseOptions.FullRootPath)
            ? Path.GetFullPath(parserOptions.BaseOptions.ProjectRoot)
            : parserOptions.BaseOptions.FullRootPath;

        var rules = CompileExclusions(parserOptions.Exclusions);

        var current = ScanCurrentProjectFileStructure(projectRoot, parserOptions.FileExtensions, rules, ct);

        var changedByDir = FindUpsertedFilesByDirectory(current.Files, lastSavedGraph, ct);

        var (deletedFiles, deletedDirs) = DiscoverDeletedPaths(lastSavedGraph, current.Files, current.DirRels, ct);

        var collapsedDeletedDirs = CollapseDeletedDirectories(deletedDirs);
        var collapsedSet = new HashSet<string>(collapsedDeletedDirs, StringComparer.OrdinalIgnoreCase);

        deletedFiles.RemoveAll(f => IsUnderAnyDeletedDirectory(f, collapsedSet));

        return Task.FromResult(new ProjectChanges(
            ChangedFilesByDirectory: FreezeChanged(changedByDir),
            DeletedFiles: deletedFiles,
            DeletedDirectories: collapsedDeletedDirs
        ));
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

        var files = new Dictionary<string, ProjectItemMeta>(StringComparer.OrdinalIgnoreCase);
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var stack = new Stack<string>();
        stack.Push(projectRoot);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var dirAbs = stack.Pop();
            var dirRel = PathNormaliser.NormaliseModule(projectRoot, dirAbs);
            dirs.Add(dirRel);

            IEnumerable<string> subdirs = [];
            try { subdirs = Directory.EnumerateDirectories(dirAbs); } catch { /* ignore */ }

            foreach (var subAbs in subdirs)
            {
                ct.ThrowIfCancellationRequested();
                if (IsExcluded(projectRoot, subAbs, rules)) continue;
                stack.Push(subAbs);
            }

            IEnumerable<string> fileAbsList = [];
            try { fileAbsList = Directory.EnumerateFiles(dirAbs); } catch { /* ignore */ }

            foreach (var fileAbs in fileAbsList)
            {
                ct.ThrowIfCancellationRequested();

                if (IsExcluded(projectRoot, fileAbs, rules)) continue;

                var ext = Path.GetExtension(fileAbs);
                if (!extensionsSet.Contains(ext)) continue;

                var fileRel = PathNormaliser.NormaliseFile(projectRoot, fileAbs);
                var writeUtc = DateTimeNormaliser.NormaliseUTC(File.GetLastWriteTimeUtc(fileAbs));

                // If duplicates -> last one wins
                files[fileRel] = new ProjectItemMeta(dirRel, writeUtc);
            }
        }

        return new ProjectFileStructure(files, dirs);
    }

    private static Dictionary<string, List<string>> FindUpsertedFilesByDirectory(
        IReadOnlyDictionary<string, ProjectItemMeta> files,
        DependencyGraph? lastSavedGraph,
        CancellationToken ct)
    {
        Dictionary<string, List<string>> changed = [];

        foreach (var (fileRel, meta) in files)
        {
            ct.ThrowIfCancellationRequested();

            var lastNode = lastSavedGraph?.FindByPath(fileRel);
            var isNew = lastNode is null;

            var isModified = false;
            if (!isNew)
            {
                var lastWrite = lastNode!.LastWriteTime;
                isModified = TrimMilliseconds(meta.LastWriteUtc) > TrimMilliseconds(lastWrite);
            }

            if (!isNew && !isModified)
                continue;

            if (!changed.TryGetValue(meta.ParentDirRel, out var list))
            {
                list = [];
                changed[meta.ParentDirRel] = list;
            }

            list.Add(fileRel);
        }

        return changed;
    }

    private static (List<string> deletedFilesRel, List<string> deletedDirsRel) DiscoverDeletedPaths(
        DependencyGraph? lastSavedGraph,
        IReadOnlyDictionary<string, ProjectItemMeta> currentFiles,
        IReadOnlySet<string> currentDirs,
        CancellationToken ct)
    {
        List<string> deletedFiles = [];
        HashSet<string> deletedDirs = [];

        if (lastSavedGraph is null)
            return (deletedFiles, deletedDirs.ToList());

        foreach (var (relPath, isFile) in EnumerateGraphEntries(lastSavedGraph))
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(relPath) || relPath.Equals("./", StringComparison.Ordinal))
                continue;

            if (isFile)
            {
                if (!currentFiles.ContainsKey(relPath))
                    deletedFiles.Add(relPath);
            }
            else
            {
                if (!currentDirs.Contains(relPath))
                    deletedDirs.Add(relPath);
            }
        }

        return (deletedFiles, deletedDirs.ToList());
    }

    private static List<string> CollapseDeletedDirectories(IEnumerable<string> deletedDirsRel) 
    { 
        var ordered = deletedDirsRel
            .Select(d => d.Replace('\\', '/'))
            .Where(d => d.Length > 0 && !d.Equals("./", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d.Length)
            .ToList();

        List<string> kept = []; 
        
        foreach (var d in ordered) 
        { 
            var dPrefix = d.EndsWith('/') ? d : d + "/"; 
            
            if ( kept.Any( k => 
                 { 
                    var kPrefix = k.EndsWith('/') ? k : k + "/"; 
                    return dPrefix.StartsWith(kPrefix, StringComparison.OrdinalIgnoreCase); 
                 })
               )
            { 
                continue; 
            } 
            kept.Add(d); 
        } 
        return kept; 
    }

    private static bool IsUnderAnyDeletedDirectory(string fileRel, IReadOnlySet<string> deletedDirsRel)
    {
        var p = (fileRel ?? string.Empty).Replace('\\', '/');

        var dir = Path.GetDirectoryName(p)?.Replace('\\', '/') ?? string.Empty;

        while (!string.IsNullOrEmpty(dir))
        {
            if (deletedDirsRel.Contains(dir) || deletedDirsRel.Contains(dir.TrimEnd('/') + "/"))
                return true;

            var idx = dir.LastIndexOf('/');
            if (idx < 0) break;

            dir = dir[..idx];
        }

        return false;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> FreezeChanged(Dictionary<string, List<string>> changed)
    {
        return changed.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)[.. kvp.Value.Distinct(StringComparer.OrdinalIgnoreCase)],
            StringComparer.OrdinalIgnoreCase
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
            if (fileName.EndsWith(suf, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    private static IEnumerable<(string RelPath, bool IsFile)> EnumerateGraphEntries(DependencyGraph root)
    {
        Stack<DependencyGraph> stack = [];
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            var children = node.GetChildren() ?? [];

            foreach (var child in children)
            {
                if (child is null) continue;

                var rel = child.Path?.Replace('\\', '/') ?? "";
                var isFile = child is DependencyGraphLeaf;
                yield return (rel, isFile);

                if (!isFile)
                    stack.Push(child);
            }
        }
    }

    public static bool MatchesSuffixPattern(string value, string pattern)
    {
        if (!pattern.Contains('*'))
            return string.Equals(value, pattern, StringComparison.Ordinal);

        var suffix = pattern.TrimStart('*');
        return value.EndsWith(suffix, StringComparison.Ordinal);
    }

    private static DateTime TrimMilliseconds(DateTime dt)
        => new(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, 0, dt.Kind);

    private static string GetRelative(string root, string path)
    {
        var rel = Path.GetRelativePath(root, path);
        return rel.Replace('\\', '/');
    }

}
