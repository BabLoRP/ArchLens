using Archlens.Domain.Models;
using Archlens.Domain.Utils;

namespace ArchlensTests.Utils;

public static class TestGraphs
{
    public static ProjectDependencyGraph MakeGraph(string projectRoot)
        => new(projectRoot);

    public static RelativePath AddProjectItem(ProjectDependencyGraph graph, RelativePath path, ProjectItemType type, params RelativePath[] deps)
    {
        graph.UpsertProjectItem(path, type);

        graph.AddDependencies(path, deps);

        return path;
    }

}