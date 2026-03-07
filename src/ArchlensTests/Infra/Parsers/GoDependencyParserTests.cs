using Archlens.Domain.Models.Enums;
using Archlens.Domain.Models.Records;
using Archlens.Infra.Parsers;
using ArchlensTests.Utils;

namespace ArchlensTests.Infra.Parsers;

public sealed class GoDependencyParserTests : IDisposable
{
    private readonly TestFileSystem _fs = new();
    public void Dispose() => _fs.Dispose();

    private ParserOptions Opts(string projectName = "github.com/myorg/myrepo") => new(
        BaseOptions: new(
            FullRootPath: _fs.Root,
            ProjectRoot: _fs.Root,
            ProjectName: projectName),
        Languages: [Language.Go],
        Exclusions: [],
        FileExtensions: [".go"]);

    private string Write(string fileName, string content) =>
        _fs.File(fileName, content);

    private RelativePath Dir(string path) =>
        RelativePath.Directory(_fs.Root, path);

    [Fact]
    public async Task Returns_Empty_WhenProjectPrefixIsEmpty()
    {
        var path = Write("main.go", """import "github.com/myorg/myrepo/domain" """);
        var result = await new GoDependencyParser(Opts("")).ParseFileDependencies(path);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Returns_Empty_ForFileWithNoImports()
    {
        var path = Write("main.go", "package main\n\nfunc main() {}");
        var result = await new GoDependencyParser(Opts()).ParseFileDependencies(path);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Handles_SingleLineImport()
    {
        var path = Write("main.go", """import "github.com/myorg/myrepo/domain/models" """);
        var result = await new GoDependencyParser(Opts()).ParseFileDependencies(path);

        Assert.Single(result);
        Assert.Equal(Dir("./domain/models/"), result[0]);
    }

    [Fact]
    public async Task Handles_BlockImport_MultiplePackages()
    {
        var path = Write("main.go", """
            import (
                "github.com/myorg/myrepo/domain/models"
                "github.com/myorg/myrepo/infra/factories"
            )
            """);
        var result = await new GoDependencyParser(Opts()).ParseFileDependencies(path);

        Assert.Equal(2, result.Count);
        Assert.Contains(Dir("./domain/models/"), result);
        Assert.Contains(Dir("./infra/factories/"), result);
    }

    [Fact]
    public async Task Filters_ExternalImports()
    {
        var path = Write("main.go", """
            import (
                "fmt"
                "os"
                "github.com/myorg/myrepo/domain/models"
            )
            """);
        var result = await new GoDependencyParser(Opts()).ParseFileDependencies(path);

        Assert.Single(result);
        Assert.Equal(Dir("./domain/models/"), result[0]);
    }

    [Fact]
    public async Task Handles_ImportWithAlias()
    {
        var path = Write("main.go", """import mdl "github.com/myorg/myrepo/domain/models" """);
        var result = await new GoDependencyParser(Opts()).ParseFileDependencies(path);

        Assert.Single(result);
        Assert.Equal(Dir("./domain/models/"), result[0]);
    }

    [Fact]
    public async Task Handles_BlankLinesInsideImportBlock()
    {
        var path = Write("main.go", """
            import (
                "github.com/myorg/myrepo/domain/models"

                "github.com/myorg/myrepo/infra"
            )
            """);
        var result = await new GoDependencyParser(Opts()).ParseFileDependencies(path);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Handles_CommentLinesInsideImportBlock()
    {
        var path = Write("main.go", """
            import (
                // internal
                "github.com/myorg/myrepo/domain/models"
                // external
                "fmt"
            )
            """);
        var result = await new GoDependencyParser(Opts()).ParseFileDependencies(path);

        Assert.Single(result);
        Assert.Equal(Dir("./domain/models/"), result[0]);
    }

    [Fact]
    public async Task Handles_InlineSingleItemBlock()
    {
        var path = Write("main.go", """import ("github.com/myorg/myrepo/domain")""");
        var result = await new GoDependencyParser(Opts()).ParseFileDependencies(path);

        Assert.Single(result);
        Assert.Equal(Dir("./domain/"), result[0]);
    }

    [Fact]
    public async Task Handles_MultipleImportDeclarations()
    {
        var path = Write("main.go", """
            import "github.com/myorg/myrepo/domain"
            import "github.com/myorg/myrepo/infra"
            """);
        var result = await new GoDependencyParser(Opts()).ParseFileDependencies(path);

        Assert.Equal(2, result.Count);
        Assert.Contains(Dir("./domain/"), result);
        Assert.Contains(Dir("./infra/"), result);
    }

    [Fact]
    public async Task DoesNotAdd_ProjectRootItself_AsDepedency()
    {
        // import "github.com/myorg/myrepo" — the root module itself; relative is empty
        var path = Write("main.go", """import "github.com/myorg/myrepo" """);
        var result = await new GoDependencyParser(Opts()).ParseFileDependencies(path);
        Assert.Empty(result);
    }

    [Fact]
    public async Task CancellationToken_Propagates_WhenPreCancelled()
    {
        var path = Write("main.go", """
            import (
                "github.com/myorg/myrepo/domain/models"
                "github.com/myorg/myrepo/infra"
            )
            """);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GoDependencyParser(Opts()).ParseFileDependencies(path, cts.Token));
    }
}
