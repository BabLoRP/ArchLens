using System;
using System.Collections.Generic;
using System.IO;
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
    public ICollection<UsingDirectiveSyntax> Usings { get; set; } = [];

    public override void VisitUsingDirective(UsingDirectiveSyntax node)
    {
        if (node.Name.ToString().StartsWith(_options.BaseOptions.ProjectName))
        {
            Usings.Add(node);
        }
    }

    public async Task<IReadOnlyList<RelativePath>> ParseFileDependencies(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Usings = [];
        string lines = "";
        List<RelativePath> usings = [];

        try
        {
            StreamReader sr = new(path);

            string line = await sr.ReadLineAsync(ct);

            while (line != null)
            {
                if (ct.IsCancellationRequested)
                {
                    sr.Close();
                    ct.ThrowIfCancellationRequested();
                }
                lines += "\n" + line;
                line = await sr.ReadLineAsync(ct);
            }

            sr.Close();
        }
        catch (Exception e)
        {
            Console.WriteLine("Exception: " + e.Message);
        }

        SyntaxTree tree = CSharpSyntaxTree.ParseText(lines, cancellationToken: ct);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot(ct);

        Visit(root);

        foreach (var directive in Usings)
        {
            ct.ThrowIfCancellationRequested();
            var directivePath = directive.Name.ToString().Replace(".", "/").Replace(_options.BaseOptions.ProjectName, ".") + "/";
            var rel = RelativePath.Directory(_options.BaseOptions.FullRootPath, directivePath);
            usings.Add(rel);
        }

        return usings;
    }
}