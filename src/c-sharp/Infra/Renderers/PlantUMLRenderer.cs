using System;
using System.Linq;
using System.Text;
using Archlens.Domain;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;

namespace Archlens.Infra.Renderers;

public sealed class PlantUMLRenderer : Renderer
{
    public override string FileExtension => "puml";

    protected override string Render(RenderGraph graph, View view, RenderOptions options)
    {
        var aliases = graph.Nodes.Values.ToDictionary(
            n => n.Path,
            n => ToAlias(n.Path.Value));

        var sb = new StringBuilder();

        sb.AppendLine("@startuml");
        sb.AppendLine("skinparam linetype ortho");
        sb.AppendLine("skinparam backgroundColor GhostWhite");
        sb.AppendLine($"title {Escape(view.ViewName)}");

        foreach (var node in graph.Nodes.Values.OrderBy(n => n.Path.Value, StringComparer.OrdinalIgnoreCase))
        {
            var alias = aliases[node.Path];
            var colour = NodeColour(node.State);

            if (node.Type == ProjectItemType.Directory)
            {
                if (string.IsNullOrEmpty(colour))
                    sb.AppendLine($"package \"{Escape(node.Label)}\" as {alias} {{}}");
                else
                    sb.AppendLine($"package \"{Escape(node.Label)}\" as {alias} {colour} {{}}");
            }
            else
            {
                if (string.IsNullOrEmpty(colour))
                    sb.AppendLine($"component \"{Escape(node.Label)}\" as {alias}");
                else
                    sb.AppendLine($"component \"{Escape(node.Label)}\" as {alias} {colour}");
            }
        }

        foreach (var edge in graph.Edges
                     .OrderBy(e => e.From.Value, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(e => e.To.Value, StringComparer.OrdinalIgnoreCase))
        {
            if (!aliases.TryGetValue(edge.From, out var fromAlias))
                continue;

            if (!aliases.TryGetValue(edge.To, out var toAlias))
                continue;

            var colour = EdgeColour(edge.State);
            var label = FormatLabel(edge.Count, edge.Delta);

            if (string.IsNullOrEmpty(colour))
                sb.AppendLine($"{fromAlias} --> {toAlias} : {label}");
            else
                sb.AppendLine($"{fromAlias} --> {toAlias} {colour} : {label}");
        }

        sb.AppendLine("@enduml");
        return sb.ToString();
    }

    private static string FormatLabel(int count, int delta)
    {
        if (delta == 0)
            return count.ToString();

        var sign = delta > 0 ? "+" : "";
        return $"{count} ({sign}{delta})";
    }

    private static string NodeColour(RenderState state) => state switch
    {
        RenderState.Added => "#LightGreen",
        RenderState.Removed => "#LightCoral",
        RenderState.Modified => "#Moccasin",
        _ => ""
    };

    private static string EdgeColour(RenderState state) => state switch
    {
        RenderState.Added => "#Green",
        RenderState.Removed => "#Red",
        RenderState.Modified => "#Orange",
        _ => ""
    };

    private static string ToAlias(string path)
    {
        var chars = path.Select(ch =>
            char.IsLetterOrDigit(ch) ? ch : '_');

        var alias = new string(chars.ToArray());

        if (string.IsNullOrWhiteSpace(alias))
            return "node";

        if (char.IsDigit(alias[0]))
            alias = "_" + alias;

        return alias;
    }

    private static string Escape(string value) =>
        value.Replace("\"", "\\\"");
}