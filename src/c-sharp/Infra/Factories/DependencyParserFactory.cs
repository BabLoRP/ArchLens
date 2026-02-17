using System;
using System.Collections.Generic;
using Archlens.Domain.Interfaces;
using Archlens.Domain.Models.Enums;
using Archlens.Domain.Models.Records;
using Archlens.Infra.Parsers;

namespace Archlens.Infra.Factories;

public sealed class DependencyParserFactory
{
    public static IReadOnlyList<IDependencyParser> SelectDependencyParser(ParserOptions o)
    {
        List<IDependencyParser> parsers = [];

        foreach (var lang in o.Languages)
        {
            IDependencyParser parser = lang switch
            {
                Language.CSharp => new CsharpDependencyParser(o),
                Language.Go => new GoDependencyParser(o),
                Language.Kotlin => new KotlinDependencyParser(o),
                _ => throw new NotSupportedException(nameof(lang))
            };
            parsers.Add(parser);
        }
        return parsers;
    }
}