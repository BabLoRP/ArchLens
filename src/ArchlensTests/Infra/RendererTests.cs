using Archlens.Domain;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using Archlens.Domain.Utils;
using Archlens.Infra.Renderers;
using ArchlensTests.Utils;
using System.Text.RegularExpressions;

namespace ArchlensTests.Infra;

public sealed class RendererTests : IDisposable
{
    private readonly TestFileSystem _fs = new();
    public void Dispose() => _fs.Dispose();
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

    private static ProjectDependencyGraph MakeRemoteGraph(string rootPath)
    {
        var graph = TestGraphs.MakeGraph(rootPath);

        var rootDir = RelativePath.Directory(rootPath, rootPath);
        var domainDir = RelativePath.Directory(rootPath, "./Domain/");
        var factoriesDir = RelativePath.Directory(rootPath, "./Domain/Factories/");
        var modelsDir = RelativePath.Directory(rootPath, "./Domain/Models");
        var recordsDir = RelativePath.Directory(rootPath, "./Domain/Models/Records/");
        var enumsDir = RelativePath.Directory(rootPath, "./Domain/Models/Enums/");
        var utilsDir = RelativePath.Directory(rootPath, "./Domain/Utils/");

        var root = TestGraphs.AddProjectItem(graph, rootDir, ProjectItemType.Directory);
        var domain = TestGraphs.AddProjectItem(graph, domainDir, ProjectItemType.Directory);
        var factory = TestGraphs.AddProjectItem(graph, factoriesDir, ProjectItemType.Directory);
        var models = TestGraphs.AddProjectItem(graph, modelsDir, ProjectItemType.Directory);
        var records = TestGraphs.AddProjectItem(graph, recordsDir, ProjectItemType.Directory);
        var enums = TestGraphs.AddProjectItem(graph, enumsDir, ProjectItemType.Directory);
        var utils = TestGraphs.AddProjectItem(graph, utilsDir, ProjectItemType.Directory);

        var infraDir = RelativePath.Directory(rootPath, "./Infra/");
        var infra = TestGraphs.AddProjectItem(graph, infraDir, ProjectItemType.Directory);

        graph.AddChild(root, domain);
        graph.AddChild(root, infra);

        graph.AddChild(domain, factory);
        graph.AddChild(domain, models);
        graph.AddChild(domain, utils);

        graph.AddChild(models, records);
        graph.AddChild(models, enums);

        var depFactoryFile = RelativePath.File(rootPath, "./Domain/Factories/DependencyParserFactory.cs");
        
        var interfacesDir = RelativePath.Directory(rootPath, "./Domain/Interfaces/");

        var depFactory = TestGraphs.AddProjectItem(graph, depFactoryFile, ProjectItemType.File, [interfacesDir, enumsDir, recordsDir]);  //no infra

        factory.AddChild(TestGraphs.AddProjectItem(rootPath, "RendererFactory.cs",
            "./Domain/Factories/RendererFactory.cs",
            "Domain.Interfaces", "Domain.Models.Enums", "Infra"));

        records.AddChild(TestGraphs.AddProjectItem(rootPath, "Options.cs",
            "./Domain/Models/Records/Options.cs",
            "Domain.Models.Enums"));

        models.AddChild(TestGraphs.AddProjectItem(rootPath, "DependencyGraph.cs",
            "./Domain/Models/DependencyGraph.cs",
            "Domain.Utils"));


        infra.AddChild(TestGraphs.AddProjectItem(rootPath, "ConfigManager.cs",
            "./Infra/ConfigManager.cs",
            "Domain.Models.Records", "Domain.Models.Enums")); //added enums

        DependencyAggregator.RecomputeAggregates(graph);

        return graph;
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
        var root = MakeDependencyGraph(opts.BaseOptions.ProjectRoot);
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
        var root = MakeDependencyGraph(opts.BaseOptions.ProjectRoot);
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
        var root = MakeDependencyGraph(opts.BaseOptions.ProjectRoot);
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
        var root = MakeDependencyGraph(opts.BaseOptions.ProjectRoot);
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
        var root = MakeDependencyGraph(opts.BaseOptions.ProjectRoot);
        var remoteRoot = MakeRemoteGraph(opts.BaseOptions.ProjectRoot);
        string result = renderer.RenderDiffView(root, remoteRoot, opts.Views[0], opts);

        var newEdge = "Domain-->Infra #Green : 2 (+1)";
        var deletedEdge = "Infra-->Domain #Red : 1 (-1)";

        Assert.NotEmpty(result);
        Assert.StartsWith("@startuml", result);
        Assert.Contains("title completeView", result);
        Assert.Contains("package \"Domain\" as Domain {", result);
        Assert.Contains("Infra", result);
        Assert.EndsWith("@enduml", result);

        Assert.Contains(newEdge, result);
        Assert.Contains(deletedEdge, result);
    }
}
