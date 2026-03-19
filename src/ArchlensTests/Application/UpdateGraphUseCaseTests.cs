using Archlens.Application;
using Archlens.Domain;
using Archlens.Domain.Interfaces;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Enums;
using Archlens.Domain.Models.Records;
using ArchlensTests.Utils;

namespace ArchlensTests.Application;

public sealed class UpdateGraphUseCaseTests : IDisposable
{
    private readonly TestFileSystem _fs = new();

    public void Dispose() => _fs.Dispose();

    private BaseOptions MakeBaseOptions() => new(
        FullRootPath: _fs.Root,
        ProjectRoot: _fs.Root,
        ProjectName: "TestProject"
    );

    private ParserOptions MakeParserOptions() => new(
        BaseOptions: MakeBaseOptions(),
        Languages: [],
        Exclusions: [],
        FileExtensions: [".cs"]
    );

    private RenderOptions MakeRenderOptions(RenderFormat format) => new(
        BaseOptions: MakeBaseOptions(),
        Format: format,
        Views: [new View("overview", [], [])],
        SaveLocation: Path.Combine(_fs.Root, "diagrams")
    );

    private SnapshotOptions MakeSnapshotOptions() => new(
        BaseOptions: MakeBaseOptions(),
        SnapshotManager: SnapshotManager.Local,
        GitInfo: new GitInfo("", "main")
    );

    private sealed class NullParser : IDependencyParser
    {
        public Task<IReadOnlyList<RelativePath>> ParseFileDependencies(string absPath, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RelativePath>>([]);
    }

    private sealed class RendererSpy : RendererBase
    {
        public int RenderCalled { get; private set; }
        public override string FileExtension => "json";

        protected override string Render(RenderGraph graph, View view, RenderOptions options)
        {
            RenderCalled++;
            return "{}";
        }
    }

    private sealed class StubSnapshotManager(ProjectDependencyGraph? snapshot) : ISnapshotManager
    {
        public int SaveCalled { get; private set; }

        public Task<ProjectDependencyGraph?> GetLastSavedDependencyGraphAsync(
            SnapshotOptions options, CancellationToken ct = default)
            => Task.FromResult(snapshot);

        public Task SaveGraphAsync(ProjectDependencyGraph graph, SnapshotOptions options, CancellationToken ct = default)
        {
            SaveCalled++;
            return Task.CompletedTask;
        }
    }

    private UpdateGraphUseCase MakeUseCase(
        RenderFormat format,
        ISnapshotManager snapshotManager,
        RendererBase renderer,
        bool diff = false) => new(
            baseOptions: MakeBaseOptions(),
            parserOptions: MakeParserOptions(),
            renderOptions: MakeRenderOptions(format),
            snapshotOptions: MakeSnapshotOptions(),
            parsers: [new NullParser()],
            renderer: renderer,
            snapshotManager: snapshotManager,
            diff: diff
        );

    private static ProjectDependencyGraph MakeEmptyGraph(string root)
    {
        var g = new ProjectDependencyGraph(root);
        g.UpsertProjectItem(RelativePath.Directory(root, "./"), ProjectItemType.Directory);
        return g;
    }

    private string SavedFilePath(bool diff = false)
    {
        var diffPart = diff ? "-diff" : "";
        return Path.Combine(_fs.Root, "diagrams", $"TestProject{diffPart}-overview.json");
    }

    [Fact]
    public async Task RunAsync_WhenFormatIsNone_DoesNotCallRenderer()
    {
        _fs.File("src/A.cs", "class A {}");
        var spy = new RendererSpy();
        var stub = new StubSnapshotManager(null);

        var sut = MakeUseCase(RenderFormat.None, stub, spy);
        await sut.RunAsync();

        Assert.Equal(0, spy.RenderCalled);
    }

    [Fact]
    public async Task RunAsync_WhenFormatIsNone_StillSavesSnapshot()
    {
        _fs.File("src/A.cs", "class A {}");
        var spy = new RendererSpy();
        var stub = new StubSnapshotManager(null);

        var sut = MakeUseCase(RenderFormat.None, stub, spy);
        await sut.RunAsync();

        Assert.Equal(1, stub.SaveCalled);
    }

