using Archlens.Domain;
using Archlens.Domain.Interfaces;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Archlens.Infra;

public sealed class LocalSnaphotManager(string _localDirName, string _localFileName) : ISnapshotManager
{
    public async Task SaveGraphAsync(DependencyGraph graph, Options options, CancellationToken ct = default)
    {
        var root = string.IsNullOrEmpty(options.FullRootPath)
            ? Path.GetFullPath(options.ProjectRoot)
            : options.FullRootPath;

        var dir = Path.Combine(root, _localDirName);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, _localFileName);

        var mergedGraph = graph;

        if (File.Exists(path))
        {
            try
            {
                var existingJson = await File.ReadAllTextAsync(path, ct);
                var existingGraph = DependencyGraphSerializer.Deserialize(existingJson, root);

                if (existingGraph is DependencyGraphNode existingRoot &&
                    graph is DependencyGraphNode newRoot)
                {
                    MergeGraphs(existingRoot, newRoot);
                    mergedGraph = existingRoot;
                }
            }
            catch
            {
                mergedGraph = graph;
            }
        }

        var json = DependencyGraphSerializer.Serialize(mergedGraph);
        await File.WriteAllTextAsync(path, json, ct);
    }

    public async Task<DependencyGraph> GetLastSavedDependencyGraphAsync(Options options, CancellationToken ct = default)
    {
        var root = string.IsNullOrEmpty(options.FullRootPath)
            ? Path.GetFullPath(options.ProjectRoot)
            : options.FullRootPath;

        var path = Path.Combine(root, _localDirName, _localFileName);

        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, ct);
        var graph = DependencyGraphSerializer.Deserialize(json, root);

        return graph ?? null;
    }

    private static void MergeGraphs(DependencyGraphNode existingRoot, DependencyGraphNode newRoot)
    {
        foreach (var newChild in newRoot.GetChildren())
            UpsertChild(existingRoot, newChild);
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

        if (existing is DependencyGraphNode existingNode && newChild is DependencyGraphNode newNode)
        {
            existingNode.ReplaceDependencies(newNode.GetDependencies());

            foreach (var grandChild in newNode.GetChildren())
                UpsertChild(existingNode, grandChild);

            return;
        }

        parent.ReplaceChild(newChild);
    }
}
