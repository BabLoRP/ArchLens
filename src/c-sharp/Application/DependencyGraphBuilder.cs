using Archlens.Domain.Interfaces;
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

public sealed class DependencyGraphBuilder(IReadOnlyList<IDependencyParser> _dependencyParsers, BaseOptions _options)
{
    public async Task<ProjectDependencyGraph> GetGraphAsync(
        ProjectChanges changes,
        ProjectDependencyGraph lastSavedDependencyGraph,
        CancellationToken ct = default)
    {
        var graph = await BuildGraphAsync(changes.ChangedFilesByDirectory, ct);

        var merged = lastSavedDependencyGraph is null
            ? graph
            : lastSavedDependencyGraph.MergeOverwrite(graph);

        ApplyDeletions(merged, changes, _options.FullRootPath);

        return merged;
    }

    private async Task<ProjectDependencyGraph> BuildGraphAsync(
        IReadOnlyDictionary<RelativePath, IReadOnlyList<RelativePath>> changedModules,
        CancellationToken ct)
    {
        var rootFull = _options.FullRootPath;
        var root = RelativePath.Directory(rootFull, rootFull);
        var graph = new ProjectDependencyGraph(rootFull);

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

                    static bool IsDirectory(string path) => Path.GetExtension(path).Length == 0;
                    var isItemDirectory = IsDirectory(itemAbsPath);

                    if (isItemDirectory)
                    {
                        graph.UpsertProjectItem(item, ProjectItemType.Directory);
                        graph.AddChild(parent, item);
                        continue;
                    }

                    List<RelativePath> dependencyPaths = [];
                    foreach (var parser in _dependencyParsers)
                    {
                        var dependencies = await parser.ParseFileDependencies(itemAbsPath, ct).ConfigureAwait(false);
                        dependencyPaths = [.. dependencies];
                    }

                    graph.UpsertProjectItem(item, ProjectItemType.File);
                    graph.AddChild(parent, item);
                    graph.AddDependencies(item, dependencyPaths);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error while processing '{item.Value}': {ex}");
                    continue;
                }
            }
        }
        return graph;
    }

    private static void ApplyDeletions(ProjectDependencyGraph graph, ProjectChanges changes, string absRoot)
    {
        var deletedFiles = changes.DeletedFiles;
        var deletedDirs = changes.DeletedDirectories;
        
        foreach (var deletedItem in deletedFiles.Concat(deletedDirs))
            graph.RemoveProjectItem(deletedItem);
    }
}

