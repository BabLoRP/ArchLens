namespace Archlens.Domain.Factories;

using Archlens.Domain.Interfaces;
using Archlens.Domain.Models.Enums;
using Archlens.Domain.Models.Records;
using Archlens.Infra;
using System;

public sealed class SnapsnotManagerFactory
{
    public static ISnapshotManager SelectSnapshotManager(Options o) => o.SnapshotManager switch
    {
        SnapshotManager.Git   => new GitSnaphotManager(o.SnapshotDir, o.SnapshotFile),
        SnapshotManager.Local => new LocalSnaphotManager(o.SnapshotDir, o.SnapshotFile),
        _ => throw new ArgumentOutOfRangeException(nameof(o.SnapshotManager))
    };
}