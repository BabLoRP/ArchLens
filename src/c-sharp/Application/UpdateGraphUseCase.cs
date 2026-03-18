using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Archlens.Domain;
using Archlens.Domain.Interfaces;
using Archlens.Domain.Models.Records;

namespace Archlens.Application;

public sealed class UpdateGraphUseCase(
    BaseOptions baseOptions,
    ParserOptions parserOptions,
    RenderOptions renderOptions,
    SnapshotOptions snapshotOptions,
    IReadOnlyList<IDependencyParser> parsers,
    RendererBase renderer,
    ISnapshotManager snapshotManager,
    bool diff = false
    )
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var snapshotGraph = await snapshotManager.GetLastSavedDependencyGraphAsync(snapshotOptions, ct);
        var projectChanges = await ChangeDetector.GetProjectChangesAsync(parserOptions, snapshotGraph, ct);
        var graph = await new DependencyGraphBuilder(parsers, baseOptions).GetGraphAsync(projectChanges, snapshotGraph, ct);

        if (diff)
        {
            var compareGraph = await snapshotManager.GetLastSavedDependencyGraphAsync(snapshotOptions, ct) ?? throw new InvalidOperationException("Diff mode requires a saved snapshot, but none was found.");
            await renderer.RenderDiffViewsAndSaveToFiles(graph, compareGraph, renderOptions, ct);
        }
        else
            await renderer.RenderViewsAndSaveToFiles(graph, renderOptions, ct);

        await snapshotManager.SaveGraphAsync(graph, snapshotOptions, ct);
    }
}
