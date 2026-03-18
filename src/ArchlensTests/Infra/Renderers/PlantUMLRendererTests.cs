using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using Archlens.Infra.Renderers;
using ArchlensTests.Utils;

namespace ArchlensTests.Infra.Renderers;

public sealed class PlantUMLRendererTests : IDisposable
{
    private readonly TestFileSystem _fs = new();
    private readonly PlantUMLRenderer _renderer = new();

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
        SaveLocation: $"{_fs.Root}/diagrams");

    private ProjectDependencyGraph DefaultGraph() =>
        TestDependencyGraph.MakeDependencyGraph(_fs.Root);

    private string Render(ProjectDependencyGraph graph, RenderOptions opts) =>
        _renderer.RenderView(graph, opts.Views[0], opts);

    private string RenderDiff(
        ProjectDependencyGraph local,
        ProjectDependencyGraph remote,
        RenderOptions opts) =>
        _renderer.RenderDiffView(local, remote, opts.Views[0], opts);

#pragma warning disable CA1859 // Use concrete types when possible for improved performance
    private static IReadOnlyList<string> Lines(string output) =>
        [.. output.Split('\n').Select(l => l.TrimEnd('\r'))];
#pragma warning restore CA1859 // Use concrete types when possible for improved performance

    [Fact]
    public void FileExtension_IsPuml()
    {
        Assert.Equal("puml", _renderer.FileExtension);
    }

    [Fact]
    public void Output_StartsWithStartUml()
    {
        var result = Render(DefaultGraph(), Opts());
        Assert.StartsWith("@startuml", result);
    }

    [Fact]
    public void Output_EndsWithEndUml()
    {
        var result = Render(DefaultGraph(), Opts());
        Assert.EndsWith("@enduml", result.Trim());
    }

    [Fact]
    public void Output_ContainsAllowMixing()
    {
        Assert.Contains("allowmixing", Render(DefaultGraph(), Opts()));
    }

    [Fact]
    public void Output_ContainsSkinparamLineTypeOrtho()
    {
        Assert.Contains("skinparam linetype ortho", Render(DefaultGraph(), Opts()));
    }

    [Fact]
    public void Output_ContainsSkinparamBackgroundColour()
    {
        Assert.Contains("skinparam backgroundColor GhostWhite", Render(DefaultGraph(), Opts()));
    }

    [Fact]
    public void StartUml_AppearsOnFirstLine()
    {
        var lines = Lines(Render(DefaultGraph(), Opts()));
        Assert.Equal("@startuml", lines[0]);
    }

    [Fact]
    public void EndUml_AppearsOnLastNonEmptyLine()
    {
        var lines = Lines(Render(DefaultGraph(), Opts()))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
        Assert.Equal("@enduml", lines[^1]);
    }

    [Fact]
    public void Title_ContainsViewName()
    {
        Assert.Contains("title myView", Render(DefaultGraph(), Opts(viewName: "myView")));
    }

    [Fact]
    public void Title_EscapesDoubleQuotes_InViewName()
    {
        var result = Render(DefaultGraph(), Opts(viewName: "my\"View"));
        Assert.Contains("title my\\\"View", result);
    }

    [Fact]
    public void Title_AppearsBeforeAnyPackageDeclaration()
    {
        var result = Render(DefaultGraph(), Opts(viewName: "myView"));
        var titleIndex = result.IndexOf("title myView", StringComparison.Ordinal);
        var packageIndex = result.IndexOf("package \"", StringComparison.Ordinal);
        Assert.True(titleIndex < packageIndex, "title should appear before the first package block");
    }

    [Fact]
    public void Packages_ContainInventoryAndWarehouse()
    {
        var result = Render(DefaultGraph(), Opts());
        Assert.Contains("package \"Inventory\"", result);
        Assert.Contains("package \"Warehouse\"", result);
    }

    [Fact]
    public void Package_SyntaxIsCorrect()
    {
        var result = Render(DefaultGraph(), Opts());
        // package "Label" as Alias {
        Assert.Matches(@"package ""[^""]+"" as \w+ \{", result);
    }

    [Fact]
    public void Package_BlocksAreClosed()
    {
        var result = Render(DefaultGraph(), Opts());
        var opens = result.Count(c => c == '{');
        var closes = result.Count(c => c == '}');
        Assert.Equal(opens, closes);
    }

    [Fact]
    public void Package_ChildIsIndentedRelativeToParent()
    {
        var opts = Opts(packages: [new Package("./Inventory/", 2)]);
        var result = Render(DefaultGraph(), opts);

        var inventoryLine = result.Split('\n')
            .First(l => l.Contains("package \"Inventory\""));
        var stockLine = result.Split('\n')
            .First(l => l.Contains("package \"Stock\""));

        var inventoryIndent = inventoryLine.Length - inventoryLine.TrimStart().Length;
        var stockIndent = stockLine.Length - stockLine.TrimStart().Length;

        Assert.True(stockIndent > inventoryIndent, "Child package should be indented deeper than its parent");
    }

    [Fact]
    public void Packages_EachIndentLevelIs4Spaces()
    {
        var opts = Opts(packages: [new Package("./Inventory/", 2)]);
        var result = Render(DefaultGraph(), opts);

        var stockLine = result.Split('\n').First(l => l.Contains("package \"Stock\""));
        var indent = stockLine.Length - stockLine.TrimStart().Length;
        Assert.Equal(4, indent);
    }

    [Fact]
    public void Packages_DoNotContainIgnoredPackage()
    {
        var opts = Opts(ignore: ["./Warehouse/"]);
        var result = Render(DefaultGraph(), opts);
        Assert.DoesNotContain("package \"Warehouse\"", result);
    }

    [Fact]
    public void Packages_EmptyOutput_WhenAllIgnored()
    {
        var opts = Opts(ignore: ["./Inventory/", "./Warehouse/", "./Shop/"]);
        var result = Render(DefaultGraph(), opts);
        Assert.DoesNotContain("package \"", result);
    }

    [Fact]
    public void Packages_DepthOne_HidesSubPackages()
    {
        var opts = Opts(packages: [new Package("./Inventory/", 1)]);
        var result = Render(DefaultGraph(), opts);
        Assert.Contains("package \"Inventory\"", result);
        Assert.DoesNotContain("package \"Stock\"", result);
    }

    [Fact]
    public void Packages_DepthTwo_ShowsDirectChildren()
    {
        var opts = Opts(packages: [new Package("./Inventory/", 2)]);
        var result = Render(DefaultGraph(), opts);
        Assert.Contains("package \"Inventory\"", result);
        Assert.Contains("package \"Stock\"", result);
    }

    [Fact]
    public void Edges_UsesArrowSyntax()
    {
        var result = Render(DefaultGraph(), Opts());
        Assert.Matches(@"\w+ --> \w+", result);
    }

    [Fact]
    public void Edges_AppearAfterAllPackageBlocks()
    {
        var result = Render(DefaultGraph(), Opts());
        var lastClosingBrace = result.LastIndexOf('}');
        var firstArrow = result.IndexOf("-->", StringComparison.Ordinal);
        Assert.True(firstArrow > lastClosingBrace, "Edges should be rendered after all package blocks");
    }

    [Fact]
    public void Edges_NoSelfEdges()
    {
        var result = Render(DefaultGraph(), Opts());
        var arrowLines = result.Split('\n').Where(l => l.Contains("-->")).ToList();
        Assert.All(arrowLines, line =>
        {
            var parts = line.Split("-->", 2);
            Assert.NotEqual(parts[0].Trim(), parts[1].Split(':')[0].Trim());
        });
    }

    [Fact]
    public void Edges_ContainLabelAfterColon()
    {
        var result = Render(DefaultGraph(), Opts());
        var arrowLines = result.Split('\n').Where(l => l.Contains("-->")).ToList();
        Assert.All(arrowLines, line => Assert.Contains(":", line));
    }

    [Fact]
    public void Edges_NotRendered_WhenAllPackagesIgnored()
    {
        var opts = Opts(ignore: ["./Inventory/", "./Warehouse/", "./Shop/"]);
        var result = Render(DefaultGraph(), opts);
        Assert.DoesNotContain("-->", result);
    }

    [Fact]
    public void EdgeLabel_IsPlainCount_WhenDeltaIsZero()
    {
        var result = Render(DefaultGraph(), Opts());
        var arrowLines = result.Split('\n').Where(l => l.Contains("-->")).ToList();

        Assert.All(arrowLines, line =>
        {
            var label = line.Split(':').Last().Trim();
            Assert.Matches(@"^\d+$", label);
        });
    }

    [Fact]
    public void EdgeLabel_IncludesPositiveDelta_WhenEdgeCreated()
    {
        var remote = DefaultGraph();
        var local = DefaultGraph();
        var labels = RelativePath.Directory(_fs.Root, "./Inventory/Stock/Labels/");
        var stockSupplier = RelativePath.File(_fs.Root, "./Warehouse/Suppliers/StockSupplier.cs");
        local.AddDependency(labels, stockSupplier);

        var result = RenderDiff(local, remote, Opts());

        var createdLine = result.Split('\n')
            .FirstOrDefault(l => l.Contains("#Green") && l.Contains("Inventory --> Warehouse"));
        Assert.NotNull(createdLine);
        Assert.Contains("+", createdLine);
    }

    [Fact]
    public void EdgeLabel_IncludesNegativeDelta_WhenEdgeDeleted()
    {
        var remote = DefaultGraph();
        var local = DefaultGraph();
        var stockSupplier = RelativePath.File(_fs.Root, "./Warehouse/Suppliers/StockSupplier.cs");
        var labels = RelativePath.Directory(_fs.Root, "./Inventory/Stock/Labels/");
        local.RemoveDependency(stockSupplier, labels);

        var result = RenderDiff(local, remote, Opts());

        var deletedLine = result.Split('\n').FirstOrDefault(l => l.Contains("#Red"));
        Assert.NotNull(deletedLine);
        Assert.Contains("-", deletedLine);
    }

    [Fact]
    public void EdgeLabel_IsPlainCount_WhenDeltaIsZeroInDiff()
    {
        var graph = DefaultGraph();
        var result = RenderDiff(graph, graph, Opts());
        var arrowLines = result.Split('\n').Where(l => l.Contains("-->")).ToList();

        Assert.All(arrowLines, line =>
        {
            var label = line.Split(':').Last().Trim();
            Assert.Matches(@"^\d+$", label);
        });
    }

    [Fact]
    public void Diff_NeutralNodes_HaveNoColourTag()
    {
        var graph = DefaultGraph();
        var result = RenderDiff(graph, graph, Opts());
        var packageLines = result.Split('\n').Where(l => l.TrimStart().StartsWith("package \"")).ToList();

        Assert.All(packageLines, line =>
        {
            Assert.DoesNotContain("#LightGreen", line);
            Assert.DoesNotContain("#LightCoral", line);
        });
    }

    [Fact]
    public void Diff_CreatedNode_HasLightGreenColour()
    {
        var local = DefaultGraph();
        var remote = DefaultGraph();
        remote.RemoveProjectItemRecursive(RelativePath.Directory(_fs.Root, "./Warehouse/"));

        var result = RenderDiff(local, remote, Opts());
        var warehouseLine = result.Split('\n')
            .FirstOrDefault(l => l.Contains("package \"Warehouse\""));

        Assert.NotNull(warehouseLine);
        Assert.Contains("#LightGreen", warehouseLine);
    }

    [Fact]
    public void Diff_DeletedNode_HasLightCoralColour()
    {
        var local = DefaultGraph();
        var remote = DefaultGraph();
        local.RemoveProjectItemRecursive(RelativePath.Directory(_fs.Root, "./Warehouse/"));

        var result = RenderDiff(local, remote, Opts());
        var warehouseLine = result.Split('\n')
            .FirstOrDefault(l => l.Contains("package \"Warehouse\""));

        Assert.NotNull(warehouseLine);
        Assert.Contains("#LightCoral", warehouseLine);
    }

    [Fact]
    public void Diff_NeutralEdges_HaveNoColourTag()
    {
        var graph = DefaultGraph();
        var result = RenderDiff(graph, graph, Opts());
        var arrowLines = result.Split('\n').Where(l => l.Contains("-->")).ToList();

        Assert.All(arrowLines, line =>
        {
            Assert.DoesNotContain("#Green", line);
            Assert.DoesNotContain("#Red", line);
        });
    }

    [Fact]
    public void Diff_CreatedEdge_HasGreenColour()
    {
        var remote = DefaultGraph();
        var local = DefaultGraph();
        var labels = RelativePath.Directory(_fs.Root, "./Inventory/Stock/Labels/");
        var stockSupplier = RelativePath.File(_fs.Root, "./Warehouse/Suppliers/StockSupplier.cs");
        local.AddDependency(labels, stockSupplier);

        var result = RenderDiff(local, remote, Opts());
        Assert.Contains("#Green", result);
    }

    [Fact]
    public void Diff_DeletedEdge_HasRedColour()
    {
        var remote = DefaultGraph();
        var local = DefaultGraph();
        var stockSupplier = RelativePath.File(_fs.Root, "./Warehouse/Suppliers/StockSupplier.cs");
        var labels = RelativePath.Directory(_fs.Root, "./Inventory/Stock/Labels/");
        local.RemoveDependency(stockSupplier, labels);

        var result = RenderDiff(local, remote, Opts());
        Assert.Contains("#Red", result);
    }

    [Fact]
    public void Diff_IdenticalGraphs_NoColourTagsAnywhere()
    {
        var graph = DefaultGraph();
        var result = RenderDiff(graph, graph, Opts());
        Assert.DoesNotContain("#LightGreen", result);
        Assert.DoesNotContain("#LightCoral", result);
        Assert.DoesNotContain("#Green", result);
        Assert.DoesNotContain("#Red", result);
    }

    [Fact]
    public void RootPackages_AreOrderedAlphabetically()
    {
        var result = Render(DefaultGraph(), Opts());
        var rootPackageLines = result.Split('\n')
            .Where(l => l.StartsWith("package \""))
            .Select(l => l.Split('"')[1])
            .ToList();

        Assert.Equal(
            rootPackageLines.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
            rootPackageLines);
    }

    [Fact]
    public void Edges_AreOrderedAlphabetically_ByFromThenTo()
    {
        var result = Render(DefaultGraph(), Opts());
        var arrowLines = result.Split('\n')
            .Where(l => l.Contains("-->"))
            .Select(l =>
            {
                var colonIdx = l.IndexOf(':');
                var arrow = colonIdx > 0 ? l[..colonIdx] : l;
                var parts = arrow.Split("-->", 2);
                return (From: parts[0].Trim(), To: parts[1].Trim());
            })
            .ToList();

        var sorted = arrowLines
            .OrderBy(p => p.From, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.To, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Equal(sorted, arrowLines);
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
    public void EmptyGraph_RendersValidEnvelope()
    {
        var graph = new ProjectDependencyGraph(_fs.Root);
        var rootPath = RelativePath.Directory(_fs.Root, _fs.Root);
        graph.UpsertProjectItem(rootPath, ProjectItemType.Directory);

        var result = Render(graph, Opts());

        Assert.StartsWith("@startuml", result);
        Assert.Contains("@enduml", result);
        Assert.DoesNotContain("package \"", result);
        Assert.DoesNotContain("-->", result);
    }

    [Fact]
    public void ViewName_WithSpecialChars_DoesNotBreakEnvelope()
    {
        var result = Render(DefaultGraph(), Opts(viewName: "view with spaces & symbols"));
        Assert.StartsWith("@startuml", result);
        Assert.Contains("@enduml", result);
    }
}
