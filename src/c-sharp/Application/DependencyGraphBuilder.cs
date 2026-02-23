using Archlens.Domain;
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
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    
    public async Task<DependencyGraph> GetGraphAsync(
        ProjectChanges changes,
        DependencyGraph? lastSavedDependencyGraph,
public sealed class DependencyGraphBuilder(IDependencyParser _dependencyParser, BaseOptions _options)
{    
    public async Task<ProjectDependencyGraph> GetGraphAsync(
        IReadOnlyDictionary<string, IEnumerable<string>> changedModules,
        ProjectDependencyGraph lastSavedDependencyGraph,
        CancellationToken ct = default)
    {
        var changedRoot = await BuildGraphAsync(changes.ChangedFilesByDirectory, ct);

        var merged = lastSavedDependencyGraph is null
            ? changedRoot
            : lastSavedDependencyGraph.Merge(changedRoot);

        ApplyDeletions(merged, changes, _options.FullRootPath);

        DependencyAggregator.RecomputeAggregates(merged);
        return merged;
    }

    private async Task<DependencyGraphNode> BuildGraphAsync(
        IReadOnlyDictionary<string, IReadOnlyList<string>> changedModules,
        CancellationToken ct)
    {
        var rootFull = _options.FullRootPath;
        var graph = new ProjectDependencyGraph(rootFull);

        var root = RelativePath.Directory(rootFull, _options.ProjectRoot);
        graph.AddProjectItem(root, ProjectItemType.Directory);

        
        foreach (var moduleRelPath in changedModules.Keys)
        {
            var module = RelativePath.Directory(rootFull, moduleRelPath);
            graph.AddProjectItem(module, ProjectItemType.Directory);
        }

        foreach (var (parentRelPath, itemRelPaths) in changedModules) // changedModules is a Dictionary where keys are module paths and values are list of file AND module paths (contents) that have changed within those modules
        {
            ct.ThrowIfCancellationRequested();
            // The keys (module paths) have already been processed in the previous loop, so we only care about the values (contents paths).
            foreach (var itemRelPath in itemRelPaths) // contents contain both file paths and module (directory) paths -> hence we call it item
            {
                var abs = PathNormaliser.GetAbsolutePath(rootFull, itemRelPath);
                if (Path.GetExtension(abs).Length == 0)
                {
                    if (!dirIds.Any(id => string.Equals(projectDependencyGraph.GetProjectItemById(id).RelativePath, itemRelPath)))
                    {
                        var id = projectDependencyGraph.AddProjectItem(
                             relPath: itemRelPath,
                             type: ProjectItemType.Directory
                        );
                        dirIds = dirIds.Append(id);
                    }
                    continue;
                }

                ct.ThrowIfCancellationRequested();

                var parentAbs = Path.GetDirectoryName(abs);
                if (parentAbs is null)
                    continue;

                var parentNode = EnsureDirectoryNode(parentAbs);

                List<string> deps = [];
                foreach (var parser in _dependencyParsers)
                    deps = [.. await parser.ParseFileDependencies(abs, ct).ConfigureAwait(false)];

                var leaf = new DependencyGraphLeaf(rootFull)
                {
                    Name = Path.GetFileName(abs),
                    Path = item,
                    LastWriteTime = File.GetLastWriteTimeUtc(abs)
                };
                leaf.AddDependencyRange(deps);

                var paresedDependencies = await _dependencyParser.ParseFileDependencies(abs, ct).ConfigureAwait(false);
                projectDependencyGraph.AddDependencyRange(fileId, dependenciesPaths: paresedDependencies);

        return rootNode;
    }

    private static void ApplyDeletions(DependencyGraph graph, ProjectChanges changes, string projectRoot)
    {
        foreach (var absFile in changes.DeletedFiles)
        {
            var rel = PathNormaliser.NormaliseModule(projectRoot, absFile);
            DependencyGraph.RemovePath(graph, rel);
        }

        foreach (var absDir in changes.DeletedDirectories)
        {
            var rel = PathNormaliser.NormaliseModule(projectRoot, absDir);
            DependencyGraph.RemoveSubtree(graph, rel);
        }
    }


    private static DependencyGraph MergeGraphs(DependencyGraph lastSaved, DependencyGraphNode changedRoot)
    {
        if (lastSaved is not DependencyGraphNode lastSavedRoot)
            throw new ArgumentException("Expected the saved graph root to be a node.", nameof(lastSaved));

        foreach (var changedChild in changedRoot.GetChildren())
            UpsertChild(lastSavedRoot, changedChild);

        return lastSavedRoot;
    }

        return projectDependencyGraph;
    }

    private static ProjectDependencyGraph MergeGraphs(ProjectDependencyGraph lastSaved, ProjectDependencyGraph updated)
    {
        lastSaved.MergeWith(updated);

        return lastSaved;
    }
}

