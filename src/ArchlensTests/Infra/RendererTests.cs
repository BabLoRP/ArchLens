using System.Text.RegularExpressions;
using Archlens.Domain;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using Archlens.Infra.Renderers;
using ArchlensTests.Utils;

namespace ArchlensTests.Infra;

public sealed class RendererTests : IDisposable
{
    private readonly TestFileSystem _fs = new();
    public void Dispose() => _fs.Dispose();
    private static DependencyGraphNode MakeGraph(string rootPath)
    {
        var root = TestGraphs.Node(rootPath, "Archlens", "./");

        var domain = TestGraphs.Node(rootPath, "Domain", "./Domain/");
        var factory = TestGraphs.Node(rootPath, "Factories", "./Domain/Factories/");
        var models = TestGraphs.Node(rootPath, "Models", "./Domain/Models/");
        var records = TestGraphs.Node(rootPath, "Records", "./Domain/Models/Records/");
        var enums = TestGraphs.Node(rootPath, "Enums", "./Domain/Models/Enums/");
        var utils = TestGraphs.Node(rootPath, "Utils", "./Domain/Utils/");

        var infra = TestGraphs.Node(rootPath, "Infra", "./Infra/");

        root.AddChild(domain);
        domain.AddChild(factory);
        domain.AddChild(models);
        domain.AddChild(utils);
        models.AddChild(records);
        models.AddChild(enums);

        root.AddChild(infra);

        factory.AddChild(TestGraphs.Leaf(rootPath, "DependencyParserFactory.cs",
            "./Domain/Factories/DependencyParserFactory.cs",
            "Domain.Interfaces", "Domain.Models.Enums", "Domain.Models.Records", "Infra"));

        factory.AddChild(TestGraphs.Leaf(rootPath, "RendererFactory.cs",
            "./Domain/Factories/RendererFactory.cs",
            "Domain.Interfaces", "Domain.Models.Enums", "Infra"));

        records.AddChild(TestGraphs.Leaf(rootPath, "Options.cs",
            "./Domain/Models/Records/Options.cs",
            "Domain.Models.Enums"));

        models.AddChild(TestGraphs.Leaf(rootPath, "DependencyGraph.cs",
            "./Domain/Models/DependencyGraph.cs",
            "Domain.Utils"));


        infra.AddChild(TestGraphs.Leaf(rootPath, "ConfigManager.cs",
            "./Infra/ConfigManager.cs",
            "Domain.Models.Records"));

        DependencyAggregator.RecomputeAggregates(root);

        return root;
    }

    private static DependencyGraphNode MakeRemoteGraph(string rootPath)
    {
        var root = TestGraphs.Node(rootPath, "Archlens", "./");

        var domain = TestGraphs.Node(rootPath, "Domain", "./Domain/");
        var factory = TestGraphs.Node(rootPath, "Factories", "./Domain/Factories/");
        var models = TestGraphs.Node(rootPath, "Models", "./Domain/Models/");
        var records = TestGraphs.Node(rootPath, "Records", "./Domain/Models/Records/");
        var enums = TestGraphs.Node(rootPath, "Enums", "./Domain/Models/Enums/");
        var utils = TestGraphs.Node(rootPath, "Utils", "./Domain/Utils/");

        var infra = TestGraphs.Node(rootPath, "Infra", "./Infra/");

        root.AddChild(domain);
        domain.AddChild(factory);
        domain.AddChild(models);
        domain.AddChild(utils);
        models.AddChild(records);
        models.AddChild(enums);

        root.AddChild(infra);

        factory.AddChild(TestGraphs.Leaf(rootPath, "DependencyParserFactory.cs",
            "./Domain/Factories/DependencyParserFactory.cs",
            "Domain.Interfaces", "Domain.Models.Enums", "Domain.Models.Records")); //no infra

        factory.AddChild(TestGraphs.Leaf(rootPath, "RendererFactory.cs",
            "./Domain/Factories/RendererFactory.cs",
            "Domain.Interfaces", "Domain.Models.Enums", "Infra"));

        records.AddChild(TestGraphs.Leaf(rootPath, "Options.cs",
            "./Domain/Models/Records/Options.cs",
            "Domain.Models.Enums"));

        models.AddChild(TestGraphs.Leaf(rootPath, "DependencyGraph.cs",
            "./Domain/Models/DependencyGraph.cs",
            "Domain.Utils"));


        infra.AddChild(TestGraphs.Leaf(rootPath, "ConfigManager.cs",
            "./Infra/ConfigManager.cs",
            "Domain.Models.Records", "Domain.Models.Enums")); //added enums

        DependencyAggregator.RecomputeAggregates(root);

        return root;
    }

