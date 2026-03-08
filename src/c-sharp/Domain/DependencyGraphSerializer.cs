using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Archlens.Domain;

public static class DependencyGraphSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record GraphSnapshotDto(
        int Version,
        IEnumerable<ProjectItemDto> Items,
        IEnumerable<ContainmentDto> Contains,
        IEnumerable<DependencySetDto> DependsOn
    );

    private sealed record ProjectItemDto(
        string Path,
        string Name,
        DateTime LastWriteTime,
        ProjectItemType Type
    );

    private sealed record ContainmentDto(
        string Parent,
        List<string> Children
    );

    private sealed record DependencySetDto(
        string From,
        List<DependencyDto> Dependencies
    );

    private sealed record DependencyDto(
        string To,
        int Count,
        DependencyType Type
    );

    public static string Serialize(ProjectDependencyGraph graph, int version = 1)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var items = graph.ProjectItems.Values
            .OrderBy(i => i.Path.Value, StringComparer.OrdinalIgnoreCase)
            .Select(i => new ProjectItemDto(
                Path: i.Path.Value,
                Name: i.Name,
                LastWriteTime: i.LastWriteTime,
                Type: i.Type));

        var contains = graph.ProjectItems.Values
            .Where(i => i.Type == ProjectItemType.Directory)
            .OrderBy(i => i.Path.Value, StringComparer.OrdinalIgnoreCase)
            .Select(dir => new ContainmentDto(
                Parent: dir.Path.Value,
                Children: [.. graph.ChildrenOf(dir.Path)
                    .Select(p => p.Value)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)]))
            .Where(x => x.Children.Count > 0);

        var dependsOn = graph.ProjectItems.Values
            .Where(i => i.Type == ProjectItemType.File)
            .OrderBy(i => i.Path.Value, StringComparer.OrdinalIgnoreCase)
            .Select(file => new DependencySetDto(
                From: file.Path.Value,
                Dependencies: [.. graph.DependenciesFrom(file.Path)
                    .OrderBy(kv => kv.Key.Value, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => new DependencyDto(
                        To: kv.Key.Value,
                        Count: kv.Value.Count,
                        Type: kv.Value.Type))]))
            .Where(x => x.Dependencies.Count > 0);

        var dto = new GraphSnapshotDto(
            Version: version,
            Items: items,
            Contains: contains,
            DependsOn: dependsOn
        );

        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    public static ProjectDependencyGraph Deserialize(string json, string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON is required.", nameof(json));

        if (string.IsNullOrWhiteSpace(projectRoot))
            throw new ArgumentException("Project root is required.", nameof(projectRoot));

        var dto = JsonSerializer.Deserialize<GraphSnapshotDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize dependency graph snapshot.");

        var graph = new ProjectDependencyGraph(projectRoot);

        var items = dto.Items
            .Select(i =>
            {
                var path = ToRelativePath(projectRoot, i.Path, i.Type);
                return new ProjectItem(
                    Path: path,
                    Name: i.Name,
                    LastWriteTime: i.LastWriteTime,
                    Type: i.Type);
            });

        graph.UpsertProjectItems(items);

        var itemTypeByPath = items.ToDictionary(
            i => i.Path.Value,
            i => i.Type,
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in dto.Contains)
        {
            var parent = RelativePath.Directory(projectRoot, entry.Parent);

            var children = entry.Children.Select(childPath =>
            {
                if (!itemTypeByPath.TryGetValue(childPath, out var childType))
                    throw new InvalidOperationException($"Child '{childPath}' does not exist in snapshot items.");

                return ToRelativePath(projectRoot, childPath, childType);
            });

            graph.AddChildren(parent, children);
        }

        foreach (var entry in dto.DependsOn)
        {
            var from = RelativePath.File(projectRoot, entry.From);

            var dependencies = entry.Dependencies.ToDictionary(
                    d => itemTypeByPath.TryGetValue(d.To, out var targetType)
                        ? ToRelativePath(projectRoot, d.To, targetType)
                        : RelativePath.File(projectRoot, d.To),
                    d => new Dependency(d.Count, d.Type)
                );

            graph.AddDependencies(from, dependencies);
        }

        return graph;
    }

    private static RelativePath ToRelativePath(string projectRoot, string path, ProjectItemType type)
    {
        return type == ProjectItemType.Directory
            ? RelativePath.Directory(projectRoot, path)
            : RelativePath.File(projectRoot, path);
    }
}