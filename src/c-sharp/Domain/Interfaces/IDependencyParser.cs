using Archlens.Domain.Utils;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Archlens.Domain.Interfaces;

public interface IDependencyParser
{
    Task<IReadOnlyList<RelativePath>> ParseFileDependencies(string absPath, CancellationToken ct = default);
}