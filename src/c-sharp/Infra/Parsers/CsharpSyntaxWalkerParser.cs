using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Archlens.Domain.Interfaces;
using Archlens.Domain.Models.Records;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Archlens.Infra.Parsers;

public class CsharpSyntaxWalkerParser(ParserOptions _options) : CSharpSyntaxWalker, IDependencyParser
{
    private sealed class UsingCollector(string projectName) : CSharpSyntaxWalker
    {
        public List<UsingDirectiveSyntax> Usings { get; } = [];

        public override void VisitUsingDirective(UsingDirectiveSyntax node)
        {
            if (node.Name.ToString().StartsWith(projectName))
                Usings.Add(node);
        }
    }

    public async Task<IReadOnlyList<RelativePath>> ParseFileDependencies(string path, CancellationToken ct = default)
    {
        var text = await File.ReadAllTextAsync(path, ct);
        var tree = CSharpSyntaxTree.ParseText(text, cancellationToken: ct);
        var walker = new UsingCollector(_options.BaseOptions.ProjectName);
        walker.Visit(tree.GetCompilationUnitRoot(ct));

        return [.. walker.Usings
            .Select(u =>
            {
                var rel = u.Name.ToString()
                    .Replace(".", "/")
                    .Replace(_options.BaseOptions.ProjectName, ".") + "/";
                return RelativePath.Directory(_options.BaseOptions.FullRootPath, rel);
            })];
    }

}