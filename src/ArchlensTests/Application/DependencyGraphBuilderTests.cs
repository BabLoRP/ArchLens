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
        Directory.CreateDirectory(Path.Combine(_fs.Root, "Shop"));

        Directory.CreateDirectory(Path.Combine(_fs.Root, "Inventory", "Suppliers"));
        Directory.CreateDirectory(Path.Combine(_fs.Root, "Inventory", "Ports"));
        Directory.CreateDirectory(Path.Combine(_fs.Root, "Inventory", "Stock", "Tags"));
        Directory.CreateDirectory(Path.Combine(_fs.Root, "Inventory", "Stock", "Labels"));
        Directory.CreateDirectory(Path.Combine(_fs.Root, "Inventory", "Tools"));
    }

    private static ProjectChanges CreateProjectChanges(IReadOnlyDictionary<RelativePath, IReadOnlyList<RelativePath>> changedFilesByDirectory,
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

        _fs.File("Inventory/Suppliers/StockSupplier.cs", "/* */");
        _fs.File("Inventory/Suppliers/ShipmentSupplier.cs", "/* */");
        _fs.File("Inventory/Stock/Labels/PriceTag.cs", "/* */");
        _fs.File("Inventory/Stock/Catalogue.cs", "/* */");

        var stockSupplierFile = RelativePath.File(_fs.Root, "./Inventory/Suppliers/StockSupplier.cs");
        var shipmentSupplierFile = RelativePath.File(_fs.Root, "./Inventory/Suppliers/ShipmentSupplier.cs");
        var priceTagFile = RelativePath.File(_fs.Root, "./Inventory/Stock/Labels/PriceTag.cs");
        var catalogueFile = RelativePath.File(_fs.Root, "./Inventory/Stock/Catalogue.cs");

        var rootDir = RelativePath.Directory(_fs.Root, _fs.Root);
        var inventoryDir = RelativePath.Directory(_fs.Root, "./Inventory");
        var suppliersDir = RelativePath.Directory(_fs.Root, "./Inventory/Suppliers");
        var stockDir = RelativePath.Directory(_fs.Root, "./Inventory/Stock");
        var labelsDir = RelativePath.Directory(_fs.Root, "./Inventory/Stock/Labels");
        var tagsDir = RelativePath.Directory(_fs.Root, "./Inventory/Stock/Tags");
        var portsDir = RelativePath.Directory(_fs.Root, "./Inventory/Ports");
        var toolsDir = RelativePath.Directory(_fs.Root, "./Inventory/Tools");
        var warehouseDir = RelativePath.Directory(_fs.Root, "./Warehouse");

        var changedModules = new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [rootDir] = [inventoryDir],
            [inventoryDir] = [suppliersDir, portsDir, stockDir, toolsDir],
            [suppliersDir] = [stockSupplierFile, shipmentSupplierFile],
            [stockDir] = [tagsDir, labelsDir, catalogueFile],
            [labelsDir] = [priceTagFile],
        };

        var changes = CreateProjectChanges(changedModules, [], []);

        var parseMap = new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [stockSupplierFile] = [portsDir, tagsDir, labelsDir, warehouseDir],
            [shipmentSupplierFile] = [portsDir, tagsDir, warehouseDir],
            [priceTagFile] = [tagsDir],
            [catalogueFile] = [toolsDir]
        };

        var parser = new DependencyParserSpy(_fs.Root, parseMap);
        var builder = CreateBuilder([parser]);

        var graph = await builder.GetGraphAsync(changes, null);

        var root = RequireItem(graph, rootDir);
        var inventory = RequireItem(graph, inventoryDir);
        var suppliers = RequireItem(graph, suppliersDir);
        var stock = RequireItem(graph, stockDir);
        var stockSupplierItem = RequireItem(graph, stockSupplierFile);
        var shipmentSupplierItem = RequireItem(graph, shipmentSupplierFile);
        var priceTagItem = RequireItem(graph, priceTagFile);
        var catalogueItem = RequireItem(graph, catalogueFile);

        Assert.Contains(inventory.Path, graph.ChildrenOf(rootDir));
        Assert.Contains(suppliers.Path, graph.ChildrenOf(inventoryDir));
        Assert.Contains(stock.Path, graph.ChildrenOf(inventoryDir));

        Assert.Equal(4, parser.Calls.Count);
        Assert.Contains(stockSupplierItem.Path, parser.Calls);
        Assert.Contains(shipmentSupplierItem.Path, parser.Calls);
        Assert.Contains(priceTagItem.Path, parser.Calls);
        Assert.Contains(catalogueItem.Path, parser.Calls);
    }

    [Fact]
    public async Task BuildGraph_DeduplicatesDuplicateFileEntries()
    {
        SetupMockProject();

        _fs.File("Inventory/Suppliers/Duplicate.cs", "/* */");

        var suppliersDirPath = RelativePath.Directory(_fs.Root, "./Inventory/Suppliers/");
        var csPath = RelativePath.File(_fs.Root, "./Inventory/Suppliers/Duplicate.cs");
        var depPath = RelativePath.Directory(_fs.Root, "./Dep/");

        var parseMap = new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [csPath] = [depPath]
        };

        var parser = new DependencyParserSpy(_fs.Root, parseMap);
        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (suppliersDirPath, new[] { csPath, csPath, csPath })
        );

        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, null);

        var dirItem = RequireItem(graph, suppliersDirPath);
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

        _fs.File("Inventory/Suppliers/Variant.cs", "/* */");

        var suppliersDirPath = RelativePath.Directory(_fs.Root, "./Inventory/Suppliers/");
        var csPath = RelativePath.File(_fs.Root, "./Inventory/Suppliers/Variant.cs");

        var parser = new DependencyParserSpy(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [csPath] = []
        });

        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (suppliersDirPath, new[] { csPath })
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, null);

        Assert.True(graph.ContainsProjectItem(csPath));

        var rel1 = RelativePath.File(_fs.Root, "./Inventory/Suppliers/Variant.cs");
        var rel2 = RelativePath.File(_fs.Root, "Inventory/Suppliers/Variant.cs");
        Assert.True(graph.ContainsProjectItem(rel1));
        Assert.True(graph.ContainsProjectItem(rel2));
    }

    [Fact]
    public async Task BuildGraph_CreatesNodesForDirectoriesMentionedInContents_EvenIfNotKeys()
    {
        SetupMockProject();
        var rootDirPath = RelativePath.Directory(_fs.Root, _fs.Root);
        var inventoryDirPath = RelativePath.Directory(_fs.Root, "./Inventory/");
        var stockDirPath = RelativePath.Directory(_fs.Root, "./Inventory/Stock/");

        var parser = new DependencyParserSpy(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>());
        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (rootDirPath, new[] { inventoryDirPath }),
            (inventoryDirPath, new[] { stockDirPath })
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, null);

        Assert.True(graph.ContainsProjectItem(inventoryDirPath));
        Assert.True(graph.ContainsProjectItem(stockDirPath));

        var _ = RequireItem(graph, inventoryDirPath);
        var stockNode = RequireItem(graph, stockDirPath);
        Assert.Contains(stockNode.Path, graph.ChildrenOf(inventoryDirPath));
    }

    [Fact]
    public async Task Merge_PrefersChangedLeafDependencies_OverLastSaved()
    {
        SetupMockProject();

        _fs.File("Inventory/Suppliers/StockSupplier.cs", "/* */");

        var suppliersDirPath = RelativePath.Directory(_fs.Root, "./Inventory/Suppliers/");
        var stockSupplierFilePath = RelativePath.File(_fs.Root, "./Inventory/Suppliers/StockSupplier.cs");

        var newDepPath = RelativePath.Directory(_fs.Root, "./New/Dep/");
        var tagsPath = RelativePath.Directory(_fs.Root, "./Inventory/Stock/Tags/");

        var lastSavedGraph = TestDependencyGraph.MakeDependencyGraph(_fs.Root);

        var parser = new DependencyParserSpy(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [stockSupplierFilePath] = [newDepPath, tagsPath]
        });

        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (suppliersDirPath, new[] { stockSupplierFilePath }),
            (newDepPath, [])
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, lastSavedGraph);

        var stockSupplierItem = RequireItem(graph, stockSupplierFilePath);

        var warehousePath = RelativePath.Directory(_fs.Root, "./Warehouse/");
        Assert.Contains(newDepPath, graph.DependenciesFrom(stockSupplierItem.Path).Keys);
        Assert.Contains(tagsPath, graph.DependenciesFrom(stockSupplierItem.Path).Keys);
        Assert.DoesNotContain(warehousePath, graph.DependenciesFrom(stockSupplierItem.Path).Keys);
    }

    [Fact]
    public async Task Merge_RetainsUnchangedSubtrees_FromLastSaved()
    {
        SetupMockProject();
        _fs.File("Inventory/Stock/Labels/PriceTag.cs", "/* */");
        _fs.File("./Inventory/Suppliers/ShipmentSupplier.cs", "/* */");

        var root = _fs.Root;
        var labelsDirPath = RelativePath.Directory(root, "./Inventory/Stock/Labels/");
        var priceTagPath = RelativePath.File(root, "./Inventory/Stock/Labels/PriceTag.cs");
        var changedDep = RelativePath.Directory(root, "./Changed/Dep/");

        var shipmentSupplierPath = RelativePath.File(root, "./Warehouse/Suppliers/ShipmentSupplier.cs");
        var cataloguePath = RelativePath.File(root, "./Inventory/Stock/Catalogue.cs");

        var lastSavedGraph = TestDependencyGraph.MakeDependencyGraph(root);

        var parser = new DependencyParserSpy(root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [priceTagPath] = [changedDep]
        });

        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (labelsDirPath, new[] { priceTagPath })
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, lastSavedGraph);

        Assert.True(graph.ContainsProjectItem(shipmentSupplierPath));
        Assert.True(graph.ContainsProjectItem(cataloguePath));

        var priceTagItem = RequireItem(graph, priceTagPath);
        Assert.Contains(changedDep, graph.DependenciesFrom(priceTagItem.Path).Keys);
    }

    [Fact]
    public async Task Merge_AddsNewFiles_ThatDidNotExistInLastSaved()
    {
        SetupMockProject();

        _fs.File("Inventory/Tools/NewUtil.cs", "/* */");

        var toolsDirPath = RelativePath.Directory(_fs.Root, "./Inventory/Tools/");
        var newPath = RelativePath.File(_fs.Root, "./Inventory/Tools/NewUtil.cs");

        var lastSavedGraph = TestDependencyGraph.MakeDependencyGraph(_fs.Root);

        var someDepDirPath = RelativePath.Directory(_fs.Root, "./Some/Dep/");
        var parser = new DependencyParserSpy(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [newPath] = [someDepDirPath]
        });

        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (toolsDirPath, new[] { newPath })
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

        var cs = _fs.File("Inventory/Suppliers/Cancellable.cs", "/* */");

        var suppliersDirPath = RelativePath.Directory(_fs.Root, "./Inventory/Suppliers/");
        var csPath = RelativePath.File(_fs.Root, "./Inventory/Suppliers/Variant.cs");

        var newDepPath = RelativePath.Directory(_fs.Root, "./New/Dep/");
        var parser = new DependencyParserSpy(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [csPath] = [newDepPath]
        });

        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (suppliersDirPath, new[] { csPath })
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

        var depGraph = _fs.File("Inventory/Stock/Catalogue.cs", "/* */");

        var rootPath = RelativePath.Directory(_fs.Root, _fs.Root);
        var inventoryDirPath = RelativePath.Directory(_fs.Root, "./Inventory/");
        var stockDirPath = RelativePath.Directory(_fs.Root, "./Inventory/Stock/");

        var cataloguePath = RelativePath.File(_fs.Root, "./Inventory/Stock/Catalogue.cs");
        var xPath = RelativePath.File(_fs.Root, "./X/");

        var parser = new DependencyParserSpy(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [cataloguePath] = [xPath]
        });

        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (rootPath, new[] { inventoryDirPath }),
            (inventoryDirPath, new[] { stockDirPath }),
            (stockDirPath, new[] { cataloguePath })
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, null);

        var _ = RequireItem(graph, rootPath);
        var inventory = RequireItem(graph, inventoryDirPath);
        var stock = RequireItem(graph, stockDirPath);

        Assert.Contains(inventory.Path, graph.ChildrenOf(rootPath));
        Assert.Contains(stock.Path, graph.ChildrenOf(inventoryDirPath));
    }

    [Fact]
    public async Task BuildGraph_DoesNotDuplicateDirectoryNodes_WhenPathsVary()
    {
        SetupMockProject();

        var srcPath = RelativePath.Directory(_fs.Root, _fs.Root);

        var dir1 = RelativePath.Directory(_fs.Root, Path.Combine(_fs.Root, "Inventory"));
        var dir2 = RelativePath.Directory(_fs.Root, $"{dir1}{Path.DirectorySeparatorChar}");
        var dir3 = RelativePath.Directory(_fs.Root, Path.Combine(_fs.Root, "./Inventory"));
        var dir4 = RelativePath.Directory(_fs.Root, "./Inventory");

        var parser = new DependencyParserSpy(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>());
        var builder = CreateBuilder([parser]);

        var changedModules = new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [srcPath] = [dir1],
            [dir1] = [dir2, dir3, dir4]
        };
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, null);

        var n1 = RequireItem(graph, dir1);
        var n2 = RequireItem(graph, dir2);
        var n3 = RequireItem(graph, dir3);
        var n4 = RequireItem(graph, dir4);

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

        var _ = _fs.File("Inventory/Stock/Catalogue.cs", "/* */");

        var stockDirPath = RelativePath.Directory(_fs.Root, "./Inventory/Stock/");
        var cataloguePath = RelativePath.File(_fs.Root, "./Inventory/Stock/Catalogue.cs");
        var changedFilePath = RelativePath.Directory(_fs.Root, "./Changed/Node/Dep/");
        var toolsPath = RelativePath.Directory(_fs.Root, "./Inventory/Tools/");

        var lastSaved = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        lastSaved.AddDependency(cataloguePath, toolsPath, DependencyType.Uses);

        var parser = new DependencyParserSpy(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [cataloguePath] = [changedFilePath]
        });
        var builder = CreateBuilder([parser]);
        var changedModules = ChangedModules(
            (stockDirPath, new[] { cataloguePath })
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, lastSaved);

        RequireItem(graph, stockDirPath);
        RequireItem(graph, cataloguePath);
        Assert.Contains(changedFilePath, graph.DependenciesFrom(cataloguePath).Keys);
        Assert.DoesNotContain(toolsPath, graph.DependenciesFrom(cataloguePath).Keys);
    }

    [Fact]
    public async Task Merge_WhenTypeConflicts_IncomingReplacesExisting()
    {
        SetupMockProject();

        var rootPath = _fs.Root;
        var lastSaved = TestDependencyGraph.MakeDependencyGraph(rootPath);

        var oldFile = RelativePath.Directory(rootPath, "./Old/Dep/");
        var bogusPath = RelativePath.Directory(rootPath, "./Inventory/Stock/");
        var bogusItem = TestGraphs.AddProjectItem(lastSaved, bogusPath, ProjectItemType.Directory, [oldFile]);

        var inventoryPath = RelativePath.Directory(rootPath, "./Inventory/");
        lastSaved.AddChild(inventoryPath, bogusItem);

        var stockDirPath = RelativePath.Directory(rootPath, "./Inventory/Stock/");

        var depGraph = _fs.File("Inventory/Stock/Catalogue.cs", "/* */");
        var cataloguePath = RelativePath.File(rootPath, "Inventory/Stock/Catalogue.cs");
        var newDepPath = RelativePath.Directory(_fs.Root, "./New/Dep/");
        var parser = new DependencyParserSpy(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [cataloguePath] = [newDepPath]
        });

        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (stockDirPath, new[] { cataloguePath })
        );
        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, lastSaved);

        var stock = graph.GetProjectItem(stockDirPath);
        Assert.NotNull(stock);
        Assert.IsType<ProjectItem>(stock);
    }

    [Fact]
    public async Task BuildGraph_IgnoresNullOrWhitespaceItems_InContents()
    {
        SetupMockProject();

        var parentDir = RelativePath.Directory(_fs.Root, "./Shop/");

        var emptyPath = RelativePath.Directory(_fs.Root, "");
        var spacesPath = RelativePath.Directory(_fs.Root, "   ");
        var tabsPath = RelativePath.Directory(_fs.Root, "\t");
        var newlinePath = RelativePath.Directory(_fs.Root, "\n");

        var parser = new DependencyParserSpy(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>());
        var builder = CreateBuilder([parser]);

        var changedModules = new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [parentDir] = [emptyPath, spacesPath, tabsPath, newlinePath]
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

        var f1 = _fs.File("Inventory/Suppliers/A.cs", "/* */");
        var f1Path = RelativePath.File(_fs.Root, "./Inventory/Suppliers/A.cs");
        var suppliersDirPath = RelativePath.Directory(_fs.Root, "./Inventory/Suppliers/");

        var changedModules = ChangedModules(
            (suppliersDirPath, [f1Path])
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

        var deleted = RelativePath.File(_fs.Root, "./Warehouse/Suppliers/ShipmentSupplier.cs");
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

        var deletedDir = RelativePath.Directory(_fs.Root, "./Inventory/Stock/");
        Assert.True(lastSaved.ContainsProjectItem(deletedDir));

        var deletedChild1 = RelativePath.Directory(_fs.Root, "./Inventory/Stock/Labels/");
        var deletedChild2 = RelativePath.Directory(_fs.Root, "./Inventory/Stock/Tags/");
        var deletedLeaf1 = RelativePath.File(_fs.Root, "./Inventory/Stock/Labels/PriceTag.cs");
        var deletedLeaf2 = RelativePath.File(_fs.Root, "./Inventory/Stock/Catalogue.cs");

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

        _fs.File("Inventory/Tools/U.cs", "/* */");

        var inventoryDir = RelativePath.Directory(_fs.Root, "./Inventory/");
        var toolsDir = RelativePath.Directory(_fs.Root, "./Inventory/Tools/");
        var uFile = RelativePath.File(_fs.Root, "./Inventory/Tools/U.cs");

        var parser = new FixedMapParser(_fs.Root, new Dictionary<RelativePath, IReadOnlyList<RelativePath>>
        {
            [uFile] = []
        });

        var builder = CreateBuilder([parser]);

        var changedModules = ChangedModules(
            (inventoryDir, new[] { toolsDir, toolsDir, toolsDir }),
            (toolsDir, new[] { uFile })
        );

        var changes = CreateProjectChanges(changedModules, [], []);

        var graph = await builder.GetGraphAsync(changes, null);

        Assert.Single(parser.Calls);
        Assert.Equal(uFile, parser.Calls[0]);

        var inventoryChildren = graph.ChildrenOf(inventoryDir);
        Assert.Single(inventoryChildren, x => x.Equals(toolsDir));
    }

    [Fact]
    public async Task ParserException_IsLogged_AndProcessingContinues_ForOtherItems()
    {
        SetupMockProject();

        _fs.File("Inventory/Suppliers/Bad.cs", "/* */");
        _fs.File("Inventory/Suppliers/Good.cs", "/* */");

        var dir = RelativePath.Directory(_fs.Root, "./Inventory/Suppliers/");
        var bad = RelativePath.File(_fs.Root, "./Inventory/Suppliers/Bad.cs");
        var good = RelativePath.File(_fs.Root, "./Inventory/Suppliers/Good.cs");

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

        var catalogue = RelativePath.File(_fs.Root, "./Inventory/Stock/Catalogue.cs");
        var toolsDir = RelativePath.Directory(_fs.Root, "./Inventory/Tools/");
        Assert.Contains(toolsDir, lastSaved.DependenciesFrom(catalogue).Keys);

        _fs.File("Inventory/Stock/Catalogue.cs", "/* */");

        var stockDir = RelativePath.Directory(_fs.Root, "./Inventory/Stock/");
        var changes = CreateProjectChanges(
            ChangedModules((stockDir, new[] { catalogue })),
            deletedFiles: [],
            deletedDirectories: []
        );

        var parser = new ThrowingParser("Catalogue.cs", new Exception("parse failed"));
        var builder = CreateBuilder([parser]);

        var merged = await builder.GetGraphAsync(changes, lastSaved);

        Assert.True(merged.ContainsProjectItem(catalogue));
        Assert.Contains(toolsDir, merged.DependenciesFrom(catalogue).Keys);
    }

    [Fact]
    public async Task OperationCanceledException_FromParser_IsNotSwallowed()
    {
        SetupMockProject();

        _fs.File("Inventory/Tools/C.cs", "/* */");

        var toolsDir = RelativePath.Directory(_fs.Root, "./Inventory/Tools/");
        var cFile = RelativePath.File(_fs.Root, "./Inventory/Tools/C.cs");

        var blocking = new BlockingUntilCancelledParser(_fs.Root);
        var builder = CreateBuilder([blocking]);

        var changes = CreateProjectChanges(
            ChangedModules((toolsDir, new[] { cFile })),
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

        _fs.File("Inventory/Suppliers/Multi.cs", "/* */");

        var dir = RelativePath.Directory(_fs.Root, "./Inventory/Suppliers/");
        var file = RelativePath.File(_fs.Root, "./Inventory/Suppliers/Multi.cs");

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
