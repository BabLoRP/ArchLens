using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Archlens.Domain;
using Archlens.Domain.Interfaces;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;

namespace Archlens.Infra.SnapshotManagers;

public sealed class LocalSnapshotManager(string _localDirName, string _localFileName) : ISnapshotManager
{
    public async Task SaveGraphAsync(ProjectDependencyGraph graph, SnapshotOptions options, CancellationToken ct = default)
    {
        var root = string.IsNullOrEmpty(options.BaseOptions.FullRootPath)
            ? Path.GetFullPath(options.BaseOptions.ProjectRoot)
            : options.BaseOptions.FullRootPath;

        var dir = Path.Combine(root, _localDirName);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, _localFileName);

        var bytes = DependencyGraphSerializer.Serialize(graph);
        await File.WriteAllBytesAsync(path, bytes, ct);
    }

    public async Task<ProjectDependencyGraph?> GetLastSavedDependencyGraphAsync(SnapshotOptions options, CancellationToken ct = default)
    {
        var root = string.IsNullOrEmpty(options.BaseOptions.FullRootPath)
            ? Path.GetFullPath(options.BaseOptions.ProjectRoot)
            : options.BaseOptions.FullRootPath;

        var path = Path.Combine(root, _localDirName, _localFileName);

        if (!File.Exists(path))
            return null;

        var bytes = await File.ReadAllBytesAsync(path, ct);
        try
        {
            return DependencyGraphSerializer.Deserialize(bytes, root);
        }
        catch { return null; }
    }
}
