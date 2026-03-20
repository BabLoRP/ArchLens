using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Archlens.Domain;
using Archlens.Domain.Interfaces;
using Archlens.Domain.Models.Enums;
using Archlens.Infra;

namespace Archlens.Application;

public sealed class UpdateGraphUseCase(
    ConfigManager configManager,
    IReadOnlyList<IDependencyParser> parsers,
    RendererBase renderer,
    ISnapshotManager snapshotManager,
    bool diff = false
    )
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var snapshotGraph = await snapshotManager.GetLastSavedDependencyGraphAsync(configManager.GetSnapshotOptions(), ct);
        var projectChanges = await ChangeDetector.GetProjectChangesAsync(configManager.GetParserOptions(), snapshotGraph, ct);
        var graph = await new DependencyGraphBuilder(parsers, configManager.GetBaseOptions()).GetGraphAsync(projectChanges, snapshotGraph, ct);

        if (configManager.GetRenderOptions().Format != RenderFormat.None)
        {
            if (diff)
            {
                var compareGraph = await snapshotManager.GetLastSavedDependencyGraphAsync(configManager.GetSnapshotOptions(), ct) ?? throw new InvalidOperationException("Diff mode requires a saved snapshot, but none was found.");
                await renderer.RenderDiffViewsAndSaveToFiles(graph, compareGraph, configManager.GetRenderOptions(), ct);
            }
            else
                await renderer.RenderViewsAndSaveToFiles(graph, configManager.GetRenderOptions(), ct);
        }

        await snapshotManager.SaveGraphAsync(graph, configManager.GetSnapshotOptions(), ct);
    }
}
