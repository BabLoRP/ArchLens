using Archlens.Domain.Models.Enums;
using System.Collections.Generic;

namespace Archlens.Domain.Models.Records;

public sealed record BaseOptions(
    string FullRootPath,
    string ProjectRoot,
    string ProjectName
);

public sealed record ParserOptions(
    BaseOptions BaseOptions,
    IReadOnlyList<Language> Languages,
    IReadOnlyList<string> Exclusions,
    IReadOnlyList<string> FileExtensions
);

public sealed record RenderOptions(
    BaseOptions BaseOptions,
    RenderFormat Format,
    IReadOnlyList<View> Views,
    string SaveLocation
);

public sealed record SnapshotOptions(
    BaseOptions BaseOptions,
    SnapshotManager SnapshotManager,
    string GitUrl,
    string SnapshotDir = ".archlens",
    string SnapshotFile = "snaphot"
);