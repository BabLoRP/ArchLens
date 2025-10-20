using System.Threading;
using System.Threading.Tasks;
using Archlens.Domain.Interfaces;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;

namespace Archlens.Infra;

public sealed class LocalBaselineManager : IBaselineManager
{
    public Task SaveGraphAsync(DependencyGraph graph, Options options, CancellationToken ct = default)
    {
        throw new System.NotImplementedException();
    }

    public Task<DependencyGraph> GetLastSavedDependencyGraphAsync(Options options, CancellationToken ct)
    {
        throw new System.NotImplementedException();
    }
}