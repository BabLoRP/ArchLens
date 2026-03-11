using System.Diagnostics;
using Archlens.Application;
using Archlens.Domain;
using Archlens.Domain.Interfaces;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Enums;
using Archlens.Domain.Models.Records;
using Archlens.Infra.Renderers;

namespace ArchlensTests.Performance;

public class PerformanceTests
{
    // DependencyAggregator

    [Fact]
    public void DependencyAggregator_Small_Under100ms()
    {
        var graph = GraphFactory.Build(moduleCount: 5, filesPerModule: 10, crossModuleDepsPerFile: 3);
        var root = RelativePath.Directory(GraphFactory.FakeRoot, GraphFactory.FakeRoot);

        var elapsed = Time(() => DependencyAggregator.GetAggregatedDependencies(graph, root));

        Assert.True(elapsed.TotalMilliseconds < 100,
            $"DependencyAggregator (small, 50 files) took {elapsed.TotalMilliseconds:F1} ms — budget 100 ms");
    }

    [Fact]
    public void DependencyAggregator_Large_Under2000ms()
    {
        var graph = GraphFactory.Build(moduleCount: 30, filesPerModule: 50, crossModuleDepsPerFile: 3);
        var root = RelativePath.Directory(GraphFactory.FakeRoot, GraphFactory.FakeRoot);

        var elapsed = Time(() => DependencyAggregator.GetAggregatedDependencies(graph, root));

        Assert.True(elapsed.TotalMilliseconds < 2000,
            $"DependencyAggregator (large, 1500 files) took {elapsed.TotalMilliseconds:F1} ms — budget 2000 ms");
    }

    // RendererBase / RenderView 

    [Fact]
    public void RenderView_Small_Under100ms()
    {
        var (graph, view, opts) = MakeRenderScenario(moduleCount: 5, filesPerModule: 10, depsPerFile: 3);
        var renderer = new JsonRenderer();

        var elapsed = Time(() => renderer.RenderView(graph, view, opts));

        Assert.True(elapsed.TotalMilliseconds < 100,
            $"RenderView (small, 50 files) took {elapsed.TotalMilliseconds:F1} ms — budget 100 ms");
    }

    [Fact]
    public void RenderView_Large_Under1000ms()
    {
        var (graph, view, opts) = MakeRenderScenario(moduleCount: 30, filesPerModule: 50, depsPerFile: 5);
        var renderer = new JsonRenderer();

        var elapsed = Time(() => renderer.RenderView(graph, view, opts));

        Assert.True(elapsed.TotalMilliseconds < 1000,
            $"RenderView (large, 1500 files) took {elapsed.TotalMilliseconds:F1} ms — budget 1000 ms");
    }

    // DependencyGraphBuilder

    [Fact]
    public async Task BuildGraph_Small_Under1000ms()
    {
        using var fs = new BuilderFileSystem(moduleCount: 5, filesPerModule: 10);

        var elapsed = await TimeAsync(() => fs.Builder.GetGraphAsync(fs.Changes, lastSavedDependencyGraph: null!));

        Assert.True(elapsed.TotalMilliseconds < 1000,
            $"BuildGraph (small, 50 files) took {elapsed.TotalMilliseconds:F1} ms — budget 1000 ms");
    }

    [Fact]
    public async Task BuildGraph_Medium_Under3000ms()
    {
        using var fs = new BuilderFileSystem(moduleCount: 8, filesPerModule: 20);

        var elapsed = await TimeAsync(() => fs.Builder.GetGraphAsync(fs.Changes, lastSavedDependencyGraph: null!));

        Assert.True(elapsed.TotalMilliseconds < 3000,
            $"BuildGraph (medium, 160 files) took {elapsed.TotalMilliseconds:F1} ms — budget 3000 ms");
    }

    // helpers

    private static TimeSpan Time(Action action)
    {
        var sw = Stopwatch.StartNew();
        action();
        return sw.Elapsed;
    }

    private static async Task<TimeSpan> TimeAsync(Func<Task> action)
    {
        var sw = Stopwatch.StartNew();
        await action();
        return sw.Elapsed;
    }

    private static (ProjectDependencyGraph graph, View view, RenderOptions opts) MakeRenderScenario(
        int moduleCount, int filesPerModule, int depsPerFile)
    {
        var graph = GraphFactory.Build(moduleCount, filesPerModule, depsPerFile);
        var baseOptions = new BaseOptions(
            FullRootPath: GraphFactory.FakeRoot,
            ProjectRoot: GraphFactory.FakeRoot,
            ProjectName: "PerfTest");
        var view = new View("perf", [], []);
        var opts = new RenderOptions(baseOptions, RenderFormat.Json, [view], Path.GetTempPath());
        return (graph, view, opts);
    }

    private sealed class BuilderFileSystem : IDisposable
    {
        public DependencyGraphBuilder Builder { get; }
        public ProjectChanges Changes { get; }

        private readonly string _root;

        public BuilderFileSystem(int moduleCount, int filesPerModule)
        {
            _root = Path.Combine(Path.GetTempPath(), $"archlens-perf-builder-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_root);

            var opts = new BaseOptions(_root, _root, "PerfTest");
            var changedByDir = new Dictionary<RelativePath, IReadOnlyList<RelativePath>>();

            for (int m = 0; m < moduleCount; m++)
            {
                var modAbs = Path.Combine(_root, $"Module{m}");
                Directory.CreateDirectory(modAbs);
                var dirPath = RelativePath.Directory(_root, modAbs);
                var contents = new List<RelativePath>();

                for (int f = 0; f < filesPerModule; f++)
                {
                    var fileAbs = Path.Combine(modAbs, $"File{f}.cs");
                    File.WriteAllText(fileAbs, $"// M{m} F{f}");
                    contents.Add(RelativePath.File(_root, fileAbs));
                }

                changedByDir[dirPath] = contents;
            }

            Changes = new ProjectChanges(changedByDir, [], []);
            Builder = new DependencyGraphBuilder([new NullParser()], opts);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
        }

        private sealed class NullParser : IDependencyParser
        {
            private static readonly Task<IReadOnlyList<RelativePath>> Empty =
                Task.FromResult<IReadOnlyList<RelativePath>>([]);

            public Task<IReadOnlyList<RelativePath>> ParseFileDependencies(
                string absPath, CancellationToken ct = default) => Empty;
        }
    }
}
