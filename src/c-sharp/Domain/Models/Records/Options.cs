using System.Collections.Generic;
using Archlens.Domain.Models.Enums;
namespace Archlens.Domain.Models.Records;

public sealed record Options(
    string ProjectRoot,
    string ProjectName,
    Language Language,
    SnapshotManager SnapshotManager,
    RenderFormat Format,
    IReadOnlyList<string> Exclusions,
    IReadOnlyList<string> FileExtensions,
    IReadOnlyList<View> Views,
    string SaveLocation,
    string SnapshotDir = ".archlens",
    string SnapshotFile = "snaphot",
    string GitUrl = "",
    string FullRootPath = ""
);

public sealed record View(
    string ViewName,
    IReadOnlyList<Package> Packages,
    IReadOnlyList<string> IgnorePackages
);

public sealed record Package(
    string Path,
    int Depth
);