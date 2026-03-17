using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;

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

public sealed record RenderRelation(
    RelativePath FromFile,
    RelativePath ToFile
);

public sealed record RenderEdge(
    RelativePath From,
    RelativePath To,
    int Count,              // current/local count shown in the rendered view
    int Delta,              // local - remote
    DependencyType Type,
    RenderState State,
    IReadOnlyList<RenderRelation> Relations
);

public sealed record RenderGraph(
    IReadOnlyDictionary<RelativePath, RenderNode> Nodes,
    IReadOnlyDictionary<RelativePath, IReadOnlyList<RelativePath>> ChildrenByParent,
    IReadOnlyList<RenderEdge> Edges,
    IReadOnlyList<RelativePath> RootNodes
);

public abstract class RendererBase
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

    public async Task RenderViewsAndSaveToFiles(ProjectDependencyGraph graph, RenderOptions options, CancellationToken ct)
    {
        await Task.WhenAll(options.Views.Select(async view =>
        {
            var content = RenderView(graph, view, options);
            await SaveViewToFileAsync(content, view, options);
        }));
    }

    public async Task RenderDiffViewsAndSaveToFiles(
        ProjectDependencyGraph localGraph,
        ProjectDependencyGraph remoteGraph,
        RenderOptions options,
        CancellationToken ct)
    {
        await Task.WhenAll(options.Views.Select(async view =>
        {
            var content = RenderDiffView(localGraph, remoteGraph, view, options);
            await SaveViewToFileAsync(content, view, options, diff: true, ct);
        }));
    }

    public async Task SaveViewToFileAsync(string content, View view, RenderOptions options, bool diff = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var dir = options.SaveLocation;
        Directory.CreateDirectory(dir);

        var diffString = diff ? "-diff" : "";
        var filename = $"{options.BaseOptions.ProjectName}{diffString}-{view.ViewName}.{FileExtension}";
        var path = Path.Combine(dir, filename);

        await File.WriteAllTextAsync(path, content, ct);
    }

    private static RenderGraph BuildRenderGraph(
        ProjectDependencyGraph graph,
        View view,
        RenderOptions options)
    {
        var projectRoot = RelativePath.Directory(
            options.BaseOptions.FullRootPath,
            options.BaseOptions.FullRootPath);

        var ignore = new HashSet<RelativePath>(
            (view.IgnorePackages ?? [])
                .Select(p => RelativePath.Directory(options.BaseOptions.FullRootPath, p)));

        var packageRoots = view.Packages is { Count: > 0 }
            ? view.Packages
                .Select(p => (
                    Path: RelativePath.Directory(options.BaseOptions.FullRootPath, p.Path),
                    Depth: p.Depth))
                .ToList()
            : graph.ChildrenOf(projectRoot)
                .Select(p => (
                    Path: p,
                    Depth: 1))
                .Where(x => graph.GetProjectItem(x.Path)?.Type == ProjectItemType.Directory)
                .ToList();

        var visibleNodes = new Dictionary<RelativePath, RenderNode>();
        var childrenByParent = new Dictionary<RelativePath, HashSet<RelativePath>>();
        var selectedRoots = new HashSet<RelativePath>();

        foreach (var (root, depth) in packageRoots)
        {
            var item = graph.GetProjectItem(root);
            if (item is null || item.Type != ProjectItemType.Directory || ignore.Contains(root))
                continue;

            selectedRoots.Add(root);

            AddVisibleDirectories(
                graph,
                root,
                depth,
                ignore,
                visibleNodes,
                childrenByParent);
        }

        var edges = AggregateEdgesToVisibleDirectories(
            graph,
            selectedRoots,
            visibleNodes.Keys.ToHashSet());

        return new RenderGraph(
            Nodes: visibleNodes,
            ChildrenByParent: childrenByParent.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<RelativePath>)[.. kv.Value.OrderBy(p => p.Value, StringComparer.OrdinalIgnoreCase)]),
            Edges: edges,
            RootNodes: [.. selectedRoots.OrderBy(p => p.Value, StringComparer.OrdinalIgnoreCase)]
        );
    }

    private static RenderGraph BuildDiffRenderGraph(
        ProjectDependencyGraph localGraph,
        ProjectDependencyGraph remoteGraph,
        View view,
        RenderOptions options)
    {
        var localRender = BuildRenderGraph(localGraph, view, options);
        var remoteRender = BuildRenderGraph(remoteGraph, view, options);

        var allNodePaths = new HashSet<RelativePath>(localRender.Nodes.Keys);
        allNodePaths.UnionWith(remoteRender.Nodes.Keys);

        var nodes = new Dictionary<RelativePath, RenderNode>();

        foreach (var path in allNodePaths)
        {
            var hasLocal = localRender.Nodes.TryGetValue(path, out var localNode);
            var hasRemote = remoteRender.Nodes.TryGetValue(path, out var remoteNode);

            var state =
                hasLocal && !hasRemote ? RenderState.CREATED :
                !hasLocal && hasRemote ? RenderState.DELETED :
                RenderState.NEUTRAL;

            var node = localNode ?? remoteNode
                ?? throw new InvalidOperationException($"Expected visible node '{path}' in local or remote render graph.");

            nodes[path] = node with { State = state };
        }

        var childrenByParent = MergeChildren(localRender, remoteRender, nodes.Keys.ToHashSet());
        var rootNodes = localRender.RootNodes
            .Concat(remoteRender.RootNodes)
            .Distinct()
            .Where(nodes.ContainsKey)
            .OrderBy(p => p.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var edges = BuildDiffEdges(localRender, remoteRender);

        return new RenderGraph(
            Nodes: nodes,
            ChildrenByParent: childrenByParent,
            Edges: edges,
            RootNodes: rootNodes
        );
    }

#pragma warning disable CA1859 // Use concrete types when possible for improved performance
    private static IReadOnlyDictionary<RelativePath, IReadOnlyList<RelativePath>> MergeChildren(
        RenderGraph localRender,
        RenderGraph remoteRender,
        IReadOnlySet<RelativePath> visibleNodes)
#pragma warning restore CA1859 // Use concrete types when possible for improved performance

    {
        var merged = new Dictionary<RelativePath, HashSet<RelativePath>>();

        static IEnumerable<(RelativePath Parent, RelativePath Child)> Flatten(RenderGraph graph)
        {
            foreach (var (parent, children) in graph.ChildrenByParent)
            {
                foreach (var child in children)
                    yield return (parent, child);
            }
        }

        foreach (var (parent, child) in Flatten(localRender).Concat(Flatten(remoteRender)))
        {
            if (!visibleNodes.Contains(parent) || !visibleNodes.Contains(child))
                continue;

            if (!merged.TryGetValue(parent, out var set))
            {
                set = [];
                merged[parent] = set;
            }

            set.Add(child);
        }

        return merged.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<RelativePath>)kv.Value
                .OrderBy(p => p.Value, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    private static List<RenderEdge> BuildDiffEdges(
        RenderGraph localRender,
        RenderGraph remoteRender)
    {
        var localEdges = localRender.Edges.ToDictionary(
            e => (e.From, e.To),
            e => e);

        var remoteEdges = remoteRender.Edges.ToDictionary(
            e => (e.From, e.To),
            e => e);

        var allKeys = new HashSet<(RelativePath From, RelativePath To)>(localEdges.Keys);
        allKeys.UnionWith(remoteEdges.Keys);

        return [.. allKeys
            .Select(key =>
            {
                var hasLocal = localEdges.TryGetValue(key, out var localEdge);
                var hasRemote = remoteEdges.TryGetValue(key, out var remoteEdge);

                var localCount = hasLocal ? localEdge!.Count : 0;
                var remoteCount = hasRemote ? remoteEdge!.Count : 0;
                var delta = localCount - remoteCount;

                var state =
                    delta > 0 ? RenderState.CREATED :
                    delta < 0 ? RenderState.DELETED :
                    RenderState.NEUTRAL;

                IReadOnlyList<RenderRelation> relations = state switch
                {
                    RenderState.DELETED => hasRemote ? remoteEdge!.Relations : [],
                    _ => hasLocal ? localEdge!.Relations : []
                };

                return new RenderEdge(
                    From: key.From,
                    To: key.To,
                    Count: localCount,
                    Delta: delta,
                    Type: hasLocal ? localEdge!.Type : remoteEdge!.Type,
                    State: state,
                    Relations: relations
                );
            })
            .OrderBy(e => e.From.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.To.Value, StringComparer.OrdinalIgnoreCase)];
    }

    private static void AddVisibleDirectories(
        ProjectDependencyGraph graph,
        RelativePath dir,
        int remainingDepth,
        HashSet<RelativePath> ignore,
        Dictionary<RelativePath, RenderNode> visibleNodes,
        Dictionary<RelativePath, HashSet<RelativePath>> childrenByParent)
    {
        var item = graph.GetProjectItem(dir);
        if (item is null || item.Type != ProjectItemType.Directory || ignore.Contains(dir))
            return;

        if (remainingDepth <= 0)
            return;

        visibleNodes[dir] = new RenderNode(
            Path: dir,
            Label: item.Name,
            Type: item.Type,
            State: RenderState.NEUTRAL);

        foreach (var child in graph.ChildrenOf(dir))
        {
            var childItem = graph.GetProjectItem(child);
            if (childItem is null || childItem.Type != ProjectItemType.Directory || ignore.Contains(child))
                continue;

            AddVisibleDirectories(
                graph,
                child,
                remainingDepth - 1,
                ignore,
                visibleNodes,
                childrenByParent);

            if (visibleNodes.ContainsKey(child))
            {
                if (!childrenByParent.TryGetValue(dir, out var set))
                {
                    set = [];
                    childrenByParent[dir] = set;
                }

                set.Add(child);
            }
        }
    }

    private static List<RenderEdge> AggregateEdgesToVisibleDirectories(
        ProjectDependencyGraph graph,
        IReadOnlySet<RelativePath> selectedRoots,
        IReadOnlySet<RelativePath> visibleDirs)
    {

        var ancestorCache = new Dictionary<RelativePath, RelativePath?>();

        RelativePath? CachedAncestor(RelativePath path)
        {
            if (!ancestorCache.TryGetValue(path, out var result))
                ancestorCache[path] = result = NearestVisibleAncestor(graph, path, visibleDirs);
            return result;
        }

        var edgeMap = new Dictionary<(RelativePath From, RelativePath To), Dependency>();
        var relationsMap = new Dictionary<(RelativePath From, RelativePath To), List<RenderRelation>>();
        var relationsKeySet = new Dictionary<(RelativePath From, RelativePath To), HashSet<(RelativePath, RelativePath)>>();

        foreach (var item in graph.ProjectItems.Values)
        {
            if (!IsUnderAnyRoot(item.Path, selectedRoots))
                continue;

            var fromVisible = CachedAncestor(item.Path);
            if (fromVisible is null)
                continue;

            foreach (var (depTarget, dep) in graph.DependenciesFrom(item.Path))
            {
                if (!IsUnderAnyRoot(depTarget, selectedRoots))
                    continue;

                var toVisible = CachedAncestor(depTarget);
                if (toVisible is null || fromVisible.Value.Equals(toVisible.Value))
                    continue;

                var key = (fromVisible.Value, toVisible.Value);

                if (edgeMap.TryGetValue(key, out var existing))
                    edgeMap[key] = existing with { Count = existing.Count + dep.Count };
                else
                    edgeMap[key] = dep;

                if (!relationsMap.TryGetValue(key, out var rels))
                {
                    rels = [];
                    relationsMap[key] = rels;
                    relationsKeySet[key] = [];
                }

                if (relationsKeySet[key].Add((item.Path, depTarget)))
                    rels.Add(new RenderRelation(item.Path, depTarget));
            }
        }

        return [.. edgeMap
            .OrderBy(kv => kv.Key.From.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kv => kv.Key.To.Value, StringComparer.OrdinalIgnoreCase)
            .Select(kv =>
            {
                relationsMap.TryGetValue(kv.Key, out var rels);
                rels ??= [];

                return new RenderEdge(
                From: kv.Key.From,
                To: kv.Key.To,
                Count: kv.Value.Count,
                Delta: 0,
                Type: kv.Value.Type,
                State: RenderState.NEUTRAL,
                rels
            );
            })];
    }

    private static RelativePath? NearestVisibleAncestor(
        ProjectDependencyGraph graph,
        RelativePath path,
        IReadOnlySet<RelativePath> visibleDirs)
    {
        var current = path;

        while (true)
        {
            var item = graph.GetProjectItem(current);
            if (item is not null && item.Type == ProjectItemType.Directory && visibleDirs.Contains(current))
                return current;

            var parent = graph.ParentOf(current);
            if (parent is null)
                return null;

            current = parent.Value;
        }
    }

    private static bool IsUnderAnyRoot(RelativePath path, IReadOnlySet<RelativePath> roots)
    {
        foreach (var root in roots)
            if (path.Value.StartsWith(root.Value, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}