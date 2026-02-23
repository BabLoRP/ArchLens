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
    int Count,
    DependencyType Type
);

public sealed class ProjectDependencyGraph(string projectRoot)
{
    private readonly string _projectRoot = projectRoot ?? throw new ArgumentNullException(nameof(projectRoot));

    private readonly Dictionary<RelativePath, ProjectItem> _projectItems = [];
    private readonly Dictionary<RelativePath, HashSet<RelativePath>> _contains = [];
    private readonly Dictionary<RelativePath, Dictionary<RelativePath, Dependency>> _dependsOn = [];
    private readonly Dictionary<RelativePath, RelativePath> _parentOf = [];

    public ProjectItem? GetItem(RelativePath id) => _projectItems.TryGetValue(id, out var item) ? item : null;

    public IReadOnlyDictionary<RelativePath, ProjectItem> Items => new ReadOnlyDictionary<RelativePath, ProjectItem>(_projectItems);

    public IReadOnlyList<RelativePath> ChildrenOf(RelativePath directoryId) => _contains.TryGetValue(directoryId, out var children) ? [.. children] : [];

    public RelativePath? ParentOf(RelativePath childId) => _parentOf.TryGetValue(childId, out var parent) ? parent : null;

    public IReadOnlyDictionary<RelativePath, Dependency> DependenciesFrom(RelativePath fromId) =>_dependsOn.TryGetValue(fromId, out var deps)
            ? new ReadOnlyDictionary<RelativePath, Dependency>(deps)
            : new ReadOnlyDictionary<RelativePath, Dependency>(new Dictionary<RelativePath, Dependency>());

    public RelativePath AddProjectItem(RelativePath relPath, ProjectItemType type)
    {
        var id = NormalisePath(relPath, type);

        if (_projectItems.ContainsKey(id))
            return id;

        var absPath = PathNormaliser.GetAbsolutePath(_projectRoot, id.Value);
        var name = type == ProjectItemType.Directory
            ? Path.GetFileName(id.Value.TrimEnd('/', '\\'))
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
            children = new HashSet<RelativePath>();
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

    public void SetDependencies(RelativePath dependentId, IReadOnlyList<RelativePath> dependencyPaths, DependencyType type = DependencyType.Uses)
    {
        if (!_projectItems.ContainsKey(dependentId))
            throw new InvalidOperationException($"Dependent '{dependentId}' does not exist in the graph.");

        var canonicalDeps = dependencyPaths.Select(p => NormalisePath(p, ProjectItemType.File)); // or infer, but be consistent

        var grouped = canonicalDeps
            .GroupBy(p => p)
            .ToDictionary(g => g.Key, g => new Dependency(g.Count(), type));

        _dependsOn[dependentId] = grouped;
    }

    public void AddDependency(RelativePath dependentId, RelativePath dependeeId, DependencyType type = DependencyType.Uses)
    {
        if (!_dependsOn.TryGetValue(dependentId, out var deps))
        {
            deps = [];
            _dependsOn[dependentId] = deps;
        }

        if (deps.TryGetValue(dependeeId, out var existing))
            deps[dependeeId] = existing with { Count = existing.Count + 1 };
        else
            deps[dependeeId] = new Dependency(1, type);
    }

    public void AddDependencyRange(RelativePath dependentId, IEnumerable<RelativePath> dependeeIds, DependencyType type = DependencyType.Uses)
    {
        foreach (var dependeeId in dependeeIds)
            AddDependency(dependentId, dependeeId, type);
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
            merged._dependsOn[kv.Key] = kv.Value.ToDictionary(x => x.Key, x => x.Value);
        foreach (var kv in other._dependsOn)
            merged._dependsOn[kv.Key] = kv.Value.ToDictionary(x => x.Key, x => x.Value);

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
