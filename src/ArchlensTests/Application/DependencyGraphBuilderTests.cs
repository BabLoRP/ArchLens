using Archlens.Application;
using Archlens.Domain.Interfaces;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using Archlens.Domain.Utils;
using ArchlensTests.Utils;

namespace ArchlensTests.Application;

public sealed class DependencyGraphBuilderTests : IDisposable
{
    private readonly TestFileSystem _fs = new();
    public void Dispose() => _fs.Dispose();

    private RenderOptions MakeOptions() => new(
        BaseOptions: new (
            ProjectRoot: _fs.Root,
            ProjectName: "Archlens",
            FullRootPath: _fs.Root
        ),
        Format: default,
        Views: [],
        SaveLocation: null        
    );

    private void SetupMockProject()
    {
        Directory.CreateDirectory(Path.Combine(_fs.Root, "Application"));

        Directory.CreateDirectory(Path.Combine(_fs.Root, "Domain", "Factories"));
        Directory.CreateDirectory(Path.Combine(_fs.Root, "Domain", "Interfaces"));
        Directory.CreateDirectory(Path.Combine(_fs.Root, "Domain", "Models", "Enums"));
        Directory.CreateDirectory(Path.Combine(_fs.Root, "Domain", "Models", "Records"));
        Directory.CreateDirectory(Path.Combine(_fs.Root, "Domain", "Utils"));
    }

    private DependencyGraphBuilder CreateBuilder(IDependencyParser parser) =>
        new(parser, MakeOptions());

    private static DependencyGraphNode RequireNode(DependencyGraph g, string anyPath)
    {
        var found = g.FindByPath(anyPath);
        Assert.NotNull(found);
        var node = Assert.IsType<DependencyGraphNode>(found);
        return node;
    }

    private static DependencyGraphLeaf RequireLeaf(DependencyGraph g, string anyPath)
    {
        var found = g.FindByPath(anyPath);
        Assert.NotNull(found);
        var leaf = Assert.IsType<DependencyGraphLeaf>(found);
        return leaf;
    }

