using Archlens.Domain.Interfaces;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Archlens.Infra.Renderers;

public sealed class JsonRenderer : IRenderer
{
    public string RenderGraph(DependencyGraph graph, RenderOptions options, CancellationToken ct = default)
    {
        var childrenJson = "";
        var childrenRelations = "";
        var children = graph.GetChildren();
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];

            if (childrenJson.Contains(child.Name)) continue;
            if (!childrenJson.EndsWith(",\n") && !string.IsNullOrEmpty(childrenJson)) childrenJson += ",\n";
            if (!childrenRelations.EndsWith(",\n") && !string.IsNullOrEmpty(childrenRelations) && !string.IsNullOrEmpty(ToJson(child))) childrenRelations += ",\n";

            childrenJson +=
                $$"""
                
                {
                    "name": "{{child.Name}}",
                    "state": "NEUTRAL"
                }
            """;

            childrenRelations += ToJson(child);
        }

        var str =
        $$"""
        {
            "title": "{{graph.Name}}",
            "packages": [
                {{childrenJson}}
            ],

            "edges": [
                {{childrenRelations}}
            ]
        }
        """;
        return str;
    }

    public string RenderGraphs(IEnumerable<DependencyGraph> graphs, string viewName, RenderOptions options, CancellationToken ct = default)
    {
        var childrenJson = "";
        var childrenRelations = "";

        foreach (var graph in graphs)
        {
            var children = graph.GetChildren();
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];

                if (childrenJson.Contains(child.Name)) continue;
                if (!childrenJson.EndsWith(",\n") && !string.IsNullOrEmpty(childrenJson)) childrenJson += ",\n";
                if (!childrenRelations.EndsWith(",\n") && !string.IsNullOrEmpty(childrenRelations) && !string.IsNullOrEmpty(ToJson(child))) childrenRelations += ",\n";

                childrenJson +=
                    $$"""
                    
                    {
                        "name": "{{child.Name}}",
                        "state": "NEUTRAL"
                    }
                    """;

                childrenRelations += ToJson(child);
            }
        }

        var str =
        $$"""
        {
            "title": "{{viewName}}",
            "packages": [
                {{childrenJson}}
            ],

            "edges": [
                {{childrenRelations}}
            ]
        }
        """;
        return str;
    }

    private static string ToJson(DependencyGraph graph)
    {
        return graph switch
        {
            DependencyGraphNode node => NodeToJson(node),
            DependencyGraphLeaf => "",
            _ => throw new InvalidOperationException("Unknown DependencyGraph type"),
        };
    }

    private static string NodeToJson(DependencyGraphNode node)
    {
        var str = "";
        var relations = "";
        var children = node.GetChildren();

        foreach (var (dep, count) in node.GetDependencies())
        {
            foreach (var child in children)
            {
                if (child.GetDependencies().TryGetValue(dep, out int val))
                {
                    var childName = child.Name.Replace("\\", ".");
                    if (!relations.EndsWith(",\n") && !string.IsNullOrEmpty(relations)) relations += ",\n";

                    relations +=
                        $$"""
                            {
                                "from_file": {
                                    "name": "{{childName}}",
                                    "path": "{{childName}}"
                                },
                                "to_file": {
                                    "name": "{{dep}}",
                                    "path": "{{dep}}"
                                }
                            }
                        """;
                }
            }

            if (!str.EndsWith(",\n") && !string.IsNullOrEmpty(str)) str += ",\n";

            str += //TODO: diff view (state)
                $$"""
                        {
                            "state": "NEUTRAL",
                            "fromPackage": "{{node.Name}}",
                            "toPackage": "{{dep}}",
                            "label": "{{count}}",
                            "relations": [
                                {{relations}}
                            ]
                        }
                    """;
        }

        return str;

    }

}