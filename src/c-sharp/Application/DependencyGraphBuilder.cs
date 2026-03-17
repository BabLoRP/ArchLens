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

        var fileItems = CollectFileItems(changedModules, root, graph, ct);
        var parsedDeps = await ParseAllAsync(fileItems, ct);

        foreach (var (parent, item, deps) in parsedDeps)
        {
            graph.UpsertProjectItem(item, ProjectItemType.File);
            graph.AddChild(parent, item);
            graph.SetDependencies(item, deps);
        }

        return graph;
    }

    private List<(RelativePath Parent, RelativePath Item, string AbsPath)> CollectFileItems(
        IReadOnlyDictionary<RelativePath, IReadOnlyList<RelativePath>> changedModules,
        RelativePath root,
        ProjectDependencyGraph graph,
        CancellationToken ct)
    {
        var fileItems = new List<(RelativePath Parent, RelativePath Item, string AbsPath)>();

        foreach (var (parent, items) in changedModules)
        {
            ct.ThrowIfCancellationRequested();
            graph.UpsertProjectItem(parent, ProjectItemType.Directory);
            foreach (var item in items)
                TryClassifyItem(item, parent, root, graph, fileItems, ct);
        }

        return fileItems;
    }

    private async Task TryClassifyItem(
        RelativePath item,
        RelativePath parent,
        RelativePath root,
        ProjectDependencyGraph graph,
        List<(RelativePath Parent, RelativePath Item, string AbsPath)> fileItems,
        CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(item.Value) || item.Value.Trim() == root.Value)
                return;

            var absPath = PathNormaliser.GetAbsolutePath(_options.FullRootPath, item.Value);

            if (Path.GetExtension(absPath).Length == 0)
            {
                graph.UpsertProjectItem(item, ProjectItemType.Directory);
                graph.AddChild(parent, item);
            }
            else
            {
                fileItems.Add((parent, item, absPath));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { await Console.Error.WriteLineAsync($"Error while processing '{item.Value}': {ex}"); }
    }

    private async Task<IEnumerable<(RelativePath Parent, RelativePath Item, IReadOnlyList<RelativePath> Deps)>> ParseAllAsync(
        List<(RelativePath Parent, RelativePath Item, string AbsPath)> fileItems,
        CancellationToken ct)
    {
        var results = new (RelativePath Parent, RelativePath Item, IReadOnlyList<RelativePath> Deps)?[fileItems.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, fileItems.Count),
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (i, innerCt) => results[i] = await ParseFileItemAsync(fileItems[i], innerCt).ConfigureAwait(false));

        return results.Where(r => r.HasValue).Select(r => r!.Value);
    }

    private async Task<(RelativePath Parent, RelativePath Item, IReadOnlyList<RelativePath> Deps)?> ParseFileItemAsync(
        (RelativePath Parent, RelativePath Item, string AbsPath) fileItem,
        CancellationToken ct)
    {
        var (parent, item, absPath) = fileItem;
        try
        {
            var deps = new List<RelativePath>();
            foreach (var parser in _dependencyParsers)
                deps.AddRange(await parser.ParseFileDependencies(absPath, ct).ConfigureAwait(false));
            return (parent, item, deps);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Error while processing '{item.Value}': {ex}");
            return null;
        }
    }

    private static void ApplyDeletions(ProjectDependencyGraph graph, ProjectChanges changes)
    {
        foreach (var file in changes.DeletedFiles)
            graph.RemoveProjectItem(file);

        foreach (var dir in changes.DeletedDirectories)
            graph.RemoveProjectItemRecursive(dir);
    }
}

