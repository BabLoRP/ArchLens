using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;

namespace ArchlensTests.Performance;

internal static class GraphFactory
{
    public static readonly string FakeRoot =
        Path.Combine(Path.GetTempPath(), "archlens-perf");

    public static ProjectDependencyGraph Build(
        int moduleCount,
        int filesPerModule,
        int crossModuleDepsPerFile,
        int seed = 42)
    {
        var root = FakeRoot;
        var graph = new ProjectDependencyGraph(root);

        var rootPath = RelativePath.Directory(root, root);

        var modulePaths = Enumerable.Range(0, moduleCount)
            .Select(m => RelativePath.Directory(root, $"./Module{m}/"))
            .ToList();

        var filePaths = Enumerable.Range(0, moduleCount)
            .SelectMany(m => Enumerable.Range(0, filesPerModule)
                .Select(f => RelativePath.File(root, $"./Module{m}/File{f}.cs")))
            .ToList();

        var items = new List<ProjectItem>(1 + moduleCount + moduleCount * filesPerModule)
        {
            new(rootPath, "root", DateTime.UtcNow, ProjectItemType.Directory)
        };

        for (int m = 0; m < moduleCount; m++)
            items.Add(new ProjectItem(modulePaths[m], $"Module{m}", DateTime.UtcNow, ProjectItemType.Directory));

        foreach (var fp in filePaths)
            items.Add(new ProjectItem(fp, fp.GetName(), DateTime.UtcNow, ProjectItemType.File));

        graph.UpsertProjectItems(items);

        foreach (var mod in modulePaths)
            graph.AddChild(rootPath, mod);

        for (int m = 0; m < moduleCount; m++)
            for (int f = 0; f < filesPerModule; f++)
                graph.AddChild(modulePaths[m], filePaths[m * filesPerModule + f]);

        if (crossModuleDepsPerFile > 0 && moduleCount > 1)
        {
            var rng = new Random(seed);
            for (int i = 0; i < filePaths.Count; i++)
            {
                int srcModule = i / filesPerModule;
                for (int d = 0; d < crossModuleDepsPerFile; d++)
                {
                    int targetModule = (srcModule + 1 + rng.Next(moduleCount - 1)) % moduleCount;
                    int targetFile = rng.Next(filesPerModule);
                    graph.AddDependency(filePaths[i], filePaths[targetModule * filesPerModule + targetFile]);
                }
            }
        }

        return graph;
    }
}
