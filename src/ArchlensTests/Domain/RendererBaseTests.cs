using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using Archlens.Infra.Renderers;
using ArchlensTests.Utils;
using System.Text.RegularExpressions;

namespace ArchlensTests.Domain;

public sealed class RendererBaseTests : IDisposable
{
    private readonly TestFileSystem _fs = new();
    public void Dispose() => _fs.Dispose();

    private RenderOptions MakeOptions() => new(
        BaseOptions: new(
            ProjectRoot: _fs.Root,
            ProjectName: "Archlens",
            FullRootPath: _fs.Root
        ),
        Format: default,
        Views: [new View("completeView", [], []), new View("ignoringView", [], ["./Infra/"])],
        SaveLocation: null
    );

    private RenderOptions MakeOptions(
        IReadOnlyList<Package>? packages = null,
        IReadOnlyList<string>? ignore = null,
        string viewName = "testView",
        string? saveLocation = null) => new(
        BaseOptions: new(
            ProjectRoot: _fs.Root,
            ProjectName: "Archlens",
            FullRootPath: _fs.Root),
        Format: default,
        Views: [new View(viewName, packages ?? [], ignore ?? [])],
        SaveLocation: saveLocation);

    private static string Minify(string s) => Regex.Replace(s, @"\s+", "");


    [Fact]
    public void JsonRendererRendersCorrectly()
    {
        JsonRenderer renderer = new();

        var opts = MakeOptions();
        var root = TestDependencyGraph.MakeDependencyGraph(opts.BaseOptions.ProjectRoot);
        string result = renderer.RenderView(root, opts.Views[0], opts);

        Assert.NotEmpty(result);
        Assert.StartsWith("{", result);
        Assert.Contains("\"title\":", result);
        Assert.Contains("\"packages\": [", result);
        Assert.Contains("\"edges\": [", result);
        Assert.EndsWith("}", result);
    }

    [Fact]
    public void PlantUMLRendererRendersCorrectly()
    {
        PlantUMLRenderer renderer = new();

        var opts = MakeOptions();
        var root = TestDependencyGraph.MakeDependencyGraph(opts.BaseOptions.ProjectRoot);
        string result = renderer.RenderView(root, opts.Views[0], opts);

        Assert.NotEmpty(result);
        Assert.StartsWith("@startuml", result);
        Assert.Contains("title completeView", result);
        Assert.Contains("package \"Domain\" as Domain", result);
        Assert.Contains("Infra", result);
        Assert.EndsWith("@enduml", result.TrimEnd());
    }

    [Fact]
    public void PlantUMLRendererIgnoresPackages()
    {
        PlantUMLRenderer renderer = new();

        var opts = MakeOptions();
        var root = TestDependencyGraph.MakeDependencyGraph(opts.BaseOptions.ProjectRoot);
        string result = renderer.RenderView(root, opts.Views[1], opts);

        Assert.NotEmpty(result);
        Assert.StartsWith("@startuml", result);
        Assert.Contains("title ignoringView", result);
        Assert.Contains("package \"Domain\" as Domain", result);
        Assert.DoesNotContain("Infra", result);
        Assert.EndsWith("@enduml", result.TrimEnd());
    }

