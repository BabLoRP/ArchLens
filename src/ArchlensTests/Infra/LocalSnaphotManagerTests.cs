using Archlens.Domain;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using Archlens.Infra.SnapshotManagers;
using ArchlensTests.Utils;

namespace ArchlensTests.Infra;

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
        var snapshotManager = new LocalSnaphotManager(dirName, fileName);

        var opts = MakeOptions();

        var rootPath = _fs.Root;
        var graph = TestDependencyGraph.MakeDependencyGraph(rootPath);

        await snapshotManager.SaveGraphAsync(graph, opts);

        var expectedDir = Path.Combine(rootPath, dirName);
        var expectedFile = Path.Combine(expectedDir, fileName);

        Assert.True(Directory.Exists(expectedDir));
        Assert.True(File.Exists(expectedFile));

        var json = await File.ReadAllTextAsync(expectedFile);

        var root = RelativePath.Directory(rootPath, rootPath);
        var application = RelativePath.Directory(rootPath, "./Application/");
        var infra = RelativePath.Directory(rootPath, "./Infra/");
        var domain = RelativePath.Directory(rootPath, "./Domain/");
        var interfaces = RelativePath.Directory(rootPath, "./Domain/Interfaces");
        var factory = RelativePath.Directory(rootPath, "./Infra/Factories/");
        var models = RelativePath.Directory(rootPath, "./Domain/Models/");
        var records = RelativePath.Directory(rootPath, "./Domain/Models/Records/");
        var enums = RelativePath.Directory(rootPath, "./Domain/Models/Enums/");
        var utils = RelativePath.Directory(rootPath, "./Domain/Utils/");

        Assert.Contains(root.ToString(), json);
        Assert.Contains(application.ToString(), json);
        Assert.Contains(infra.ToString(), json);
        Assert.Contains(domain.ToString(), json);
        Assert.Contains(interfaces.ToString(), json);
        Assert.Contains(factory.ToString(), json);
        Assert.Contains(models.ToString(), json);
        Assert.Contains(records.ToString(), json);
        Assert.Contains(enums.ToString(), json);
        Assert.Contains(utils.ToString(), json);
    }

    [Fact]
    public async Task SaveThenLoad_Get_Name_And_LastWriteTime()
    {
        var snapshotManager = new LocalSnaphotManager(".archlens", "snapshot.json");
        var opts = MakeOptions();

        var rootPath = _fs.Root;
        var graph = TestDependencyGraph.MakeDependencyGraph(rootPath);

        await snapshotManager.SaveGraphAsync(graph, opts);
        var loaded = await snapshotManager.GetLastSavedDependencyGraphAsync(opts);

        var root = RelativePath.Directory(rootPath, rootPath);
        var application = RelativePath.Directory(rootPath, "./Application/");
        var infra = RelativePath.Directory(rootPath, "./Infra/");
        var domain = RelativePath.Directory(rootPath, "./Domain/");
        var interfaces = RelativePath.Directory(rootPath, "./Domain/Interfaces");
        var factory = RelativePath.Directory(rootPath, "./Infra/Factories/");
        var models = RelativePath.Directory(rootPath, "./Domain/Models/");
        var records = RelativePath.Directory(rootPath, "./Domain/Models/Records/");
        var enums = RelativePath.Directory(rootPath, "./Domain/Models/Enums/");
        var utils = RelativePath.Directory(rootPath, "./Domain/Utils/");

        var loadedItems = loaded.ProjectItems;
        Assert.Contains(root, loadedItems);
        Assert.Contains(application, loadedItems);
        Assert.Contains(infra, loadedItems);
        Assert.Contains(domain, loadedItems);
        Assert.Contains(interfaces, loadedItems);
        Assert.Contains(factory, loadedItems);
        Assert.Contains(models, loadedItems);
        Assert.Contains(records, loadedItems);
        Assert.Contains(enums, loadedItems);
        Assert.Contains(utils, loadedItems);

        Assert.Equal(graph.GetProjectItem(root).LastWriteTime.ToString("dd-MM-yyyy HH:mm:ss"), loaded.GetProjectItem(root).LastWriteTime.ToString("dd-MM-yyyy HH:mm:ss"));
    }

    [Fact]
    public async Task GetLastSavedDependencyGraphAsync_ReturnsNull_WhenFileMissing()
    {
        var snapshotManager = new LocalSnaphotManager(".archlens", "snapshot.json");
        var opts = MakeOptions();

        var loaded = await snapshotManager.GetLastSavedDependencyGraphAsync(opts);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Uses_CustomDirAndFileNames()
    {
        var customDir = "_state";
        var customFile = "dep.json";

        var snapshotManager = new LocalSnaphotManager(customDir, customFile);
        var opts = MakeOptions();

        var graph = new ProjectDependencyGraph(_fs.Root);
        var expectedPath = Path.Combine(_fs.Root, customDir, customFile);

        await snapshotManager.SaveGraphAsync(graph, opts);

        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public async Task Load_ReturnsGraph_WhenFilePresent()
    {
        var snapshotManager = new LocalSnaphotManager(".archlens", "snapshot.json");
        var opts = MakeOptions();

        var graph = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        await snapshotManager.SaveGraphAsync(graph, opts);

        var loaded = await snapshotManager.GetLastSavedDependencyGraphAsync(opts);

        Assert.Equal(graph.ProjectItems, loaded.ProjectItems);
    }

    [Fact]
    public async Task Load_ReturnsMultiLevelGraph_WhenPresent()
    {
        var snapshotManager = new LocalSnaphotManager(".archlens", "snapshot.json");
        var opts = MakeOptions();

        var root = _fs.Root;
        var graph = TestDependencyGraph.MakeDependencyGraph(root);
        await snapshotManager.SaveGraphAsync(graph, opts);

        var loaded = await snapshotManager.GetLastSavedDependencyGraphAsync(opts);

        Assert.Equal(graph.ProjectItems, loaded.ProjectItems);

        var rootPath = RelativePath.Directory(root, "./");
        Assert.Equal(graph.ChildrenOf(rootPath).Count, loaded.ChildrenOf(rootPath).Count);

        var domainPath = RelativePath.Directory(root, "./Domain/");
        var domain = loaded.GetProjectItem(domainPath);

        Assert.NotNull(domain);
        Assert.Equal(3, graph.ChildrenOf(domainPath).Count);
    }

    [Fact]
    public async Task Load_ReturnsSubModuleDependencies_WhenPresent()
    {
        var snapshotManager = new LocalSnaphotManager(".archlens", "snapshot.json");
        var opts = MakeOptions();

        var root = _fs.Root;
        var graph = TestDependencyGraph.MakeDependencyGraph(root);
        await snapshotManager.SaveGraphAsync(graph, opts);

        var loaded = await snapshotManager.GetLastSavedDependencyGraphAsync(opts);

        var rootPath = RelativePath.Directory(root, "./");
        var applicationPath = RelativePath.Directory(root, "./Application/");

        Assert.NotNull(loaded.GetProjectItem(applicationPath));

        var rootDeps = DependencyAggregator.GetAggregatedDependencies(loaded, rootPath);
        var applicationDeps = DependencyAggregator.GetAggregatedDependencies(loaded, applicationPath);

        Assert.Empty(rootDeps);
        Assert.Equal(3, applicationDeps.Count);
    }
}