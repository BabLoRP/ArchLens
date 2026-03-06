using Archlens.Domain;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using ArchlensTests.Utils;

namespace ArchlensTests.Domain;

public sealed class DependencyAggregatorTests : IDisposable
{
    private readonly TestFileSystem _fs = new();

    public void Dispose() => _fs.Dispose();

    [Fact]
    public void GetAggregatedDependencies_ForFile_EqualsDirectDependencies()
    {
        var root = _fs.Root;
        var graph = TestDependencyGraph.MakeDependencyGraph(root);

        var file = RelativePath.File(root, "./Domain/Models/DependencyGraph.cs");
        var direct = graph.DependenciesFrom(file);
        var aggregated = DependencyAggregator.GetAggregatedDependencies(graph, file);

        Assert.Equal(direct.Count, aggregated.Count);

        foreach (var (k, v) in direct)
        {
            Assert.True(aggregated.TryGetValue(k, out var av));
            Assert.Equal(v.Count, av.Count);
            Assert.Equal(v.Type, av.Type);
        }
    }

    [Fact]
    public void GetAggregatedDependencies_Drops_Internal_Subtree_Dependencies_ForDirectory()
    {
        var root = _fs.Root;
        var graph = TestDependencyGraph.MakeDependencyGraph(root);

        var domain = RelativePath.Directory(root, "./Domain/");
        var infra = RelativePath.Directory(root, "./Infra/");

        var interfaces = RelativePath.Directory(root, "./Domain/Interfaces/");
        var models = RelativePath.Directory(root, "./Domain/Models/");
        var records = RelativePath.Directory(root, "./Domain/Models/Records/");
        var enums = RelativePath.Directory(root, "./Domain/Models/Enums/");
        var utils = RelativePath.Directory(root, "./Domain/Utils/");

        var domainAgg = DependencyAggregator.GetAggregatedDependencies(graph, domain);
        var infraAgg = DependencyAggregator.GetAggregatedDependencies(graph, infra);

        Assert.DoesNotContain(interfaces, domainAgg.Keys);
        Assert.DoesNotContain(models, domainAgg.Keys);
        Assert.DoesNotContain(records, domainAgg.Keys);
        Assert.DoesNotContain(enums, domainAgg.Keys);
        Assert.DoesNotContain(utils, domainAgg.Keys);

        Assert.Contains(interfaces, infraAgg.Keys);
        Assert.Contains(enums, infraAgg.Keys);
        Assert.Contains(records, infraAgg.Keys);
    }

    [Fact]
    public void GetAggregatedDependencies_AggregatesCountsAcrossSubtree()
    {
        var root = _fs.Root;
        var graph = TestDependencyGraph.MakeDependencyGraph(root);

        var factories = RelativePath.Directory(root, "./Infra/Factories/");
        var infra = RelativePath.Directory(root, "./Infra/");

        var factoryAgg = DependencyAggregator.GetAggregatedDependencies(graph, factories);

        Assert.True(factoryAgg.TryGetValue(infra, out var dep));
        Assert.Equal(2, dep.Count); // 1 + 1 across the two files
        Assert.Equal(DependencyType.Uses, dep.Type);
    }
}