    [Fact]
    public void JsonRendererRendersDiffCorrectly()
    {
        JsonRenderer renderer = new();
        var rootPath = _fs.Root;

        var opts = MakeOptions();
        var remoteGraph = TestDependencyGraph.MakeDependencyGraph(opts.BaseOptions.ProjectRoot);
        var localGraph = TestDependencyGraph.MakeDependencyGraph(opts.BaseOptions.ProjectRoot);

        var records = RelativePath.Directory(rootPath, "./Domain/Models/Records/");
        var dependencyParserFactory = RelativePath.File(rootPath, "./Infra/Factories/DependencyParserFactory.cs");

        localGraph.RemoveDependency(dependencyParserFactory, records); // local removes one Infra -> Domain
        localGraph.AddDependency(records, dependencyParserFactory);
        localGraph.AddDependency(records, dependencyParserFactory);

        var result = renderer.RenderDiffView(localGraph, remoteGraph, opts.Views[0], opts);

        var newEdge = $$"""
                        "state": "CREATED",
                        "fromPackage": "Domain",
                        "toPackage": "Infra",
                        "label": "2 (+2)",
                        """;

        var deletedEdge = $$"""
                        "state": "DELETED",
                        "fromPackage": "Infra",
                        "toPackage": "Domain",
                        "label": "4 (-1)",
                        """;


        Assert.NotEmpty(result);
        Assert.StartsWith("{", result);
        Assert.Contains("\"title\":", result);
        Assert.Contains("\"packages\": [", result);
        Assert.Contains("\"edges\": [", result);
        Assert.EndsWith("}", result);

        result = Regex.Replace(result, @"\s*", "");
        newEdge = Regex.Replace(newEdge, @"\s*", "");
        deletedEdge = Regex.Replace(deletedEdge, @"\s*", "");

        Assert.Contains(newEdge, result);
        Assert.Contains(deletedEdge, result);
    }

    [Fact]
    public void PlantUMLRendererRendersDiffCorrectly()
    {
        PlantUMLRenderer renderer = new();

        var opts = MakeOptions();

        var rootPath = _fs.Root;
        var remoteGraph = TestDependencyGraph.MakeDependencyGraph(opts.BaseOptions.ProjectRoot);
        var localGraph = TestDependencyGraph.MakeDependencyGraph(opts.BaseOptions.ProjectRoot);

        var records = RelativePath.Directory(rootPath, "./Domain/Models/Records/");
        var dependencyParserFactory = RelativePath.File(rootPath, "./Infra/Factories/DependencyParserFactory.cs");

        localGraph.AddDependency(records, dependencyParserFactory);
        localGraph.RemoveDependency(dependencyParserFactory, records);

        string result = renderer.RenderDiffView(localGraph, remoteGraph, opts.Views[0], opts);

        var newEdge = "Domain --> Infra #Green : 1 (+1)";
        var deletedEdge = "Infra --> Domain #Red : 4 (-1)";

        Assert.NotEmpty(result);
        Assert.StartsWith("@startuml", result);
        Assert.Contains("title completeView", result);
        Assert.Contains("package \"Domain\" as Domain", result);
        Assert.Contains("Infra", result);
        Assert.EndsWith("@enduml", result.TrimEnd());

        Assert.Contains(newEdge, result);
        Assert.Contains(deletedEdge, result);
    }

    [Fact]
    public void EdgesAreOrderedByFromThenTo()
    {
        var opts = MakeOptions();
        var graph = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        var result = new JsonRenderer().RenderView(graph, opts.Views[0], opts);

        var froms = Regex.Matches(result, @"""fromPackage""\s*:\s*""([^""]+)""")
                         .Select(m => m.Groups[1].Value)
                         .ToList();

        Assert.Equal(froms.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList(), froms);
    }

    [Fact]
    public void IgnoredPackageAbsentFromOutput()
    {
        var opts = MakeOptions(ignore: ["./Infra/"]);
        var result = new PlantUMLRenderer().RenderView(
            TestDependencyGraph.MakeDependencyGraph(_fs.Root), opts.Views[0], opts);

        Assert.DoesNotContain("Infra", result);
    }

    [Fact]
    public void NonIgnoredPackagesPresentInOutput()
    {
        var opts = MakeOptions();
        var result = new PlantUMLRenderer().RenderView(
            TestDependencyGraph.MakeDependencyGraph(_fs.Root), opts.Views[0], opts);

        Assert.Contains("Domain", result);
        Assert.Contains("Infra", result);
    }

    [Fact]
    public void NoEdgesReferenceIgnoredPackage()
    {
        var opts = MakeOptions(ignore: ["./Infra/"]);
        var result = new JsonRenderer().RenderView(
            TestDependencyGraph.MakeDependencyGraph(_fs.Root), opts.Views[0], opts);

        var edgeSection = result.Contains("\"edges\"")
            ? result[result.IndexOf("\"edges\"")..]
            : "";
        Assert.DoesNotContain("\"Infra\"", edgeSection);
    }

