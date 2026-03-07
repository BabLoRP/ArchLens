using Archlens.Domain.Models.Enums;
using Archlens.Domain.Models.Records;
using Archlens.Infra.Parsers;
using ArchlensTests.Utils;

namespace ArchlensTests.Infra.Parsers;

public sealed class CsharpSyntaxWalkerParserTests : IDisposable
{
    private readonly TestFileSystem _fs = new();
    public void Dispose() => _fs.Dispose();

    private ParserOptions Opts(string projectName = "Archlens") => new(
        BaseOptions: new(
            FullRootPath: _fs.Root,
            ProjectRoot: _fs.Root,
            ProjectName: projectName),
        Languages: [Language.CSharp],
        Exclusions: [],
        FileExtensions: [".cs"]);

    private string Write(string fileName, string content) =>
        _fs.File(fileName, content);

    private RelativePath Dir(string path) =>
        RelativePath.Directory(_fs.Root, path);

    [Fact]
    public async Task Returns_Empty_ForFileWithNoUsings()
    {
        var path = Write("A.cs", "class A {}");
        var result = await new CsharpSyntaxWalkerParser(Opts()).ParseFileDependencies(path);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Returns_Empty_ForEmptyFile()
    {
        var path = Write("Empty.cs", "");
        var result = await new CsharpSyntaxWalkerParser(Opts()).ParseFileDependencies(path);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Captures_SingleProjectUsing()
    {
        var path = Write("A.cs", "using Archlens.Domain.Models;");
        var result = await new CsharpSyntaxWalkerParser(Opts()).ParseFileDependencies(path);

        Assert.Single(result);
        Assert.Equal(Dir("./Domain/Models/"), result[0]);
    }

    [Fact]
    public async Task Captures_MultipleProjectUsings()
    {
        var path = Write("A.cs", """
            using Archlens.Domain.Models;
            using Archlens.Infra.Factories;
            """);
        var result = await new CsharpSyntaxWalkerParser(Opts()).ParseFileDependencies(path);

        Assert.Equal(2, result.Count);
        Assert.Contains(Dir("./Domain/Models/"), result);
        Assert.Contains(Dir("./Infra/Factories/"), result);
    }

    [Fact]
    public async Task Filters_ThirdParty_Usings()
    {
        var path = Write("A.cs", """
            using System;
            using Microsoft.CodeAnalysis;
            using Archlens.Domain.Models;
            """);
        var result = await new CsharpSyntaxWalkerParser(Opts()).ParseFileDependencies(path);

        Assert.Single(result);
        Assert.Equal(Dir("./Domain/Models/"), result[0]);
    }

    [Fact]
    public async Task Converts_DotSeparatedNamespace_ToSlashPath()
    {
        var path = Write("A.cs", "using Archlens.Domain.Models.Records;");
        var result = await new CsharpSyntaxWalkerParser(Opts()).ParseFileDependencies(path);

        Assert.Single(result);
        Assert.Equal(Dir("./Domain/Models/Records/"), result[0]);
        Assert.DoesNotContain('.', result[0].Value.Replace("./", ""));
    }

    [Fact]
    public async Task Handles_UsingStatic_Directive()
    {
        var path = Write("A.cs", "using static Archlens.Domain.Utils.PathHelper;");
        var result = await new CsharpSyntaxWalkerParser(Opts()).ParseFileDependencies(path);

        Assert.Single(result);
        Assert.Equal(Dir("./Domain/Utils/PathHelper/"), result[0]);
    }

    [Fact]
    public async Task Handles_UsingAlias_Directive()
    {
        var path = Write("A.cs", "using MyAlias = Archlens.Domain.Models;");
        var result = await new CsharpSyntaxWalkerParser(Opts()).ParseFileDependencies(path);

        Assert.Single(result);
        Assert.Equal(Dir("./Domain/Models/"), result[0]);
    }

    [Fact]
    public async Task Handles_GlobalUsing_Directive()
    {
        var path = Write("A.cs", "global using Archlens.Domain.Models;");
        var result = await new CsharpSyntaxWalkerParser(Opts()).ParseFileDependencies(path);

        Assert.Single(result);
        Assert.Equal(Dir("./Domain/Models/"), result[0]);
    }

    [Fact]
    public async Task Does_Not_Match_OtherProjectName()
    {
        var path = Write("A.cs", "using OtherProject.Domain.Models;");
        var result = await new CsharpSyntaxWalkerParser(Opts("Archlens")).ParseFileDependencies(path);
        Assert.Empty(result);
    }

    [Fact]
    public async Task CancellationToken_PropagatesFromRoslyn_WhenPreCancelled()
    {
        var path = Write("A.cs", "using Archlens.Domain.Models;");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CsharpSyntaxWalkerParser(Opts()).ParseFileDependencies(path, cts.Token));
    }
}
