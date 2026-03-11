using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Archlens.Domain.Models.Records;

namespace Archlens.Domain.Interfaces;

public interface IDependencyParser
{
    Task<IReadOnlyList<RelativePath>> ParseFileDependencies(string absPath, CancellationToken ct = default);
}