    [Fact]
    public void DepthOneHidesSubPackages()
    {
        var opts = MakeOptions(packages: [new Package("./Domain/", 1)]);
        var result = new JsonRenderer().RenderView(
            TestDependencyGraph.MakeDependencyGraph(_fs.Root), opts.Views[0], opts);

        Assert.DoesNotContain("Models", result);
    }

    [Fact]
    public void DepthTwoShowsDirectChildren()
    {
        var opts = MakeOptions(packages: [new Package("./Domain/", 2)]);
        var result = new JsonRenderer().RenderView(
            TestDependencyGraph.MakeDependencyGraph(_fs.Root), opts.Views[0], opts);

        Assert.Contains("Models", result);
    }

    [Fact]
    public void IdenticalGraphsProduceNoCreatedOrDeletedNodes()
    {
        var opts = MakeOptions();
        var graph = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        var result = Minify(new JsonRenderer().RenderDiffView(graph, graph, opts.Views[0], opts));

        Assert.DoesNotContain(@"""state"":""CREATED""", result);
        Assert.DoesNotContain(@"""state"":""DELETED""", result);
    }

    [Fact]
    public void NodeOnlyInLocalIsMarkedCreated()
    {
        var opts = MakeOptions();
        var remote = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        var local = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        remote.RemoveProjectItemRecursive(RelativePath.Directory(_fs.Root, "./Infra/"));

        var result = Minify(new JsonRenderer().RenderDiffView(local, remote, opts.Views[0], opts));

        Assert.Contains(@"""state"":""CREATED""", result);
    }

    [Fact]
    public void NodeOnlyInRemoteIsMarkedDeleted()
    {
        var opts = MakeOptions();
        var remote = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        var local = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        local.RemoveProjectItemRecursive(RelativePath.Directory(_fs.Root, "./Infra/"));

        var result = Minify(new JsonRenderer().RenderDiffView(local, remote, opts.Views[0], opts));

        Assert.Contains(@"""state"":""DELETED""", result);
    }

    [Fact]
    public void UnchangedEdgesAreNeutral()
    {
        var opts = MakeOptions();
        var graph = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        var result = Minify(new JsonRenderer().RenderDiffView(graph, graph, opts.Views[0], opts));

        Assert.Empty(Regex.Matches(result, @"""state"":""(CREATED|DELETED)"""));
    }

    [Fact]
    public void NewEdgeIsMarkedCreated()
    {
        var opts = MakeOptions();
        var remote = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        var local = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        local.AddDependency(
            RelativePath.Directory(_fs.Root, "./Domain/Models/Records/"),
            RelativePath.File(_fs.Root, "./Infra/Factories/DependencyParserFactory.cs"));

        var result = Minify(new JsonRenderer().RenderDiffView(local, remote, opts.Views[0], opts));

        Assert.Contains(@"""state"":""CREATED""", result);
    }

    [Fact]
    public void IncreasedEdgeCountShowsGreenLabelInPlantUML()
    {
        var opts = MakeOptions();
        var remote = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        var local = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        local.AddDependency(
            RelativePath.Directory(_fs.Root, "./Domain/Models/Records/"),
            RelativePath.File(_fs.Root, "./Infra/Factories/DependencyParserFactory.cs"));

        var result = new PlantUMLRenderer().RenderDiffView(local, remote, opts.Views[0], opts);

        Assert.Contains("(+1)", result);
        Assert.Contains("#Green", result);
    }

    [Fact]
    public void DecreasedEdgeCountShowsRedLabelInPlantUML()
    {
        var opts = MakeOptions();
        var remote = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        var local = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        local.RemoveDependency(
            RelativePath.File(_fs.Root, "./Infra/Factories/DependencyParserFactory.cs"),
            RelativePath.Directory(_fs.Root, "./Domain/Models/Records/"));

        var result = new PlantUMLRenderer().RenderDiffView(local, remote, opts.Views[0], opts);

        Assert.Contains("(-1)", result);
        Assert.Contains("#Red", result);
    }

