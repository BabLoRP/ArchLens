using Archlens.Domain.Interfaces;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace Archlens.Infra;

public sealed class JsonRenderer : IRenderer
{

    public string RenderGraph(DependencyGraph graph, Options options, CancellationToken ct = default)
    {
        var childrenJson = "";
        var children = graph.GetChildren();
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];

            if (childrenJson.Contains(child.Name)) continue;

            if (i > 0) childrenJson += ",\n";

            childrenJson +=
                $$"""
                
                {
                    "name": "{{child.Name}}",
                    "state": "NEUTRAL"
                }
            """;
        }

        var str =
        $$"""
        {
            "title": "{{graph.Name}}",
            "packages": [
                {{childrenJson}}
            ],

            "edges": [
            {{ToJson(graph)}}

            ]
        }
        """;
        return str;
    }

    public async Task SaveGraphToFileAsync(DependencyGraph graph, Options options, CancellationToken ct = default)
    {
        var root = string.IsNullOrEmpty(options.FullRootPath) ? Path.GetFullPath(options.ProjectRoot) : options.FullRootPath;

        var dir = Path.Combine(root, "diagrams");
        Directory.CreateDirectory(dir);

        var filename = "graph-json.json"; //TODO
        var path = Path.Combine(root, filename);

        var content = RenderGraph(graph, options, ct);

        await File.WriteAllTextAsync(path, content, ct);
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
        var dependencies = node.GetDependencies();
        if (dependencies.Keys.Count > 0)
        {
            for (int i = 0; i < dependencies.Keys.Count; i++)
            {
                var dep = dependencies.Keys.ElementAt(i);
                var relations = "";
                for (int j = 0; j < dependencies[dep]; j++)
                {
                    var rel = dependencies[dep];
                    if (j > 0) relations += ",\n";

                    relations +=
                    $$"""
                            {
                                "from_file": {
                                    "name": "{{dependencies.Keys}}",
                                    "path": "{{dependencies.Keys}}"
                                },
                                "to_file": {
                                    "name": "{{dep}}",
                                    "path": "{{dep}}"
                                }
                            }
                    """;
                }

                if (i > 0) str += ",\n";

                str +=
                $$"""
                    {
                        "state": "NEUTRAL",
                        "fromPackage": "{{node.Name}}",
                        "toPackage": "{{dep}}",
                        "label": "{{dependencies[dep]}}",
                        "relations": [
                            {{relations}}
                        ]
                    }
                """;
            }

        }

        var children = node.GetChildren();
        for (int c = 0; c < children.Count; c++)
        {
            var child = children[c];
            var childJson = ToJson(child);
            if (c > 0 && childJson != "" && !childJson.StartsWith(',') && str != "")
                str += ",\n";

            str += childJson;
        }

        return str;
    }

}