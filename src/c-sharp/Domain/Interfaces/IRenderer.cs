using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Enums;
using Archlens.Domain.Models.Records;

namespace Archlens.Domain.Interfaces;

public interface IRenderer
{
    public string RenderGraph(DependencyGraph graph, Options options, CancellationToken ct = default);

    public string RenderGraphs(IEnumerable<DependencyGraph> graphs, string ViewName, Options options, CancellationToken ct = default);

    public async Task SaveGraphToFileAsync(DependencyGraph graph, Options options, CancellationToken ct = default)
    {
        var dir = options.SaveLocation;
        Directory.CreateDirectory(dir);

        foreach (var view in options.Views)
        {
            var fileExtension = options.Format.ToFileExtension();
            var filename = $"{options.ProjectName}-{view.ViewName}.{fileExtension}";
            var path = Path.Combine(dir, filename);

            if (view.Packages.Count == 0)
            {
                var content = RenderGraph(graph, options, ct);
                await File.WriteAllTextAsync(path, content, ct);
            }
            else
            {
                List<DependencyGraph> graphs = [];

                foreach (var package in view.Packages)
                {
                    var packagePath = package.Path;

                    var graphPath = Path.Combine(options.FullRootPath, packagePath);
                    var g = graph.FindByPath(graphPath); //TODO: Debug why this only works on second run
                    if (g != null) graphs.Add(g);
                }

                var content = RenderGraphs(graphs, view.ViewName, options, ct);

                if (content != "")
                    await File.WriteAllTextAsync(path, content, ct);
            }
        }
    }
}