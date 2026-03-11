using System.Collections.Concurrent;
using System.Text;
using Archlens.Application;
using Archlens.Domain.Interfaces;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
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

    private sealed class DependencyParserSpy(string root, IReadOnlyDictionary<RelativePath, IReadOnlyList<RelativePath>> _map) : IDependencyParser
    {
        public ConcurrentBag<RelativePath> Calls { get; } = [];

        public Task<IReadOnlyList<RelativePath>> ParseFileDependencies(string absPath, CancellationToken ct = default)
        {
            var isDirectory = absPath.EndsWith('/');
            var path = isDirectory ? RelativePath.Directory(root, absPath) : RelativePath.File(root, absPath);
            Calls.Add(path);
            ct.ThrowIfCancellationRequested();

            if (_map.TryGetValue(path, out var deps))
                return Task.FromResult(deps);

            return Task.FromResult((IReadOnlyList<RelativePath>)[]);
        }
    }

    [Fact]
    public async Task BuildGraph_BuildsExpectedTreeStructure_ForHappyPath()
    {
        SetupMockProject();

        _fs.File("Domain/Factories/DependencyParserFactory.cs", "/* */");
        _fs.File("Domain/Factories/RendererFactory.cs", "/* */");
        _fs.File("Domain/Models/Records/Options.cs", "/* */");
        _fs.File("Domain/Models/DependencyGraph.cs", "/* */");

        var depFactoryFile = RelativePath.File(_fs.Root, "./Domain/Factories/DependencyParserFactory.cs");
        var rendFactoryFile = RelativePath.File(_fs.Root, "./Domain/Factories/RendererFactory.cs");
        var optionsFile = RelativePath.File(_fs.Root, "./Domain/Models/Records/Options.cs");
        var depGraphFile = RelativePath.File(_fs.Root, "./Domain/Models/DependencyGraph.cs");

        var rootDir = RelativePath.Directory(_fs.Root, _fs.Root);
        var domainDir = RelativePath.Directory(_fs.Root, "./Domain");
        var factoriesDir = RelativePath.Directory(_fs.Root, "./Domain/Factories");
        var modelsDir = RelativePath.Directory(_fs.Root, "./Domain/Models");
        var recordsDir = RelativePath.Directory(_fs.Root, "./Domain/Models/Records");
        var enumsDir = RelativePath.Directory(_fs.Root, "./Domain/Models/Enums");
        var interfacesDir = RelativePath.Directory(_fs.Root, "./Domain/Interfaces");
        var utilsDir = RelativePath.Directory(_fs.Root, "./Domain/Utils");
        var infraDir = RelativePath.Directory(_fs.Root, "./Infra");

        var changedModules = new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [rootDir] = [domainDir],
            [domainDir] = [factoriesDir, interfacesDir, modelsDir, utilsDir],
            [factoriesDir] = [depFactoryFile, rendFactoryFile],
            [modelsDir] = [enumsDir, recordsDir, depGraphFile],
            [recordsDir] = [optionsFile],
        };

        var changes = CreateProjectChanges(changedModules, [], []);

        var parseMap = new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [depFactoryFile] = [interfacesDir, enumsDir, recordsDir, infraDir],
            [rendFactoryFile] = [interfacesDir, enumsDir, infraDir],
            [optionsFile] = [enumsDir],
            [depGraphFile] = [utilsDir]
        };

        var parser = new DependencyParserSpy(_fs.Root, parseMap);
        var builder = CreateBuilder([parser]);

        var graph = await builder.GetGraphAsync(changes, null);

        var root = RequireItem(graph, rootDir);
        var domain = RequireItem(graph, domainDir);
        var factories = RequireItem(graph, factoriesDir);
        var models = RequireItem(graph, modelsDir);
        var depFactItem = RequireItem(graph, depFactoryFile);
        var rendFactItem = RequireItem(graph, rendFactoryFile);
        var optionsItem = RequireItem(graph, optionsFile);
        var depGraphItem = RequireItem(graph, depGraphFile);

        Assert.Contains(domain.Path, graph.ChildrenOf(rootDir));
        Assert.Contains(factories.Path, graph.ChildrenOf(domainDir));
        Assert.Contains(models.Path, graph.ChildrenOf(domainDir));

        Assert.Equal(4, parser.Calls.Count);
        Assert.Contains(depFactItem.Path, parser.Calls);
        Assert.Contains(rendFactItem.Path, parser.Calls);
        Assert.Contains(optionsItem.Path, parser.Calls);
        Assert.Contains(depGraphItem.Path, parser.Calls);
    }

    [Fact]
    public async Task BuildGraph_DeduplicatesDuplicateFileEntries()
    {
        SetupMockProject();

        _fs.File("Domain/Factories/Duplicate.cs", "/* */");

        var factoryDirPath = RelativePath.Directory(_fs.Root, "./Domain/Factories/");
        var csPath = RelativePath.File(_fs.Root, "./Domain/Factories/Duplicate.cs");
        var depPath = RelativePath.Directory(_fs.Root, "./Dep/");

        var parseMap = new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [csPath] = [depPath]
        };

        var parser = new DependencyParserSpy(_fs.Root, parseMap);
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

        _fs.File("Domain/Factories/Variant.cs", "/* */");

        var factoryDirPath = RelativePath.Directory(_fs.Root, "./Domain/Factories/");
        var csPath = RelativePath.File(_fs.Root, "./Domain/Factories/Variant.cs");

        var parser = new DependencyParserSpy(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [csPath] = []
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

        var parser = new DependencyParserSpy(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>());
        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (rootDirPath, new[] { domainDirPath }),
            (domainDirPath, new[] { modelsDirPath })
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, null);

        Assert.True(graph.ContainsProjectItem(domainDirPath));
        Assert.True(graph.ContainsProjectItem(modelsDirPath));

        var _ = RequireItem(graph, domainDirPath);
        var modelsNode = RequireItem(graph, modelsDirPath);
        Assert.Contains(modelsNode.Path, graph.ChildrenOf(domainDirPath));
    }

    [Fact]
    public async Task Merge_PrefersChangedLeafDependencies_OverLastSaved()
    {
        SetupMockProject();

        _fs.File("Domain/Factories/DependencyParserFactory.cs", "/* */");

        var depFactoryDirPath = RelativePath.Directory(_fs.Root, "./Domain/Factories/");
        var depFactoryFilePath = RelativePath.File(_fs.Root, "./Domain/Factories/DependencyParserFactory.cs");

        var newDepPath = RelativePath.Directory(_fs.Root, "./New/Dep/");
        var enumsPath = RelativePath.Directory(_fs.Root, "./Domain/Models/Enums/");

        var lastSavedGraph = TestDependencyGraph.MakeDependencyGraph(_fs.Root);

        var parser = new DependencyParserSpy(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [depFactoryFilePath] = [newDepPath, enumsPath]
        });

        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (depFactoryDirPath, new[] { depFactoryFilePath }),
            (newDepPath, [])
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, lastSavedGraph);

        var depFactoryProjectItem = RequireItem(graph, depFactoryFilePath);

        var infraPath = RelativePath.Directory(_fs.Root, "./Infra/");
        Assert.Contains(newDepPath, graph.DependenciesFrom(depFactoryProjectItem.Path).Keys);
        Assert.Contains(enumsPath, graph.DependenciesFrom(depFactoryProjectItem.Path).Keys);
        Assert.DoesNotContain(infraPath, graph.DependenciesFrom(depFactoryProjectItem.Path).Keys);
    }

    [Fact]
    public async Task Merge_RetainsUnchangedSubtrees_FromLastSaved()
    {
        SetupMockProject();
        _fs.File("Domain/Models/Records/Options.cs", "/* */");
        _fs.File("./Domain/Factories/RendererFactory.cs", "/* */");

        var root = _fs.Root;
        var recordDirPath = RelativePath.Directory(root, "./Domain/Models/Records/");
        var optionsPath = RelativePath.File(root, "./Domain/Models/Records/Options.cs");
        var changedDep = RelativePath.Directory(root, "./Changed/Dep/");

        var renderFactoryPath = RelativePath.File(root, "./Infra/Factories/RendererFactory.cs");
        var dependencyGraphPath = RelativePath.File(root, "./Domain/Models/DependencyGraph.cs");

        var lastSavedGraph = TestDependencyGraph.MakeDependencyGraph(root);

        var parser = new DependencyParserSpy(root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [optionsPath] = [changedDep]
        });

        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (recordDirPath, new[] { optionsPath })
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, lastSavedGraph);

        Assert.True(graph.ContainsProjectItem(renderFactoryPath));
        Assert.True(graph.ContainsProjectItem(dependencyGraphPath));

        var optionsItem = RequireItem(graph, optionsPath);
        Assert.Contains(changedDep, graph.DependenciesFrom(optionsItem.Path).Keys);
    }

    [Fact]
    public async Task Merge_AddsNewFiles_ThatDidNotExistInLastSaved()
    {
        SetupMockProject();

        _fs.File("Domain/Utils/NewUtil.cs", "/* */");

        var utilsDirPath = RelativePath.Directory(_fs.Root, "./Domain/Utils/");
        var newPath = RelativePath.File(_fs.Root, "./Domain/Utils/NewUtil.cs");

        var lastSavedGraph = TestDependencyGraph.MakeDependencyGraph(_fs.Root);

        var someDepDirPath = RelativePath.Directory(_fs.Root, "./Some/Dep/");
        var parser = new DependencyParserSpy(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [newPath] = [someDepDirPath]
        });

        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (utilsDirPath, new[] { newPath })
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, lastSavedGraph);

        Assert.True(graph.ContainsProjectItem(newPath));

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

        var newDepPath = RelativePath.Directory(_fs.Root, "./New/Dep/");
        var parser = new DependencyParserSpy(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [csPath] = [newDepPath]
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
        var xPath = RelativePath.File(_fs.Root, "./X/");

        var parser = new DependencyParserSpy(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [depGraphPath] = [xPath]
        });

        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (rootPath, new[] { domainDirPath }),
            (domainDirPath, new[] { modelsDirPath }),
            (modelsDirPath, new[] { depGraphPath })
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, null);

        var _ = RequireItem(graph, rootPath);
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

        var parser = new DependencyParserSpy(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>());
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
        Assert.Equal(changedModules.Count, graph.ProjectItems.Count);
    }

    [Fact]
    public async Task Merge_PrefersChangedNodeStructure_AndDependencies()
    {
        SetupMockProject();

        var _ = _fs.File("Domain/Models/DependencyGraph.cs", "/* */");

        var modelsDirPath = RelativePath.Directory(_fs.Root, "./Domain/Models/");
        var depGraphPath = RelativePath.File(_fs.Root, "./Domain/Models/DependencyGraph.cs");
        var changedFilePath = RelativePath.Directory(_fs.Root, "./Changed/Node/Dep/");
        var domainUtilPath = RelativePath.Directory(_fs.Root, "./Domain/Utils/");

        var lastSaved = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        lastSaved.AddDependency(depGraphPath, domainUtilPath, DependencyType.Uses);

        var parser = new DependencyParserSpy(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [depGraphPath] = [changedFilePath]
        });
        var builder = CreateBuilder([parser]);
        var changedModules = ChangedModules(
            (modelsDirPath, new[] { depGraphPath })
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, lastSaved);

        RequireItem(graph, modelsDirPath);
        RequireItem(graph, depGraphPath);
        Assert.Contains(changedFilePath, graph.DependenciesFrom(depGraphPath).Keys);
        Assert.DoesNotContain(domainUtilPath, graph.DependenciesFrom(depGraphPath).Keys);
    }

    [Fact]
    public async Task Merge_WhenTypeConflicts_IncomingReplacesExisting()
    {
        SetupMockProject();

        var rootPath = _fs.Root;
        var lastSaved = TestDependencyGraph.MakeDependencyGraph(rootPath);

        var oldFile = RelativePath.Directory(rootPath, "./Old/Dep/");
        var bogusPath = RelativePath.Directory(rootPath, "./Domain/Models/");
        var bogusItem = TestGraphs.AddProjectItem(lastSaved, bogusPath, ProjectItemType.Directory, [oldFile]);

        var domainPath = RelativePath.Directory(rootPath, "./Domain/");
        lastSaved.AddChild(domainPath, bogusItem);

        var modelsDirPath = RelativePath.Directory(rootPath, "./Domain/Models/");

        var depGraph = _fs.File("Domain/Models/DependencyGraph.cs", "/* */");
        var depGraphPath = RelativePath.File(rootPath, "Domain/Models/DependencyGraph.cs");
        var newDepPath = RelativePath.Directory(_fs.Root, "./New/Dep/");
        var parser = new DependencyParserSpy(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [depGraphPath] = [newDepPath]
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

        var parser = new DependencyParserSpy(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>());
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

        var xPath = RelativePath.File(_fs.Root, "./X/");
        var yPath = RelativePath.File(_fs.Root, "./Y/");
        var parser = new DependencyParserSpy(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [f1Path] = [xPath, yPath]
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

    private sealed class ThrowingParser(string toThrowOnAbsContains, Exception ex) : IDependencyParser
    {
        public List<string> AbsCalls { get; } = [];

        public Task<IReadOnlyList<RelativePath>> ParseFileDependencies(string absPath, CancellationToken ct = default)
        {
            AbsCalls.Add(absPath);
            if (absPath.Contains(toThrowOnAbsContains, StringComparison.OrdinalIgnoreCase))
                throw ex;
            return Task.FromResult((IReadOnlyList<RelativePath>)[]);
        }
    }

    private sealed class FixedMapParser(string root, IReadOnlyDictionary<RelativePath, IReadOnlyList<RelativePath>> map) : IDependencyParser
    {
        public List<RelativePath> Calls { get; } = [];

        public Task<IReadOnlyList<RelativePath>> ParseFileDependencies(string absPath, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var rel = RelativePath.File(root, absPath);
            Calls.Add(rel);

            if (map.TryGetValue(rel, out var deps))
                return Task.FromResult(deps);

            return Task.FromResult((IReadOnlyList<RelativePath>)[]);
        }
    }

    private sealed class BlockingUntilCancelledParser(string root) : IDependencyParser
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<RelativePath>> ParseFileDependencies(string absPath, CancellationToken ct = default)
        {
            _ = RelativePath.File(root, absPath);
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return [];
        }
    }

    [Fact]
    public async Task GetGraphAsync_AppliesDeletedFiles_RemovingItemsFromMergedGraph()
    {
        SetupMockProject();

        var lastSaved = TestDependencyGraph.MakeDependencyGraph(_fs.Root);

        var deleted = RelativePath.File(_fs.Root, "./Infra/Factories/RendererFactory.cs");
        Assert.True(lastSaved.ContainsProjectItem(deleted));

        var changes = CreateProjectChanges(
            changedFilesByDirectory: new Dictionary<RelativePath, IReadOnlyList<RelativePath>>(),
            deletedFiles: [deleted],
            deletedDirectories: []
        );

        var builder = CreateBuilder([new FixedMapParser(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>())]);

        var merged = await builder.GetGraphAsync(changes, lastSaved);

        Assert.False(merged.ContainsProjectItem(deleted));

        foreach (var item in merged.ProjectItems.Keys)
            Assert.DoesNotContain(deleted, merged.DependenciesFrom(item).Keys);
    }

    [Fact]
    public async Task GetGraphAsync_AppliesDeletedDirectories_RemovingSubtree()
    {
        SetupMockProject();

        var lastSaved = TestDependencyGraph.MakeDependencyGraph(_fs.Root);

        var deletedDir = RelativePath.Directory(_fs.Root, "./Domain/Models/");
        Assert.True(lastSaved.ContainsProjectItem(deletedDir));

        var deletedChild1 = RelativePath.Directory(_fs.Root, "./Domain/Models/Records/");
        var deletedChild2 = RelativePath.Directory(_fs.Root, "./Domain/Models/Enums/");
        var deletedLeaf1 = RelativePath.File(_fs.Root, "./Domain/Models/Records/Options.cs");
        var deletedLeaf2 = RelativePath.File(_fs.Root, "./Domain/Models/DependencyGraph.cs");

        var changes = CreateProjectChanges(
            changedFilesByDirectory: new Dictionary<RelativePath, IReadOnlyList<RelativePath>>(),
            deletedFiles: [],
            deletedDirectories: [deletedDir]
        );

        var builder = CreateBuilder([new FixedMapParser(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>())]);

        var merged = await builder.GetGraphAsync(changes, lastSaved);

        Assert.False(merged.ContainsProjectItem(deletedDir));

        Assert.False(merged.ContainsProjectItem(deletedChild1));
        Assert.False(merged.ContainsProjectItem(deletedChild2));
        Assert.False(merged.ContainsProjectItem(deletedLeaf1));
        Assert.False(merged.ContainsProjectItem(deletedLeaf2));
    }

    [Fact]
    public async Task BuildGraph_DoesNotCallParser_ForDirectoriesOnlyForFiles()
    {
        SetupMockProject();

        _fs.File("Domain/Utils/U.cs", "/* */");

        var domainDir = RelativePath.Directory(_fs.Root, "./Domain/");
        var utilsDir = RelativePath.Directory(_fs.Root, "./Domain/Utils/");
        var uFile = RelativePath.File(_fs.Root, "./Domain/Utils/U.cs");

        var parser = new FixedMapParser(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>
        {
            [uFile] = []
        });

        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (domainDir, new[] { utilsDir, utilsDir, utilsDir }),
            (utilsDir, new[] { uFile })
        );

        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, null);

        Assert.Single(parser.Calls);
        Assert.Equal(uFile, parser.Calls[0]);

        var domainChildren = graph.ChildrenOf(domainDir);
        Assert.Single(domainChildren.Where(x => x.Equals(utilsDir)));
    }

    [Fact]
    public async Task ParserException_IsLogged_AndProcessingContinues_ForOtherItems()
    {
        SetupMockProject();

        _fs.File("Domain/Factories/Bad.cs", "/* */");
        _fs.File("Domain/Factories/Good.cs", "/* */");

        var dir = RelativePath.Directory(_fs.Root, "./Domain/Factories/");
        var bad = RelativePath.File(_fs.Root, "./Domain/Factories/Bad.cs");
        var good = RelativePath.File(_fs.Root, "./Domain/Factories/Good.cs");

        var parser = new ThrowingParser("Bad.cs", new InvalidOperationException("Contains Bad.cs"));
        var builder = CreateBuilder([parser]);

        var changes = CreateProjectChanges(
            ChangedModules((dir, new[] { bad, good })),
            deletedFiles: [],
            deletedDirectories: []
        );

        var priorErr = Console.Error;
        var sw = new StringWriter(new StringBuilder());
        Console.SetError(sw);
        try
        {
            var graph = await builder.GetGraphAsync(changes, null);

            Assert.True(graph.ContainsProjectItem(good));
            Assert.False(graph.ContainsProjectItem(bad));
        }
        finally
        {
            Console.SetError(priorErr);
        }

        var err = sw.ToString();
        Assert.Contains("Error while processing", err, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Bad.cs", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ParserException_DoesNotWipeExistingNode_WhenMergingWithLastSaved()
    {
        SetupMockProject();

        var lastSaved = TestDependencyGraph.MakeDependencyGraph(_fs.Root);

        var depGraph = RelativePath.File(_fs.Root, "./Domain/Models/DependencyGraph.cs");
        var utilsDir = RelativePath.Directory(_fs.Root, "./Domain/Utils/");
        Assert.Contains(utilsDir, lastSaved.DependenciesFrom(depGraph).Keys);

        _fs.File("Domain/Models/DependencyGraph.cs", "/* */");

        var modelsDir = RelativePath.Directory(_fs.Root, "./Domain/Models/");
        var changes = CreateProjectChanges(
            ChangedModules((modelsDir, new[] { depGraph })),
            deletedFiles: [],
            deletedDirectories: []
        );

        var parser = new ThrowingParser("DependencyGraph.cs", new Exception("parse failed"));
        var builder = CreateBuilder([parser]);

        var merged = await builder.GetGraphAsync(changes, lastSaved);

        Assert.True(merged.ContainsProjectItem(depGraph));
        Assert.Contains(utilsDir, merged.DependenciesFrom(depGraph).Keys);
    }

    [Fact]
    public async Task OperationCanceledException_FromParser_IsNotSwallowed()
    {
        SetupMockProject();

        _fs.File("Domain/Utils/C.cs", "/* */");

        var utilsDir = RelativePath.Directory(_fs.Root, "./Domain/Utils/");
        var cFile = RelativePath.File(_fs.Root, "./Domain/Utils/C.cs");

        var blocking = new BlockingUntilCancelledParser(_fs.Root);
        var builder = CreateBuilder([blocking]);

        var changes = CreateProjectChanges(
            ChangedModules((utilsDir, new[] { cFile })),
            deletedFiles: [],
            deletedDirectories: []
        );

        using var cts = new CancellationTokenSource();

        var task = builder.GetGraphAsync(changes, null, cts.Token);

        await blocking.Started.Task;
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }

    [Fact]
    public async Task SkipsRootItem_WhenItAppearsAsAChildOfItself()
    {
        SetupMockProject();

        var rootDir = RelativePath.Directory(_fs.Root, _fs.Root);

        var parser = new FixedMapParser(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>());
        var builder = CreateBuilder([parser]);

        var changes = CreateProjectChanges(
            ChangedModules((rootDir, new[] { rootDir })),
            deletedFiles: [],
            deletedDirectories: []
        );

        var graph = await builder.GetGraphAsync(changes, null);

        Assert.True(graph.ContainsProjectItem(rootDir));
        Assert.DoesNotContain(rootDir, graph.ChildrenOf(rootDir));
        Assert.Empty(parser.Calls);
    }

    [Fact]
    public async Task MultipleParsers_ShouldAggregateDependencies()
    {
        SetupMockProject();

        _fs.File("Domain/Factories/Multi.cs", "/* */");

        var dir = RelativePath.Directory(_fs.Root, "./Domain/Factories/");
        var file = RelativePath.File(_fs.Root, "./Domain/Factories/Multi.cs");

        var depA = RelativePath.Directory(_fs.Root, "./Dep/A/");
        var depB = RelativePath.Directory(_fs.Root, "./Dep/B/");

        var p1 = new FixedMapParser(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>
        {
            [file] = [depA]
        });

        var p2 = new FixedMapParser(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>
        {
            [file] = [depB]
        });

        var builder = CreateBuilder([p1, p2]);

        var changes = CreateProjectChanges(
            ChangedModules((dir, new[] { file })),
            deletedFiles: [],
            deletedDirectories: []
        );

        var graph = await builder.GetGraphAsync(changes, null);

        Assert.Contains(depA, graph.DependenciesFrom(file).Keys);
        Assert.Contains(depB, graph.DependenciesFrom(file).Keys);
    }
}
