using Archlens.Domain;
using Archlens.Domain.Models.Records;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

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
        sb.AppendLine("allowmixing");
        sb.AppendLine("skinparam linetype ortho");
        sb.AppendLine("skinparam backgroundColor GhostWhite");
        sb.AppendLine($"title {Escape(view.ViewName)}");

        foreach (var root in graph.RootNodes.OrderBy(p => p.Value, StringComparer.OrdinalIgnoreCase))
        {
            RenderNodeRecursive(sb, graph, aliases, root, 0);
            sb.AppendLine();
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

    private static void RenderNodeRecursive(
        StringBuilder sb,
        RenderGraph graph,
        IReadOnlyDictionary<RelativePath, string> aliases,
        RelativePath path,
        int indent)
    {
        if (!graph.Nodes.TryGetValue(path, out var node))
            return;

        var prefix = new string(' ', indent * 4);
        var alias = aliases[path];
        var colour = NodeColour(node.State);

        sb.Append(prefix);
        sb.Append($"package \"{Escape(node.Label)}\" as {alias}");
        if (!string.IsNullOrEmpty(colour))
            sb.Append($" {colour}");
        sb.AppendLine(" {");

        if (graph.ChildrenByParent.TryGetValue(path, out var children))
        {
            foreach (var child in children.OrderBy(c => c.Value, StringComparer.OrdinalIgnoreCase))
                RenderNodeRecursive(sb, graph, aliases, child, indent + 1);
        }

        sb.Append(prefix);
        sb.AppendLine("}");
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
        RenderState.CREATED => "#LightGreen",
        RenderState.DELETED => "#LightCoral",
        RenderState.NEUTRAL => "",
        _ => ""
    };

    private static string EdgeColour(RenderState state) => state switch
    {
        RenderState.CREATED => "#Green",
        RenderState.DELETED => "#Red",
        RenderState.NEUTRAL => "",
        _ => ""
    };

    private static string ToAlias(string path)
    {
        var alias = Regex.Replace(path, @"[^\w]", "");

        if (string.IsNullOrWhiteSpace(alias))
            return "node";

        if (char.IsDigit(alias[0]))
            alias = "_" + alias;

        return alias;
    }

    private static string Escape(string value) =>
        value.Replace("\"", "\\\"");
}