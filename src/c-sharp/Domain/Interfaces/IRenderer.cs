using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Enums;
using Archlens.Domain.Models.Records;

namespace Archlens.Domain.Interfaces;

public interface IRenderer
{
    public string RenderView(DependencyGraph graph, View view, RenderOptions options);

    public async Task SaveGraphToFileAsync(DependencyGraph graph, RenderOptions options, CancellationToken ct = default)
    {
        var dir = options.SaveLocation;
        Directory.CreateDirectory(dir);

        foreach (var view in options.Views)
        {
            var fileExtension = options.Format.ToFileExtension();
            var filename = $"{options.BaseOptions.ProjectName}-{view.ViewName}.{fileExtension}";
            var path = Path.Combine(dir, filename);

            var content = RenderView(graph, view, options);
            await File.WriteAllTextAsync(path, content, ct);
        }
    }
}