using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;

namespace ArchlensTests.Utils;

public static class TestDependencyGraph
{
    public static ProjectDependencyGraph MakeDependencyGraph(string rootPath)
    {
        var graph = new ProjectDependencyGraph(rootPath);

        var root = RelativePath.Directory(rootPath, rootPath);
        var application = RelativePath.Directory(rootPath, "./Application/");
        var infra = RelativePath.Directory(rootPath, "./Infra/");
        var domain = RelativePath.Directory(rootPath, "./Domain/");
        var interfaces = RelativePath.Directory(rootPath, "./Domain/Interfaces");
        var factory = RelativePath.Directory(rootPath, "./Infra/Factories/");
        var models = RelativePath.Directory(rootPath, "./Domain/Models/");
        var records = RelativePath.Directory(rootPath, "./Domain/Models/Records/");
        var enums = RelativePath.Directory(rootPath, "./Domain/Models/Enums/");
        var utils = RelativePath.Directory(rootPath, "./Domain/Utils/");

        graph.UpsertProjectItem(root, ProjectItemType.Directory);
        graph.UpsertProjectItem(application, ProjectItemType.Directory);
        graph.UpsertProjectItem(infra, ProjectItemType.Directory);
        graph.UpsertProjectItem(domain, ProjectItemType.Directory);
        graph.UpsertProjectItem(interfaces, ProjectItemType.Directory);
        graph.UpsertProjectItem(factory, ProjectItemType.Directory);
        graph.UpsertProjectItem(models, ProjectItemType.Directory);
        graph.UpsertProjectItem(records, ProjectItemType.Directory);
        graph.UpsertProjectItem(enums, ProjectItemType.Directory);
        graph.UpsertProjectItem(utils, ProjectItemType.Directory);

        graph.AddChild(root, application);
        graph.AddChild(root, domain);
        graph.AddChild(root, infra);
        graph.AddChild(infra, factory);
        graph.AddChild(domain, models);
        graph.AddChild(domain, interfaces);
        graph.AddChild(domain, utils);
        graph.AddChild(models, records);
        graph.AddChild(models, enums);

        var changeDetector = RelativePath.File(rootPath, "./Application/ChangeDetector.cs");
        var dependencyParserFactory = RelativePath.File(rootPath, "./Infra/Factories/DependencyParserFactory.cs");
        var rendererFactory = RelativePath.File(rootPath, "./Infra/Factories/RendererFactory.cs");
        var options = RelativePath.File(rootPath, "./Domain/Models/Records/Options.cs");
        var dependencyGraph = RelativePath.File(rootPath, "./Domain/Models/DependencyGraph.cs");

        graph.UpsertProjectItem(changeDetector, ProjectItemType.File);
        graph.UpsertProjectItem(dependencyParserFactory, ProjectItemType.File);
        graph.UpsertProjectItem(rendererFactory, ProjectItemType.File);
        graph.UpsertProjectItem(options, ProjectItemType.File);
        graph.UpsertProjectItem(dependencyGraph, ProjectItemType.File);

        graph.AddChild(application, changeDetector);
        graph.AddChild(factory, dependencyParserFactory);
        graph.AddChild(factory, rendererFactory);
        graph.AddChild(records, options);
        graph.AddChild(models, dependencyGraph);

        var dependencies = new Dictionary<RelativePath, IReadOnlyList<RelativePath>>()
        {
            [changeDetector] = [models, records, utils], // application 2 dependencies to models and 1 to utils
            [dependencyParserFactory] = [interfaces, enums, records, infra], // factories 1 to interfaces, 2 to models and 1 to infra 
            [rendererFactory] = [interfaces, enums, infra],  // factories 1 to interfaces, 2 to models and 1 to infra 
            [options] = [enums], // no visible deps at deth 1
            [dependencyGraph] = [utils] // no visible deps at deth 1
        };

        foreach (var (source, targets) in dependencies)
            graph.AddDependencies(source, targets);

        return graph;
    }
}
