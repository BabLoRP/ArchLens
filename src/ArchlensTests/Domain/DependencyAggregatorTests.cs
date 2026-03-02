using Archlens.Domain;
using Archlens.Domain.Models;
using Archlens.Domain.Utils;
using ArchlensTests.Utils;

namespace ArchlensTests.Domain;

public sealed class DependencyAggregatorTests : IDisposable
{
    private readonly TestFileSystem _fs = new();
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

    public void Dispose() => _fs.Dispose();

    [Fact]
    public void Aggregation_Drops_Internal_Subtree_Relations_And_Preserves_External()
    {
        var rootPath = _fs.Root;
        var graph = MakeDependencyGraph(_fs.Root);
        DependencyAggregator.RecomputeAggregates(graph);

        var domainPath = RelativePath.Directory(rootPath, "./Domain/");
        var factoryPath = RelativePath.Directory(rootPath, "./Domain/Factories/");
        var modelsPath = RelativePath.Directory(rootPath, "./Domain/Models/");

        var interfacesPath = RelativePath.Directory(rootPath, "./Domain/Interfaces/");
        var enumsPath = RelativePath.Directory(rootPath, "./Domain/Models/Enums/");
        var recordsPath = RelativePath.Directory(rootPath, "./Domain/Models/Records/");
        var infraPath = RelativePath.Directory(rootPath, "./Infra/");

        Assert.True(graph.DependenciesFrom(factoryPath).ContainsKey(interfacesPath));
        Assert.True(graph.DependenciesFrom(factoryPath).ContainsKey(enumsPath));
        Assert.True(graph.DependenciesFrom(factoryPath).ContainsKey(recordsPath));
        Assert.True(graph.DependenciesFrom(factoryPath).ContainsKey(infraPath));

        var utilsPath = RelativePath.Directory(rootPath, "./Domain/Utils/");

        Assert.True (graph.DependenciesFrom(modelsPath).ContainsKey(utilsPath));
        Assert.False(graph.DependenciesFrom(modelsPath).ContainsKey(enumsPath));

        var domainDeps = graph.DependenciesFrom(domainPath);
        Assert.DoesNotContain(enumsPath, domainDeps);
        Assert.DoesNotContain(recordsPath, domainDeps);
        Assert.DoesNotContain(utilsPath, domainDeps);
        Assert.DoesNotContain(interfacesPath, domainDeps);
        Assert.Contains(infraPath, domainDeps);
    }

    [Fact]
    public void Node_Shows_All_External_Relations()
    {
        var root = _fs.Root;

        var graph = MakeDependencyGraph(root);
        DependencyAggregator.RecomputeAggregates(graph);

        var rootPath = RelativePath.Directory(root, root);
        var rootDep = graph.DependenciesFrom(rootPath);

        var infraPath = RelativePath.Directory(root, "./Infra/");
        var domainPath = RelativePath.Directory(root, "./Domain/");

        var rootChildren = graph.ChildrenOf(rootPath);
        Assert.Contains(infraPath, rootChildren);
        Assert.Contains(domainPath, rootChildren);

        var domainDeps = graph.DependenciesFrom(domainPath).Keys;
        Assert.Contains(infraPath, domainDeps);
    }
}
