using Archlens.Domain.Interfaces;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using Archlens.Domain.Utils;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Archlens.Infra.Renderers;

public sealed class JsonRenderer : IRenderer
{
    private string packagesJson = "";
    private List<string> packageNames = [];
    private string edgesJson = "";

    public async Task RenderViewsAndSaveToFiles(DependencyGraph graph, RenderOptions options)
    {
        foreach (var view in options.Views)
        {
            string content = RenderView(graph, view, options);
            await SaveViewToFileAsync(content, view, options);
        }
    }

    public async Task RenderDiffViewsAndSaveToFiles(DependencyGraph localGraph, DependencyGraph remoteGraph, RenderOptions options)
    {
        foreach (var view in options.Views)
        {
            string localContent = RenderView(localGraph, view, options);
            string remoteContent = RenderView(remoteGraph, view, options);
            string content = CompareAndMerge(localContent, remoteContent);
            await SaveViewToFileAsync(content, view, options);
        }
    }

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

    private string CompareAndMerge(string localContent, string remoteContent)
    {
        var merged = localContent;

        var regex = $@"""state"": NEUTRAL,
                        ""fromPackage"": ""(.+)"",
                        ""toPackage"": ""(.+)"",
                        ""label"": ""(\d+)""";

        foreach (Match match in Regex.Matches(localContent, regex))
        {
            var from = match.Groups[1].Value;
            var to = match.Groups[2].Value;
            var count = int.Parse(match.Groups[3].Value);

            var dependencyRegex = $@"""state"": NEUTRAL,
                                    ""fromPackage"": ""{from}"",
                                    ""toPackage"": ""{to}"",
                                    ""label"": ""(\d+)""";

            if (Regex.IsMatch(remoteContent, dependencyRegex))
            {
                //Update count, add (+x) or (-x)
                var remoteMatch = Regex.Match(remoteContent, dependencyRegex);
                var remoteCount = int.Parse(remoteMatch.Groups[1].Value);
                var diff = count - remoteCount;
                if (diff != 0)
                {
                    var sign = diff > 0 ? "+" : "";
                    var color = diff > 0 ? "#Green" : "#Red";

                    var mergedValue = $@"""state"": {color},
                                    ""fromPackage"": ""{from}"",
                                    ""toPackage"": ""{to}"",
                                    ""label"": ""{count} ({sign}{diff})""";

                    merged = merged.Replace(match.Value, mergedValue);
                }
            }
        }
        return merged;
    }


    public async Task SaveViewToFileAsync(string content, View view, RenderOptions options)
    {
        var dir = options.SaveLocation;
        Directory.CreateDirectory(dir);
        var filename = $"{options.BaseOptions.ProjectName}-{view.ViewName}.json";
        var path = Path.Combine(dir, filename);

        await File.WriteAllTextAsync(path, content);
    }
}