using Archlens.Domain.Interfaces;
using Archlens.Domain.Models.Records;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Archlens.Infra.Parsers;

public class KotlinDependencyParser(Options _options) : IDependencyParser
{
    readonly string _rootPackage = _options.ProjectName;

    public async Task<IReadOnlyList<string>> ParseFileDependencies(
        string path,
        CancellationToken ct = default)
    {
        var imports = new List<string>();

        if (string.IsNullOrWhiteSpace(_rootPackage))
            return imports;

        var regex = new Regex(
            $@"^\s*import\s+{Regex.Escape(_rootPackage)}\.(.+?)(\s+as\s+\w+)?\s*$",
            RegexOptions.Compiled);

        try
        {
            StreamReader reader = new(path);

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                ct.ThrowIfCancellationRequested();

                var match = regex.Match(line);
                if (!match.Success)
                    continue;

                var dep = match.Groups[1].Value.Trim();
                if (dep.Length > 0)
                    imports.Add(dep);
            }

            return imports;
        }
        catch (Exception e)
        {
            Console.WriteLine($"KotlinDependencyParser: failed to parse '{path}': {e.Message}");
            return [];
        }
    }
}