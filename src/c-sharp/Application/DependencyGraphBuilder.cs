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

public sealed class DependencyGraphBuilder(IDependencyParser _dependencyParser, Options _options)
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    
    public async Task<DependencyGraph> GetGraphAsync(
        IReadOnlyDictionary<string, IEnumerable<string>> changedModules,
        DependencyGraph lastSavedDependencyGraph,
        CancellationToken ct = default)
    {
        var changedRoot = await BuildGraphAsync(changedModules, ct).ConfigureAwait(false);

        var merged = lastSavedDependencyGraph is null
            ? changedRoot
            : MergeGraphs(lastSavedDependencyGraph, changedRoot);

        DependencyAggregator.RecomputeAggregates(merged);
        return merged;
    }

    private async Task<DependencyGraphNode> BuildGraphAsync(
        IReadOnlyDictionary<string, IEnumerable<string>> changedModules,
        CancellationToken ct)
    {
        var rootFull = _options.FullRootPath;
        var nodes = new Dictionary<string, DependencyGraphNode>(PathComparer);

        var rootNode = new DependencyGraphNode(rootFull)
        {
            Name = _options.ProjectName,
            Path = _options.ProjectRoot,
            LastWriteTime = File.GetLastWriteTimeUtc(rootFull)
        };
        nodes[PathNormaliser.RelativeRoot] = rootNode;

        DependencyGraphNode EnsureDirectoryNode(string dirPath)
        {
            var abs = PathNormaliser.CombinePaths(rootFull, dirPath);
            var key = PathNormaliser.NormaliseModule(rootFull, abs);

            if (nodes.TryGetValue(key, out var existing))
                return existing;

            var node = new DependencyGraphNode(rootFull)
            {
                Name = key == PathNormaliser.RelativeRoot ? _options.ProjectName : PathNormaliser.GetFileOrModuleName(key),
                Path = dirPath,
                LastWriteTime = File.GetLastWriteTimeUtc(abs)
            };

            nodes[key] = node;

            var parentAbs = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(abs));
            var parent = parentAbs is null || PathComparer.Equals(parentAbs, rootFull)
                ? rootNode
                : EnsureDirectoryNode(parentAbs);

            parent.AddChild(node);
            return node;
        }

        foreach (var module in changedModules.Keys)
            EnsureDirectoryNode(module);

        foreach (var (_, contents) in changedModules)
        {
            foreach (var item in contents)
            {
                if (string.IsNullOrWhiteSpace(item))
                    continue;

                var abs = PathNormaliser.CombinePaths(rootFull, item);

                if (!(Path.GetExtension(abs).Length != 0))
                {
                    EnsureDirectoryNode(abs);
                    continue;
                }

                ct.ThrowIfCancellationRequested();

                var parentAbs = Path.GetDirectoryName(abs);
                if (parentAbs is null)
                    continue;

                var parentNode = EnsureDirectoryNode(parentAbs);

                var deps = await _dependencyParser.ParseFileDependencies(abs, ct).ConfigureAwait(false);

                var leaf = new DependencyGraphLeaf(rootFull)
                {
                    Name = Path.GetFileName(abs),
                    Path = item,
                    LastWriteTime = File.GetLastWriteTimeUtc(abs)
                };
                leaf.AddDependencyRange(deps);

                UpsertChild(parentNode, leaf);
            }
        }

        return rootNode;
    }

    private static DependencyGraph MergeGraphs(DependencyGraph lastSaved, DependencyGraphNode changedRoot)
    {
        if (lastSaved is not DependencyGraphNode lastSavedRoot)
            throw new ArgumentException("Expected the saved graph root to be a node.", nameof(lastSaved));

        foreach (var changedChild in changedRoot.GetChildren())
            UpsertChild(lastSavedRoot, changedChild);

        return lastSavedRoot;
    }

    private static void UpsertChild(DependencyGraphNode parent, DependencyGraph newChild)
    {
        var existing = parent.GetChildren()
            .FirstOrDefault(c => string.Equals(c.Path, newChild.Path, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            parent.AddChild(newChild);
            return;
        }

        if (existing is DependencyGraphNode existingNode && newChild is DependencyGraphNode incomingNode)
        {
            existingNode.ReplaceDependencies(incomingNode.GetDependencies());

            foreach (var grandChild in incomingNode.GetChildren())
                UpsertChild(existingNode, grandChild);

            return;
        }

        parent.ReplaceChild(newChild);
    }
}