    [Fact]
    public void IntraPackageDependencyProducesNoSelfLoopEdge()
    {
        var opts = MakeOptions();
        var graph = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        var recordA = RelativePath.File(_fs.Root, "./Domain/Models/RecordA.cs");
        var recordB = RelativePath.File(_fs.Root, "./Domain/Models/RecordB.cs");

        graph.UpsertProjectItem(recordA, ProjectItemType.File);
        graph.UpsertProjectItem(recordB, ProjectItemType.File);
        graph.AddDependency(
            RelativePath.File(_fs.Root, "./Domain/Models/RecordA.cs"),
            RelativePath.File(_fs.Root, "./Domain/Models/RecordB.cs"));

        var result = Minify(new JsonRenderer().RenderView(graph, opts.Views[0], opts));

        Assert.DoesNotContain(@"""fromPackage"":""Domain"",""toPackage"":""Domain""", result);
    }

    [Fact]
    public async Task SaveViewCreatesFileWithCorrectName()
    {
        var saveDir = Path.Combine(_fs.Root, "out");
        var opts = MakeOptions(saveLocation: saveDir);
        await new JsonRenderer().SaveViewToFileAsync("content", opts.Views[0], opts);

        Assert.True(File.Exists(Path.Combine(saveDir, "Archlens-testView.json")));
    }

    [Fact]
    public async Task SaveDiffViewCreatesFileWithDiffSuffix()
    {
        var saveDir = Path.Combine(_fs.Root, "out");
        var opts = MakeOptions(saveLocation: saveDir);
        await new JsonRenderer().SaveViewToFileAsync("content", opts.Views[0], opts, diff: true);

        Assert.True(File.Exists(Path.Combine(saveDir, "Archlens-diff-testView.json")));
    }

    [Fact]
    public async Task SaveViewCreatesDirectoryIfMissing()
    {
        var saveDir = Path.Combine(_fs.Root, "does", "not", "exist");
        var opts = MakeOptions(saveLocation: saveDir);
        await new JsonRenderer().SaveViewToFileAsync("x", opts.Views[0], opts);

        Assert.True(Directory.Exists(saveDir));
    }

    [Fact]
    public async Task SaveViewWritesCorrectContent()
    {
        var saveDir = Path.Combine(_fs.Root, "out2");
        var opts = MakeOptions(saveLocation: saveDir);
        await new JsonRenderer().SaveViewToFileAsync("hello world", opts.Views[0], opts);

        Assert.Equal("hello world", await File.ReadAllTextAsync(Directory.GetFiles(saveDir).Single()));
    }

    [Fact]
    public async Task RenderViewsAndSaveCreatesOneFilePerView()
    {
        var saveDir = Path.Combine(_fs.Root, "multi");
        var opts = new RenderOptions(
            BaseOptions: new(ProjectRoot: _fs.Root, ProjectName: "Archlens", FullRootPath: _fs.Root),
            Format: default,
            Views: [new View("viewA", [], []), new View("viewB", [], [])],
            SaveLocation: saveDir);

        await new JsonRenderer().RenderViewsAndSaveToFiles(
            TestDependencyGraph.MakeDependencyGraph(_fs.Root), opts, ct: default);

        var files = Directory.GetFiles(saveDir).Select(Path.GetFileName).ToHashSet();
        Assert.Contains("Archlens-viewA.json", files);
        Assert.Contains("Archlens-viewB.json", files);
    }

