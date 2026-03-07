using Archlens.Domain.Models.Enums;
using Archlens.Infra;
using ArchlensTests.Utils;

namespace ArchlensTests.Infra;

public sealed class ConfigManagerTests : IDisposable
{
    private readonly TestFileSystem _fs = new();

    public void Dispose() => _fs.Dispose();

    private record ConfigOverrides(
        string? ProjectRoot = null,
        string? RootFolder = null,
        string? ProjectName = null,
        string? Name = null,
        string? Format = "\"puml\"",
        string? GitUrl = "\"https://github.com/owner/repo\"",
        string? GitBranch = "\"main\"",
        string? SnapshotDir = "\".archlens\"",
        string? SnapshotFile = "\"snapshot\"",
        string? Exclusions = "[]",
        string? FileExtensions = "[\".cs\"]",
        string? Views = "{\"testView\":{\"packages\":[],\"ignorePackages\":[]}}",
        string? SaveLocation = "\"diagrams\""
    );

    private string WriteConfig(ConfigOverrides? overrides = null)
    {
        var o = overrides ?? new ConfigOverrides();
        var rootFolder = o.RootFolder ?? $"\"{EscapeJson(_fs.Root)}\"";
        var projectRoot = o.ProjectRoot ?? "\"\"";
        var name = o.Name ?? "\"TestProject\"";
        var projectName = o.ProjectName ?? "\"\"";

        var json = $$"""
            {
              "projectRoot": {{projectRoot}},
              "rootFolder": {{rootFolder}},
              "projectName": {{projectName}},
              "name": {{name}},
              "format": {{o.Format}},
              "github": { "url": {{o.GitUrl}}, "branch": {{o.GitBranch}} },
              "snapshotDir": {{o.SnapshotDir}},
              "snapshotFile": {{o.SnapshotFile}},
              "exclusions": {{o.Exclusions}},
              "fileExtensions": {{o.FileExtensions}},
              "views": {{o.Views}},
              "saveLocation": {{o.SaveLocation}}
            }
            """;

        return _fs.File("archlens.json", json);
    }

    private static string EscapeJson(string path) =>
        path.Replace("\\", "\\\\");

    private static ConfigManager Manager(string configPath) => new(configPath);

