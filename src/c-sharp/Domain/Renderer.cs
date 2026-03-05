using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using Archlens.Domain.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Archlens.Domain;

public enum RenderState
{
    NEUTRAL,
    CREATED,
    DELETED
}

public sealed record RenderNode(
    RelativePath Path,
    string Label,
    ProjectItemType Type,
    RenderState State
);

public sealed record RenderEdge(
    RelativePath From,
    RelativePath To,
    int Count,              // current/local count shown in the rendered view
    int Delta,              // local - remote
    DependencyType Type,
    RenderState State
);

public sealed record RenderGraph(
    IReadOnlyDictionary<RelativePath, RenderNode> Nodes,
    IReadOnlyList<RenderEdge> Edges
);

public abstract class Renderer
{
    public abstract string FileExtension { get; }

    protected abstract string Render(RenderGraph graph, View view, RenderOptions options);

    public string RenderView(ProjectDependencyGraph graph, View view, RenderOptions options)
    {
        var renderGraph = BuildRenderGraph(graph, view, options);
        return Render(renderGraph, view, options);
    }

    public string RenderDiffView(
        ProjectDependencyGraph localGraph,
        ProjectDependencyGraph remoteGraph,
        View view,
        RenderOptions options)
    {
        var renderGraph = BuildDiffRenderGraph(localGraph, remoteGraph, view, options);
        return Render(renderGraph, view, options);
    }

    public async Task RenderViewsAndSaveToFiles(ProjectDependencyGraph graph, RenderOptions options)
    {
        foreach (var view in options.Views)
        {
            var content = RenderView(graph, view, options);
            await SaveViewToFileAsync(content, view, options);
        }
    }

    public async Task RenderDiffViewsAndSaveToFiles(
        ProjectDependencyGraph localGraph,
        ProjectDependencyGraph remoteGraph,
        RenderOptions options)
    {
        foreach (var view in options.Views)
        {
            var content = RenderDiffView(localGraph, remoteGraph, view, options);
            await SaveViewToFileAsync(content, view, options, diff: true);
        }
    }

    public async Task SaveViewToFileAsync(string content, View view, RenderOptions options, bool diff = false)
    {
        var dir = options.SaveLocation;
        Directory.CreateDirectory(dir);

        var diffString = diff ? "-diff" : "";
        var filename = $"{options.BaseOptions.ProjectName}{diffString}-{view.ViewName}.{FileExtension}";
        var path = Path.Combine(dir, filename);

        await File.WriteAllTextAsync(path, content);
    }

    private static RenderGraph BuildRenderGraph(
        ProjectDependencyGraph graph,
        View view,
        RenderOptions options)
    {
        var scope = CollectScope(graph, view, options);

        var nodes = scope.ToDictionary(
            path => path,
            path =>
            {
                var item = graph.GetProjectItem(path)
                    ?? throw new InvalidOperationException($"Missing project item '{path}'.");
                return new RenderNode(
                    Path: path,
                    Label: item.Name,
                    Type: item.Type,
                    State: RenderState.Neutral);
            });

        var edges = new List<RenderEdge>();

        foreach (var from in scope)
        {
            foreach (var (to, dep) in graph.DependenciesFrom(from))
            {
                if (!scope.Contains(to))
                    continue;

                edges.Add(new RenderEdge(
                    From: from,
                    To: to,
                    Count: dep.Count,
                    Delta: 0,
                    Type: dep.Type,
                    State: RenderState.Neutral));
            }
        }

        return new RenderGraph(nodes, edges);
    }

    private static RenderGraph BuildDiffRenderGraph(
        ProjectDependencyGraph localGraph,
        ProjectDependencyGraph remoteGraph,
        View view,
        RenderOptions options)
    {
        var localScope = CollectScope(localGraph, view, options);
        var remoteScope = CollectScope(remoteGraph, view, options);

        var allPaths = new HashSet<RelativePath>(localScope);
        allPaths.UnionWith(remoteScope);

        var nodes = new Dictionary<RelativePath, RenderNode>();

        foreach (var path in allPaths)
        {
            var localItem = localGraph.GetProjectItem(path);
            var remoteItem = remoteGraph.GetProjectItem(path);

            var state =
                localItem is not null && remoteItem is null ? RenderState.Added :
                localItem is null && remoteItem is not null ? RenderState.Removed :
                IsModified(localGraph, remoteGraph, path) ? RenderState.Modified :
                RenderState.Neutral;

            var item = localItem ?? remoteItem
                ?? throw new InvalidOperationException($"Expected '{path}' in either local or remote graph.");

            nodes[path] = new RenderNode(
                Path: path,
                Label: item.Name,
                Type: item.Type,
                State: state);
        }

        var edges = BuildDiffEdges(localGraph, remoteGraph, allPaths);

        return new RenderGraph(nodes, edges);
    }

