using Archlens.Domain.Interfaces;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using Archlens.Domain.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Archlens.Infra.Renderers;

public sealed class PlantUMLRenderer : IRenderer
{
    private string packagesPuml = "";
    private List<string> packageNames = [];
    private string dependenciesPuml = "";

    public string RenderView(DependencyGraph rootGraph, View view, RenderOptions options)
    {
        packagesPuml = "";
        packageNames = [];
        dependenciesPuml = "";

        if (view.Packages.Count > 0)
        {
            foreach (var package in view.Packages)
            {
                var graphPath = Path.Combine(options.BaseOptions.FullRootPath, package.Path);
                var g = rootGraph.FindByPath(graphPath);
                if (g != null)
                    UpdatePackages(g, view, package);
            }

            foreach (var package in view.Packages)
            {
                var graphPath = Path.Combine(options.BaseOptions.FullRootPath, package.Path);
                var g = rootGraph.FindByPath(graphPath);
                if (g != null)
                    UpdateDependencies(g, view, package);
            }
        }
        else
        {
            var nodeChildren = rootGraph.GetChildren().Where(ch => ch is DependencyGraphNode);

            foreach (var child in nodeChildren)
            {
                var package = new Package(child.Path, 0);
                UpdatePackages(child, view, package);
            }

            foreach (var child in nodeChildren)
            {
                var package = new Package(child.Path, 0);
                UpdateDependencies(child, view, package);
            }
        }

        string uml_str = $"""
        @startuml
        skinparam linetype ortho
        skinparam backgroundColor GhostWhite
        title {view.ViewName}
        {packagesPuml}
        {dependenciesPuml}
        @enduml
        """;

        return uml_str;
    }

    private void UpdatePackages(DependencyGraph graph, View view, Package package)
    {
        packagesPuml += GetChildrenPackages(graph, view, package.Depth);
    }

    private string GetChildrenPackages(DependencyGraph graph, View view, int depth)
    {
        if (!view.IgnorePackages.Contains(graph.Name) && !view.IgnorePackages.Contains(graph.Path))
        {
            packageNames.Add(PathNormaliser.GetPathDotSeparated(graph.Path));
            var packages = $"package \"{graph.Name}\" as {graph.Name} {{\n";

            if (depth >= 1)
            {
                var newDepth = depth - 1;
                foreach (var child in graph.GetChildren().Where(ch => ch is DependencyGraphNode))
                {
                    packages += GetChildrenPackages(child, view, newDepth);
                }
            }

            packages += "}\n";

            return packages;
        }
        else return "";
    }

    private void UpdateDependencies(DependencyGraph graph, View view, Package package)
    {
        UpdateDependenciesForChildren(graph, view, package.Depth);
    }

    private void UpdateDependenciesForChildren(DependencyGraph graph, View view, int depth)
    {
        if (!view.IgnorePackages.Contains(graph.Name) && !view.IgnorePackages.Contains(graph.Path))
        {
            if (depth >= 1)
            {
                var newDepth = depth - 1;
                foreach (var child in graph.GetChildren())
                {
                    if (child is DependencyGraphNode)
                        UpdateDependenciesForChildren(child, view, newDepth);
                    else
                    {
                        Dictionary<string, int> relevantDeps = [];

                        foreach (var (dep, count) in child.GetDependencies())
                        {
                            if (packageNames.Contains(dep))
                            {
                                UpsertDependencyPuml(graph.Name, dep, count);
                            }
                            else if (packageNames.Any(p => dep.StartsWith(p)))
                            {
                                var trimmedDep = packageNames.Find(p => dep.StartsWith(p));
                                UpsertDependencyPuml(graph.Name, trimmedDep, count);
                            }
                        }
                    }
                }
            }
            else
            {
                Dictionary<string, int> relevantDeps = [];

                foreach (var (dep, count) in graph.GetDependencies())
                {
                    if (packageNames.Contains(dep))
                    {
                        UpsertDependencyPuml(graph.Name, dep, count);
                    }
                    else if (packageNames.Any(p => dep.StartsWith(p)))
                    {
                        var trimmedDep = packageNames.Find(p => dep.StartsWith(p));
                        UpsertDependencyPuml(graph.Name, trimmedDep, count);
                    }
                }
            }
        }
    }

    private void UpsertDependencyPuml(string fromName, string toName, int count)
    {
        if (toName.StartsWith(fromName)) return; //Ignore dependencies to children

        var regex = $@"{fromName}-->{toName} : (\d+)\n";

        if (Regex.IsMatch(dependenciesPuml, regex))
        {
            var match = Regex.Match(dependenciesPuml, regex);
            var existingCount = int.Parse(match.Groups[1].Value);
            var newCount = existingCount + count;
            var newPuml = $"{fromName}-->{toName} : {newCount}\n"; //TODO: Add color depending on diff

            dependenciesPuml = Regex.Replace(dependenciesPuml, regex, newPuml);
        }
        else
        {
            dependenciesPuml += $"{fromName}-->{toName} : {count}\n"; //TODO: Add color depending on diff   
        }
    }
}