using Archlens.Domain;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Archlens.Infra.Renderers;

public sealed class JsonRenderer : RendererBase
{
    private sealed record JsonDependencyDTO(
        string name,
        string path
    );

    private sealed record JsonRelationsDTO(
        JsonDependencyDTO from_file,
        JsonDependencyDTO to_file
    );

    private sealed record JsonRenderDto(
        string title,
        List<JsonPackageDto> packages,
        List<JsonEdgeDto> edges
    );

    private sealed record JsonPackageDto(
        string name,
        string path,
        ProjectItemType type,
        string state
    );

    private sealed record JsonEdgeDto(
        string state,
        string fromPackage,
        string toPackage,
        string label,
        List<JsonRelationsDTO> relations
    );

    public override string FileExtension => "json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    protected override string Render(RenderGraph graph, View view, RenderOptions options)
    {
        var labelByPath = graph.Nodes.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Label);

        var dto = new JsonRenderDto(
            title: view.ViewName,
            packages: [.. graph.Nodes.Values
                .OrderBy(n => n.Path.Value, StringComparer.OrdinalIgnoreCase)
                .Select(n => new JsonPackageDto(
                    name: n.Label,
                    path: n.Path.Value,
                    type: n.Type,
                    state: n.State.ToString()))],

            edges: [.. graph.Edges
                .OrderBy(e => e.From.Value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.To.Value, StringComparer.OrdinalIgnoreCase)
                .Select(e => new JsonEdgeDto(
                    state: e.State.ToString(),
                    fromPackage: labelByPath[e.From],
                    toPackage: labelByPath[e.To],
                    label: FormatLabel(e.Count, e.Delta),
                    relations: [.. e.Relations
                        .Select(r => new JsonRelationsDTO(
                            from_file: new JsonDependencyDTO(
                                name: r.FromFile.GetName(),
                                path: r.FromFile.Value),
                            to_file: new JsonDependencyDTO(
                                name: r.ToFile.GetName(),
                                path: r.ToFile.Value)))]
                ))]
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