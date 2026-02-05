using Archlens.Domain.Interfaces;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Archlens.Infra.Renderers;

public sealed class PlantUMLRenderer : IRenderer
{
    public string RenderGraph(DependencyGraph graph, RenderOptions options, CancellationToken ct = default)
    {
        string title = graph.Name;
        List<string> graphString = ToPlantUML(graph); //TODO: diff
        graphString.Sort((s1, s2) => s1.Contains("package") ? (s2.Contains("package") ? 0 : -1) : (s2.Contains("package") ? 1 : 0));

        string uml_str = $"""
        @startuml
        skinparam linetype ortho
        skinparam backgroundColor GhostWhite
        title {title}
        {string.Join("\n", graphString.ToArray())}
        @enduml
        """;

        return uml_str;
    }

    public string RenderGraphs(IEnumerable<DependencyGraph> graphs, string viewName, RenderOptions options, CancellationToken ct = default)
    {
        List<string> graphString = [];
        foreach (var graph in graphs)
        {
            graphString.Add($"package \"{graph.Name.Replace("\\", ".")}\" as {graph.Name.Replace("\\", ".")} {{ }}");
            graphString.AddRange(ToPlantUML(graph)); //TODO: diff
        }

        graphString.Sort((s1, s2) => s1.Contains("package") ? (s2.Contains("package") ? 0 : -1) : (s2.Contains("package") ? 1 : 0));
        graphString = [.. graphString.Distinct()];

        string uml_str = $"""
        @startuml
        skinparam linetype ortho
        skinparam backgroundColor GhostWhite
        title {viewName}
        {string.Join("\n", graphString.ToArray())}
        @enduml
        """;

        return uml_str;
    }

    public static List<string> ToPlantUML(DependencyGraph graph, bool isRoot = true)
    {
        return graph switch
        {
            DependencyGraphNode node => NodeToPuml(node, isRoot),
            DependencyGraphLeaf => [],
            _ => throw new InvalidOperationException("Unknown DependencyGraph type"),
        };
    }

    private static List<string> NodeToPuml(DependencyGraphNode node, bool isRoot = true)
    {
        List<string> puml = [];

        if (isRoot)
        {
            var packages = $"package \"{node.Name}\" as {node.Name} {{\n";
            foreach (var child in node.GetChildren())
            {
                if (child is DependencyGraphNode)
                {
                    var childList = ToPlantUML(child, false);

                    var parentEdge = childList.Find(s => s.StartsWith($"{child.Name}-->{node.Name}"));
                    if (!string.IsNullOrEmpty(parentEdge)) childList.Remove(parentEdge);

                    puml.AddRange(childList);
                    packages += $"package \"{child.Name}\" as {child.Name} {{ }}\n";
                }
            }
            packages += "}";
            puml.Add(packages);
        }
        else
        {
            foreach (var (dep, count) in node.GetDependencies())
            {
                var fromName = node.Name;
                var toName = dep.Split(".")[0];
                var existing = puml.Find(p => p.StartsWith($"{fromName}-->{toName} : "));
                if (string.IsNullOrEmpty(existing))
                    puml.Add($"{fromName}-->{toName} : {count}"); //TODO: Add color depending on diff
                else
                {
                    var existingCount = existing.Replace($"{fromName}-->{toName} : ", "");
                    var canParse = int.TryParse(existingCount, out var exCount);

                    if (!canParse) Console.WriteLine("Error parsing " + existingCount);

                    var newCount = canParse ? exCount + count : count;

                    puml.Remove(existing);
                    puml.Add($"{fromName}-->{toName} : {newCount}"); //TODO: Add color depending on diff
                }
            }
        }
        return puml;
    }
}