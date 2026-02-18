using Archlens.Domain.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Archlens.Domain.Models;

public readonly record struct NodeId(Guid Id);

public enum ProjectItemType { Directory, File }

public sealed record ProjectItem(
    NodeId Id,
    string Name,
    string RelativePath,
    DateTime LastWriteTime,
    ProjectItemType Kind
);

public enum DependencyType { Uses }

public sealed record Dependency(
    int Count,
    DependencyType Type
);

public sealed class ProjectDependencyGraph(
    Dictionary<NodeId, ProjectItem> nodes,
    Dictionary<NodeId, List<NodeId>> contains,
    Dictionary<NodeId, Dictionary<NodeId, Dependency>> dependsOn)
{
    private readonly Dictionary<NodeId, ProjectItem> _nodes = nodes;
    private readonly Dictionary<NodeId, List<NodeId>> _contains = contains; // directory -> children
    private readonly Dictionary<NodeId, Dictionary<NodeId, Dependency>> _dependsOn = dependsOn; // from -> (to, count, type)

    public Dependency AddDependency(NodeId dependendId, NodeId dependeeId)
    {
        if (_dependsOn.TryGetValue(dependendId, out Dictionary<NodeId, Dependency> dependencies)) 
        {
            if (dependencies.TryGetValue(dependeeId, out Dependency dependency))
            {
                var updated = dependency with { Count = dependency.Count + 1 };
                _dependsOn[dependendId][dependeeId] = updated;

            }
        }
        else
            _dependsOn[dependendId][dependeeId] = new Dependency(Count: 1, Type: DependencyType.Uses ); // TODO: support other types

        return _dependsOn[dependendId][dependeeId];
    }

    public IEnumerable<Dependency> AddDependencyRange(NodeId dependendId, IReadOnlyList<NodeId> dependenciesId)
    {
        foreach (var depId in dependenciesId)
        {
            yield return AddDependency(dependendId, depId);
        }
    }

    public ProjectItem? GetNode(NodeId nodeId)
    {
        if (_nodes.TryGetValue(nodeId, out ProjectItem node))
            return node;
        else
            return null;
    }

    public NodeId GetChildId(NodeId parent, NodeId childId)
    {
        if (_nodes.TryGetValue(parent, out ProjectItem node))
            return node;
        else
            return null;
    }

    public virtual IReadOnlyList<ProjectDependencyGraph> GetChildren() => [];
    public override string ToString() => $"{Name} ({Path})";

    public bool ContainsPath(string path) => FindByPath(path) is not null;

    public ProjectDependencyGraph? FindByPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var (asModule, asFile) = GetNormalisedModuleAndFilePaths(path);

        var stack = new Stack<ProjectDependencyGraph>();
        stack.Push(this);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            if (PathComparer.Equals(current.Path, asModule) || PathComparer.Equals(current.Path, asFile))
                return current;

            var children = current.GetChildren();
            for (int i = children.Count - 1; i >= 0; i--)
                stack.Push(children[i]);
        }

        return null;
    }

    private (string asModule, string asFile) GetNormalisedModuleAndFilePaths(string path)
    {
        var asModule = PathNormaliser.NormaliseModule(_projectRoot, path);
        var asFile = PathNormaliser.NormaliseFile(_projectRoot, path);
        return (asModule, asFile);
    }

    public IEnumerator<ProjectDependencyGraph> GetEnumerator() =>
        GetChildren().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

}

public class DependencyGraphNode(string projectRoot) : ProjectDependencyGraph(projectRoot)
{
    private List<ProjectDependencyGraph> _children { get; init; } = [];

    protected override string NormaliseOwnPath(string value) =>
        PathNormaliser.NormaliseModule(projectRoot, value);

    public override IReadOnlyList<ProjectDependencyGraph> GetChildren() => _children;
    public void AddChildren(IEnumerable<ProjectDependencyGraph> childr)
    {
        foreach (var child in childr)
        {
            AddChild(child);
        }
    }

    public void AddChild(ProjectDependencyGraph child)
    {
        if (_children.Any(c => ReferenceEquals(c, child) || PathComparer.Equals(c.Path, child.Path)))
            return;

        _children.Add(child);
    }

    public void ReplaceChild(ProjectDependencyGraph replacement)
    {
        for (var i = 0; i < _children.Count; i++)
        {
            if (PathComparer.Equals(_children[i].Path, replacement.Path))
            {
                _children[i] = replacement;
                return;
            }
        }

        _children.Add(replacement);
    }

    internal void ReplaceDependencies(IDictionary<string, int> newDeps)
    {
        var dict = GetDependencies();
        dict.Clear();
        foreach (var kv in newDeps)
            dict[kv.Key] = kv.Value;
    }

    public override string ToString() => $"{Name} ({Path})";
}

public class DependencyGraphLeaf(string projectRoot) : ProjectDependencyGraph(projectRoot)
{
    protected override string NormaliseOwnPath(string value) =>
        PathNormaliser.NormaliseFile(projectRoot, value);

    public override string ToString() => $"{Name} ({Path}) deps={GetDependencies().Count}";
}
