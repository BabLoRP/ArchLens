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

        var memo = new Dictionary<RelativePath, Dictionary<RelativePath, Dependency>>();
        return Fold(graph, source, memo);
    }

    private static Dictionary<RelativePath, Dependency> Fold(
        ProjectDependencyGraph graph,
        RelativePath source,
        Dictionary<RelativePath, Dictionary<RelativePath, Dependency>> memo)
    {
        if (memo.TryGetValue(source, out var cached))
            return new Dictionary<RelativePath, Dependency>(cached);

        var item = graph.GetProjectItem(source)
            ?? throw new InvalidOperationException($"Project item '{source}' does not exist.");

        if (item.Type == ProjectItemType.File)
        {
            var direct = new Dictionary<RelativePath, Dependency>(graph.DependenciesFrom(source));
            memo[source] = direct;
            return new Dictionary<RelativePath, Dependency>(direct);
        }

        var aggregate = new Dictionary<RelativePath, Dependency>();

        foreach (var child in graph.ChildrenOf(source))
        {
            var childAgg = Fold(graph, child, memo);

            foreach (var (target, dep) in childAgg)
            {
                if (IsInternalToDirectory(source, target))
                    continue;

                if (aggregate.TryGetValue(target, out var existing))
                {
                    aggregate[target] = existing with
                    {
                        Count = existing.Count + dep.Count
                    };
                }
                else
                {
                    aggregate[target] = dep;
                }
            }
        }

        memo[source] = aggregate;
        return new Dictionary<RelativePath, Dependency>(aggregate);
    }

    private static bool IsInternalToDirectory(RelativePath directoryPath, RelativePath dependencyTarget)
    {
        var dir = EnsureTrailingSlash(directoryPath.Value);
        var target = dependencyTarget.Value;

        return target.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSlash(string path) =>
        path.EndsWith('/') || path.EndsWith('\\') ? path : path + '/';
}