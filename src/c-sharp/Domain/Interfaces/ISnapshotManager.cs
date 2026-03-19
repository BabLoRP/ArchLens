using System.Threading;
using System.Threading.Tasks;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
namespace Archlens.Domain.Interfaces;

public interface ISnapshotManager
{
    Task SaveGraphAsync(ProjectDependencyGraph graph,
                   SnapshotOptions options,
                   CancellationToken ct = default);
    Task<ProjectDependencyGraph?> GetLastSavedDependencyGraphAsync(SnapshotOptions options, CancellationToken ct = default);
}