    private static IReadOnlyDictionary<string, IEnumerable<string>> ChangedModules(params (string moduleAbs, IEnumerable<string> contentsAbs)[] entries)
    {
        var dict = new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (m, c) in entries)
            dict[m] = c;
        return dict;
    }

    private sealed class DependencyParserSpy(IReadOnlyDictionary<string, IReadOnlyList<string>> _map) : IDependencyParser
    {
        public List<string> Calls { get; } = [];

        public Task<IReadOnlyList<string>> ParseFileDependencies(string absPath, CancellationToken ct = default)
        {
            Calls.Add(absPath);
            ct.ThrowIfCancellationRequested();

            if (_map.TryGetValue(absPath, out var deps))
                return Task.FromResult(deps);

            return Task.FromResult((IReadOnlyList<string>)[]);
        }
    }

    [Fact]
    public async Task BuildGraph_EmptyChangedModules_ReturnsOnlyRoot()
    {
        SetupMockProject();

        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));
        var builder = CreateBuilder(parser);

        var graph = await builder.GetGraphAsync(
            changedModules: new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase),
            lastSavedDependencyGraph: null);

        Assert.Equal("Archlens", graph.Name);
        Assert.True(graph.Path is "./" or "./");
        Assert.Empty(graph.GetChildren());
        Assert.Empty(parser.Calls);
    }

    [Fact]
    public async Task BuildGraph_BuildsExpectedTreeStructure_ForHappyPath()
    {
        SetupMockProject();

        var depFactory = _fs.File("Domain/Factories/DependencyParserFactory.cs", "/* */");
        var rendFactory = _fs.File("Domain/Factories/RendererFactory.cs", "/* */");
        var options = _fs.File("Domain/Models/Records/Options.cs", "/* */");
        var depGraph = _fs.File("Domain/Models/DependencyGraph.cs", "/* */");

        var domainDir = Path.Combine(_fs.Root, "Domain");
        var factoriesDir = Path.Combine(domainDir, "Factories");
        var modelsDir = Path.Combine(domainDir, "Models");
        var recordsDir = Path.Combine(modelsDir, "Records");

        var changedModules = new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [_fs.Root] = [domainDir],
            [domainDir] = [factoriesDir, Path.Combine(domainDir, "Interfaces"), modelsDir, Path.Combine(domainDir, "Utils")],
            [factoriesDir] = [depFactory, rendFactory],
            [modelsDir] = [Path.Combine(modelsDir, "Enums"), recordsDir, depGraph],
            [recordsDir] = [options]
        };

        var parseMap = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [depFactory] = ["Domain.Interfaces", "Domain.Models.Enums", "Domain.Models.Records", "Infra"],
            [rendFactory] = ["Domain.Interfaces", "Domain.Models.Enums", "Infra"],
            [options] = ["Domain.Models.Enums"],
            [depGraph] = ["Domain.Utils"]
        };

        var parser = new DependencyParserSpy(parseMap);
        var builder = CreateBuilder(parser);

        var graph = await builder.GetGraphAsync(changedModules, null);

        var root = RequireNode(graph, _fs.Root);
        var domain = RequireNode(graph, domainDir);
        var factories = RequireNode(graph, factoriesDir);
        var models = RequireNode(graph, modelsDir);
        RequireLeaf(graph, depFactory);
        RequireLeaf(graph, rendFactory);
        RequireLeaf(graph, options);
        RequireLeaf(graph, depGraph);

        Assert.Same(root, graph);
        Assert.Contains(domain, root.GetChildren());
        Assert.Contains(factories, domain.GetChildren());
        Assert.Contains(models, domain.GetChildren());

        Assert.Equal(4, parser.Calls.Count);
        Assert.Contains(depFactory, parser.Calls);
        Assert.Contains(rendFactory, parser.Calls);
        Assert.Contains(options, parser.Calls);
        Assert.Contains(depGraph, parser.Calls);
    }

    [Fact]
    public async Task BuildGraph_DeduplicatesDuplicateFileEntries()
    {
        SetupMockProject();

        var moduleDir = Path.Combine(_fs.Root, "Domain", "Factories");
        var cs = _fs.File("Domain/Factories/Duplicate.cs", "/* */");

        var parseMap = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [cs] = ["Dep"]
        };

        var parser = new DependencyParserSpy(parseMap);
        var builder = CreateBuilder(parser);

        var changedModules = ChangedModules(
            (moduleDir, new[] { cs, cs, cs })
        );

        var graph = await builder.GetGraphAsync(changedModules, null);

        var moduleNode = RequireNode(graph, moduleDir);

        var matching = moduleNode.GetChildren().Where(c => c is DependencyGraphLeaf && string.Equals(c.Path, PathNormaliser.NormaliseFile(_fs.Root, cs), StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(matching);
    }

    [Fact]
    public async Task ContainsPath_AcceptsAbsoluteAndRelativeVariants_ForSameFile()
    {
        SetupMockProject();

        var moduleDir = Path.Combine(_fs.Root, "Domain", "Factories");
        var cs = _fs.File("Domain/Factories/Variant.cs", "/* */");

        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [cs] = []
        });

        var builder = CreateBuilder(parser);

        var changedModules = ChangedModules(
            (moduleDir, new[] { cs })
        );

        var graph = await builder.GetGraphAsync(changedModules, null);

        Assert.True(graph.ContainsPath(cs));

        var rel1 = "./Domain/Factories/Variant.cs";
        var rel2 = "Domain/Factories/Variant.cs";
        Assert.True(graph.ContainsPath(rel1));
        Assert.True(graph.ContainsPath(rel2));
    }

    [Fact]
    public async Task BuildGraph_CreatesNodesForDirectoriesMentionedInContents_EvenIfNotKeys()
    {
        SetupMockProject();

        var domainDir = Path.Combine(_fs.Root, "Domain");
        var modelsDir = Path.Combine(domainDir, "Models");

        // Note: modelsDir exists (SetupMockProject), but we do NOT include it as a module key.
        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));
        var builder = CreateBuilder(parser);

        var changedModules = ChangedModules(
            (_fs.Root, new[] { domainDir }),
            (domainDir, new[] { modelsDir }) // modelsDir only appears as content
        );

        var graph = await builder.GetGraphAsync(changedModules, null);

        Assert.True(graph.ContainsPath(domainDir));
        Assert.True(graph.ContainsPath(modelsDir));

        var domainNode = RequireNode(graph, domainDir);
        var modelsNode = RequireNode(graph, modelsDir);
        Assert.Contains(modelsNode, domainNode.GetChildren());
    }

    private static DependencyGraphNode MakeDependencyGraph(string rootPath)
    {
        var root = TestGraphs.Node(rootPath, "Archlens", "./");

        var application = TestGraphs.Node(rootPath, "Application", "./Application/");
        var domain = TestGraphs.Node(rootPath, "Domain", "./Domain/");
        var factory = TestGraphs.Node(rootPath, "Factories", "./Domain/Factories/");
        var models = TestGraphs.Node(rootPath, "Models", "./Domain/Models/");
        var records = TestGraphs.Node(rootPath, "Records", "./Domain/Models/Records/");
        var enums = TestGraphs.Node(rootPath, "Enums", "./Domain/Models/Enums/");
        var utils = TestGraphs.Node(rootPath, "Utils", "./Domain/Utils/");

        root.AddChild(application);
        root.AddChild(domain);
        domain.AddChild(factory);
        domain.AddChild(models);
        domain.AddChild(utils);
        models.AddChild(records);
        models.AddChild(enums);

        factory.AddChild(TestGraphs.Leaf(rootPath, "ChangeDetector.cs",
            "./Application/ChangeDetector.cs",
            "Domain.Model", "Domain.Models.Records", "Domain.Utils"));

        factory.AddChild(TestGraphs.Leaf(rootPath, "DependencyParserFactory.cs",
            "./Domain/Factories/DependencyParserFactory.cs",
            "Domain.Interfaces", "Domain.Models.Enums", "Domain.Models.Records", "Infra"));

        factory.AddChild(TestGraphs.Leaf(rootPath, "RendererFactory.cs",
            "./Domain/Factories/RendererFactory.cs",
            "Domain.Interfaces", "Domain.Models.Enums", "Infra"));

        records.AddChild(TestGraphs.Leaf(rootPath, "Options.cs",
            "./Domain/Models/Records/Options.cs",
            "Domain.Models.Enums"));

        models.AddChild(TestGraphs.Leaf(rootPath, "DependencyGraph.cs",
            "./Domain/Models/DependencyGraph.cs",
            "Domain.Utils"));

        return root;
    }

    [Fact]
    public async Task Merge_PrefersChangedLeafDependencies_OverLastSaved()
    {
        SetupMockProject();

        // The file exists on disk so the builder can parse it.
        var depFactoryAbs = _fs.File("Domain/Factories/DependencyParserFactory.cs", "/* */");
        var factoriesDirAbs = Path.Combine(_fs.Root, "Domain", "Factories");

        var lastSavedGraph = MakeDependencyGraph(_fs.Root);

        // Change the dependencies for the same logical file.
        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [depFactoryAbs] = ["New.Dep", "Domain.Models.Enums"]
        });

        var builder = CreateBuilder(parser);

        var changedModules = ChangedModules(
            (factoriesDirAbs, new[] { depFactoryAbs })
        );

        var merged = await builder.GetGraphAsync(changedModules, lastSavedGraph);

        // Find by either absolute or normalised path; the graph should resolve it.
        var leaf = RequireLeaf(merged, depFactoryAbs);

        Assert.Contains("New.Dep", leaf.GetDependencies().Keys);
        Assert.DoesNotContain("Infra", leaf.GetDependencies().Keys);
    }

    [Fact]
    public async Task Merge_RetainsUnchangedSubtrees_FromLastSaved()
    {
        SetupMockProject();

        var optionsAbs = _fs.File("Domain/Models/Records/Options.cs", "/* */");
        var recordsDirAbs = Path.Combine(_fs.Root, "Domain", "Models", "Records");

        var lastSavedGraph = MakeDependencyGraph(_fs.Root);

        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [optionsAbs] = ["Changed.Dep"]
        });

        var builder = CreateBuilder(parser);

        var changedModules = ChangedModules(
            (recordsDirAbs, new[] { optionsAbs })
        );

        var merged = await builder.GetGraphAsync(changedModules, lastSavedGraph);

        // Something not in the changed set should still exist.
        Assert.True(merged.ContainsPath("./Domain/Factories/RendererFactory.cs"));
        Assert.True(merged.ContainsPath("./Domain/Models/DependencyGraph.cs"));

        // And the changed file should exist with the changed dep.
        var optionsLeaf = RequireLeaf(merged, optionsAbs);
        Assert.Contains("Changed.Dep", optionsLeaf.GetDependencies().Keys);
    }

    [Fact]
    public async Task Merge_AddsNewFiles_ThatDidNotExistInLastSaved()
    {
        SetupMockProject();

        var newAbs = _fs.File("Domain/Utils/NewUtil.cs", "/* */");
        var utilsDirAbs = Path.Combine(_fs.Root, "Domain", "Utils");

        var lastSavedGraph = MakeDependencyGraph(_fs.Root);

        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [newAbs] = ["Some.Dep"]
        });

        var builder = CreateBuilder(parser);

        var changedModules = ChangedModules(
            (utilsDirAbs, new[] { newAbs })
        );

        var merged = await builder.GetGraphAsync(changedModules, lastSavedGraph);

        Assert.True(merged.ContainsPath(newAbs));

        var newLeaf = RequireLeaf(merged, newAbs);
        Assert.Contains("Some.Dep", newLeaf.GetDependencies().Keys);
    }

    [Fact]
    public async Task Cancellation_StopsParsing_AndThrows()
    {
        SetupMockProject();

        var cs = _fs.File("Domain/Factories/Cancellable.cs", "/* */");
        var moduleDir = Path.Combine(_fs.Root, "Domain", "Factories");

        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [cs] = ["Dep"]
        });

        var builder = CreateBuilder(parser);

        var changedModules = ChangedModules(
            (moduleDir, new[] { cs })
        );

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            builder.GetGraphAsync(changedModules, null, cts.Token));
    }

    [Fact]
    public async Task BuildGraph_CreatesCorrectParentChain_ForDeepDirectory()
    {
        SetupMockProject();

        var depGraph = _fs.File("Domain/Models/DependencyGraph.cs", "/* */");

        var domainDir = Path.Combine(_fs.Root, "Domain");
        var modelsDir = Path.Combine(domainDir, "Models");

        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [depGraph] = ["X"]
        });

        var builder = CreateBuilder(parser);

        var changedModules = ChangedModules(
            (_fs.Root, new[] { domainDir }),
            (domainDir, new[] { modelsDir }),
            (modelsDir, new[] { depGraph })
        );

        var graph = await builder.GetGraphAsync(changedModules, null);

        var root = RequireNode(graph, _fs.Root);
        var domain = RequireNode(graph, domainDir);
        var models = RequireNode(graph, modelsDir);

        Assert.Contains(domain, root.GetChildren());
        Assert.Contains(models, domain.GetChildren());
    }

    [Fact]
    public async Task BuildGraph_DoesNotDuplicateDirectoryNodes_WhenPathsVary()
    {
        SetupMockProject();

        var domainAbs = Path.Combine(_fs.Root, "Domain");
        var domainAbsTrailing = domainAbs + Path.DirectorySeparatorChar;
        var domainRel = "Domain";
        var domainDotRel = "./Domain";

        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));
        var builder = CreateBuilder(parser);

        var changedModules = new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [_fs.Root] = [domainAbs],
            [domainAbs] = [domainAbsTrailing, domainRel, domainDotRel]
        };

        var graph = await builder.GetGraphAsync(changedModules, null);

        var n1 = RequireNode(graph, domainAbs);
        var n2 = RequireNode(graph, domainAbsTrailing);
        var n3 = RequireNode(graph, domainRel);
        var n4 = RequireNode(graph, domainDotRel);

        Assert.True(ReferenceEquals(n1, n2));
        Assert.True(ReferenceEquals(n1, n3));
        Assert.True(ReferenceEquals(n1, n4));

        var root = RequireNode(graph, _fs.Root);
        Assert.Single(root.GetChildren(), c => string.Equals(c.Path, n1.Path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Merge_PrefersChangedNodeStructure_AndDependencies()
    {
        SetupMockProject();

        var modelsDir = Path.Combine(_fs.Root, "Domain", "Models");
        var depGraphAbs = _fs.File("Domain/Models/DependencyGraph.cs", "/* */");

        var lastSaved = MakeDependencyGraph(_fs.Root);

        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [depGraphAbs] = ["Changed.Node.Dep"]
        });

        var builder = CreateBuilder(parser);

        var changedModules = ChangedModules(
            (modelsDir, new[] { depGraphAbs })
        );

        var merged = await builder.GetGraphAsync(changedModules, lastSaved);

        var modelsNode = RequireNode(merged, modelsDir);

        Assert.Contains("Changed.Node.Dep", modelsNode.GetDependencies().Keys);
        Assert.DoesNotContain("Domain.Utils", modelsNode.GetDependencies().Keys);
    }

    [Fact]
    public async Task Merge_WhenTypeConflicts_IncomingReplacesExisting()
    {
        SetupMockProject();

        var rootPath = _fs.Root;
        var lastSaved = MakeDependencyGraph(rootPath);

        var bogusLeaf = TestGraphs.Leaf(rootPath, "Models", "./Domain/Models/", "Old.Dep");
        var domain = (DependencyGraphNode)lastSaved.FindByPath("./Domain/")!;
        domain.ReplaceChild(bogusLeaf);

        var modelsDirAbs = Path.Combine(_fs.Root, "Domain", "Models");
        var depGraphAbs = _fs.File("Domain/Models/DependencyGraph.cs", "/* */");

        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [depGraphAbs] = ["New.Dep"]
        });

        var builder = CreateBuilder(parser);

        var changedModules = ChangedModules(
            (modelsDirAbs, new[] { depGraphAbs })
        );

        var merged = await builder.GetGraphAsync(changedModules, lastSaved);

        var models = merged.FindByPath(modelsDirAbs);
        Assert.NotNull(models);
        Assert.IsType<DependencyGraphNode>(models);
    }

    [Fact]
    public async Task BuildGraph_IgnoresNullOrWhitespaceItems_InContents()
    {
        SetupMockProject();

        var moduleDir = Path.Combine(_fs.Root, "Domain");
        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));
        var builder = CreateBuilder(parser);

        var changedModules = new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [moduleDir] = ["", "   ", "\t", "\n"]
        };

        var graph = await builder.GetGraphAsync(changedModules, null);

        Assert.Empty(parser.Calls);
        Assert.NotNull(graph);
    }

    private static IReadOnlyDictionary<string, IReadOnlyCollection<string>> SnapshotPathsAndDeps(DependencyGraph root)
    {
        var result = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<DependencyGraph>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            result[current.Path] = [.. current.GetDependencies().Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)];

            foreach (var child in current.GetChildren())
                stack.Push(child);
        }

        return result;
    }

    [Fact]
    public async Task BuildGraph_IsDeterministic_ForSameInputs()
    {
        SetupMockProject();

        var f1 = _fs.File("Domain/Factories/A.cs", "/* */");
        var factoriesDir = Path.Combine(_fs.Root, "Domain", "Factories");

        var changedModules = ChangedModules(
            (factoriesDir, new[] { f1 })
        );

        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [f1] = ["X", "Y"]
        });

        var builder = CreateBuilder(parser);

        var g1 = await builder.GetGraphAsync(changedModules, null);
        var g2 = await builder.GetGraphAsync(changedModules, null);

        var s1 = SnapshotPathsAndDeps(g1);
        var s2 = SnapshotPathsAndDeps(g2);

        Assert.Equal(s1.Count, s2.Count);
        foreach (var (path, deps) in s1)
        {
            Assert.True(s2.ContainsKey(path));
            Assert.Equal(deps, s2[path]);
        }
    }
}
