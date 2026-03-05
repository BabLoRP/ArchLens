using Archlens.Domain;
using Archlens.Domain.Interfaces;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using Archlens.Domain.Utils;
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
                ct.ThrowIfCancellationRequested();

            }
        }

        foreach (var (parentRelPath, itemRelPaths) in changedModules) 
        {
            ct.ThrowIfCancellationRequested();
            foreach (var itemRelPath in itemRelPaths) 
            {
                ct.ThrowIfCancellationRequested();

                var itemAbsPath = PathNormaliser.GetAbsolutePath(rootFull, itemRelPath.Value);

                static bool IsDirectory(string path) => Path.GetExtension(path).Length == 0;

                var isItemDirectory = IsDirectory(itemAbsPath);

                if (isItemDirectory)
                {
                    graph.UpsertProjectItem(itemRelPath, ProjectItemType.Directory);
                        graph.AddChild(parent, item);
                    continue;
                }
                
                List<RelativePath> dependencyPaths = [];
                foreach (var parser in _dependencyParsers)
                {
                    var paths = await parser.ParseFileDependencies(itemAbsPath, ct).ConfigureAwait(false);
                    var dependencies = paths.Select(p => IsDirectory(p) ? RelativePath.Directory(rootFull, p) : RelativePath.File(rootFull, p));
                    dependencyPaths = [.. dependencies];
                }
                graph.AddDependencies(itemRelPath, dependencyPaths);
                graph.UpsertProjectItem(itemRelPath, ProjectItemType.File);
                graph.AddChild(parentRelPath, itemRelPath);
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

