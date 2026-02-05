using Archlens.Domain.Interfaces;
using Archlens.Domain.Models.Records;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Archlens.Infra.Parsers;

public class GoDependencyParser(ParserOptions _options) : IDependencyParser
{
    readonly string _projectImportPrefix =
        string.IsNullOrWhiteSpace(_options.BaseOptions.ProjectName)
            ? string.Empty
            : _options.BaseOptions.ProjectName.TrimEnd('/') + "/";

    public async Task<IReadOnlyList<string>> ParseFileDependencies(
        string path,
        CancellationToken ct = default)
    {
        var deps = new List<string>();

        
        if (string.IsNullOrEmpty(_projectImportPrefix))
            return deps; // if we do not know the project prefix we cannot decide what is internal

        StreamReader reader = new(path);

        var insideBlock = false;

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync();
            if (line is null)
                break;

            var trimmed = line.Trim();

            if (!insideBlock)
            {
                if (!trimmed.StartsWith("import", StringComparison.Ordinal))
                    continue;

                if (!trimmed.Contains('('))
                {
                    ExtractImportFromLine(trimmed, deps);
                    continue;
                }

                insideBlock = true;
                ExtractImportFromLine(trimmed, deps);
                if (trimmed.Contains(')'))
                    insideBlock = false;
            }
            else
            {
                if (trimmed.StartsWith(")", StringComparison.Ordinal))
                {
                    insideBlock = false;
                    continue;
                }
                ExtractImportFromLine(trimmed, deps);
            }
        }

        return deps;
    }

    private void ExtractImportFromLine(string line, List<string> deps)
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

    private void AddIfInternal(string importPath, List<string> deps)
    {
        if (!importPath.StartsWith(_projectImportPrefix, StringComparison.Ordinal))
            return;

        var relative = importPath.Substring(_projectImportPrefix.Length);
        if (relative.Length == 0)
            return;

        var canonical = relative.Replace('/', '.');
        deps.Add(canonical);
    }
}
