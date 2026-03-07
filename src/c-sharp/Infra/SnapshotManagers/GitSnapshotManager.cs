using Archlens.Domain;
using Archlens.Domain.Interfaces;
using Archlens.Domain.Models;
using Archlens.Domain.Models.Records;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Archlens.Infra.SnapshotManagers;

public sealed class GitSnapshotManager : ISnapshotManager
{
    private readonly string _gitDirName;
    private readonly string _gitFileName;
    private readonly LocalSnapshotManager _localManager;
    private readonly HttpClient _http; // injected for tests

    public GitSnapshotManager(string gitDirName, string gitFileName)
        : this(gitDirName, gitFileName, handler: null) { }

    public GitSnapshotManager(string gitDirName, string gitFileName, HttpMessageHandler handler)
    {
        _gitDirName = gitDirName;
        _gitFileName = gitFileName;
        _localManager = new LocalSnapshotManager(gitDirName, gitFileName);
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Archlens-GitSnapshot/1.0");
    }

    public async Task SaveGraphAsync(ProjectDependencyGraph graph, SnapshotOptions options, CancellationToken ct = default)
        => await _localManager.SaveGraphAsync(graph, options, ct);
    
    public async Task<ProjectDependencyGraph> GetLastSavedDependencyGraphAsync(SnapshotOptions options, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.GitInfo.Url))
            throw new ArgumentException("GitUrl must be provided for GitSnaphotManager. Options has registered GitUrl as Null or Whitespace - has it been correctly configured in .archlens json?");

        if (!TryParseGitHubRepo(options.GitInfo.Url, out var owner, out var repo))
            throw new ArgumentException("Could not parse GitUrl (accepted formats: https://github.com/owner/repo, https://github.com/owner/repo.git, http(s)://github.enterprise.tld/owner/repo).");

        var url = BuildRawUrl(owner, repo, options.GitInfo.Branch, options.BaseOptions.ProjectRoot, _gitDirName, _gitFileName);

        try
        {
            var json = await HttpGetAsync(url, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                throw new Exception("Unable to find main branch's graph snapshot");
            var graph = DependencyGraphSerializer.Deserialize(json, options.BaseOptions.FullRootPath);
            if (graph is not null) return graph;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        { 
            throw new Exception($"Error fetching graph snapshot from GitHub: {e.Message}");
        }
        return null;
    }

    private static bool TryParseGitHubRepo(string url, out string owner, out string repo)
    {
        owner = repo = string.Empty;

        try
        {
            // Accept:
            // - https://github.com/owner/repo
            // - https://github.com/owner/repo.git
            // - http(s)://github.enterprise.tld/owner/repo
            var uri = new Uri(url);
            if (!uri.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase))
                return false;

            var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return false;

            owner = parts[0];
            repo = parts[1];
            if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                repo = repo[..^4];

            return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repo);
        }
        catch
        {
            return false;
        }
    }

    private static string BuildRawUrl(string owner, string repo, string branch, string rootFolder, string snapshotDir, string snapshotFile)
    {
        // https://raw.githubusercontent.com/{owner}/{repo}/refs/heads/{branch}/{rootfolder}/{dir}/{file}
        static string Seg(string s) => WebUtility.UrlEncode(s ?? string.Empty).Replace("+", "%20");

        var sb = new StringBuilder("https://raw.githubusercontent.com/");
        sb.Append(Seg(owner)).Append('/').Append(Seg(repo)).Append("/refs/heads/").Append(branch).Append('/').Append(rootFolder);
        if (!sb.ToString().EndsWith('/')) sb.Append('/');
        if (!string.IsNullOrWhiteSpace(snapshotDir))
        {
            var trimmed = snapshotDir.Trim('/', '\\');
            if (!string.IsNullOrEmpty(trimmed))
                sb.Append(Seg(trimmed)).Append('/');
        }
        sb.Append(Seg(snapshotFile));
        return sb.ToString();
    }

    private async Task<string> HttpGetAsync(string url, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}