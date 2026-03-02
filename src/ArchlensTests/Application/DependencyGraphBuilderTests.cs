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

    private BaseOptions MakeOptions() => new(
        ProjectRoot: _fs.Root,
        ProjectName: "Archlens",
        FullRootPath: _fs.Root
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

    private ProjectChanges CreateProjectChanges(IReadOnlyDictionary<RelativePath, IReadOnlyList<RelativePath>> changedFilesByDirectory,
                                                IReadOnlyList<RelativePath> deletedFiles,
                                                IReadOnlyList<RelativePath> deletedDirectories) =>
        new(changedFilesByDirectory, deletedFiles, deletedDirectories);

    private DependencyGraphBuilder CreateBuilder(IReadOnlyList<IDependencyParser> parser) =>
        new(parser, MakeOptions());

    private static ProjectItem RequireItem(ProjectDependencyGraph graph, RelativePath path)
    {
        var found = graph.GetProjectItem(path);
        Assert.NotNull(found);
        var node = Assert.IsType<ProjectItem>(found);
        return node;
    }

    private static IReadOnlyDictionary<RelativePath, IReadOnlyList<RelativePath>> ChangedModules(params (RelativePath moduleAbs, IReadOnlyList<RelativePath> contentsAbs)[] entries)
    {
        var dict = new Dictionary<RelativePath, IReadOnlyList<RelativePath>>();
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
        var builder = CreateBuilder([parser]);

        var changes = CreateProjectChanges(new Dictionary<RelativePath, IReadOnlyList<RelativePath>>(), [], []);

        var graph = await builder.GetGraphAsync(
            changes: changes,
            lastSavedDependencyGraph: null);

        Assert.Empty(graph.ProjectItems);
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

        var depFactoryFile  = RelativePath.File(_fs.Root, "./Domain/Factories/DependencyParserFactory.cs");
        var rendFactoryFile = RelativePath.File(_fs.Root, "./Domain/Factories/RendererFactory.cs");
        var optionsFile     = RelativePath.File(_fs.Root, "./Domain/Models/Records/Options.cs");
        var depGraphFile    = RelativePath.File(_fs.Root, "./Domain/Models/DependencyGraph.cs");

        var rootDir         = RelativePath.Directory(_fs.Root, _fs.Root);
        var domainDir       = RelativePath.Directory(_fs.Root, "./Domain");
        var factoriesDir    = RelativePath.Directory(_fs.Root, "./Domain/Factories");
        var modelsDir       = RelativePath.Directory(_fs.Root, "./Domain/Models");
        var recordsDir      = RelativePath.Directory(_fs.Root, "./Domain/Models/Records");
        var enumsDir        = RelativePath.Directory(_fs.Root, "./Domain/Models/Enums");
        var interfacesDir   = RelativePath.Directory(_fs.Root, "./Domain/Interfaces");
        var utilsDir        = RelativePath.Directory(_fs.Root, "./Domain/Utils");

        var changedModules = new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [rootDir] = [domainDir],
            [domainDir] = [factoriesDir, interfacesDir, modelsDir, utilsDir],
            [factoriesDir] = [depFactoryFile, rendFactoryFile],
            [modelsDir] = [enumsDir, recordsDir, depGraphFile],
            [recordsDir] = [optionsFile]
        };

        var changes = CreateProjectChanges(changedModules, [], []);

        var parseMap = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [depFactory] = ["Domain.Interfaces", "Domain.Models.Enums", "Domain.Models.Records", "Infra"],
            [rendFactory] = ["Domain.Interfaces", "Domain.Models.Enums", "Infra"],
            [options] = ["Domain.Models.Enums"],
            [depGraph] = ["Domain.Utils"]
        };

        var parser = new DependencyParserSpy(parseMap);
        var builder = CreateBuilder([parser]);

        var graph = await builder.GetGraphAsync(changes, null);

        var root        = RequireItem(graph, rootDir);
        var domain      = RequireItem(graph, domainDir);
        var factories   = RequireItem(graph, factoriesDir);
        var models      = RequireItem(graph, modelsDir);
        RequireItem(graph, depFactoryFile);
        RequireItem(graph, rendFactoryFile);
        RequireItem(graph, optionsFile);
        RequireItem(graph, depGraphFile);

        Assert.Same(root, graph);
        Assert.Contains(domain.Path, graph.ChildrenOf(rootDir));
        Assert.Contains(factories.Path, graph.ChildrenOf(domainDir));
        Assert.Contains(models.Path, graph.ChildrenOf(domainDir));

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

        var cs = _fs.File("Domain/Factories/Duplicate.cs", "/* */");

        var factoryDirPath = RelativePath.Directory(_fs.Root, "./Domain/Factories/");
        var csPath = RelativePath.File(_fs.Root, "./Domain/Factories/Duplicate.cs");

        var parseMap = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [cs] = ["Dep"]
        };

        var parser = new DependencyParserSpy(parseMap);
        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (factoryDirPath, new[] { csPath, csPath, csPath })
        );

        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, null);

        var dirItem = RequireItem(graph, factoryDirPath);
        var dirItemChildren = graph.ChildrenOf(dirItem.Path);
        Assert.Single(dirItemChildren);

        var existing = RequireItem(graph, csPath);
        Assert.Same(existing, graph.GetProjectItem(csPath));
        Assert.Equal(existing.Path, dirItemChildren[0]);
    }

    [Fact]
    public async Task ContainsPath_AcceptsRelativeVariants_ForSameFile()
    {
        SetupMockProject();

        var cs = _fs.File("Domain/Factories/Variant.cs", "/* */");

        var factoryDirPath = RelativePath.Directory(_fs.Root, "./Domain/Factories/");
        var csPath = RelativePath.File(_fs.Root, "./Domain/Factories/Variant.cs");

        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [cs] = []
        });

        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (factoryDirPath, new[] { csPath })
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, null);

        Assert.True(graph.ContainsProjectItem(csPath));

        var rel1 = RelativePath.File(_fs.Root, "./Domain/Factories/Variant.cs");
        var rel2 = RelativePath.File(_fs.Root, "Domain/Factories/Variant.cs");
        Assert.True(graph.ContainsProjectItem(rel1));
        Assert.True(graph.ContainsProjectItem(rel2));
    }

    [Fact]
    public async Task BuildGraph_CreatesNodesForDirectoriesMentionedInContents_EvenIfNotKeys()
    {
        SetupMockProject();
        var rootDirPath = RelativePath.Directory(_fs.Root, _fs.Root);
        var domainDirPath = RelativePath.Directory(_fs.Root, "./Domain/");
        var modelsDirPath = RelativePath.Directory(_fs.Root, "./Domain/Models/");

        // Note: modelsDir exists (SetupMockProject), but we do NOT include it as a module key.
        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));
        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (rootDirPath, new[] { domainDirPath }),
            (domainDirPath, new[] { modelsDirPath }) // modelsDir only appears as content
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, null);

        Assert.True(graph.ContainsProjectItem(domainDirPath));
        Assert.True(graph.ContainsProjectItem(modelsDirPath));

        var _ = RequireItem(graph, domainDirPath);
        var modelsNode = RequireItem(graph, modelsDirPath);
        Assert.Contains(modelsNode.Path, graph.ChildrenOf(domainDirPath));
    }

    private static ProjectDependencyGraph MakeDependencyGraph(string rootPath)
    {
        var graph = new ProjectDependencyGraph(rootPath);

        var root        = RelativePath.Directory(rootPath, rootPath);
        var application = RelativePath.Directory(rootPath, "./Application/");
        var infra       = RelativePath.Directory(rootPath, "./Infra/");
        var domain      = RelativePath.Directory(rootPath, "./Domain/");
        var interfaces  = RelativePath.Directory(rootPath, "./Domain/Interfaces");
        var factory     = RelativePath.Directory(rootPath, "./Domain/Factories/");
        var models      = RelativePath.Directory(rootPath, "./Domain/Models/");
        var records     = RelativePath.Directory(rootPath, "./Domain/Models/Records/");
        var enums       = RelativePath.Directory(rootPath, "./Domain/Models/Enums/");
        var utils       = RelativePath.Directory(rootPath, "./Domain/Utils/");

        graph.AddChild(root, application);
        graph.AddChild(root, domain);
        graph.AddChild(domain, factory);
        graph.AddChild(domain, models);
        graph.AddChild(domain, utils);
        graph.AddChild(models, records);
        graph.AddChild(models, enums);

        var changeDetector          = RelativePath.File(rootPath, "./Application/ChangeDetector.cs");
        var dependencyParserFactory = RelativePath.File(rootPath, "./Domain/Factories/DependencyParserFactory.cs");
        var rendererFactory         = RelativePath.File(rootPath, "./Domain/Factories/RendererFactory.cs");
        var options                 = RelativePath.File(rootPath, "./Domain/Models/Records/Options.cs");
        var dependencyGraph         = RelativePath.File(rootPath, "./Domain/Models/DependencyGraph.cs");

        var dependencies = new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [changeDetector] = [models, records, utils],
            [dependencyParserFactory] = [interfaces, enums, records, infra],
            [rendererFactory] = [interfaces, enums, infra],
            [options] = [enums],
            [dependencyGraph] = [utils]
        };

        foreach (var (source, targets) in dependencies)
            graph.AddDependencies(source, targets);

        return graph;
    }

    [Fact]
    public async Task Merge_PrefersChangedLeafDependencies_OverLastSaved()
    {
        SetupMockProject();

        // The file exists on disk so the builder can parse it.
        var depFactory = _fs.File("Domain/Factories/DependencyParserFactory.cs", "/* */");
        
        var depFactoryDirPath = RelativePath.Directory(_fs.Root, "./Domain/Factories/");
        var depFactoryFilePath = RelativePath.File(_fs.Root, "./Domain/Factories/DependencyParserFactory.cs");

        var lastSavedGraph = MakeDependencyGraph(_fs.Root);

        // Change the dependencies for the same logical file.
        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [depFactory] = ["New.Dep", "Domain.Models.Enums"]
        });

        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (depFactoryDirPath, new[] { depFactoryFilePath })
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, lastSavedGraph);

        // Find by either absolute or normalised path; the graph should resolve it.
        var depFactoryProjectItem = RequireItem(graph, depFactoryFilePath);

        var newDepPath = RelativePath.Directory(_fs.Root, "./New/Dep/");
        var enumsPath = RelativePath.Directory(_fs.Root, "./Domain/Models/Enums");
        var infraPath = RelativePath.Directory(_fs.Root, "./Infra/");

        Assert.Contains(newDepPath, graph.DependenciesFrom(depFactoryProjectItem.Path).Keys);
        Assert.Contains(enumsPath, graph.DependenciesFrom(depFactoryProjectItem.Path).Keys);
        Assert.DoesNotContain(infraPath, graph.DependenciesFrom(depFactoryProjectItem.Path).Keys);
    }

    [Fact]
    public async Task Merge_RetainsUnchangedSubtrees_FromLastSaved()
    {
        SetupMockProject();

        var optionsAbs = _fs.File("Domain/Models/Records/Options.cs", "/* */");

        var recordDirPath = RelativePath.Directory(_fs.Root, "./Domain/Models/Records/");
        var optionsPath = RelativePath.File(_fs.Root, "./Domain/Models/Records/Options.cs");

        var lastSavedGraph = MakeDependencyGraph(_fs.Root);

        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [optionsAbs] = ["Changed.Dep"]
        });

        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (recordDirPath, new[] { optionsPath })
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, lastSavedGraph);

        // Something not in the changed set should still exist.
        var renderFactoryPath = RelativePath.File(_fs.Root, "./Domain/Factories/RendererFactory.cs");
        var dependencyGraphPath = RelativePath.File(_fs.Root, "./Domain/Models/DependencyGraph.cs");
        Assert.True(graph.ContainsProjectItem(renderFactoryPath));
        Assert.True(graph.ContainsProjectItem(dependencyGraphPath));

        // And the changed file should exist with the changed dep.
        var changedDep = RelativePath.Directory(_fs.Root, "./Changed/Dep/");
        var optionsItem = RequireItem(graph, optionsPath);
        Assert.Contains(changedDep, graph.DependenciesFrom(optionsItem.Path).Keys);
    }

    [Fact]
    public async Task Merge_AddsNewFiles_ThatDidNotExistInLastSaved()
    {
        SetupMockProject();

        var newAbs = _fs.File("Domain/Utils/NewUtil.cs", "/* */");

        var utilsDirPath = RelativePath.Directory(_fs.Root, "./Domain/Utils/");
        var newPath = RelativePath.File(_fs.Root, "./Domain/Utils/NewUtil.cs");

        var lastSavedGraph = MakeDependencyGraph(_fs.Root);

        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [newAbs] = ["Some.Dep"]
        });

        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (utilsDirPath, new[] { newPath })
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, lastSavedGraph);

        Assert.True(graph.ContainsProjectItem(newPath));

        var someDepDirPath = RelativePath.Directory(_fs.Root, "./Some/Dep/");
        var newLeaf = RequireItem(graph, newPath);
        Assert.Contains(someDepDirPath, graph.DependenciesFrom(newLeaf.Path).Keys);
    }

    [Fact]
    public async Task Cancellation_StopsParsing_AndThrows()
    {
        SetupMockProject();

        var cs = _fs.File("Domain/Factories/Cancellable.cs", "/* */");

        var factoryDirPath = RelativePath.Directory(_fs.Root, "./Domain/Factories/");
        var csPath = RelativePath.File(_fs.Root, "./Domain/Factories/Variant.cs");

        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [cs] = ["Dep"]
        });

        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (factoryDirPath, new[] { csPath })
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            builder.GetGraphAsync(changes, null, cts.Token));
    }

    [Fact]
    public async Task BuildGraph_CreatesCorrectParentChain_ForDeepDirectory()
    {
        SetupMockProject();

        var depGraph = _fs.File("Domain/Models/DependencyGraph.cs", "/* */");

        var rootPath = RelativePath.Directory(_fs.Root, _fs.Root);
        var domainDirPath = RelativePath.Directory(_fs.Root, "./Domain/");
        var modelsDirPath = RelativePath.Directory(_fs.Root, "./Domain/Models/");

        var depGraphPath = RelativePath.File(_fs.Root, "./Domain/Models/DependencyGraph.cs");

        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [depGraph] = ["X"]
        });

        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (rootPath, new[] { domainDirPath }),
            (domainDirPath, new[] { modelsDirPath }),
            (modelsDirPath, new[] { depGraphPath })
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, null);

        var _   = RequireItem(graph, rootPath);
        var domain = RequireItem(graph, domainDirPath);
        var models = RequireItem(graph, modelsDirPath);

        Assert.Contains(domain.Path, graph.ChildrenOf(rootPath));
        Assert.Contains(models.Path, graph.ChildrenOf(domainDirPath));
    }

    [Fact]
    public async Task BuildGraph_DoesNotDuplicateDirectoryNodes_WhenPathsVary()
    {
        SetupMockProject();

        var srcPath = RelativePath.Directory(_fs.Root, _fs.Root);

        var domain1 = RelativePath.Directory(_fs.Root, Path.Combine(_fs.Root, "Domain"));
        var domain2 = RelativePath.Directory(_fs.Root, $"{domain1}{Path.DirectorySeparatorChar}");
        var domain3 = RelativePath.Directory(_fs.Root, Path.Combine(_fs.Root, "./Domain"));
        var domain4 = RelativePath.Directory(_fs.Root, "./Domain");
        var domain5 = RelativePath.Directory(_fs.Root, "Domain");

        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));
        var builder = CreateBuilder([parser]);

        var changedModules = new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [srcPath] = [domain1],
            [domain1] = [domain2, domain3, domain4]
        };
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, null);

        var n1 = RequireItem(graph, domain1);
        var n2 = RequireItem(graph, domain2);
        var n3 = RequireItem(graph, domain3);
        var n4 = RequireItem(graph, domain4);

        Assert.True(ReferenceEquals(n1, n2));
        Assert.True(ReferenceEquals(n1, n3));
        Assert.True(ReferenceEquals(n1, n4));

        var _ = RequireItem(graph, srcPath);
        Assert.Single(graph.ProjectItems);
    }

    [Fact]
    public async Task Merge_PrefersChangedNodeStructure_AndDependencies()
    {
        SetupMockProject();

        var depGraphAbs = _fs.File("Domain/Models/DependencyGraph.cs", "/* */");

        var modelsDirPath = RelativePath.Directory(_fs.Root, "./Domain/Models/");
        var depGraphPath = RelativePath.File(_fs.Root, "./Domain/Models/DependencyGraph.cs");

        var lastSaved = MakeDependencyGraph(_fs.Root);

        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [depGraphAbs] = ["Changed.Node.Dep"]
        });

        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (modelsDirPath, new[] { depGraphPath })
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, lastSaved);

        var modelsItem = RequireItem(graph, modelsDirPath);

        var changedFilePath = RelativePath.Directory(_fs.Root, "./Changed/Node/Dep/");
        var domainUtilPath = RelativePath.Directory(_fs.Root, "./Domain/Utils/");

        Assert.Contains(changedFilePath, graph.DependenciesFrom(modelsItem.Path).Keys);
        Assert.DoesNotContain(domainUtilPath, graph.DependenciesFrom(modelsItem.Path).Keys);
    }

    [Fact]
    public async Task Merge_WhenTypeConflicts_IncomingReplacesExisting()
    {
        SetupMockProject();

        var rootPath = _fs.Root;
        var lastSaved = MakeDependencyGraph(rootPath);

        var bogusItem = TestGraphs.AddProjectItem(rootPath, "Models", "./Domain/Models/", "Old.Dep");

        var domainPath = RelativePath.Directory(rootPath, "./Domain/");
        var domain = lastSaved.GetProjectItem(domainPath);
        lastSaved.AddChild(domain, bogusItem);

        var modelsDirPath = RelativePath.Directory(rootPath, "./Domain/Models/");

        var depGraph = _fs.File("Domain/Models/DependencyGraph.cs", "/* */");
        var depGraphPath = RelativePath.File(rootPath, "Domain/Models/DependencyGraph.cs");

        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [depGraph] = ["New.Dep"]
        });

        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (modelsDirPath, new[] { depGraphPath })
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, lastSaved);

        var models = graph.GetProjectItem(modelsDirPath);
        Assert.NotNull(models);
        Assert.IsType<ProjectItem>(models);
    }

    [Fact]
    public async Task BuildGraph_IgnoresNullOrWhitespaceItems_InContents()
    {
        SetupMockProject();

        var modelsDirPath = RelativePath.Directory(_fs.Root, "./Domain/");

        var emptyPath = RelativePath.Directory(_fs.Root, "");
        var spacesPath = RelativePath.Directory(_fs.Root, "   ");
        var tabsPath = RelativePath.Directory(_fs.Root, "\t");
        var newlinePath = RelativePath.Directory(_fs.Root, "\n");

        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));
        var builder = CreateBuilder([parser]);

        var changedModules = new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [modelsDirPath] = [emptyPath, spacesPath, tabsPath, newlinePath]
        };
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, null);

        Assert.Empty(parser.Calls);
        Assert.NotNull(graph);
    }

    private static IReadOnlyDictionary<RelativePath, IReadOnlyCollection<RelativePath>> SnapshotPathsAndDeps(ProjectDependencyGraph graph)
    {
        var result = new Dictionary<RelativePath, IReadOnlyCollection<RelativePath>>();
        var stack = new Stack<ProjectItem>();

        foreach (var item in graph.ProjectItems)
        {
            result[item.Key] = [.. graph.DependenciesFrom(item.Key).Keys.OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase)];
        }

        return result;
    }

    [Fact]
    public async Task BuildGraph_IsDeterministic_ForSameInputs()
    {
        SetupMockProject();

        var f1 = _fs.File("Domain/Factories/A.cs", "/* */");
        var f1Path = RelativePath.File(_fs.Root, "./Domain/Factories/A.cs");
        var factoriesDirPath = RelativePath.Directory(_fs.Root, "./Domain/Factories/");

        var changedModules = ChangedModules(
            (factoriesDirPath, [f1Path])
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        var parser = new DependencyParserSpy(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [f1] = ["X", "Y"]
        });

        var builder = CreateBuilder([parser]);

        var g1 = await builder.GetGraphAsync(changes, null);
        var g2 = await builder.GetGraphAsync(changes, null);

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
