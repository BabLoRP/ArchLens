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
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            if (node.Name.ToString().StartsWith(projectName))
                Usings.Add(node);
#pragma warning restore CS8602 // Dereference of a possibly null reference.

        }
    }

    public async Task<IReadOnlyList<RelativePath>> ParseFileDependencies(string absPath, CancellationToken ct = default)
    {
        var text = await File.ReadAllTextAsync(absPath, ct);
        var tree = CSharpSyntaxTree.ParseText(text, cancellationToken: ct);
        var walker = new UsingCollector(_options.BaseOptions.ProjectName);
        walker.Visit(tree.GetCompilationUnitRoot(ct));

        return [.. walker.Usings
            .Select(u =>
            {
                var isStatic = u?.StaticKeyword.Text == "static";
                // If using is static it references a concrete class, we trim that away to only have the folder
                var name = isStatic ? string.Join('.', u?.Name?.ToString().Split('.').SkipLast(1)!) : u?.Name?.ToString();
                var rel = name?
                    .Replace(".", "/")
                    .Replace(_options.BaseOptions.ProjectName, ".") + "/";
                return RelativePath.Directory(_options.BaseOptions.FullRootPath, rel);
            })];
    }

}
