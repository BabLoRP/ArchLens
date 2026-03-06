using Archlens.Domain.Models.Records;
using Archlens.Infra.Renderers;
using ArchlensTests.Utils;
using System.Text.RegularExpressions;

namespace ArchlensTests.Infra;

public sealed class RendererTests : IDisposable
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
                        "label": "2 (+2)"
                        """;

        var deletedEdge = $$"""
                        "state": "DELETED",
                        "fromPackage": "Infra",
                        "toPackage": "Domain",
                        "label": "4 (-1)"
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
}
