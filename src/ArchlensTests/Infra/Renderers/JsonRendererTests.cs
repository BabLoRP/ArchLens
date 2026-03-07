using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using Archlens.Infra.Renderers;
using ArchlensTests.Utils;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ArchlensTests.Infra.Renderers;

public sealed class JsonRendererTests : IDisposable
{
    private readonly TestFileSystem _fs = new();
    private readonly JsonRenderer _renderer = new();

    public void Dispose() => _fs.Dispose();

    private RenderOptions Opts(
        string viewName = "testView",
        IReadOnlyList<Package>? packages = null,
        IReadOnlyList<string>? ignore = null) => new(
        BaseOptions: new(
            ProjectRoot: _fs.Root,
            ProjectName: "Archlens",
            FullRootPath: _fs.Root),
        Format: default,
        Views: [new View(viewName, packages ?? [], ignore ?? [])],
        SaveLocation: null);

    private JsonObject ParseJson(string json) =>
        JsonNode.Parse(json)!.AsObject();

    private JsonArray Packages(JsonObject root) =>
        root["packages"]!.AsArray();

    private JsonArray Edges(JsonObject root) =>
        root["edges"]!.AsArray();

    private ProjectDependencyGraph DefaultGraph() =>
        TestDependencyGraph.MakeDependencyGraph(_fs.Root);

    private string Render(ProjectDependencyGraph graph, RenderOptions opts) =>
        _renderer.RenderView(graph, opts.Views[0], opts);

    private string RenderDiff(
        ProjectDependencyGraph local,
        ProjectDependencyGraph remote,
        RenderOptions opts) =>
        _renderer.RenderDiffView(local, remote, opts.Views[0], opts);


    [Fact]
    public void Output_IsValidJson()
    {
        var result = Render(DefaultGraph(), Opts());
        var ex = Record.Exception(() => JsonDocument.Parse(result));
        Assert.Null(ex);
    }

    [Fact]
    public void FileExtension_IsJson()
    {
        Assert.Equal("json", _renderer.FileExtension);
    }

    [Fact]
    public void Output_ContainsTopLevelFields_Title_Packages_Edges()
    {
        var doc = ParseJson(Render(DefaultGraph(), Opts()));
        Assert.NotNull(doc["title"]);
        Assert.NotNull(doc["packages"]);
        Assert.NotNull(doc["edges"]);
    }

    [Fact]
    public void Title_MatchesViewName()
    {
        var doc = ParseJson(Render(DefaultGraph(), Opts(viewName: "myView")));
        Assert.Equal("myView", doc["title"]!.GetValue<string>());
    }

    [Fact]
    public void Packages_ContainExpectedNodes()
    {
        var packages = Packages(ParseJson(Render(DefaultGraph(), Opts())));
        var names = packages.Select(p => p!["name"]!.GetValue<string>()).ToList();

        Assert.Contains("Domain", names);
        Assert.Contains("Infra", names);
    }

    [Fact]
    public void Packages_HaveRequiredFields_Name_Path_Type_State()
    {
        var packages = Packages(ParseJson(Render(DefaultGraph(), Opts())));
        Assert.All(packages, node =>
        {
            Assert.NotNull(node!["name"]);
            Assert.NotNull(node["path"]);
            Assert.NotNull(node["type"]);
            Assert.NotNull(node["state"]);
        });
    }

    [Fact]
    public void Packages_AreOrderedAlphabetically()
    {
        var packages = Packages(ParseJson(Render(DefaultGraph(), Opts())));
        var paths = packages.Select(p => p!["path"]!.GetValue<string>()).ToList();
        Assert.Equal(paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList(), paths);
    }

    [Fact]
    public void Package_TypeField_IsSerializedAsString()
    {
        var packages = Packages(ParseJson(Render(DefaultGraph(), Opts())));
        Assert.All(packages, node =>
            Assert.True(
                node!["type"]!.GetValueKind() == JsonValueKind.String,
                $"Expected 'type' to be a string, got {node["type"]!.GetValueKind()}"));
    }

