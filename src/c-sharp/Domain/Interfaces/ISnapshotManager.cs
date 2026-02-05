using System.Threading;
using System.Threading.Tasks;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
namespace Archlens.Domain.Interfaces;

public interface ISnapshotManager
{
    Task SaveGraphAsync(DependencyGraph graph,
                   SnapshotOptions options,
                   CancellationToken ct = default);
    Task<DependencyGraph> GetLastSavedDependencyGraphAsync(SnapshotOptions options, CancellationToken ct = default);
}