using System.Text.RegularExpressions;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using Archlens.Infra.Renderers;
using ArchlensTests.Utils;

namespace ArchlensTests.Domain;

public sealed partial class RendererBaseTests : IDisposable
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
        Views: [new View("completeView", [], []), new View("ignoringView", [], ["./Warehouse/"])],
        SaveLocation: $"{_fs.Root}/diagrams"
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
        SaveLocation: saveLocation ?? $"{_fs.Root}/diagrams");

    private static string Minify(string s) => StringOneOrMoreRegex().Replace(s, "");


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
        Assert.Contains("package \"Inventory\" as Inventory", result);
        Assert.Contains("Warehouse", result);
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
        Assert.Contains("package \"Inventory\" as Inventory", result);
        Assert.DoesNotContain("Warehouse", result);
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

        var labels = RelativePath.Directory(rootPath, "./Inventory/Stock/Labels/");
        var stockSupplier = RelativePath.File(rootPath, "./Warehouse/Suppliers/StockSupplier.cs");

        localGraph.RemoveDependency(stockSupplier, labels); // local removes one Warehouse -> Inventory
        localGraph.AddDependency(labels, stockSupplier);
        localGraph.AddDependency(labels, stockSupplier);

        var result = renderer.RenderDiffView(localGraph, remoteGraph, opts.Views[0], opts);

        var newEdge = $$"""
                        "state": "CREATED",
                        "fromPackage": "Inventory",
                        "toPackage": "Warehouse",
                        "label": "2 (+2)",
                        """;

        var deletedEdge = $$"""
                        "state": "DELETED",
                        "fromPackage": "Warehouse",
                        "toPackage": "Inventory",
                        "label": "4 (-1)",
                        """;


        Assert.NotEmpty(result);
        Assert.StartsWith("{", result);
        Assert.Contains("\"title\":", result);
        Assert.Contains("\"packages\": [", result);
        Assert.Contains("\"edges\": [", result);
        Assert.EndsWith("}", result);

        result = StringZeroMoreRegex().Replace(result, "");
        newEdge = StringZeroMoreRegex().Replace(newEdge, "");
        deletedEdge = StringZeroMoreRegex().Replace(deletedEdge, "");

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

        var labels = RelativePath.Directory(rootPath, "./Inventory/Stock/Labels/");
        var stockSupplier = RelativePath.File(rootPath, "./Warehouse/Suppliers/StockSupplier.cs");

        localGraph.AddDependency(labels, stockSupplier);
        localGraph.RemoveDependency(stockSupplier, labels);

        string result = renderer.RenderDiffView(localGraph, remoteGraph, opts.Views[0], opts);

        var newEdge = "Inventory --> Warehouse #Green : 1 (+1)";
        var deletedEdge = "Warehouse --> Inventory #Red : 4 (-1)";

        Assert.NotEmpty(result);
        Assert.StartsWith("@startuml", result);
        Assert.Contains("title completeView", result);
        Assert.Contains("package \"Inventory\" as Inventory", result);
        Assert.Contains("Warehouse", result);
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

        var froms = FromRegex().Matches(result)
                         .Select(m => m.Groups[1].Value)
                         .ToList();

        Assert.Equal(froms.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList(), froms);
    }

    [Fact]
    public void IgnoredPackageAbsentFromOutput()
    {
        var opts = MakeOptions(ignore: ["./Warehouse/"]);
        var result = new PlantUMLRenderer().RenderView(
            TestDependencyGraph.MakeDependencyGraph(_fs.Root), opts.Views[0], opts);

        Assert.DoesNotContain("Warehouse", result);
    }

    [Fact]
    public void NonIgnoredPackagesPresentInOutput()
    {
        var opts = MakeOptions();
        var result = new PlantUMLRenderer().RenderView(
            TestDependencyGraph.MakeDependencyGraph(_fs.Root), opts.Views[0], opts);

        Assert.Contains("Inventory", result);
        Assert.Contains("Warehouse", result);
    }

    [Fact]
    public void NoEdgesReferenceIgnoredPackage()
    {
        var opts = MakeOptions(ignore: ["./Warehouse/"]);
        var result = new JsonRenderer().RenderView(
            TestDependencyGraph.MakeDependencyGraph(_fs.Root), opts.Views[0], opts);

        var edgeSection = result.Contains("\"edges\"")
            ? result[result.IndexOf("\"edges\"")..]
            : "";
        Assert.DoesNotContain("\"Warehouse\"", edgeSection);
    }

    [Fact]
    public void DepthOneHidesSubPackages()
    {
        var opts = MakeOptions(packages: [new Package("./Inventory/", 1)]);
        var result = new JsonRenderer().RenderView(
            TestDependencyGraph.MakeDependencyGraph(_fs.Root), opts.Views[0], opts);

        Assert.DoesNotContain("Stock", result);
    }

    [Fact]
    public void DepthTwoShowsDirectChildren()
    {
        var opts = MakeOptions(packages: [new Package("./Inventory/", 2)]);
        var result = new JsonRenderer().RenderView(
            TestDependencyGraph.MakeDependencyGraph(_fs.Root), opts.Views[0], opts);

        Assert.Contains("Stock", result);
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
        remote.RemoveProjectItemRecursive(RelativePath.Directory(_fs.Root, "./Warehouse/"));

        var result = Minify(new JsonRenderer().RenderDiffView(local, remote, opts.Views[0], opts));

        Assert.Contains(@"""state"":""CREATED""", result);
    }

    [Fact]
    public void NodeOnlyInRemoteIsMarkedDeleted()
    {
        var opts = MakeOptions();
        var remote = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        var local = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        local.RemoveProjectItemRecursive(RelativePath.Directory(_fs.Root, "./Warehouse/"));

        var result = Minify(new JsonRenderer().RenderDiffView(local, remote, opts.Views[0], opts));

        Assert.Contains(@"""state"":""DELETED""", result);
    }

    [Fact]
    public void UnchangedEdgesAreNeutral()
    {
        var opts = MakeOptions();
        var graph = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        var result = Minify(new JsonRenderer().RenderDiffView(graph, graph, opts.Views[0], opts));

        Assert.Empty(StateRegex().Matches(result));
    }

    [Fact]
    public void NewEdgeIsMarkedCreated()
    {
        var opts = MakeOptions();
        var remote = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        var local = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        local.AddDependency(
            RelativePath.Directory(_fs.Root, "./Inventory/Stock/Labels/"),
            RelativePath.File(_fs.Root, "./Warehouse/Suppliers/StockSupplier.cs"));

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
            RelativePath.Directory(_fs.Root, "./Inventory/Stock/Labels/"),
            RelativePath.File(_fs.Root, "./Warehouse/Suppliers/StockSupplier.cs"));

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
            RelativePath.File(_fs.Root, "./Warehouse/Suppliers/StockSupplier.cs"),
            RelativePath.Directory(_fs.Root, "./Inventory/Stock/Labels/"));

        var result = new PlantUMLRenderer().RenderDiffView(local, remote, opts.Views[0], opts);

        Assert.Contains("(-1)", result);
        Assert.Contains("#Red", result);
    }

    [Fact]
    public void IntraPackageDependencyProducesNoSelfLoopEdge()
    {
        var opts = MakeOptions();
        var graph = TestDependencyGraph.MakeDependencyGraph(_fs.Root);
        var itemA = RelativePath.File(_fs.Root, "./Inventory/Stock/ItemA.cs");
        var itemB = RelativePath.File(_fs.Root, "./Inventory/Stock/ItemB.cs");

        graph.UpsertProjectItem(itemA, ProjectItemType.File);
        graph.UpsertProjectItem(itemB, ProjectItemType.File);
        graph.AddDependency(
            RelativePath.File(_fs.Root, "./Inventory/Stock/ItemA.cs"),
            RelativePath.File(_fs.Root, "./Inventory/Stock/ItemB.cs"));

        var result = Minify(new JsonRenderer().RenderView(graph, opts.Views[0], opts));

        Assert.DoesNotContain(@"""fromPackage"":""Inventory"",""toPackage"":""Inventory""", result);
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
            RelativePath.Directory(_fs.Root, "./Inventory/Stock/Labels/"),
            RelativePath.File(_fs.Root, "./Warehouse/Suppliers/StockSupplier.cs"));

        Assert.Equal(
            new JsonRenderer().RenderDiffView(local, remote, opts.Views[0], opts),
            new JsonRenderer().RenderDiffView(local, remote, opts.Views[0], opts));
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex StringOneOrMoreRegex();
    [GeneratedRegex(@"\s*")]
    private static partial Regex StringZeroMoreRegex();
    [GeneratedRegex(@"""fromPackage""\s*:\s*""([^""]+)""")]
    private static partial Regex FromRegex();
    [GeneratedRegex(@"""state"":""(CREATED|DELETED)""")]
    private static partial Regex StateRegex();
}
