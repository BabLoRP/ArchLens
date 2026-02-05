using Archlens.Domain.Interfaces;
using Archlens.Domain.Models.Records;
using System.Threading;
using System.Threading.Tasks;

namespace Archlens.Application;

public sealed class UpdateDependencyGraphUseCase(
    ParserOptions parserOptions,
    RenderOptions renderOptions,
    SnapshotOptions snapshotOptions,
    IDependencyParser parser,
    IRenderer renderer,
    ISnapshotManager snapshotManager
    )
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var snapshotGraph = await snapshotManager.GetLastSavedDependencyGraphAsync(snapshotOptions, ct);
        var projectChanges = await ChangeDetector.GetChangedProjectPathsAsync(parserOptions, snapshotGraph, ct);
        var graph = await new DependencyGraphBuilder(parser, renderOptions).GetGraphAsync(projectChanges, ct);

        await renderer.SaveGraphToFileAsync(graph, renderOptions, ct);
        await snapshotManager.SaveGraphAsync(graph, snapshotOptions, ct);
    }
}