    [Fact]
    public void Package_StateField_IsNeutral_ForUnchangedGraph()
    {
        var packages = Packages(ParseJson(Render(DefaultGraph(), Opts())));
        Assert.All(packages, node =>
            Assert.Equal("NEUTRAL", node!["state"]!.GetValue<string>()));
    }

    [Fact]
    public void Packages_EmptyList_WhenAllIgnored()
    {
        var opts = Opts(ignore: ["./Domain/", "./Infra/", "./Application/"]);
        var packages = Packages(ParseJson(Render(DefaultGraph(), opts)));
        Assert.Empty(packages);
    }

    [Fact]
    public void Packages_DoNotIncludeIgnoredPackage()
    {
        var opts = Opts(ignore: ["./Infra/"]);
        var packages = Packages(ParseJson(Render(DefaultGraph(), opts)));
        var names = packages.Select(p => p!["name"]!.GetValue<string>()).ToList();
        Assert.DoesNotContain("Infra", names);
    }

    [Fact]
    public void Packages_DepthOne_HidesSubPackages()
    {
        var opts = Opts(packages: [new Package("./Domain/", 1)]);
        var packages = Packages(ParseJson(Render(DefaultGraph(), opts)));
        var names = packages.Select(p => p!["name"]!.GetValue<string>()).ToList();

        Assert.Contains("Domain", names);
        Assert.DoesNotContain("Models", names);
        Assert.DoesNotContain("Records", names);
    }

    [Fact]
    public void Packages_DepthTwo_ShowsDirectChildren()
    {
        var opts = Opts(packages: [new Package("./Domain/", 2)]);
        var packages = Packages(ParseJson(Render(DefaultGraph(), opts)));
        var names = packages.Select(p => p!["name"]!.GetValue<string>()).ToList();

        Assert.Contains("Domain", names);
        Assert.Contains("Models", names);
    }

    [Fact]
    public void Edges_HaveRequiredFields_State_FromPackage_ToPackage_Label_Relations()
    {
        var edges = Edges(ParseJson(Render(DefaultGraph(), Opts())));
        Assert.All(edges, node =>
        {
            Assert.NotNull(node!["state"]);
            Assert.NotNull(node["fromPackage"]);
            Assert.NotNull(node["toPackage"]);
            Assert.NotNull(node["label"]);
            Assert.NotNull(node["relations"]);
        });
    }

