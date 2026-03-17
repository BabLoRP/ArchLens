using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Archlens.Domain.Interfaces;
using Archlens.Domain.Models.Records;

namespace Archlens.Infra.Parsers;

public class GoDependencyParser(ParserOptions _options) : IDependencyParser
{
    readonly string _projectImportPrefix =
        string.IsNullOrWhiteSpace(_options.BaseOptions.ProjectName)
            ? string.Empty
            : _options.BaseOptions.ProjectName.TrimEnd('/') + "/";

    public async Task<IReadOnlyList<RelativePath>> ParseFileDependencies(
        string path,
        CancellationToken ct = default)
    {
        var deps = new List<RelativePath>();


        if (string.IsNullOrEmpty(_projectImportPrefix))
            return deps; // if we do not know the project prefix we cannot decide what is internal

        StreamReader reader = new(path);

        var insideBlock = false;

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct);
            if (line is null)
                break;

            insideBlock = ProcessImportLine(line.Trim(), insideBlock, deps);
        }

        return deps;
    }
    private bool ProcessImportLine(string trimmed, bool insideBlock, List<RelativePath> deps)
    {
        if (insideBlock)
        {
            if (trimmed.StartsWith(')'))
                return false;

            ExtractImportFromLine(trimmed, deps);
            return true;
        }

        if (!trimmed.StartsWith("import", StringComparison.Ordinal))
            return false;

        ExtractImportFromLine(trimmed, deps);

        if (!trimmed.Contains('('))
            return false;

        return !trimmed.Contains(')');
    }

    private void ExtractImportFromLine(string line, List<RelativePath> deps)
    {
        var firstQuote = line.IndexOf('"');
        if (firstQuote < 0)
            return;

        var secondQuote = line.IndexOf('"', firstQuote + 1);
        if (secondQuote <= firstQuote)
            return;

        var importPath = line.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
        AddIfInternal(importPath, deps);
    }

    private void AddIfInternal(string importPath, List<RelativePath> deps)
    {
        if (!importPath.StartsWith(_projectImportPrefix, StringComparison.Ordinal))
            return;

        var relative = importPath.Substring(_projectImportPrefix.Length);
        if (relative.Length == 0)
            return;

        var canonical = RelativePath.Directory(_options.BaseOptions.FullRootPath, relative);
        deps.Add(canonical);
    }
}