    [Fact]
    public async Task RunAsync_WhenFormatIsNone_NoOutputFilesAreCreated()
    {
        _fs.File("src/A.cs", "class A {}");
        var spy = new RendererSpy();
        var stub = new StubSnapshotManager(null);

        var sut = MakeUseCase(RenderFormat.None, stub, spy);
        await sut.RunAsync();

        Assert.False(File.Exists(SavedFilePath()), "Expected no output file when format is None.");
    }

    [Fact]
    public async Task RunAsync_WhenFormatIs_NotNoneAndNotDiff_CallsRenderViews()
    {
        _fs.File("src/A.cs", "class A {}");
        var spy = new RendererSpy();
        var stub = new StubSnapshotManager(null);

        var sut = MakeUseCase(RenderFormat.Json, stub, spy);
        await sut.RunAsync();

        Assert.Equal(1, spy.RenderCalled);
    }

    [Fact]
    public async Task RunAsync_WhenFormatIs_NotNoneAndNotDiff_CreatesOutputFile()
    {
        _fs.File("src/A.cs", "class A {}");
        var spy = new RendererSpy();
        var stub = new StubSnapshotManager(null);

        var sut = MakeUseCase(RenderFormat.Json, stub, spy);
        await sut.RunAsync();

        Assert.True(File.Exists(SavedFilePath()), "Expected output file to be written.");
        Assert.False(File.Exists(SavedFilePath(diff: true)), "Expected no diff output file.");
    }

    [Fact]
    public async Task RunAsync_WhenFormatIs_NotNoneAndNotDiff_StillSavesSnapshot()
    {
        _fs.File("src/A.cs", "class A {}");
        var spy = new RendererSpy();
        var stub = new StubSnapshotManager(null);

        var sut = MakeUseCase(RenderFormat.Json, stub, spy);
        await sut.RunAsync();

        Assert.Equal(1, stub.SaveCalled);
    }

    [Fact]
    public async Task RunAsync_WhenDiffMode_AndSnapshotExists_CreatesDiffOutputFile()
    {
        _fs.File("src/A.cs", "class A {}");
        var spy = new RendererSpy();
        var snapshot = MakeEmptyGraph(_fs.Root);
        var stub = new StubSnapshotManager(snapshot);

        var sut = MakeUseCase(RenderFormat.Json, stub, spy, diff: true);
        await sut.RunAsync();

        Assert.True(File.Exists(SavedFilePath(diff: true)), "Expected diff output file to be written.");
        Assert.False(File.Exists(SavedFilePath(diff: false)), "Expected no regular output file in diff mode.");
    }

    [Fact]
    public async Task RunAsync_WhenDiffMode_AndSnapshotExists_StillSavesSnapshot()
    {
        _fs.File("src/A.cs", "class A {}");
        var spy = new RendererSpy();
        var snapshot = MakeEmptyGraph(_fs.Root);
        var stub = new StubSnapshotManager(snapshot);

        var sut = MakeUseCase(RenderFormat.Json, stub, spy, diff: true);
        await sut.RunAsync();

        Assert.Equal(1, stub.SaveCalled);
    }

    [Fact]
    public async Task RunAsync_WhenDiffMode_AndNoSnapshot_ThrowsInvalidOperationException()
    {
        _fs.File("src/A.cs", "class A {}");
        var spy = new RendererSpy();
        var stub = new StubSnapshotManager(null);

        var sut = MakeUseCase(RenderFormat.Json, stub, spy, diff: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RunAsync());
    }

    [Fact]
    public async Task RunAsync_PropagatesCancellation()
    {
        _fs.File("src/A.cs", "class A {}");
        var spy = new RendererSpy();
        var stub = new StubSnapshotManager(null);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = MakeUseCase(RenderFormat.None, stub, spy);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.RunAsync(cts.Token));
    }
}
