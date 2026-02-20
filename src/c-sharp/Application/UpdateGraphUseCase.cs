using Archlens.Domain;
using Archlens.Domain.Interfaces;
using Archlens.Domain.Models.Records;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Archlens.Application;

public sealed class UpdateGraphUseCase(
    BaseOptions baseOptions,
    ParserOptions parserOptions,
    RenderOptions renderOptions,
    SnapshotOptions snapshotOptions,
    IReadOnlyList<IDependencyParser> parsers,
    Renderer renderer,
    ISnapshotManager snapshotManager
    )
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var snapshotGraph = await snapshotManager.GetLastSavedDependencyGraphAsync(snapshotOptions, ct);
        var projectChanges = await ChangeDetector.GetProjectChangesAsync(parserOptions, snapshotGraph, ct);
        var graph = await new DependencyGraphBuilder(parsers, baseOptions).GetGraphAsync(projectChanges, snapshotGraph, ct);

        await renderer.RenderViewsAndSaveToFiles(graph, renderOptions);
        await snapshotManager.SaveGraphAsync(graph, snapshotOptions, ct);
    }
}