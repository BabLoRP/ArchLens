using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Archlens.Domain.Interfaces;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using Archlens.Domain.Utils;

namespace Archlens.Application;

public sealed class DependencyGraphBuilder(IReadOnlyList<IDependencyParser> _dependencyParsers, BaseOptions _options)
{
    public async Task<ProjectDependencyGraph> GetGraphAsync(
        ProjectChanges changes,
        ProjectDependencyGraph? lastSavedDependencyGraph,
        CancellationToken ct = default)
    {
        var graph = await BuildGraphAsync(changes.ChangedFilesByDirectory, ct);

        var merged = lastSavedDependencyGraph is null
            ? graph
            : lastSavedDependencyGraph.MergeOverwrite(graph);

        ApplyDeletions(merged, changes);

        return merged;
    }

    private async Task<ProjectDependencyGraph> BuildGraphAsync(
        IReadOnlyDictionary<RelativePath, IReadOnlyList<RelativePath>> changedModules,
        CancellationToken ct)
    {
        var rootFull = _options.FullRootPath;
        var root = RelativePath.Directory(rootFull, rootFull);
        var graph = new ProjectDependencyGraph(rootFull);

        var fileItems = new List<(RelativePath Parent, RelativePath Item, string AbsPath)>();

        foreach (var (parent, items) in changedModules)
        {
            ct.ThrowIfCancellationRequested();

            graph.UpsertProjectItem(parent, ProjectItemType.Directory);

            foreach (var item in items)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(item.Value) || item.Value.Trim() == root.Value)
                        continue;

                    var itemAbsPath = PathNormaliser.GetAbsolutePath(rootFull, item.Value);

                    if (Path.GetExtension(itemAbsPath).Length == 0)
                    {
                        graph.UpsertProjectItem(item, ProjectItemType.Directory);
                        graph.AddChild(parent, item);
                    }
                    else
                    {
                        fileItems.Add((parent, item, itemAbsPath));
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error while processing '{item.Value}': {ex}");
                }
            }
        }

        var parsedDeps = new (RelativePath Parent, RelativePath Item, IReadOnlyList<RelativePath> Deps)?[fileItems.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, fileItems.Count),
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (i, innerCt) =>
            {
                var (parent, item, absPath) = fileItems[i];
                try
                {
                    var deps = new List<RelativePath>();
                    foreach (var parser in _dependencyParsers)
                    {
                        var d = await parser.ParseFileDependencies(absPath, innerCt).ConfigureAwait(false);
                        deps.AddRange(d);
                    }
                    parsedDeps[i] = (parent, item, deps);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error while processing '{item.Value}': {ex}");
                }
            });

        foreach (var entry in parsedDeps)
        {
            if (entry is not { } parsed)
                continue;

            graph.UpsertProjectItem(parsed.Item, ProjectItemType.File);
            graph.AddChild(parsed.Parent, parsed.Item);
            graph.SetDependencies(parsed.Item, parsed.Deps);
        }

        return graph;
    }

    private static void ApplyDeletions(ProjectDependencyGraph graph, ProjectChanges changes)
    {
        foreach (var file in changes.DeletedFiles)
            graph.RemoveProjectItem(file);

        foreach (var dir in changes.DeletedDirectories)
            graph.RemoveProjectItemRecursive(dir);
    }
}

