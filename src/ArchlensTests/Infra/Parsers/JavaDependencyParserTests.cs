using Archlens.Domain.Models.Enums;
using Archlens.Domain.Models.Records;
using Archlens.Infra.Parsers;
using ArchlensTests.Utils;

namespace ArchlensTests.Infra.Parsers;

public sealed class JavaDependencyParserTests : IDisposable
{
    private readonly TestFileSystem _fs = new();
    public void Dispose() => _fs.Dispose();

    private ParserOptions Opts(string projectName = "com.example") => new(
        BaseOptions: new(
            FullRootPath: _fs.Root,
            ProjectRoot: _fs.Root,
            ProjectName: projectName),
        Languages: [Language.Java],
        Exclusions: [],
        FileExtensions: [".java"]);

    private string Write(string fileName, string content) =>
        _fs.File(fileName, content);

    private RelativePath Dir(string path) =>
        RelativePath.Directory(_fs.Root, path);

    [Fact]
    public async Task Returns_Empty_ForFileWithNoImports()
    {
        var path = Write("A.java", "public class A {}");
        var result = await new JavaDependencyParser(Opts()).ParseFileDependencies(path);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Captures_BasicImport()
    {
        var path = Write("A.java", "import com.example.Domain;");
        var result = await new JavaDependencyParser(Opts()).ParseFileDependencies(path);
        Assert.Single(result);
    }

    [Fact]
    public async Task Captures_StaticImport()
    {
        var path = Write("A.java", "import static com.example.Domain.Utils;");
        var result = await new JavaDependencyParser(Opts()).ParseFileDependencies(path);
        Assert.Single(result);
    }

    [Fact]
    public async Task Filters_NonProjectImports()
    {
        var path = Write("A.java", """
            import java.util.List;
            import org.springframework.Component;
            import com.example.Domain;
            """);
        var result = await new JavaDependencyParser(Opts()).ParseFileDependencies(path);
        Assert.Single(result);
    }

    [Fact]
    public async Task Captures_MultipleImports()
    {
        var path = Write("A.java", """
            import com.example.Domain;
            import com.example.Infra;
            """);
        var result = await new JavaDependencyParser(Opts()).ParseFileDependencies(path);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task SingleLevelImport_PathUsesSlashes_NotDots()
    {
        var path = Write("A.java", "import com.example.Domain;");
        var result = await new JavaDependencyParser(Opts()).ParseFileDependencies(path);

        Assert.Single(result);
        Assert.Equal(Dir("./Domain/"), result[0]);
        Assert.DoesNotContain('.', result[0].Value.Replace("./", ""));
    }

    [Fact]
    public async Task MultiLevelImport_PathUsesSlashes_NotDots()
    {
        var path = Write("A.java", "import com.example.Domain.Models.Records;");
        var result = await new JavaDependencyParser(Opts()).ParseFileDependencies(path);

        Assert.Single(result);
        Assert.Equal(Dir("./Domain/Models/Records/"), result[0]);
    }

    [Fact]
    public async Task StaticImport_PathUsesSlashes_NotDots()
    {
        var path = Write("A.java", "import static com.example.Domain.Utils.PathHelper;");
        var result = await new JavaDependencyParser(Opts()).ParseFileDependencies(path);

        Assert.Single(result);
        Assert.Equal(Dir("./Domain/Utils/PathHelper/"), result[0]);
    }

    [Fact]
    public async Task WildcardImport_MapsToParentDirectory()
    {
        var path = Write("A.java", "import com.example.Domain.Models.*;");
        var result = await new JavaDependencyParser(Opts()).ParseFileDependencies(path);

        Assert.Single(result);
        Assert.Equal(Dir("./Domain/Models/"), result[0]);
        Assert.DoesNotContain('*', result[0].Value);
    }

    [Fact]
    public async Task CancellationToken_Propagates_WhenPreCancelled()
    {
        var path = Write("A.java", """
            import com.example.Domain;
            import com.example.Infra;
            """);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new JavaDependencyParser(Opts()).ParseFileDependencies(path, cts.Token));
    }
}
