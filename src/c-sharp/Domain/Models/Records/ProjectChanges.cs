
using System.Collections.Generic;

namespace Archlens.Domain.Models.Records;

public sealed record ProjectChanges(
    IReadOnlyDictionary<string, IReadOnlyList<string>> ChangedFilesByDirectory,
    IReadOnlyList<string> DeletedFiles,
    IReadOnlyList<string> DeletedDirectories
);