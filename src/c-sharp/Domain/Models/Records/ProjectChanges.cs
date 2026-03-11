
using System.Collections.Generic;

namespace Archlens.Domain.Models.Records;

public sealed record ProjectChanges(
    IReadOnlyDictionary<RelativePath, IReadOnlyList<RelativePath>> ChangedFilesByDirectory,
    IReadOnlyList<RelativePath> DeletedFiles,
    IReadOnlyList<RelativePath> DeletedDirectories
);