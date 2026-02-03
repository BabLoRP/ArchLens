using Archlens.Domain;
using Archlens.Domain.Interfaces;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
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
        var root = await BuildGraphAsync(changedModules, ct);
        DependencyAggregator.RecomputeAggregates(root);
        return root;
    }

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
        var comparer = StringComparer.OrdinalIgnoreCase;
        var nodes = new Dictionary<string, DependencyGraphNode>(comparer);

        var rootNode = new DependencyGraphNode(_options.FullRootPath)
        {
            Name = _options.ProjectName,
            Path = _options.ProjectRoot,
            LastWriteTime = File.GetLastWriteTimeUtc(_options.FullRootPath)
        };
        nodes["."] = rootNode;

        foreach (var moduleAbs in changedModules.Keys)
        {
            var key = CanonRel(_options.FullRootPath, moduleAbs);
            var name = key == "." ? _options.ProjectName : Path.GetFileName(key);
            nodes[key] = new DependencyGraphNode(_options.FullRootPath)
            {
                Name = name,
                Path = moduleAbs,
                LastWriteTime = File.GetLastWriteTimeUtc(moduleAbs)
            };
        }

        foreach (var key in nodes.Keys.OrderBy(k => k.Count(c => c == Path.DirectorySeparatorChar)))
        {
            if (key == ".") continue;
            var parentKey = Path.GetDirectoryName(key);
            if (string.IsNullOrEmpty(parentKey)) parentKey = ".";
            nodes[parentKey].AddChild(nodes[key]);
        }

        foreach (var (moduleAbs, contents) in changedModules)
        {
            var moduleKey = CanonRel(_options.FullRootPath, moduleAbs);
            var moduleNode = nodes[moduleKey];

            foreach (var absPath in contents)
            {
                var childKey = JoinRel(moduleKey, absPath);

                if (nodes.TryGetValue(childKey, out var childDir))
                {
                    moduleNode.AddChild(childDir);
                    continue;
                }

                if (!Path.HasExtension(absPath)) continue;

                var deps = await _dependencyParser.ParseFileDependencies(absPath, ct).ConfigureAwait(false);
                var leaf = new DependencyGraphLeaf(_options.FullRootPath)
                {
                    Name = Path.GetFileName(childKey),
                    Path = $"{absPath}",
                    LastWriteTime = File.GetLastWriteTimeUtc(absPath)
                };
                leaf.AddDependencyRange(deps);
                moduleNode.AddChild(leaf);
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

