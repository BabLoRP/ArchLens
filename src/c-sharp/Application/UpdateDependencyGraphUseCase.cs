using Archlens.Domain.Interfaces;
using Archlens.Domain.Models.Enums;
using Archlens.Domain.Models.Records;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Archlens.Application;

public sealed class UpdateDependencyGraphUseCase(
    BaseOptions baseOptions,
    ParserOptions parserOptions,
    RenderOptions renderOptions,
    SnapshotOptions snapshotOptions,
    IReadOnlyList<IDependencyParser> parsers,
    IRenderer renderer,
    ISnapshotManager snapshotManager
    )
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var snapshotGraph = await snapshotManager.GetLastSavedDependencyGraphAsync(snapshotOptions, ct);
        var projectChanges = await ChangeDetector.GetProjectChangesAsync(parserOptions, snapshotGraph, ct);
        var graph = await new DependencyGraphBuilder(parsers, baseOptions)
            .GetGraphAsync(projectChanges, snapshotGraph, ct);

        if (snapshotOptions.SnapshotManager == SnapshotManager.Local)
            await renderer.RenderViewsAndSaveToFiles(graph, renderOptions);
        else
            await renderer.RenderDiffViewsAndSaveToFiles(graph, snapshotGraph, renderOptions);

        await snapshotManager.SaveGraphAsync(graph, snapshotOptions, ct); //TODO: Should this also change behaviour when diff?
    }
}