    private RenderOptions MakeOptions() => new(
        BaseOptions: new(
            ProjectRoot: _fs.Root,
            ProjectName: "Archlens",
            FullRootPath: _fs.Root
        ),
        Format: default,
        Views: [new View("completeView", [], []), new View("ignoringView", [], ["Infra"])],
        SaveLocation: null
    );


    [Fact]
    public void JsonRendererRendersCorrectly()
    {
        JsonRenderer renderer = new();

        var opts = MakeOptions();
        var root = MakeGraph(opts.BaseOptions.ProjectRoot);
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
        var root = MakeGraph(opts.BaseOptions.ProjectRoot);
        string result = renderer.RenderView(root, opts.Views[0], opts);

        Assert.NotEmpty(result);
        Assert.StartsWith("@startuml", result);
        Assert.Contains("title completeView", result);
        Assert.Contains("package \"Domain\" as Domain {", result);
        Assert.Contains("Infra", result);
        Assert.EndsWith("@enduml", result);
    }

    [Fact]
    public void PlantUMLRendererIgnoresPackages()
    {
        PlantUMLRenderer renderer = new();

        var opts = MakeOptions();
        var root = MakeGraph(opts.BaseOptions.ProjectRoot);
        string result = renderer.RenderView(root, opts.Views[1], opts);

        Assert.NotEmpty(result);
        Assert.StartsWith("@startuml", result);
        Assert.Contains("title ignoringView", result);
        Assert.Contains("package \"Domain\" as Domain {", result);
        Assert.DoesNotContain("Infra", result);
        Assert.EndsWith("@enduml", result);
    }

    [Fact]
    public void JsonRendererRendersDiffCorrectly()
    {
        JsonRenderer renderer = new();

        var opts = MakeOptions();
        var root = MakeGraph(opts.BaseOptions.ProjectRoot);
        var remoteRoot = MakeRemoteGraph(opts.BaseOptions.ProjectRoot);
        var result = renderer.RenderDiffView(root, remoteRoot, opts.Views[0], opts);

        var newEdge = $$"""
                        "state": "CREATED",
                        "fromPackage": "Domain",
                        "toPackage": "Infra",
                        "label": "2 (+1)",
                        """;

        var deletedEdge = $$"""
                        "state": "DELETED",
                        "fromPackage": "Infra",
                        "toPackage": "Domain",
                        "label": "1 (-1)",
                        """;


        //Basic assertions
        Assert.NotEmpty(result);
        Assert.StartsWith("{", result);
        Assert.Contains("\"title\":", result);
        Assert.Contains("\"packages\": [", result);
        Assert.Contains("\"edges\": [", result);
        Assert.EndsWith("}", result);

        //Simpler to compare without whitespace
        result = Regex.Replace(result, @"\s*", "");
        newEdge = Regex.Replace(newEdge, @"\s*", "");
        deletedEdge = Regex.Replace(deletedEdge, @"\s*", "");

        //Diff specific assertions
        Assert.Contains(newEdge, result);
        Assert.Contains(deletedEdge, result);
    }

    [Fact]
    public void PlantUMLRendererRendersDiffCorrectly()
    {
        PlantUMLRenderer renderer = new();

        var opts = MakeOptions();
        var root = MakeGraph(opts.BaseOptions.ProjectRoot);
        var remoteRoot = MakeRemoteGraph(opts.BaseOptions.ProjectRoot);
        string result = renderer.RenderDiffView(root, remoteRoot, opts.Views[0], opts);

        var newEdge = "Domain-->Infra #Green : 2 (+1)";
        var deletedEdge = "Infra-->Domain #Red : 1 (-1)";

        //Basic assertions
        Assert.NotEmpty(result);
        Assert.StartsWith("@startuml", result);
        Assert.Contains("title completeView", result);
        Assert.Contains("package \"Domain\" as Domain {", result);
        Assert.Contains("Infra", result);
        Assert.EndsWith("@enduml", result);

        //Diff specific assertions
        Assert.Contains(newEdge, result);
        Assert.Contains(deletedEdge, result);
    }
}
