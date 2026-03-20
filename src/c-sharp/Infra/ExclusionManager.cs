using System;
using System.Collections.Generic;
using System.IO;

namespace Archlens.Infra;

public class ExclusionManager
{
    public sealed record ExclusionRule(
        string[] DirPrefixes,
        string[] Segments,
        string[] FileSuffixes
    );

    public static bool IsExcluded(string projectRoot, string content, ExclusionRule rules)
    {
        var path = GetRelative(projectRoot, content);
        var pathSeparater = '/';
        var pathWithSlash = path + pathSeparater;
        var pathWithBothSlashes = pathSeparater + path + pathSeparater;

        // Do not change to linq - this is called on every file in a project and linq would allocate too much space on large systems
        foreach (var rule in rules.DirPrefixes)
        {
            if (pathWithSlash.StartsWith(rule, StringComparison.OrdinalIgnoreCase)
                || pathWithBothSlashes.Contains(pathSeparater + rule, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var segments = path.Split(pathSeparater, StringSplitOptions.RemoveEmptyEntries);
        // Do not change to linq - this is called on every file in a project and linq would allocate too much space on large systems
        foreach (var segment in segments)
        {
            foreach (var ban in rules.Segments)
            {
                if (MatchesSuffixPattern(segment, ban))
                    return true;
            }
        }

        var fileName = Path.GetFileName(path);
        // Do not change to linq - this is called on every file in a project and linq would allocate too much space on large systems
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

    // Returns null for blank/empty entries; otherwise strips **/  prefix, normalises slashes, and trims trailing dot.
    public static string? NormaliseExclusionEntry(string? entry)
    {
        var exclusion = (entry ?? string.Empty).Trim();
        if (exclusion.Length == 0) return null;

        if (exclusion.StartsWith("**/", StringComparison.Ordinal)) exclusion = exclusion[3..];

        var norm = exclusion.Replace('\\', '/');
        return norm.EndsWith('.') ? norm[..^1] : norm;
    }

    public static ExclusionRule CompileExclusions(IReadOnlyList<string> exclusions)
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

    private static string GetRelative(string root, string path)
    {
        var rel = Path.GetRelativePath(root, path);
        return rel.Replace('\\', '/');
    }
}
