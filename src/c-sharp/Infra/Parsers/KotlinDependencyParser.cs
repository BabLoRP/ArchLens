using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Archlens.Domain.Interfaces;
using Archlens.Domain.Models.Records;

namespace Archlens.Infra.Parsers;

public class KotlinDependencyParser(ParserOptions _options) : IDependencyParser
{
    readonly string _rootPackage = _options.BaseOptions.ProjectName;

    public async Task<IReadOnlyList<RelativePath>> ParseFileDependencies(
        string path,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var imports = new List<RelativePath>();

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
                if (ct.IsCancellationRequested)
                {
                    reader.Close();
                    ct.ThrowIfCancellationRequested();
                }
                var match = regex.Match(line);
                if (!match.Success)
                    continue;

                var dep = match.Groups[1].Value.Trim();
                if (dep.Length > 0)
                {
                    var packagePath = dep.TrimEnd('*').TrimEnd('.').Replace('.', '/');
                    var rel = RelativePath.Directory(_options.BaseOptions.FullRootPath, packagePath);
                    imports.Add(rel);
                }
            }
            reader.Close();

            return imports;
        }
        catch (Exception e)
        {
            Console.WriteLine($"KotlinDependencyParser: failed to parse '{path}': {e.Message}");
            return [];
        }
    }
}