    [Fact]
    public void Edges_AreOrderedAlphabetically_ByFromThenTo()
    {
        var edges = Edges(ParseJson(Render(DefaultGraph(), Opts())));
        var pairs = edges
            .Select(e => (
                From: e!["fromPackage"]!.GetValue<string>(),
                To: e!["toPackage"]!.GetValue<string>()))
            .ToList();

        var sorted = pairs
            .OrderBy(p => p.From, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.To, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Equal(sorted, pairs);
    }

    [Fact]
    public void Edges_NoDuplicateFromToPairs()
    {
        var edges = Edges(ParseJson(Render(DefaultGraph(), Opts())));
        var pairs = edges
            .Select(e => (
                From: e!["fromPackage"]!.GetValue<string>(),
                To: e!["toPackage"]!.GetValue<string>()))
            .ToList();

        Assert.Equal(pairs.Count, pairs.Distinct().Count());
    }

    [Fact]
    public void Edge_RelationsField_IsArray()
    {
        var edges = Edges(ParseJson(Render(DefaultGraph(), Opts())));
        Assert.All(edges, node =>
            Assert.True(
                node!["relations"]!.GetValueKind() == JsonValueKind.Array,
                "Expected 'relations' to be a JSON array"));
    }

    [Fact]
    public void Edge_Relations_HaveFromFileAndToFile()
    {
        var edges = Edges(ParseJson(Render(DefaultGraph(), Opts())));
        var withRelations = edges.Where(e => e!["relations"]!.AsArray().Count > 0).ToList();

        Assert.All(withRelations, edge =>
            Assert.All(edge!["relations"]!.AsArray(), rel =>
            {
                Assert.NotNull(rel!["from_file"]);
                Assert.NotNull(rel["to_file"]);
                Assert.NotNull(rel["from_file"]!["name"]);
                Assert.NotNull(rel["from_file"]!["path"]);
                Assert.NotNull(rel["to_file"]!["name"]);
                Assert.NotNull(rel["to_file"]!["path"]);
            }));
    }

    [Fact]
    public void Edge_NoSelfEdges()
    {
        var edges = Edges(ParseJson(Render(DefaultGraph(), Opts())));
        Assert.All(edges, node =>
            Assert.NotEqual(
                node!["fromPackage"]!.GetValue<string>(),
                node!["toPackage"]!.GetValue<string>()));
    }

    [Fact]
    public void Edges_EmptyList_WhenAllPackagesIgnored()
    {
        var opts = Opts(ignore: ["./Domain/", "./Infra/", "./Application/"]);
        var edges = Edges(ParseJson(Render(DefaultGraph(), opts)));
        Assert.Empty(edges);
    }

    [Fact]
    public void EdgeLabel_IsCount_WhenDeltaIsZero()
    {
        var edges = Edges(ParseJson(Render(DefaultGraph(), Opts())));
        var neutral = edges.FirstOrDefault(e => e!["state"]!.GetValue<string>() == "NEUTRAL");
        Assert.NotNull(neutral);

        var label = neutral!["label"]!.GetValue<string>();
        Assert.Matches(@"^\d+$", label);
    }

    [Fact]
    public void EdgeLabel_IncludesPositiveDelta_WhenEdgeCreated()
    {
        var opts = Opts();
        var remote = DefaultGraph();
        var local = DefaultGraph();

        var records = RelativePath.Directory(_fs.Root, "./Domain/Models/Records/");
        var factory = RelativePath.File(_fs.Root, "./Infra/Factories/DependencyParserFactory.cs");
        local.AddDependency(records, factory);
        local.AddDependency(records, factory);

        var edges = Edges(ParseJson(RenderDiff(local, remote, opts)));
        var created = edges.FirstOrDefault(e =>
            e!["fromPackage"]!.GetValue<string>() == "Domain" &&
            e!["toPackage"]!.GetValue<string>() == "Infra" &&
            e!["state"]!.GetValue<string>() == "CREATED");

        Assert.NotNull(created);
        var label = created!["label"]!.GetValue<string>();
        Assert.Contains("+", label);
    }

    [Fact]
    public void EdgeLabel_IncludesNegativeDelta_WhenEdgeDeleted()
    {
        var opts = Opts();
        var remote = DefaultGraph();
        var local = DefaultGraph();

        var factory = RelativePath.File(_fs.Root, "./Infra/Factories/DependencyParserFactory.cs");
        var records = RelativePath.Directory(_fs.Root, "./Domain/Models/Records/");
        local.RemoveDependency(factory, records);

        var edges = Edges(ParseJson(RenderDiff(local, remote, opts)));
        var deleted = edges.FirstOrDefault(e => e!["state"]!.GetValue<string>() == "DELETED");

        Assert.NotNull(deleted);
        var label = deleted!["label"]!.GetValue<string>();
        Assert.Contains("-", label);
    }

    [Fact]
    public void EdgeLabel_IsPlainCount_WhenDeltaIsZeroInDiff()
    {
        var opts = Opts();
        var graph = DefaultGraph();
        var edges = Edges(ParseJson(RenderDiff(graph, graph, opts)));

        Assert.All(edges, node =>
        {
            var label = node!["label"]!.GetValue<string>();
            Assert.Matches(@"^\d+$", label);
        });
    }

    [Fact]
    public void Diff_IdenticalGraphs_AllNodesAreNeutral()
    {
        var graph = DefaultGraph();
        var packages = Packages(ParseJson(RenderDiff(graph, graph, Opts())));
        Assert.All(packages, node =>
            Assert.Equal("NEUTRAL", node!["state"]!.GetValue<string>()));
    }

    [Fact]
    public void Diff_IdenticalGraphs_AllEdgesAreNeutral()
    {
        var graph = DefaultGraph();
        var edges = Edges(ParseJson(RenderDiff(graph, graph, Opts())));
        Assert.All(edges, node =>
            Assert.Equal("NEUTRAL", node!["state"]!.GetValue<string>()));
    }

    [Fact]
    public void Diff_NodeOnlyInLocal_IsMarkedCreated()
    {
        var local = DefaultGraph();
        var remote = DefaultGraph();
        remote.RemoveProjectItemRecursive(RelativePath.Directory(_fs.Root, "./Infra/"));

        var packages = Packages(ParseJson(RenderDiff(local, remote, Opts())));
        var infra = packages.FirstOrDefault(p => p!["name"]!.GetValue<string>() == "Infra");

        Assert.NotNull(infra);
        Assert.Equal("CREATED", infra!["state"]!.GetValue<string>());
    }

    [Fact]
    public void Diff_NodeOnlyInRemote_IsMarkedDeleted()
    {
        var local = DefaultGraph();
        var remote = DefaultGraph();
        local.RemoveProjectItemRecursive(RelativePath.Directory(_fs.Root, "./Infra/"));

        var packages = Packages(ParseJson(RenderDiff(local, remote, Opts())));
        var infra = packages.FirstOrDefault(p => p!["name"]!.GetValue<string>() == "Infra");

        Assert.NotNull(infra);
        Assert.Equal("DELETED", infra!["state"]!.GetValue<string>());
    }

    [Fact]
    public void Diff_NewEdge_IsMarkedCreated()
    {
        var remote = DefaultGraph();
        var local = DefaultGraph();
        var records = RelativePath.Directory(_fs.Root, "./Domain/Models/Records/");
        var factory = RelativePath.File(_fs.Root, "./Infra/Factories/DependencyParserFactory.cs");
        local.AddDependency(records, factory);

        var edges = Edges(ParseJson(RenderDiff(local, remote, Opts())));
        var created = edges.Where(e => e!["state"]!.GetValue<string>() == "CREATED").ToList();
        Assert.NotEmpty(created);
    }

    [Fact]
    public void Diff_RemovedEdge_IsMarkedDeleted()
    {
        var remote = DefaultGraph();
        var local = DefaultGraph();
        var factory = RelativePath.File(_fs.Root, "./Infra/Factories/DependencyParserFactory.cs");
        var records = RelativePath.Directory(_fs.Root, "./Domain/Models/Records/");
        local.RemoveDependency(factory, records);

        var edges = Edges(ParseJson(RenderDiff(local, remote, Opts())));
        var deleted = edges.Where(e => e!["state"]!.GetValue<string>() == "DELETED").ToList();
        Assert.NotEmpty(deleted);
    }

    [Fact]
    public void Render_IsDeterministic()
    {
        var graph = DefaultGraph();
        var opts = Opts();
        Assert.Equal(Render(graph, opts), Render(graph, opts));
    }

    [Fact]
    public void RenderDiff_IsDeterministic()
    {
        var local = DefaultGraph();
        var remote = DefaultGraph();
        var opts = Opts();
        Assert.Equal(RenderDiff(local, remote, opts), RenderDiff(local, remote, opts));
    }

    [Fact]
    public void EmptyGraph_RendersValidJsonWithEmptyPackagesAndEdges()
    {
        var graph = new ProjectDependencyGraph(_fs.Root);
        var rootPath = RelativePath.Directory(_fs.Root, _fs.Root);
        graph.UpsertProjectItem(rootPath, ProjectItemType.Directory);

        var doc = ParseJson(Render(graph, Opts()));

        Assert.Empty(Packages(doc));
        Assert.Empty(Edges(doc));
    }

    [Fact]
    public void Output_IsWrittenWithIndentation()
    {
        var result = Render(DefaultGraph(), Opts());
        Assert.Contains("\n", result);
    }
}