    [Fact]
    public async Task Throws_ArgumentException_WhenPathIsEmpty()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Manager("").LoadAsync());
    }

    [Fact]
    public async Task Throws_ArgumentException_WhenPathIsWhitespace()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Manager("   ").LoadAsync());
    }

    [Fact]
    public async Task Throws_FileNotFoundException_WhenConfigFileMissing()
    {
        var nonExistent = Path.Combine(_fs.Root, "missing.json");
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            Manager(nonExistent).LoadAsync());
    }

    [Fact]
    public async Task Throws_InvalidOperationException_WhenJsonIsInvalid()
    {
        var path = _fs.File("bad.json", "this is not json");
        await Assert.ThrowsAsync<Exception>(() => Manager(path).LoadAsync());
    }

    [Fact]
    public async Task Throws_DirectoryNotFoundException_WhenProjectRootDoesNotExist()
    {
        var nonExistentDir = Path.Combine(_fs.Root, "does-not-exist");
        var path = WriteConfig(new(
            RootFolder: $"\"{EscapeJson(nonExistentDir)}\"",
            ProjectRoot: "\"\""));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            Manager(path).LoadAsync());
    }

    [Fact]
    public async Task Throws_OperationCanceledException_WhenTokenCancelled()
    {
        var path = WriteConfig();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Manager(path).LoadAsync(ct: cts.Token));
    }

    [Fact]
    public async Task BaseOptions_UsesName_WhenProjectNameIsEmpty()
    {
        var path = WriteConfig(new(ProjectName: "\"\"", Name: "\"FallbackName\""));
        var (baseOptions, _, _, _) = await Manager(path).LoadAsync();
        Assert.Equal("FallbackName", baseOptions.ProjectName);
    }

    [Fact]
    public async Task BaseOptions_ProjectName_TakesPriorityOver_Name()
    {
        var path = WriteConfig(new(ProjectName: "\"Primary\"", Name: "\"Fallback\""));
        var (baseOptions, _, _, _) = await Manager(path).LoadAsync();
        Assert.Equal("Primary", baseOptions.ProjectName);
    }

    [Fact]
    public async Task BaseOptions_UsesRootFolder_WhenProjectRootIsEmpty()
    {
        var path = WriteConfig(new(ProjectRoot: "\"\""));
        var (baseOptions, _, _, _) = await Manager(path).LoadAsync();
        Assert.False(string.IsNullOrEmpty(baseOptions.ProjectRoot));
    }

    [Fact]
    public async Task BaseOptions_ProjectRoot_TakesPriorityOver_RootFolder()
    {
        var subDir = _fs.Dir("subproject");
        var path = WriteConfig(new(
            ProjectRoot: $"\"{EscapeJson(subDir)}\"",
            RootFolder: $"\"{EscapeJson(_fs.Root)}\""));

        var (baseOptions, _, _, _) = await Manager(path).LoadAsync();
        Assert.Equal(subDir, baseOptions.ProjectRoot);
    }

    [Fact]
    public async Task BaseOptions_FullRootPath_IsAbsolutePath()
    {
        var path = WriteConfig();
        var (baseOptions, _, _, _) = await Manager(path).LoadAsync();
        Assert.True(Path.IsPathFullyQualified(baseOptions.FullRootPath));
    }

    [Fact]
    public async Task BaseOptions_FullRootPath_ResolvesRelativePath_AgainstConfigFileDirectory()
    {
        var subDir = _fs.Dir("src");
        var configPath = _fs.File("nested/archlens.json", "");

        var json = $$"""
            {
              "projectRoot": "",
              "rootFolder": "../src",
              "projectName": "",
              "name": "TestProject",
              "format": "puml",
              "github": { "url": "https://github.com/owner/repo", "branch": "main" },
              "snapshotDir": ".archlens",
              "snapshotFile": "snapshot",
              "exclusions": [],
              "fileExtensions": [".cs"],
              "views": { "v": { "packages": [], "ignorePackages": [] } },
              "saveLocation": "diagrams"
            }
            """;
        File.WriteAllText(configPath, json);

        var (baseOptions, _, _, _) = await Manager(configPath).LoadAsync();

        Assert.Equal(new DirectoryInfo(subDir).FullName, baseOptions.FullRootPath);
    }

    [Fact]
    public async Task ParserOptions_NormalizesExtension_AddsDotPrefix()
    {
        var path = WriteConfig(new(FileExtensions: "[\"cs\"]"));
        var (_, parserOptions, _, _) = await Manager(path).LoadAsync();
        Assert.Contains(".cs", parserOptions.FileExtensions);
    }

    [Fact]
    public async Task ParserOptions_PreservesExtension_WithExistingDotPrefix()
    {
        var path = WriteConfig(new(FileExtensions: "[\".cs\"]"));
        var (_, parserOptions, _, _) = await Manager(path).LoadAsync();
        Assert.Contains(".cs", parserOptions.FileExtensions);
        Assert.DoesNotContain("..cs", parserOptions.FileExtensions);
    }

    [Fact]
    public async Task ParserOptions_Throws_WhenFileExtensionsIsEmpty()
    {
        var path = WriteConfig(new(FileExtensions: "[]"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Manager(path).LoadAsync());
    }

    [Theory]
    [InlineData(".cs", Language.CSharp)]
    [InlineData(".go", Language.Go)]
    [InlineData(".kt", Language.Kotlin)]
    [InlineData(".java", Language.Java)]
    public async Task ParserOptions_MapsLanguage_Correctly(string ext, Language expected)
    {
        var path = WriteConfig(new(FileExtensions: $"[\"{ext}\"]"));
        var (_, parserOptions, _, _) = await Manager(path).LoadAsync();
        Assert.Contains(expected, parserOptions.Languages);
    }

    [Fact]
    public async Task ParserOptions_Throws_ForUnsupportedExtension()
    {
        var path = WriteConfig(new(FileExtensions: "[\".rb\"]"));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            Manager(path).LoadAsync());
    }

    [Fact]
    public async Task ParserOptions_MultipleLangauges_AllMapped()
    {
        var path = WriteConfig(new(FileExtensions: "[\".cs\", \".java\"]"));
        var (_, parserOptions, _, _) = await Manager(path).LoadAsync();
        Assert.Contains(Language.CSharp, parserOptions.Languages);
        Assert.Contains(Language.Java, parserOptions.Languages);
    }

    [Fact]
    public async Task ParserOptions_TrimsWhitespace_FromExclusions()
    {
        var path = WriteConfig(new(Exclusions: "[\"  obj/  \", \"  bin/  \"]"));
        var (_, parserOptions, _, _) = await Manager(path).LoadAsync();
        Assert.Contains("obj/", parserOptions.Exclusions);
        Assert.Contains("bin/", parserOptions.Exclusions);
    }

    [Fact]
    public async Task ParserOptions_FiltersOut_BlankExclusions()
    {
        var path = WriteConfig(new(Exclusions: "[\"\", \"   \", \"obj/\"]"));
        var (_, parserOptions, _, _) = await Manager(path).LoadAsync();
        Assert.DoesNotContain("", parserOptions.Exclusions);
        Assert.DoesNotContain("   ", parserOptions.Exclusions);
        Assert.Contains("obj/", parserOptions.Exclusions);
    }

    [Fact]
    public async Task ParserOptions_EmptyExclusions_ProducesEmptyList()
    {
        var path = WriteConfig(new(Exclusions: "[]"));
        var (_, parserOptions, _, _) = await Manager(path).LoadAsync();
        Assert.Empty(parserOptions.Exclusions);
    }

    [Theory]
    [InlineData("\"json\"", RenderFormat.Json)]
    [InlineData("\"application/json\"", RenderFormat.Json)]
    [InlineData("\"puml\"", RenderFormat.PlantUML)]
    [InlineData("\"plantuml\"", RenderFormat.PlantUML)]
    [InlineData("\"plant-uml\"", RenderFormat.PlantUML)]
    public async Task RenderOptions_MapsFormat_Correctly(string jsonLiteral, RenderFormat expected)
    {
        var path = WriteConfig(new(Format: jsonLiteral));
        var (_, _, renderOptions, _) = await Manager(path).LoadAsync();
        Assert.Equal(expected, renderOptions.Format);
    }

    [Fact]
    public async Task RenderOptions_FormatMapping_IsCaseInsensitive()
    {
        var path = WriteConfig(new(Format: "\"PUML\""));
        var (_, _, renderOptions, _) = await Manager(path).LoadAsync();
        Assert.Equal(RenderFormat.PlantUML, renderOptions.Format);
    }

    [Fact]
    public async Task RenderOptions_Throws_ForUnsupportedFormat()
    {
        var path = WriteConfig(new(Format: "\"svg\""));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            Manager(path).LoadAsync());
    }

    [Fact]
    public async Task RenderOptions_ConfigFormat_TakesPriorityOver_FormatParameter()
    {
        var path = WriteConfig(new(Format: "\"json\""));
        var (_, _, renderOptions, _) = await Manager(path).LoadAsync(format: "puml");
        Assert.Equal(RenderFormat.Json, renderOptions.Format);
    }

    [Fact]
    public async Task RenderOptions_UsesFormatParameter_WhenConfigFormatIsNull()
    {
        var path = WriteConfig(new(Format: "null"));
        var (_, _, renderOptions, _) = await Manager(path).LoadAsync(format: "json");
        Assert.Equal(RenderFormat.Json, renderOptions.Format);
    }

    [Fact]
    public async Task RenderOptions_LoadsViews_WithCorrectName()
    {
        var path = WriteConfig(new(
            Views: "{\"myView\":{\"packages\":[],\"ignorePackages\":[]}}"));

        var (_, _, renderOptions, _) = await Manager(path).LoadAsync();

        Assert.Single(renderOptions.Views);
        Assert.Equal("myView", renderOptions.Views[0].ViewName);
    }

    [Fact]
    public async Task RenderOptions_LoadsMultipleViews()
    {
        var path = WriteConfig(new(
            Views: """
            {
              "viewA": { "packages": [], "ignorePackages": [] },
              "viewB": { "packages": [], "ignorePackages": [] }
            }
            """));

        var (_, _, renderOptions, _) = await Manager(path).LoadAsync();
        Assert.Equal(2, renderOptions.Views.Count);
    }

    [Fact]
    public async Task RenderOptions_View_LoadsPackagesWithPathAndDepth()
    {
        var path = WriteConfig(new(
            Views: """
            {
              "v": {
                "packages": [{ "path": "./Domain/", "depth": 2 }],
                "ignorePackages": []
              }
            }
            """));

        var (_, _, renderOptions, _) = await Manager(path).LoadAsync();
        var pkg = Assert.Single(renderOptions.Views[0].Packages);

        Assert.Equal("./Domain/", pkg.Path);
        Assert.Equal(2, pkg.Depth);
    }

    [Fact]
    public async Task RenderOptions_View_PackageDepth_DefaultsToZero_WhenOmitted()
    {
        var path = WriteConfig(new(
            Views: """
            {
              "v": {
                "packages": [{ "path": "./Domain/" }],
                "ignorePackages": []
              }
            }
            """));

        var (_, _, renderOptions, _) = await Manager(path).LoadAsync();
        var pkg = Assert.Single(renderOptions.Views[0].Packages);
        Assert.Equal(0, pkg.Depth);
    }

    [Fact]
    public async Task RenderOptions_View_LoadsIgnorePackages()
    {
        var path = WriteConfig(new(
            Views: """
            {
              "v": {
                "packages": [],
                "ignorePackages": ["./Infra/", "./Application/"]
              }
            }
            """));

        var (_, _, renderOptions, _) = await Manager(path).LoadAsync();
        var view = Assert.Single(renderOptions.Views);
        Assert.Contains("./Infra/", view.IgnorePackages);
        Assert.Contains("./Application/", view.IgnorePackages);
    }

    [Fact]
    public async Task RenderOptions_SaveLocation_IsSet()
    {
        var path = WriteConfig(new(SaveLocation: "\"diagrams\""));
        var (_, _, renderOptions, _) = await Manager(path).LoadAsync();
        Assert.NotEmpty(renderOptions.SaveLocation);
        Assert.Contains("diagrams", renderOptions.SaveLocation);
    }

    [Fact]
    public async Task SnapshotOptions_DiffFalse_UsesLocalManager()
    {
        var path = WriteConfig();
        var (_, _, _, snapshotOptions) = await Manager(path).LoadAsync(diff: false);
        Assert.Equal(SnapshotManager.Local, snapshotOptions.SnapshotManager);
    }

    [Fact]
    public async Task SnapshotOptions_DiffTrue_UsesGitManager()
    {
        var path = WriteConfig();
        var (_, _, _, snapshotOptions) = await Manager(path).LoadAsync(diff: true);
        Assert.Equal(SnapshotManager.Git, snapshotOptions.SnapshotManager);
    }

    [Fact]
    public async Task SnapshotOptions_MapsGitUrl()
    {
        var path = WriteConfig(new(GitUrl: "\"https://github.com/myorg/myrepo\""));
        var (_, _, _, snapshotOptions) = await Manager(path).LoadAsync();
        Assert.Equal("https://github.com/myorg/myrepo", snapshotOptions.GitInfo.Url);
    }

    [Fact]
    public async Task SnapshotOptions_MapsGitBranch()
    {
        var path = WriteConfig(new(GitBranch: "\"develop\""));
        var (_, _, _, snapshotOptions) = await Manager(path).LoadAsync();
        Assert.Equal("develop", snapshotOptions.GitInfo.Branch);
    }

    [Fact]
    public async Task SnapshotOptions_UsesConfigSnapshotDir()
    {
        var path = WriteConfig(new(SnapshotDir: "\".mystate\""));
        var (_, _, _, snapshotOptions) = await Manager(path).LoadAsync();
        Assert.Equal(".mystate", snapshotOptions.SnapshotDir);
    }

    [Fact]
    public async Task SnapshotOptions_UsesConfigSnapshotFile()
    {
        var path = WriteConfig(new(SnapshotFile: "\"my-snapshot.json\""));
        var (_, _, _, snapshotOptions) = await Manager(path).LoadAsync();
        Assert.Equal("my-snapshot.json", snapshotOptions.SnapshotFile);
    }

    [Fact]
    public async Task SnapshotOptions_SnapshotDir_DefaultsToArcblens_WhenNull()
    {
        var path = WriteConfig(new(SnapshotDir: "null"));
        var (_, _, _, snapshotOptions) = await Manager(path).LoadAsync();
        Assert.Equal(".archlens", snapshotOptions.SnapshotDir);
    }

    [Fact]
    public async Task SnapshotOptions_SnapshotFile_DefaultsToSnapshot_WhenNull()
    {
        var path = WriteConfig(new(SnapshotFile: "null"));
        var (_, _, _, snapshotOptions) = await Manager(path).LoadAsync();
        Assert.Equal("snapshot", snapshotOptions.SnapshotFile);
    }
}