using Archlens.Domain.Models;
using Archlens.Domain.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Archlens.Domain;

public static class DependencyAggregator
{
    public static void RecomputeAggregates(ProjectDependencyGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        foreach (var rootDir in graph.ProjectItems.Values
                     .Where(i => i.Type == ProjectItemType.Directory)
                     .Where(i => graph.ParentOf(i.Path) is null))
        {
            Fold(graph, rootDir.Path);
        }
    }

    private static Dictionary<RelativePath, Dependency> Fold(
        ProjectDependencyGraph graph,
        RelativePath itemPath)
    {
        var item = graph.GetProjectItem(itemPath)
            ?? throw new InvalidOperationException($"Project item '{itemPath}' does not exist.");

        if (item.Type == ProjectItemType.File)
            return new Dictionary<RelativePath, Dependency>(graph.DependenciesFrom(itemPath));

        var aggregate = new Dictionary<RelativePath, Dependency>();

        foreach (var childPath in graph.ChildrenOf(itemPath))
        {
            var childAggregate = Fold(graph, childPath);

            foreach (var (depPath, dep) in childAggregate)
            {
                if (IsInternalToDirectory(itemPath, depPath))
                    continue;

                if (aggregate.TryGetValue(depPath, out var existing))
                {
                    aggregate[depPath] = existing with
                    {
                        Count = existing.Count + dep.Count
                    };
                }
                else
                    aggregate[depPath] = dep;
            }
        }

        graph.ReplaceDependencies(itemPath, aggregate);
        return aggregate;
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