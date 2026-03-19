using System;
using Archlens.Domain.Interfaces;
using Archlens.Domain.Models.Enums;
using Archlens.Domain.Models.Records;
using Archlens.Infra.SnapshotManagers;

namespace Archlens.Infra.Factories;

public static class SnapshotManagerFactory
{
    public static ISnapshotManager SelectSnapshotManager(SnapshotOptions o) => o.SnapshotManager switch
    {
        SnapshotManager.Git => new GitSnapshotManager(o.SnapshotDir, o.SnapshotFile),
        SnapshotManager.Local => new LocalSnapshotManager(o.SnapshotDir, o.SnapshotFile),
        _ => throw new ArgumentOutOfRangeException(nameof(o.SnapshotManager))
    };
}