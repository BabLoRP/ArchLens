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
    string SnapshotDir = ".archlens",
    string SnapshotFile = "snaphot",
    string GitUrl = "",
    string FullRootPath = ""
);

public sealed record View(
    string ViewName,
    IReadOnlyList<string> Packages,
    IReadOnlyList<string> IgnorePackages
);