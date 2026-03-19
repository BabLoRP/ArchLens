using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Archlens.Domain.Models.Records;
using Archlens.Domain.Utils;

namespace Archlens.Domain.Models;

public enum ProjectItemType
{
    Directory,
    File
}

public sealed record ProjectItem(
    RelativePath Path,
    string Name,
    DateTime LastWriteTime,
    ProjectItemType Type
);

public enum DependencyType
{
    Uses
}

public sealed record Dependency(
    int Count,
    DependencyType Type
);

public class ProjectDependencyGraph(string projectRoot)
{
    private readonly string _projectRoot = projectRoot ?? throw new ArgumentNullException(nameof(projectRoot));

    private readonly Dictionary<RelativePath, ProjectItem> _projectItems = [];
    private readonly Dictionary<RelativePath, HashSet<RelativePath>> _childrenByParent = [];
    private readonly Dictionary<RelativePath, RelativePath> _parentByChild = [];
    private readonly Dictionary<RelativePath, Dictionary<RelativePath, Dependency>> _dependenciesBySource = [];

    private static readonly IReadOnlyDictionary<RelativePath, Dependency> EmptyDependencies =
        new ReadOnlyDictionary<RelativePath, Dependency>(new Dictionary<RelativePath, Dependency>());

    public IReadOnlyDictionary<RelativePath, ProjectItem> ProjectItems => _projectItems;

    public ProjectItem? GetProjectItem(RelativePath path) => _projectItems.TryGetValue(path, out var item) ? item : null;

    public bool ContainsProjectItem(RelativePath path) => _projectItems.ContainsKey(path);