    [Fact]
    public async Task RenderDiffViewsAndSaveCreatesOneFilePerView()
    {
        var saveDir = Path.Combine(_fs.Root, "diff-multi");
        var opts = new RenderOptions(
            BaseOptions: new(ProjectRoot: _fs.Root, ProjectName: "Archlens", FullRootPath: _fs.Root),
            Format: default,
            Views: [new View("viewA", [], []), new View("viewB", [], [])],
            SaveLocation: saveDir);
        var graph = TestDependencyGraph.MakeDependencyGraph(_fs.Root);

        await new JsonRenderer().RenderDiffViewsAndSaveToFiles(graph, graph, opts, ct: default);

        var files = Directory.GetFiles(saveDir).Select(Path.GetFileName).ToHashSet();
        Assert.Contains("Archlens-diff-viewA.json", files);
        Assert.Contains("Archlens-diff-viewB.json", files);
    }

    [Fact]
    public async Task EmptyViewListDoesNotThrow()
    {
        var opts = new RenderOptions(
            BaseOptions: new(ProjectRoot: _fs.Root, ProjectName: "P", FullRootPath: _fs.Root),
            Format: default,
            Views: [],
            SaveLocation: Path.Combine(_fs.Root, "out"));

        var ex = await Record.ExceptionAsync(() =>
            new JsonRenderer().RenderViewsAndSaveToFiles(
                TestDependencyGraph.MakeDependencyGraph(_fs.Root), opts, ct: default));

        Assert.Null(ex);
    }


    [Fact]
    public void JsonOutputStartsAndEndsWithBraces()
    {
        var opts = MakeOptions();
        var result = new JsonRenderer().RenderView(
            TestDependencyGraph.MakeDependencyGraph(_fs.Root), opts.Views[0], opts);

        Assert.StartsWith("{", result);
        Assert.EndsWith("}", result);
    }

    [Fact]
    public void PlantUMLOutputHasCorrectEnvelope()
    {
        var opts = MakeOptions();
        var result = new PlantUMLRenderer().RenderView(
            TestDependencyGraph.MakeDependencyGraph(_fs.Root), opts.Views[0], opts);

        Assert.StartsWith("@startuml", result);
        Assert.EndsWith("@enduml", result.Trim());
    }

    [Fact]
    public void ViewNameAppearsInJsonOutput()
    {
        var opts = MakeOptions(viewName: "specialView");
        var result = new JsonRenderer().RenderView(
            TestDependencyGraph.MakeDependencyGraph(_fs.Root), opts.Views[0], opts);

        Assert.Contains("specialView", result);
    }

    [Fact]
    public void ViewNameAppearsInPlantUMLTitle()
    {
        var opts = MakeOptions(viewName: "specialView");
        var result = new PlantUMLRenderer().RenderView(
            TestDependencyGraph.MakeDependencyGraph(_fs.Root), opts.Views[0], opts);

        Assert.Contains("title specialView", result);
    }


    [Fact]
    public void JsonRenderIsDeterministic()
    {
        var opts = MakeOptions();
        var graph = TestDependencyGraph.MakeDependencyGraph(_fs.Root);

        Assert.Equal(
            new JsonRenderer().RenderView(graph, opts.Views[0], opts),
            new JsonRenderer().RenderView(graph, opts.Views[0], opts));
    }

    [Fact]
    public void PlantUMLRenderIsDeterministic()
    {
        var opts = MakeOptions();
        var graph = TestDependencyGraph.MakeDependencyGraph(_fs.Root);

        Assert.Equal(
            new PlantUMLRenderer().RenderView(graph, opts.Views[0], opts),
            new PlantUMLRenderer().RenderView(graph, opts.Views[0], opts));
    }

    [Fact]
    public void DiffRenderIsDeterministic()
    {
        var opts = MakeOptions();
        var local = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        var remote = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        local.AddDependency(
            RelativePath.Directory(_fs.Root, "./Domain/Models/Records/"),
            RelativePath.File(_fs.Root, "./Infra/Factories/DependencyParserFactory.cs"));

        Assert.Equal(
            new JsonRenderer().RenderDiffView(local, remote, opts.Views[0], opts),
            new JsonRenderer().RenderDiffView(local, remote, opts.Views[0], opts));
    }
}
