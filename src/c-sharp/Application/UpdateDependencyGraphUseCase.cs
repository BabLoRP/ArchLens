using Archlens.Domain.Interfaces;
using Archlens.Domain.Models.Records;
using System.Threading;
using System.Threading.Tasks;

namespace Archlens.Application;

public sealed class UpdateDependencyGraphUseCase(Options options,
    ISnapshotManager snapshotManager,
    IDependencyParser parser,
    IRenderer renderer
    )
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var snapshotGraph = await snapshotManager.GetLastSavedDependencyGraphAsync(options, ct);
        var projectChanges = await ChangeDetector.GetChangedProjectPathsAsync(options, snapshotGraph, ct);
        var graph = await new DependencyGraphBuilder(parser, options).GetGraphAsync(projectChanges, ct);

        await renderer.SaveGraphToFileAsync(graph, options, ct);
        await snapshotManager.SaveGraphAsync(graph, options, ct);
    }
}