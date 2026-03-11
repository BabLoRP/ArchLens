using System;
using System.Collections.Generic;
using System.Linq;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using MessagePack;
using MessagePack.Resolvers;

namespace Archlens.Domain;

public static class DependencyGraphSerializer
{
    private static readonly MessagePackSerializerOptions MsgPackOptions =
        MessagePackSerializerOptions.Standard
            .WithResolver(StandardResolver.Instance)
            .WithCompression(MessagePackCompression.Lz4BlockArray);

    [MessagePackObject]
    public sealed class GraphSnapshotDto
    {
        [Key(0)] public int Version { get; set; }
        [Key(1)] public List<ProjectItemDto> Items { get; set; } = [];
        [Key(2)] public List<ContainmentDto> Contains { get; set; } = [];
        [Key(3)] public List<DependencySetDto> DependsOn { get; set; } = [];
    }

    [MessagePackObject]
    public sealed class ProjectItemDto
    {
        [Key(0)] public string Path { get; set; } = "";
        [Key(1)] public string Name { get; set; } = "";
        [Key(2)] public DateTime LastWriteTime { get; set; }
        [Key(3)] public int Type { get; set; }
    }

    [MessagePackObject]
    public sealed class ContainmentDto
    {
        [Key(0)] public string Parent { get; set; } = "";
        [Key(1)] public List<string> Children { get; set; } = [];
    }

    [MessagePackObject]
    public sealed class DependencySetDto
    {
        [Key(0)] public string From { get; set; } = "";
        [Key(1)] public List<DependencyDto> Dependencies { get; set; } = [];
    }

    [MessagePackObject]
    public sealed class DependencyDto
    {
        [Key(0)] public string To { get; set; } = "";
        [Key(1)] public int Count { get; set; }
        [Key(2)] public int Type { get; set; }
    }

    public static byte[] Serialize(ProjectDependencyGraph graph, int version = 1)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var items = graph.ProjectItems.Values
            .OrderBy(i => i.Path.Value, StringComparer.OrdinalIgnoreCase)
            .Select(i => new ProjectItemDto
            {
                Path = i.Path.Value,
                Name = i.Name,
                LastWriteTime = i.LastWriteTime,
                Type = (int)i.Type,
            })
            .ToList();

        var contains = graph.ProjectItems.Values
            .Where(i => i.Type == ProjectItemType.Directory)
            .OrderBy(i => i.Path.Value, StringComparer.OrdinalIgnoreCase)
            .Select(dir => new ContainmentDto
            {
                Parent = dir.Path.Value,
                Children = [.. graph.ChildrenOf(dir.Path)
                    .Select(p => p.Value)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)],
            })
            .Where(x => x.Children.Count > 0)
            .ToList();

        var dependsOn = graph.ProjectItems.Values
            .Where(i => i.Type == ProjectItemType.File)
            .OrderBy(i => i.Path.Value, StringComparer.OrdinalIgnoreCase)
            .Select(file => new DependencySetDto
            {
                From = file.Path.Value,
                Dependencies = [.. graph.DependenciesFrom(file.Path)
                    .OrderBy(kv => kv.Key.Value, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => new DependencyDto
                    {
                        To = kv.Key.Value,
                        Count = kv.Value.Count,
                        Type = (int)kv.Value.Type,
                    })],
            })
            .Where(x => x.Dependencies.Count > 0)
            .ToList();

        var dto = new GraphSnapshotDto
        {
            Version = version,
            Items = items,
            Contains = contains,
            DependsOn = dependsOn,
        };

        return MessagePackSerializer.Serialize(dto, MsgPackOptions);
    }

    public static ProjectDependencyGraph Deserialize(byte[] data, string projectRoot)
    {
        if (data is null || data.Length == 0)
            throw new ArgumentException("Data is required to serialise.", nameof(data));

        if (string.IsNullOrWhiteSpace(projectRoot))
            throw new ArgumentException("Project root is required.", nameof(projectRoot));

        var dto = MessagePackSerializer.Deserialize<GraphSnapshotDto>(data, MsgPackOptions)
            ?? throw new InvalidOperationException("Failed to deserialise dependency graph snapshot.");

        var graph = new ProjectDependencyGraph(projectRoot);

        var items = dto.Items
            .Select(i =>
            {
                var type = (ProjectItemType)i.Type;
                var path = type == ProjectItemType.Directory
                    ? RelativePath.Directory(projectRoot, i.Path)
                    : RelativePath.File(projectRoot, i.Path);
                return new ProjectItem(
                    Path: path,
                    Name: i.Name,
                    LastWriteTime: i.LastWriteTime,
                    Type: type);
            })
            .ToList();

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

                return childType == ProjectItemType.Directory
                    ? RelativePath.Directory(projectRoot, childPath)
                    : RelativePath.File(projectRoot, childPath);
            });

            graph.AddChildren(parent, children);
        }

        foreach (var entry in dto.DependsOn)
        {
            var from = RelativePath.File(projectRoot, entry.From);

            var dependencies = entry.Dependencies.ToDictionary(
                d => itemTypeByPath.TryGetValue(d.To, out var targetType)
                    ? (targetType == ProjectItemType.Directory
                        ? RelativePath.Directory(projectRoot, d.To)
                        : RelativePath.File(projectRoot, d.To))
                    : RelativePath.File(projectRoot, d.To),
                d => new Dependency(d.Count, (DependencyType)d.Type)
            );

            graph.AddDependencies(from, dependencies);
        }

        return graph;
    }
}
