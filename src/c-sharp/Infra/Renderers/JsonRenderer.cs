using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Archlens.Domain;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;

namespace Archlens.Infra.Renderers;

public sealed class JsonRenderer : Renderer
{
    private sealed record JsonRenderDto(
        string Title,
        IEnumerable<JsonPackageDto> Packages,
        IEnumerable<JsonEdgeDto> Edges
    );

    private sealed record JsonPackageDto(
        string Name,
        string Path,
        ProjectItemType Type,
        string State
    );

    private sealed record JsonEdgeDto(
        string State,
        string FromPackage,
        string ToPackage,
        string Label,
        int Count,
        int Delta,
        string DependencyType
    );

    public override string FileExtension => "json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    protected override string Render(RenderGraph graph, View view, RenderOptions options)
    {
        var dto = new JsonRenderDto(
            Title: view.ViewName,
            Packages: graph.Nodes.Values
                .OrderBy(n => n.Path.Value, StringComparer.OrdinalIgnoreCase)
                .Select(n => new JsonPackageDto(
                    Name: n.Label,
                    Path: n.Path.Value,
                    Type: n.Type,
                    State: n.State.ToString().ToUpperInvariant())),

            Edges: graph.Edges
                .OrderBy(e => e.From.Value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.To.Value, StringComparer.OrdinalIgnoreCase)
                .Select(e => new JsonEdgeDto(
                    State: e.State.ToString().ToUpperInvariant(),
                    FromPackage: e.From.Value,
                    ToPackage: e.To.Value,
                    Label: FormatLabel(e.Count, e.Delta),
                    Count: e.Count,
                    Delta: e.Delta,
                    DependencyType: e.Type.ToString().ToUpperInvariant()))
        );

        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    private static string FormatLabel(int count, int delta)
    {
        if (delta == 0)
            return count.ToString();

        var sign = delta > 0 ? "+" : "";
        return $"{count} ({sign}{delta})";
    }
}