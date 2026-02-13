using System.Threading.Tasks;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;

namespace Archlens.Domain.Interfaces;

public interface IRenderer
{
    public Task RenderViewsAndSaveToFiles(DependencyGraph graph, RenderOptions options);
    public Task RenderDiffViewsAndSaveToFiles(DependencyGraph localGraph, DependencyGraph remoteGraph, RenderOptions options);
    public string RenderView(DependencyGraph graph, View view, RenderOptions options);
    public Task SaveViewToFileAsync(string content, View view, RenderOptions options);
}