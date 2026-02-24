using Archlens.Domain.Models;
using Archlens.Domain.Utils;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Archlens.Domain;

public static class DependencyGraphSerializer
{
    private sealed record GraphSnapshotDto(
        int Version,
        IEnumerable<ProjectItem> Items,
        IReadOnlyDictionary<RelativePath, IReadOnlySet<RelativePath>> Contains,
        IReadOnlyDictionary<RelativePath, IReadOnlyList<Dependency>> DependsOn);

    public static string Serialize(ProjectDependencyGraph graph, int version = 0)
    {
        var items = graph.Items.Values;
        var contains = graph.Contains;
        var dependsOn = graph.Dependencies;

        var dto = new GraphSnapshotDto(
            Version: version,
            Items: items,
            Contains: contains,
            DependsOn: dependsOn
        );

        return JsonSerializer.Serialize(dto);
    }

    public static ProjectDependencyGraph Deserialize(string json, string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON is required.", nameof(json));
        if (string.IsNullOrWhiteSpace(projectRoot))
            throw new ArgumentException("Project root is required.", nameof(projectRoot));

        var dto = JsonSerializer.Deserialize<GraphSnapshotDto>(json)
                  ?? throw new InvalidOperationException("Failed to deserialize dependency graph snapshot.");

        var graph = new ProjectDependencyGraph(projectRoot);

        graph.AddProjectItemRange(dto.Items);

        foreach (var (parent, children) in dto.Contains)
            graph.AddChildrenRange(parent, children);

        foreach (var (dependend, dependencies) in dto.DependsOn)
            graph.AddDependencyRange(dependend, dependencies);

        DependencyAggregator.RecomputeAggregates(graph);

        return graph;
    }
}
