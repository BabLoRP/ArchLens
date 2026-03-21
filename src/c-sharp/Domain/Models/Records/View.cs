using System.Collections.Generic;

namespace Archlens.Domain.Models.Records;

public sealed record View(
    string ViewName,
    IReadOnlyList<Package> Packages,
    IReadOnlyList<string> IgnorePackages
);
