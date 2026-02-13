using Archlens.Domain.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Archlens.Domain.Models;

public class DependencyGraph(string _projectRoot) : IEnumerable<DependencyGraph>
{
    protected internal string ProjectRoot => _projectRoot;
    private readonly DateTime _lastWriteTime;
    private readonly string _path;
    private Dictionary<string, int> _dependencies { get; init; } = [];

    protected static StringComparer PathComparer => StringComparer.OrdinalIgnoreCase;

    protected virtual string NormaliseOwnPath(string value) =>
        PathNormaliser.NormaliseModule(_projectRoot, value);

    required public DateTime LastWriteTime
    {
        get => _lastWriteTime;
        init { _lastWriteTime = DateTimeNormaliser.NormaliseUTC(value); }
    }

    required public string Name { get; init; }
    required public string Path
    {
        get => _path;
        init { _path = NormaliseOwnPath(value); }
    }

    public IDictionary<string, int> GetDependencies() => _dependencies;

    public void AddDependency(string depPath)
    {
        if (_dependencies.TryGetValue(depPath, out int value))
            _dependencies[depPath] = ++value;
        else
            _dependencies[depPath] = 1;
    }

    public void AddDependencyRange(IReadOnlyList<string> depPaths)
    {
        foreach (var depPath in depPaths)
        {
            AddDependency(depPath);
        }
    }

    public virtual DependencyGraph? GetChild(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var (asModule, asFile) = GetNormalisedModuleAndFilePaths(path);

        return GetChildren().FirstOrDefault(child =>
            PathComparer.Equals(child.Path, asModule) || PathComparer.Equals(child.Path, asFile));
    }

    public virtual IReadOnlyList<DependencyGraph> GetChildren() => [];
    public override string ToString() => $"{Name} ({Path})";

    public bool ContainsPath(string path) => FindByPath(path) is not null;

    public DependencyGraph? FindByPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var (asModule, asFile) = GetNormalisedModuleAndFilePaths(path);

        var stack = new Stack<DependencyGraph>();
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

    public static bool RemovePath(DependencyGraph graph, string path)
    {
        if (graph is not DependencyGraphNode root || string.IsNullOrWhiteSpace(path))
            return false;

        var asModule = PathNormaliser.NormaliseModule(root.ProjectRoot, path);
        var asFile = PathNormaliser.NormaliseFile(root.ProjectRoot, path);

        var removed = root.RemoveMatching(
            child => PathComparer.Equals(child.Path, asModule) || PathComparer.Equals(child.Path, asFile)
        );

        root.PruneEmptyDirectories(isRoot: true);
        return removed;
    }

    public static bool RemoveSubtree(DependencyGraph graph, string path)
    {
        if (graph is not DependencyGraphNode root || string.IsNullOrWhiteSpace(path))
            return false;

        var asModule = PathNormaliser.NormaliseModule(root.ProjectRoot, path);
        var prefix = EnsureTrailingSlash(asModule);

        var removedCount = root.RemoveMatching(child => IsSubtree(child, prefix));

        root.PruneEmptyDirectories(isRoot: true);
        return removedCount;
    }

    private static string EnsureTrailingSlash(string p)
    {
        p = (p ?? string.Empty).Replace('\\', '/');
        return p.EndsWith("/", StringComparison.Ordinal) ? p : p + "/";
    }

    private static bool IsSubtree(DependencyGraph node, string dirPrefix)
    {
        var p = (node.Path ?? string.Empty).Replace('\\', '/');

        if (node is DependencyGraphNode)
            p = EnsureTrailingSlash(p);

        return p.StartsWith(dirPrefix, StringComparison.OrdinalIgnoreCase)
               || string.Equals(p.TrimEnd('/'), dirPrefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    private (string asModule, string asFile) GetNormalisedModuleAndFilePaths(string path)
    {
        var asModule = PathNormaliser.NormaliseModule(_projectRoot, path);
        var asFile = PathNormaliser.NormaliseFile(_projectRoot, path);
        return (asModule, asFile);
    }

    public IEnumerator<DependencyGraph> GetEnumerator() =>
        GetChildren().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

}

public class DependencyGraphNode(string projectRoot) : DependencyGraph(projectRoot)
{
    private List<DependencyGraph> _children { get; init; } = [];

    protected override string NormaliseOwnPath(string value) =>
        PathNormaliser.NormaliseModule(projectRoot, value);

    public override IReadOnlyList<DependencyGraph> GetChildren() => _children;
    public void AddChildren(IEnumerable<DependencyGraph> childr)
    {
        foreach (var child in childr)
        {
            AddChild(child);
        }
    }

    public void AddChild(DependencyGraph child)
    {
        if (_children.Any(c => ReferenceEquals(c, child) || PathComparer.Equals(c.Path, child.Path)))
            return;

        _children.Add(child);
    }

    public void ReplaceChild(DependencyGraph replacement)
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

    internal bool RemoveMatching(Func<DependencyGraph, bool> predicate)
    {
        var removedAny = false;

        for (int i = _children.Count - 1; i >= 0; i--)
        {
            if (predicate(_children[i]))
            {
                _children.RemoveAt(i);
                removedAny = true;
            }
        }

        for (int i = 0; i < _children.Count; i++)
        {
            if (_children[i] is DependencyGraphNode n)
                removedAny |= n.RemoveMatching(predicate);
        }

        return removedAny;
    }

    internal void PruneEmptyDirectories(bool isRoot)
    {
        for (int i = _children.Count - 1; i >= 0; i--)
        {
            if (_children[i] is not DependencyGraphNode n)
                continue;

            n.PruneEmptyDirectories(isRoot: false);

            if (!isRoot && n._children.Count == 0)
                _children.RemoveAt(i);
        }
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

public class DependencyGraphLeaf(string projectRoot) : DependencyGraph(projectRoot)
{
    protected override string NormaliseOwnPath(string value) =>
        PathNormaliser.NormaliseFile(projectRoot, value);

    public override string ToString() => $"{Name} ({Path}) deps={GetDependencies().Count}";
}
