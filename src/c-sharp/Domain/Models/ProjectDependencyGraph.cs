using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Archlens.Domain.Utils;

namespace Archlens.Domain.Models;

public enum ProjectItemType { Directory, File }

public sealed record ProjectItem(
    RelativePath Path, // path is unique - works as Id
    string Name,
    DateTime LastWriteTime,
    ProjectItemType Type
);

public enum DependencyType { Uses }

public sealed record Dependency(
    RelativePath To, // the item being depended on
    int Count,
    DependencyType Type
);

public sealed class ProjectDependencyGraph(string projectRoot)
{
    private readonly string _projectRoot = projectRoot ?? throw new ArgumentNullException(nameof(projectRoot));

    private readonly Dictionary<RelativePath, ProjectItem> _projectItems = [];
    private readonly Dictionary<RelativePath, HashSet<RelativePath>> _contains = [];
    private readonly Dictionary<RelativePath, List<Dependency>> _dependsOn = [];
    private readonly Dictionary<RelativePath, RelativePath> _parentOf = [];

    public ProjectItem? GetItem(RelativePath id) => _projectItems.TryGetValue(id, out var item) ? item : null;

    public IReadOnlyDictionary<RelativePath, ProjectItem> Items => new ReadOnlyDictionary<RelativePath, ProjectItem>(_projectItems);
    public IReadOnlyDictionary<RelativePath, IReadOnlySet<RelativePath>> Contains => new ReadOnlyDictionary<RelativePath, IReadOnlySet<RelativePath>>((IDictionary<RelativePath, IReadOnlySet<RelativePath>>)_contains);
    public IReadOnlyDictionary<RelativePath, IReadOnlyList<Dependency>> Dependencies => (IReadOnlyDictionary<RelativePath, IReadOnlyList<Dependency>>)_dependsOn.ToDictionary(kv => kv.Key, kv => new ReadOnlyCollection<Dependency>(kv.Value));

    public IReadOnlyList<RelativePath> ChildrenOf(RelativePath directoryId) => _contains.TryGetValue(directoryId, out var children) ? [.. children] : [];

    public RelativePath? ParentOf(RelativePath childId) => _parentOf.TryGetValue(childId, out var parent) ? parent : null;

    public IReadOnlyList<Dependency> DependenciesFrom(RelativePath fromId) =>_dependsOn.TryGetValue(fromId, out var deps)
            ? new ReadOnlyCollection<Dependency>(deps)
            : [];

    public RelativePath AddProjectItem(RelativePath relPath, ProjectItemType type)
    {
        var id = NormalisePath(relPath, type);

        if (_projectItems.ContainsKey(id))
            return id;

        var absPath = PathNormaliser.GetAbsolutePath(_projectRoot, id.Value);
        var name = type == ProjectItemType.Directory
            ? Path.GetFileName(id.Value.TrimEnd('/', '\\', Path.PathSeparator))
            : Path.GetFileName(id.Value);

        var lastWriteTime = File.GetLastWriteTimeUtc(absPath);

        _projectItems[id] = new ProjectItem(
            Path: id,
            Name: name,
            LastWriteTime: lastWriteTime,
            Type: type
        );

        return id;
    }

    public IEnumerable<RelativePath> AddProjectItemRange(IEnumerable<ProjectItem> items)
    {
        foreach (var item in items)
        {
            var id = item.Path;

            if (_projectItems.ContainsKey(id))
            {
                yield return id;
                continue;
            }

            _projectItems[id] = item;
            yield return id;
        }
    }

    public ProjectItem UpdateProjectItem(RelativePath id, DateTime? lastWriteTime = null)
    {
        var item = GetItem(id);
        var updated = item with { LastWriteTime = lastWriteTime ?? item.LastWriteTime };
        _projectItems[id] = updated;
        return updated;
    }

    public void AddChild(RelativePath parentId, RelativePath childId)
    {
        if (!_projectItems.TryGetValue(parentId, out var parent))
            throw new InvalidOperationException($"Parent '{parentId}' does not exist in the graph.");

        if (parent.Type != ProjectItemType.Directory)
            throw new InvalidOperationException($"Parent '{parentId}' is not a directory.");

        if (!_projectItems.ContainsKey(childId))
            throw new InvalidOperationException($"Child '{childId}' does not exist in the graph.");

        if (!_contains.TryGetValue(parentId, out var children))
        {
            children = [];
            _contains[parentId] = children;
        }

        if (children.Add(childId))
            _parentOf[childId] = parentId;
    }

    public void AddChildrenRange(RelativePath parentId, IEnumerable<RelativePath> childIds)
    {
        foreach (var childId in childIds)
            AddChild(parentId, childId);
    }

    public void AddDependency(RelativePath dependentId, RelativePath dependeeId, DependencyType type = DependencyType.Uses)
    {
        if (!_dependsOn.TryGetValue(dependentId, out var deps))
        {
            deps = [];
            _dependsOn[dependentId] = deps;
        }

        var existing = deps.FirstOrDefault(dep => dep.To == dependeeId, null);

        if (existing is not null)
            existing = existing with { Count = existing.Count + 1 };
        else
            deps.Add(new Dependency(dependeeId, 1, type));
    }

    public void AddDependencyRange(RelativePath dependentId, IEnumerable<RelativePath> dependeeIds, DependencyType type = DependencyType.Uses)
    {
        foreach (var dependeeId in dependeeIds)
            AddDependency(dependentId, dependeeId, type);
    }

    public void AddDependencyRange(RelativePath dependentId, IEnumerable<Dependency> dependencies)
    {
        if (!_dependsOn.TryGetValue(dependentId, out var deps))
        {
            deps = [];
            _dependsOn[dependentId] = deps;
        }

        foreach (var dependency in dependencies)
        {
            var existing = deps.FirstOrDefault(dep => dep.To == dependency.To, null);
            if (existing is not null)
                existing = existing with { Count = existing.Count + dependency.Count };
            else
                deps.Add(dependency);
        }
    }


    public ProjectDependencyGraph Merge(ProjectDependencyGraph other)
    {
        if (other is null) return this;
        if (!StringComparer.Ordinal.Equals(_projectRoot, other._projectRoot))
            throw new InvalidOperationException("Cannot merge graphs with different project roots.");

        var merged = new ProjectDependencyGraph(_projectRoot);

        foreach (var kv in _projectItems)
            merged._projectItems[kv.Key] = kv.Value;
        foreach (var kv in other._projectItems)
            merged._projectItems[kv.Key] = kv.Value;

        foreach (var kv in _contains)
            merged._contains[kv.Key] = [.. kv.Value];
        foreach (var kv in other._contains)
            merged._contains[kv.Key] = [.. kv.Value];

        foreach (var kv in _dependsOn)
            merged.AddDependencyRange(kv.Key, kv.Value);
        foreach (var kv in other._dependsOn)
            merged.AddDependencyRange(kv.Key, kv.Value);

        foreach (var kv in merged._contains)
        {
            foreach (var child in kv.Value)
                merged._parentOf[child] = kv.Key;
        }

        return merged;
    }

    public RelativePath AddParent(RelativePath parent, RelativePath child)
    {
        if(!_parentOf.TryAdd(child, parent))
            _parentOf[child] = parent;

        if (_contains.TryGetValue(parent, out var children))
            children.Add(child);
        else
            _contains[parent] = [child];

        return parent;
    }

    private RelativePath NormalisePath(RelativePath path, ProjectItemType type) =>
        type == ProjectItemType.Directory
            ? RelativePath.Directory(_projectRoot, path.Value)
            : RelativePath.File(_projectRoot, path.Value);
}
