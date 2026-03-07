using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using System;
using System.Collections.Generic;

namespace Archlens.Domain;

public static class DependencyAggregator
{
    public static IReadOnlyDictionary<RelativePath, Dependency> GetAggregatedDependencies(
        ProjectDependencyGraph graph,
        RelativePath source)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var memo = new Dictionary<RelativePath, IReadOnlyDictionary<RelativePath, Dependency>>();
        return Fold(graph, source, memo);
    }

    private static IReadOnlyDictionary<RelativePath, Dependency> Fold(
        ProjectDependencyGraph graph,
        RelativePath source,
        Dictionary<RelativePath, IReadOnlyDictionary<RelativePath, Dependency>> memo)
    {
        if (memo.TryGetValue(source, out var cached))
            return cached;

        var item = graph.GetProjectItem(source)
            ?? throw new InvalidOperationException($"Project item '{source}' does not exist.");

        if (item.Type == ProjectItemType.File)
        {
            var direct = graph.DependenciesFrom(source);
            memo[source] = direct;
            return direct;
        }

        var aggregate = new Dictionary<RelativePath, Dependency>();

        foreach (var child in graph.ChildrenOf(source))
        {
            foreach (var (target, dep) in Fold(graph, child, memo))
            {
                if (IsInternalToDirectory(source, target))
                    continue;

                if (aggregate.TryGetValue(target, out var existing))
                    aggregate[target] = existing with { Count = existing.Count + dep.Count };
                else
                    aggregate[target] = dep;
            }
        }

        memo[source] = aggregate;
        return aggregate;
    }

    private static bool IsInternalToDirectory(RelativePath directoryPath, RelativePath dependencyTarget)
    {
        var dir = EnsureTrailingSlash(directoryPath.Value);
        return dependencyTarget.Value.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSlash(string path) =>
        path.EndsWith('/') || path.EndsWith('\\') ? path : path + '/';
}