    public RelativePath UpsertProjectItem(RelativePath path, ProjectItemType type)
    {
        if (string.IsNullOrWhiteSpace(path.Value))
            throw new ArgumentNullException(nameof(path));

        var id = NormalisePath(path, type);
        var absPath = PathNormaliser.GetAbsolutePath(_projectRoot, id.Value);

        var name = type == ProjectItemType.Directory
            ? Path.GetFileName(id.Value.TrimEnd('/', '\\'))
            : Path.GetFileName(id.Value);

        DateTime lastWriteTime;

        try
        {
            lastWriteTime = File.GetLastWriteTimeUtc(absPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to access file system for path '{absPath}'.", ex);
        }

        if (_projectItems.TryGetValue(id, out var existing))
        {
            if (existing.Type != type)
                throw new InvalidOperationException(
                    $"Project item '{id}' already exists as {existing.Type}, not {type}.");

            _projectItems[id] = existing with
            {
                Name = name,
                LastWriteTime = lastWriteTime
            };

            return id;
        }

        _projectItems[id] = new ProjectItem(
            Path: id,
            Name: name,
            LastWriteTime: lastWriteTime,
            Type: type
        );

        return id;
    }

    public void UpsertProjectItems(IEnumerable<ProjectItem> items)
    {
        foreach (var item in items)
        {
            var id = NormalisePath(item.Path, item.Type);

            if (_projectItems.TryGetValue(id, out var existing))
            {
                if (existing.Type != item.Type)
                    throw new InvalidOperationException(
                        $"Project item '{id}' already exists as {existing.Type}, not {item.Type}.");

                _projectItems[id] = existing with
                {
                    Name = item.Name,
                    LastWriteTime = item.LastWriteTime
                };
            }
            else
            {
                _projectItems[id] = item with { Path = id };
            }
        }
    }

    public IReadOnlyList<RelativePath> ChildrenOf(RelativePath parent) => _childrenByParent.TryGetValue(parent, out var children) ? [.. children] : [];

    public RelativePath? ParentOf(RelativePath child) => _parentByChild.TryGetValue(child, out var parent) ? parent : null;

    public void AddChild(RelativePath parent, RelativePath child)
    {
        if (!_projectItems.TryGetValue(parent, out var parentItem))
            throw new InvalidOperationException($"Parent '{parent}' does not exist.");

        if (parentItem.Type != ProjectItemType.Directory)
            throw new InvalidOperationException($"Parent '{parent}' is not a directory.");

        if (!_projectItems.ContainsKey(child))
            throw new InvalidOperationException($"Child '{child}' does not exist.");

        if (_parentByChild.TryGetValue(child, out var oldParent))
        {
            if (oldParent.Equals(parent))
                return;

            if (_childrenByParent.TryGetValue(oldParent, out var oldChildren))
            {
                oldChildren.Remove(child);
                if (oldChildren.Count == 0)
                    _childrenByParent.Remove(oldParent);
            }
        }

        if (!_childrenByParent.TryGetValue(parent, out var children))
        {
            children = [];
            _childrenByParent[parent] = children;
        }

        children.Add(child);
        _parentByChild[child] = parent;
    }

    public void AddChildren(RelativePath parent, IEnumerable<RelativePath> children)
    {
        foreach (var child in children)
            AddChild(parent, child);
    }

    public IReadOnlyDictionary<RelativePath, Dependency> DependenciesFrom(RelativePath source)
    {
        return _dependenciesBySource.TryGetValue(source, out var deps)
            ? new ReadOnlyDictionary<RelativePath, Dependency>(deps)
            : EmptyDependencies;
    }

    public void SetDependencies(
        RelativePath source,
        IEnumerable<RelativePath> targets,
        DependencyType type = DependencyType.Uses)
    {
        if (!_projectItems.ContainsKey(source))
            throw new InvalidOperationException($"Dependency source '{source}' does not exist.");

        var grouped = new Dictionary<RelativePath, Dependency>();

        foreach (var target in targets)
        {
            if (grouped.TryGetValue(target, out var existing))
            {
                grouped[target] = existing with { Count = existing.Count + 1 };
            }
            else
            {
                grouped[target] = new Dependency(1, type);
            }
        }

        _dependenciesBySource[source] = grouped;
    }

    public void ReplaceDependencies(
    RelativePath source,
    IReadOnlyDictionary<RelativePath, Dependency> dependencies)
    {
        if (!_projectItems.ContainsKey(source))
            throw new InvalidOperationException($"Dependency source '{source}' does not exist.");

        _dependenciesBySource[source] = new Dictionary<RelativePath, Dependency>(dependencies);
    }

    public void AddDependency(
        RelativePath source,
        RelativePath target,
        DependencyType type = DependencyType.Uses)
    {
        if (!_projectItems.ContainsKey(source))
            throw new InvalidOperationException($"Dependency source '{source}' does not exist.");

        if (!_dependenciesBySource.TryGetValue(source, out var deps))
        {
            deps = [];
            _dependenciesBySource[source] = deps;
        }

        if (deps.TryGetValue(target, out var existing))
        {
            deps[target] = existing with { Count = existing.Count + 1 };
        }
        else
        {
            deps[target] = new Dependency(1, type);
        }
    }

    public void AddDependencies(
        RelativePath source,
        IEnumerable<RelativePath> targets,
        DependencyType type = DependencyType.Uses)
    {
        foreach (var target in targets)
            AddDependency(source, target, type);
    }

    public void AddDependencies(
        RelativePath source,
        IReadOnlyDictionary<RelativePath, Dependency> dependencies)
    {
        if (!_projectItems.ContainsKey(source))
            throw new InvalidOperationException($"Dependency source '{source}' does not exist.");

        if (!_dependenciesBySource.TryGetValue(source, out var deps))
        {
            deps = [];
            _dependenciesBySource[source] = deps;
        }

        foreach (var (target, dependency) in dependencies)
        {
            if (deps.TryGetValue(target, out var existing))
            {
                deps[target] = existing with
                {
                    Count = existing.Count + dependency.Count
                };
            }
            else
            {
                deps[target] = dependency;
            }
        }
    }

    public void RemoveProjectItem(RelativePath path)
    {
        if (!_projectItems.ContainsKey(path))
            return;

        if (_childrenByParent.TryGetValue(path, out var children) && children.Count > 0)
            throw new InvalidOperationException(
                $"Cannot remove directory '{path}' because it still contains children.");

        if (_parentByChild.TryGetValue(path, out var parent))
        {
            if (_childrenByParent.TryGetValue(parent, out var siblings))
            {
                siblings.Remove(path);
                if (siblings.Count == 0)
                    _childrenByParent.Remove(parent);
            }

            _parentByChild.Remove(path);
        }

        _childrenByParent.Remove(path);
        _dependenciesBySource.Remove(path);

        foreach (var deps in _dependenciesBySource.Values)
            deps.Remove(path);

        _projectItems.Remove(path);
    }

    public bool RemoveDependency(RelativePath source, RelativePath target)
    {
        if (!_dependenciesBySource.TryGetValue(source, out var deps))
            return false;

        if (deps.TryGetValue(target, out Dependency? dependency))
        {
            if (dependency.Count == 1)
                deps.Remove(target);
            else
                deps[target] = dependency with { Count = dependency.Count - 1 };
        }
        return true;
    }

    public void RemoveProjectItemRecursive(RelativePath path)
    {
        if (_childrenByParent.TryGetValue(path, out var children))
        {
            foreach (var child in children.ToArray())
                RemoveProjectItemRecursive(child);
        }

        RemoveProjectItem(path);
    }


    public ProjectDependencyGraph MergeOverwrite(ProjectDependencyGraph other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (!StringComparer.Ordinal.Equals(_projectRoot, other._projectRoot))
            throw new InvalidOperationException("Cannot merge graphs with different project roots.");

        var merged = new ProjectDependencyGraph(_projectRoot);

        CopyOverwriteInto(merged, this);
        CopyOverwriteInto(merged, other);

        return merged;
    }

    private static void CopyOverwriteInto(ProjectDependencyGraph target, ProjectDependencyGraph source)
    {
        foreach (var (path, item) in source._projectItems)
            target._projectItems[path] = item;

        foreach (var (parent, children) in source._childrenByParent)
        {
            if (!target._childrenByParent.TryGetValue(parent, out var existing))
                target._childrenByParent[parent] = [.. children];
            else
                foreach (var child in children)
                    existing.Add(child);
        }

        foreach (var (child, parent) in source._parentByChild)
            target._parentByChild[child] = parent;

        foreach (var (sourcePath, deps) in source._dependenciesBySource)
            target._dependenciesBySource[sourcePath] = new Dictionary<RelativePath, Dependency>(deps);
    }

    private RelativePath NormalisePath(RelativePath path, ProjectItemType type)
    {
        return type == ProjectItemType.Directory
            ? RelativePath.Directory(_projectRoot, path.Value)
            : RelativePath.File(_projectRoot, path.Value);
    }
}