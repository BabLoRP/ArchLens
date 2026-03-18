using Archlens.Domain.Models.Enums;
using Archlens.Domain.Models.Records;
using Archlens.Infra.Parsers;
using ArchlensTests.Utils;

namespace ArchlensTests.Infra.Parsers;

public sealed class KotlinDependencyParserTests : IDisposable
{
    private readonly TestFileSystem _fs = new();
    public void Dispose() => _fs.Dispose();

    private ParserOptions Opts(string projectName = "com.example") => new(
        BaseOptions: new(
            FullRootPath: _fs.Root,
            ProjectRoot: _fs.Root,
            ProjectName: projectName),
        Languages: [Language.Kotlin],
        Exclusions: [],
        FileExtensions: [".kt"]);

    private string Write(string fileName, string content) =>
        _fs.File(fileName, content);

    private RelativePath Dir(string path) =>
        RelativePath.Directory(_fs.Root, path);

    [Fact]
    public async Task Returns_Empty_WhenRootPackageIsEmpty()
    {
        var path = Write("A.kt", "import com.example.Inventory");
        var result = await new KotlinDependencyParser(Opts("")).ParseFileDependencies(path);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Returns_Empty_WhenRootPackageIsWhitespace()
    {
        var path = Write("A.kt", "import com.example.Inventory");
        var result = await new KotlinDependencyParser(Opts("   ")).ParseFileDependencies(path);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Returns_Empty_ForFileWithNoImports()
    {
        var path = Write("A.kt", "class A");
        var result = await new KotlinDependencyParser(Opts()).ParseFileDependencies(path);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Captures_BasicImport()
    {
        var path = Write("A.kt", "import com.example.Inventory");
        var result = await new KotlinDependencyParser(Opts()).ParseFileDependencies(path);
        Assert.Single(result);
    }

    [Fact]
    public async Task Handles_ImportWithAlias()
    {
        var path = Write("A.kt", "import com.example.Inventory.Stock as S");
        var result = await new KotlinDependencyParser(Opts()).ParseFileDependencies(path);
        Assert.Single(result);
    }

    [Fact]
    public async Task Filters_NonProjectImports()
    {
        var path = Write("A.kt", """
            import kotlin.collections.List
            import java.io.File
            import com.example.Inventory
            """);
        var result = await new KotlinDependencyParser(Opts()).ParseFileDependencies(path);
        Assert.Single(result);
    }

    [Fact]
    public async Task Captures_MultipleImports()
    {
        var path = Write("A.kt", """
            import com.example.Inventory
            import com.example.Warehouse
            """);
        var result = await new KotlinDependencyParser(Opts()).ParseFileDependencies(path);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Handles_LeadingWhitespace_InImportLine()
    {
        var path = Write("A.kt", "    import com.example.Inventory");
        var result = await new KotlinDependencyParser(Opts()).ParseFileDependencies(path);
        Assert.Single(result);
    }

    [Fact]
    public async Task AliasImport_DoesNotInclude_AliasInPath()
    {
        var path = Write("A.kt", "import com.example.Inventory as MyInventory");
        var result = await new KotlinDependencyParser(Opts()).ParseFileDependencies(path);

        Assert.Single(result);
        Assert.DoesNotContain("MyInventory", result[0].Value);
        Assert.DoesNotContain("as", result[0].Value);
    }

    [Fact]
    public async Task SingleLevelImport_PathUsesSlashes_NotDots()
    {
        var path = Write("A.kt", "import com.example.Inventory");
        var result = await new KotlinDependencyParser(Opts()).ParseFileDependencies(path);

        Assert.Single(result);
        Assert.Equal(Dir("./Inventory/"), result[0]);
        Assert.DoesNotContain('.', result[0].Value.Replace("./", ""));
    }

    [Fact]
    public async Task MultiLevelImport_PathUsesSlashes_NotDots()
    {
        var path = Write("A.kt", "import com.example.Inventory.Stock.Labels");
        var result = await new KotlinDependencyParser(Opts()).ParseFileDependencies(path);

        Assert.Single(result);
        Assert.Equal(Dir("./Inventory/Stock/Labels/"), result[0]);
    }

    [Fact]
    public async Task AliasImport_PathUsesSlashes_NotDots()
    {
        var path = Write("A.kt", "import com.example.Inventory.Stock as S");
        var result = await new KotlinDependencyParser(Opts()).ParseFileDependencies(path);

        Assert.Single(result);
        Assert.Equal(Dir("./Inventory/Stock/"), result[0]);
    }

    [Fact]
    public async Task WildcardImport_MapsToParentDirectory()
    {
        var path = Write("A.kt", "import com.example.Inventory.Stock.*");
        var result = await new KotlinDependencyParser(Opts()).ParseFileDependencies(path);

        Assert.Single(result);
        Assert.Equal(Dir("./Inventory/Stock/"), result[0]);
        Assert.DoesNotContain('*', result[0].Value);
    }

    [Fact]
    public async Task CancellationToken_Propagates_WhenPreCancelled()
    {
        var path = Write("A.kt", """
            import com.example.Inventory
            import com.example.Warehouse
            """);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new KotlinDependencyParser(Opts()).ParseFileDependencies(path, cts.Token));
    }
}