    private static List<RenderEdge> BuildDiffEdges(
        ProjectDependencyGraph localGraph,
        ProjectDependencyGraph remoteGraph,
        IReadOnlySet<RelativePath> scope)
    {
        List<RenderEdge> result = [];

        var allSources = new HashSet<RelativePath>(scope);

        foreach (var from in allSources)
        {
            var localDeps = localGraph.DependenciesFrom(from);
            var remoteDeps = remoteGraph.DependenciesFrom(from);

            var allTargets = new HashSet<RelativePath>(localDeps.Keys);
            allTargets.UnionWith(remoteDeps.Keys);

            foreach (var to in allTargets)
            {
                if (!scope.Contains(to))
                    continue;

                var hasLocal = localDeps.TryGetValue(to, out var localDep);
                var hasRemote = remoteDeps.TryGetValue(to, out var remoteDep);

                var localCount = hasLocal ? localDep.Count : 0;
                var remoteCount = hasRemote ? remoteDep.Count : 0;
                var delta = localCount - remoteCount;

                var state =
                    hasLocal && !hasRemote ? RenderState.Added :
                    !hasLocal && hasRemote ? RenderState.Removed :
                    delta != 0 ? RenderState.Modified :
                    RenderState.Neutral;

                result.Add(new RenderEdge(
                    From: from,
                    To: to,
                    Count: localCount,
                    Delta: delta,
                    Type: hasLocal ? localDep.Type : remoteDep.Type,
                    State: state));
            }
        }

        return result;
    }

    private static bool IsModified(
        ProjectDependencyGraph localGraph,
        ProjectDependencyGraph remoteGraph,
        RelativePath path)
    {
        var local = localGraph.GetProjectItem(path);
        var remote = remoteGraph.GetProjectItem(path);

        if (local is null || remote is null)
            return false;

        if (local.Type != remote.Type)
            return true;

        var localChildren = new HashSet<RelativePath>(localGraph.ChildrenOf(path));
        var remoteChildren = new HashSet<RelativePath>(remoteGraph.ChildrenOf(path));

        if (!localChildren.SetEquals(remoteChildren))
            return true;

        var localDeps = localGraph.DependenciesFrom(path);
        var remoteDeps = remoteGraph.DependenciesFrom(path);

        if (localDeps.Count != remoteDeps.Count)
            return true;

        foreach (var (depPath, dep) in localDeps)
        {
            if (!remoteDeps.TryGetValue(depPath, out var other))
                return true;

            if (!Equals(dep, other))
                return true;
        }

        return false;
    }

    private static HashSet<RelativePath> CollectScope(
        ProjectDependencyGraph graph,
        View view,
        RenderOptions options)
    {
        var projectRoot = RelativePath.Directory(
            options.BaseOptions.FullRootPath,
            options.BaseOptions.ProjectRoot);

        var ignore = new HashSet<RelativePath>(
            (view.IgnorePackages ?? [])
                .Select(p => RelativePath.Directory(options.BaseOptions.FullRootPath, p)),
            EqualityComparer<RelativePath>.Default);

        var roots =
            view.Packages is { Count: > 0 }
                ? view.Packages
                    .Select(p => RelativePath.Directory(options.BaseOptions.FullRootPath, p.Path))
                : graph.ChildrenOf(projectRoot).ToList();

        var visited = new HashSet<RelativePath>();
        var stack = new Stack<RelativePath>(roots);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current))
                continue;

            if (ignore.Contains(current))
                continue;

            foreach (var child in graph.ChildrenOf(current))
                stack.Push(child);
        }

        return visited;
    }
}