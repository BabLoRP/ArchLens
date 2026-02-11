using Archlens.Domain.Interfaces;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using Archlens.Domain.Utils;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Archlens.Infra.Renderers;

public sealed class JsonRenderer : IRenderer
{
    private string packagesJson = "";
    private List<string> packageNames = [];
    private string edgesJson = "";

    public string RenderView(DependencyGraph graph, View view, RenderOptions options)
    {
        packagesJson = "";
        packageNames = [];
        edgesJson = "";

        if (view.Packages.Count > 0)
        {
            foreach (var package in view.Packages)
            {
                var graphPath = Path.Combine(options.BaseOptions.FullRootPath, package.Path);
                var packageGraph = graph.FindByPath(graphPath);
                if (packageGraph != null)
                {
                    foreach (var child in packageGraph.GetChildren().Where(c => c is DependencyGraphNode && !IsIgnored(view.IgnorePackages, c)))
                    {
                        packagesJson += //TODO: diff view (state)
                        $$"""
                
                            {
                                "name": "{{child.Name}}",
                                "state": "NEUTRAL"
                            },
                        """;

                        packageNames.Add(PathNormaliser.GetPathDotSeparated(child.Path));
                    }
                }
            }

            foreach (var package in view.Packages)
            {
                var graphPath = Path.Combine(options.BaseOptions.FullRootPath, package.Path);
                var packageGraph = graph.FindByPath(graphPath);
                if (packageGraph != null)
                {
                    foreach (var child in packageGraph.GetChildren().Where(c => c is DependencyGraphNode && !IsIgnored(view.IgnorePackages, c)))
                    {
                        edgesJson += GetEdge(child as DependencyGraphNode);
                    }
                }
            }

        }
        else
        {
            var children = graph.GetChildren().Where(c => c is DependencyGraphNode && !IsIgnored(view.IgnorePackages, c));
            foreach (var child in children)
            {
                packagesJson += //TODO: diff view (state)
                $$"""
        
                    {
                        "name": "{{child.Name}}",
                        "state": "NEUTRAL"
                    },
                """;

                packageNames.Add(PathNormaliser.GetPathDotSeparated(child.Path));
            }

            foreach (var child in children)
            {
                edgesJson += GetEdge(child as DependencyGraphNode);
            }
        }

        var str =
        $$"""
        {
            "title": "{{view.ViewName}}",
            "packages": [
                {{packagesJson.TrimEnd(',')}}
            ],

            "edges": [
                {{edgesJson.TrimEnd(',')}}
            ]
        }
        """;
        return str;
    }

    private string GetEdge(DependencyGraphNode node)
    {
        var str = "";

        foreach (var (dep, count) in node.GetDependencies().Where(d => packageNames.Contains(d.Key)))
        {
            var relations = GetChildrenDependencyRelations(node, dep);

            str += //TODO: diff view (state)
                $$"""
                    {
                        "state": "NEUTRAL",
                        "fromPackage": "{{node.Name}}",
                        "toPackage": "{{dep.Split('.').Last()}}",
                        "label": "{{relations.Split("from_file").Length - 1}}",
                        "relations": [
                            {{relations.TrimEnd(',')}}
                        ]
                    },
                """;
        }

        return str;
    }

    private string GetChildrenDependencyRelations(DependencyGraphNode node, string dep)
    {
        string relations = "";

        foreach (var child in node.GetChildren())
        {
            if (child is DependencyGraphLeaf)
            {
                var subDependencies = child.GetDependencies().Where((d) => d.Key.StartsWith(dep));

                foreach (var subDependency in subDependencies)
                {
                    relations +=
                    $$"""
                        {
                            "from_file": {
                                "name": "{{child.Name}}",
                                "path": "{{child.Path}}"
                            },
                            "to_file": {
                                "name": "{{subDependency.Key.Split('.').Last()}}",
                                "path": "{{subDependency.Key}}"
                            }
                        },
                    """;
                }

            }
            else if (child is DependencyGraphNode childNode)
            {
                relations += GetChildrenDependencyRelations(childNode, dep);
            }
        }

        return relations;
    }

    private static bool IsIgnored(IEnumerable<string> ignorePackages, DependencyGraph graph)
    {
        return ignorePackages.Contains(graph.Name) || ignorePackages.Contains(graph.Path);
    }
}