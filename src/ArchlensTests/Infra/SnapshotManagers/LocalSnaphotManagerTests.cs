using Archlens.Domain;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using Archlens.Infra.SnapshotManagers;
using ArchlensTests.Utils;

namespace ArchlensTests.Infra.SnapshotManagers;

public sealed class LocalSnapshotManagerTests : IDisposable
{
    private readonly TestFileSystem _fs = new();
    public void Dispose() => _fs.Dispose();

    private SnapshotOptions MakeOptions() => new(
        BaseOptions: new(
            ProjectRoot: _fs.Root,
            ProjectName: "Archlens",
            FullRootPath: _fs.Root
        ),
        SnapshotManager: default,
        SnapshotDir: ".archlens",
        SnapshotFile: "snapshot.json",
        GitInfo: new("", "")
    );

    [Fact]
    public async Task SaveGraphAsync_CreatesDirectoryAndFile_AtConfiguredLocation()
    {
        var dirName = ".archlens";
        var fileName = "snapshot.json";
        var snapshotManager = new LocalSnapshotManager(dirName, fileName);

        var opts = MakeOptions();

        var rootPath = _fs.Root;
        var graph = TestDependencyGraph.MakeDependencyGraph(rootPath);

        await snapshotManager.SaveGraphAsync(graph, opts);

        var expectedDir = Path.Combine(rootPath, dirName);
        var expectedFile = Path.Combine(expectedDir, fileName);

        Assert.True(Directory.Exists(expectedDir));
        Assert.True(File.Exists(expectedFile));

        var bytes = await File.ReadAllBytesAsync(expectedFile);
        Assert.NotEmpty(bytes);

        var loaded = DependencyGraphSerializer.Deserialize(bytes, rootPath);

        var root = RelativePath.Directory(rootPath, rootPath);
        var shop = RelativePath.Directory(rootPath, "./Shop/");
        var warehouse = RelativePath.Directory(rootPath, "./Warehouse/");
        var inventory = RelativePath.Directory(rootPath, "./Inventory/");
        var ports = RelativePath.Directory(rootPath, "./Inventory/Ports");
        var suppliers = RelativePath.Directory(rootPath, "./Warehouse/Suppliers/");
        var stock = RelativePath.Directory(rootPath, "./Inventory/Stock/");
        var labels = RelativePath.Directory(rootPath, "./Inventory/Stock/Labels/");
        var tags = RelativePath.Directory(rootPath, "./Inventory/Stock/Tags/");
        var tools = RelativePath.Directory(rootPath, "./Inventory/Tools/");

        Assert.Contains(root, loaded.ProjectItems);
        Assert.Contains(shop, loaded.ProjectItems);
        Assert.Contains(warehouse, loaded.ProjectItems);
        Assert.Contains(inventory, loaded.ProjectItems);
        Assert.Contains(ports, loaded.ProjectItems);
        Assert.Contains(suppliers, loaded.ProjectItems);
        Assert.Contains(stock, loaded.ProjectItems);
        Assert.Contains(labels, loaded.ProjectItems);
        Assert.Contains(tags, loaded.ProjectItems);
        Assert.Contains(tools, loaded.ProjectItems);
    }

    [Fact]
    public async Task SaveThenLoad_Get_Name_And_LastWriteTime()
    {
        var snapshotManager = new LocalSnapshotManager(".archlens", "snapshot.json");
        var opts = MakeOptions();

        var rootPath = _fs.Root;
        var graph = TestDependencyGraph.MakeDependencyGraph(rootPath);

        await snapshotManager.SaveGraphAsync(graph, opts);
        var loaded = await snapshotManager.GetLastSavedDependencyGraphAsync(opts);

        var root = RelativePath.Directory(rootPath, rootPath);
        var shop = RelativePath.Directory(rootPath, "./Shop/");
        var warehouse = RelativePath.Directory(rootPath, "./Warehouse/");
        var inventory = RelativePath.Directory(rootPath, "./Inventory/");
        var ports = RelativePath.Directory(rootPath, "./Inventory/Ports");
        var suppliers = RelativePath.Directory(rootPath, "./Warehouse/Suppliers/");
        var stock = RelativePath.Directory(rootPath, "./Inventory/Stock/");
        var labels = RelativePath.Directory(rootPath, "./Inventory/Stock/Labels/");
        var tags = RelativePath.Directory(rootPath, "./Inventory/Stock/Tags/");
        var tools = RelativePath.Directory(rootPath, "./Inventory/Tools/");

        var loadedItems = loaded?.ProjectItems;

        if (loadedItems is null)
            Assert.Fail("Loaded items is null");

        Assert.Contains(root, loadedItems);
        Assert.Contains(shop, loadedItems);
        Assert.Contains(warehouse, loadedItems);
        Assert.Contains(inventory, loadedItems);
        Assert.Contains(ports, loadedItems);
        Assert.Contains(suppliers, loadedItems);
        Assert.Contains(stock, loadedItems);
        Assert.Contains(labels, loadedItems);
        Assert.Contains(tags, loadedItems);
        Assert.Contains(tools, loadedItems);

        Assert.Equal(graph?.GetProjectItem(root)?.LastWriteTime.ToString("dd-MM-yyyy HH:mm:ss"), loaded?.GetProjectItem(root)?.LastWriteTime.ToString("dd-MM-yyyy HH:mm:ss"));
    }

    [Fact]
    public async Task GetLastSavedDependencyGraphAsync_ReturnsNull_WhenFileMissing()
    {
        var snapshotManager = new LocalSnapshotManager(".archlens", "snapshot.json");
        var opts = MakeOptions();

        var loaded = await snapshotManager.GetLastSavedDependencyGraphAsync(opts);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Uses_CustomDirAndFileNames()
    {
        var customDir = "_state";
        var customFile = "dep.json";

        var snapshotManager = new LocalSnapshotManager(customDir, customFile);
        var opts = MakeOptions();

        var graph = new ProjectDependencyGraph(_fs.Root);
        var expectedPath = Path.Combine(_fs.Root, customDir, customFile);

        await snapshotManager.SaveGraphAsync(graph, opts);

        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public async Task Load_ReturnsGraph_WhenFilePresent()
    {
        var snapshotManager = new LocalSnapshotManager(".archlens", "snapshot.json");
        var opts = MakeOptions();

        var graph = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        await snapshotManager.SaveGraphAsync(graph, opts);

        var loaded = await snapshotManager.GetLastSavedDependencyGraphAsync(opts);

        Assert.Equal(graph.ProjectItems, loaded?.ProjectItems);
    }

    [Fact]
    public async Task Load_ReturnsMultiLevelGraph_WhenPresent()
    {
        var snapshotManager = new LocalSnapshotManager(".archlens", "snapshot.json");
        var opts = MakeOptions();

        var root = _fs.Root;
        var graph = TestDependencyGraph.MakeDependencyGraph(root);
        await snapshotManager.SaveGraphAsync(graph, opts);

        var loaded = await snapshotManager.GetLastSavedDependencyGraphAsync(opts);

        Assert.Equal(graph.ProjectItems, loaded?.ProjectItems);

        var rootPath = RelativePath.Directory(root, "./");
        Assert.Equal(graph.ChildrenOf(rootPath).Count, loaded?.ChildrenOf(rootPath).Count);

        var inventoryPath = RelativePath.Directory(root, "./Inventory/");
        var inventory = loaded?.GetProjectItem(inventoryPath);

        Assert.NotNull(inventory);
        Assert.Equal(3, graph.ChildrenOf(inventoryPath).Count);
    }
}
