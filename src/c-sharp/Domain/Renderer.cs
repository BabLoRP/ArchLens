using System.IO;
using System.Threading.Tasks;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;

namespace Archlens.Domain;

public abstract class Renderer
{
    public abstract string FileExtension { get; }
    public abstract string RenderView(DependencyGraph graph, View view, RenderOptions options);
    public abstract string Merge(string localContent, string remoteContent);

    public async Task RenderViewsAndSaveToFiles(DependencyGraph graph, RenderOptions options)
    {
        foreach (var view in options.Views)
        {
            string content = RenderView(graph, view, options);
            await SaveViewToFileAsync(content, view, options);
        }
    }

    public async Task RenderDiffViewsAndSaveToFiles(DependencyGraph localGraph, DependencyGraph remoteGraph, RenderOptions options)
    {
        foreach (var view in options.Views)
        {
            string content = RenderDiffView(localGraph, remoteGraph, view, options);
            await SaveViewToFileAsync(content, view, options);
        }
    }

    public string RenderDiffView(DependencyGraph localGraph, DependencyGraph remoteGraph, View view, RenderOptions options)
    {
        string localContent = RenderView(localGraph, view, options);
        string remoteContent = RenderView(remoteGraph, view, options);
        string content = Merge(localContent, remoteContent);
        return content;
    }

    public async Task SaveViewToFileAsync(string content, View view, RenderOptions options)
    {
        var dir = options.SaveLocation;
        Directory.CreateDirectory(dir);
        var filename = $"{options.BaseOptions.ProjectName}-{view.ViewName}.{FileExtension}";
        var path = Path.Combine(dir, filename);

        await File.WriteAllTextAsync(path, content);
    }
}