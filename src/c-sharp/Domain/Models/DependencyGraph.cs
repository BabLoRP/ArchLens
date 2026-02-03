using Archlens.Domain.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Archlens.Domain.Models;

public class DependencyGraph(string _projectRoot) : IEnumerable<DependencyGraph>
{
    private readonly DateTime _lastWriteTime;
    private readonly string _path;
    private Dictionary<string, int> _dependencies { get; init; } = [];

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

    public virtual DependencyGraph GetChild(string path) => GetChildren().Where(child => child.Path == path).FirstOrDefault();

    public virtual IReadOnlyList<DependencyGraph> GetChildren() => [];
    public override string ToString() => Name;

    public bool ContainsPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var target = PathNormaliser.NormalisePath(_projectRoot, path);

        var stack = new Stack<DependencyGraph>();
        stack.Push(this);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            if (current.Path == target)
                return true;

            var children = current.GetChildren();
            for (int i = children.Count - 1; i >= 0; i--)
                stack.Push(children[i]);
        }

        return false;
    }

    public DependencyGraph? FindByPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var target = PathNormaliser.NormalisePath(_projectRoot, path);

        var stack = new Stack<DependencyGraph>();
        stack.Push(this);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.Path == target)
                return current;

            var children = current.GetChildren();
            for (int i = children.Count - 1; i >= 0; i--)
                stack.Push(children[i]);
        }

        return null;
    }

    private (string asModule, string asFile) NormaliseQueryPathBothWays(string path)
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
        if (_children.Any(c => ReferenceEquals(c, child) || c.Path == child.Path))
            return;
        _children.Add(child);
    }

    public void ReplaceChild(DependencyGraph replacement)
    {
        for (var i = 0; i < _children.Count; i++)
        {
            if (string.Equals(_children[i].Path, replacement.Path, StringComparison.OrdinalIgnoreCase))
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

    public override string ToString()
    {
        string res = Name + $" ({GetDependencies()})";
        foreach (var c in _children)
            res += "\n \t" + c;
        return res;
    }
}

public class DependencyGraphLeaf(string projectRoot) : DependencyGraph(projectRoot)
{
    protected override string NormaliseOwnPath(string value) =>
        PathNormaliser.NormaliseFile(projectRoot, value);

        foreach (var d in GetDependencies().Keys)
            res += "\n \t \t --> " + d;
        return res;
    }
}
