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

        var graph = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        handler.When(mainUrl, HttpStatusCode.OK, DependencyGraphSerializer.Serialize(graph));
        handler.When(masterUrl, HttpStatusCode.NotFound);

        var opts = MakeOptions("https://github.com/owner/repo");

        var lastSaved = await manager.GetLastSavedDependencyGraphAsync(opts, default);

        Assert.Equal(graph.ProjectItems, lastSaved.ProjectItems);
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
