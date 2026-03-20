using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Archlens.Domain.Interfaces;
using Archlens.Domain.Models.Records;

namespace Archlens.Infra.Parsers;

public class JavaDependencyParser(ParserOptions _options) : IDependencyParser
{
    public async Task<IReadOnlyList<RelativePath>> ParseFileDependencies(string path, CancellationToken ct = default)
    {
        /*
            open file from given path
            match regex "import ProjectName." or "import static ProjectName."
            take all matches and put in list
            return list
        */
        ct.ThrowIfCancellationRequested();
        List<RelativePath> usings = [];

        string regex = $$"""import\s+(static\s+)?{{_options.BaseOptions.ProjectName}}\.(.+);""";

        try
        {
            using (StreamReader sr = new(path))
            {
                string? line = await sr.ReadLineAsync(ct);

                while (line != null)
                {
                    if (ct.IsCancellationRequested)
                    {
                        sr.Close();
                        ct.ThrowIfCancellationRequested();
                    }

                    var match = Regex.Match(line, regex, RegexOptions.None, TimeSpan.FromMilliseconds(200));
                    if (match.Success)
                    {
                        var packagePath = match.Groups[2].Value.TrimEnd('*').TrimEnd('.').Replace('.', '/');
                        var relativePath = RelativePath.Directory(_options.BaseOptions.FullRootPath, packagePath);
                        usings.Add(relativePath);
                    }
                    line = await sr.ReadLineAsync(ct);
                }
            }
            return usings;
        }
        catch (Exception e)
        {
            Console.WriteLine("Exception: " + e.Message);
            return [];
        }

    }
}
