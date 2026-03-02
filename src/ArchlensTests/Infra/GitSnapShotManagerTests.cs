using Archlens.Domain;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Enums;
using Archlens.Domain.Models.Records;
using Archlens.Domain.Utils;
using Archlens.Infra.SnapshotManagers;
using ArchlensTests.Utils;
using System.Net;

namespace ArchlensTests.Infra;

public sealed class GitSnapShotManagerTests : IDisposable
{
    private readonly TestFileSystem _fs = new();
    public void Dispose() => _fs.Dispose();
    private SnapshotOptions MakeOptions(string gitUrl, string branch = "main") => new(
        BaseOptions: new(
            ProjectRoot: _fs.Root,
            ProjectName: "Archlens",
            FullRootPath: _fs.Root
        ),
        SnapshotManager: SnapshotManager.Git,
        SnapshotDir: ".archlens",
        SnapshotFile: "snapshot.json",
        GitInfo: new (gitUrl, branch)
    );

    private static ProjectDependencyGraph MakeDependencyGraph(string rootPath)
    {
        var graph = new ProjectDependencyGraph(rootPath);

        var root = RelativePath.Directory(rootPath, rootPath);
        var application = RelativePath.Directory(rootPath, "./Application/");
        var infra = RelativePath.Directory(rootPath, "./Infra/");
        var domain = RelativePath.Directory(rootPath, "./Domain/");
        var interfaces = RelativePath.Directory(rootPath, "./Domain/Interfaces");
        var factory = RelativePath.Directory(rootPath, "./Domain/Factories/");
        var models = RelativePath.Directory(rootPath, "./Domain/Models/");
        var records = RelativePath.Directory(rootPath, "./Domain/Models/Records/");
        var enums = RelativePath.Directory(rootPath, "./Domain/Models/Enums/");
        var utils = RelativePath.Directory(rootPath, "./Domain/Utils/");

        graph.AddChild(root, application);
        graph.AddChild(root, domain);
        graph.AddChild(domain, factory);
        graph.AddChild(domain, models);
        graph.AddChild(domain, utils);
        graph.AddChild(models, records);
        graph.AddChild(models, enums);

        var changeDetector = RelativePath.File(rootPath, "./Application/ChangeDetector.cs");
        var dependencyParserFactory = RelativePath.File(rootPath, "./Domain/Factories/DependencyParserFactory.cs");
        var rendererFactory = RelativePath.File(rootPath, "./Domain/Factories/RendererFactory.cs");
        var options = RelativePath.File(rootPath, "./Domain/Models/Records/Options.cs");
        var dependencyGraph = RelativePath.File(rootPath, "./Domain/Models/DependencyGraph.cs");

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
    public async Task GetLastSavedDependencyGraphAsync_Throws_When_GitUrl_Missing()
    {
        var handler = new TestHttpHandler();
        var manager = new GitSnaphotManager(".archlens", "snapshot.json", handler);

        var opts = MakeOptions(gitUrl: "  ");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => manager.GetLastSavedDependencyGraphAsync(opts, default));
        Assert.Contains("GitUrl must be provided", ex.Message);
    }

    [Theory]
    [InlineData("https://example.com/owner/repo")]
    [InlineData("https://github.com/owner")]
    [InlineData("notaurl")]
    public async Task GetLastSavedDependencyGraphAsync_Throws_When_GitUrl_Unparsable(string badUrl)
    {
        var handler = new TestHttpHandler();
        var manager = new GitSnaphotManager(".archlens", "snapshot.json", handler);

        var opts = MakeOptions(badUrl);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => manager.GetLastSavedDependencyGraphAsync(opts, default));
        Assert.Contains("Colud not parse GitUrl", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Returns_Graph_From_Main_When_Present()
    {
        var handler = new TestHttpHandler();
        var manager = new GitSnaphotManager(".archlens", "snapshot.json", handler);

        var mainUrl = "https://raw.githubusercontent.com/owner/repo/main/.archlens/snapshot.json";
        var masterUrl = "https://raw.githubusercontent.com/owner/repo/master/.archlens/snapshot.json";

        var graph = MakeDependencyGraph(_fs.Root);
        handler.When(mainUrl, HttpStatusCode.OK, DependencyGraphSerializer.Serialize(graph));
        handler.When(masterUrl, HttpStatusCode.NotFound);

        var opts = MakeOptions("https://github.com/owner/repo");

        var lastSaved = await manager.GetLastSavedDependencyGraphAsync(opts, default);

        Assert.Equal(graph.ProjectItems, lastSaved.ProjectItems);
        Assert.Equal(graph.Deps, lastSaved.Deps);
    }

    [Fact]
    public async Task Throws_When_Both_Branches_Missing()
    {
        var handler = new TestHttpHandler();
        var manager = new GitSnaphotManager(".archlens", "snapshot.json", handler);

        var mainUrl = "https://raw.githubusercontent.com/owner/repo/main/.archlens/snapshot.json";
        var masterUrl = "https://raw.githubusercontent.com/owner/repo/master/.archlens/snapshot.json";

        handler.When(mainUrl, HttpStatusCode.NotFound);
        handler.When(masterUrl, HttpStatusCode.NotFound);

        var opts = MakeOptions("https://github.com/owner/repo");

        var ex = await Assert.ThrowsAsync<Exception>(() => manager.GetLastSavedDependencyGraphAsync(opts, default));
        Assert.Contains("Unable to find main branch", ex.Message);
    }

}
