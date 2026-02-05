using Archlens.Domain.Interfaces;
using Archlens.Domain.Models.Enums;
using Archlens.Domain.Models.Records;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Archlens.Infra;

public class ConfigManager(string _path)
{
    private sealed class ConfigDto
    {
#pragma warning disable CS8632
        public string ProjectRoot { get; set; }
        public string RootFolder { get; set; }
        public string ProjectName { get; set; }
        public string Name { get; set; }
        public string Language { get; set; }
        public string SnapshotManager { get; set; }
        public string Format { get; set; }
        public string GitUrl { get; set; }
        public string SnapshotDir { get; set; }
        public string SnapshotFile { get; set; }
        public string[] Exclusions { get; set; }
        public string[] FileExtensions { get; set; }
        public Dictionary<string, ViewDto> Views { get; set; }
        public string SaveLocation { get; set; }
#pragma warning restore CS8632
    }

    private sealed class ViewDto
    {
#pragma warning disable CS8632
        public PackageDto[] Packages { get; set; }
        public string[] IgnorePackages { get; set; }
#pragma warning restore CS8632
    }

    private sealed class PackageDto
    {
#pragma warning disable CS8632
        public string Path { get; set; }
        public int? Depth { get; set; }
#pragma warning restore CS8632
    }

    public async Task<(BaseOptions, ParserOptions, RenderOptions, SnapshotOptions)> LoadAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_path))
            throw new ArgumentException("Config path is null/empty.", nameof(_path));

        var configFile = Path.GetFullPath(_path);
        if (!File.Exists(configFile))
            throw new FileNotFoundException($"Config file not found: {configFile}", configFile);

        await using var fileStream = File.OpenRead(configFile);

        var dto = await JsonSerializer.DeserializeAsync<ConfigDto>(
            fileStream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            ct
        ) ?? throw new InvalidOperationException($"Could not parse JSON in {configFile}.");

        var baseDir = Path.GetDirectoryName(configFile) ?? Environment.CurrentDirectory;

        var baseOptions = MapBaseOptions(dto, baseDir);

        var parserOptions = MapParserOptions(dto, baseOptions);
        var renderOptions = MapRenderOptions(dto, baseDir, baseOptions);
        var snapshotOptions = MapSnapshotOptions(dto, baseDir, baseOptions);

        return (baseOptions, parserOptions, renderOptions, snapshotOptions);
    }

    private static BaseOptions MapBaseOptions(ConfigDto dto, string baseDir)
    {
        var projectRoot = MapProjectRoot(dto) ?? baseDir;
        var projectName = MapName(dto) ?? baseDir.Split("\\").Last();      
        var fullRootPath = GetFullRootPath(projectRoot);

        if (!Directory.Exists(fullRootPath))
            throw new DirectoryNotFoundException($"projectRoot does not exist: {projectRoot}");

        return new BaseOptions(
            FullRootPath: fullRootPath,
            ProjectRoot: projectRoot,
            ProjectName: projectName
        );
    }

    private static ParserOptions MapParserOptions(ConfigDto dto, BaseOptions options)
    {
        var language = MapLanguage(dto.Language ?? "c#");

        var exclusions = (dto.Exclusions ?? []).Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();

        var fileExts = (dto.FileExtensions ?? DefaultExtensions(language)).Select(NormalizeExtension).ToArray();
        if (fileExts.Length == 0)
            throw new InvalidOperationException("fileExtensions resolved to an empty list.");

        return new ParserOptions(
            BaseOptions: options,
            Language: language,
            Exclusions: fileExts.Length == 0 ? [] : exclusions,
            FileExtensions: fileExts
        );
    }

    private static RenderOptions MapRenderOptions(ConfigDto dto, string baseDir, BaseOptions options)
    {
        var format = MapFormat(dto.Format ?? "json");
        var views = MapViews(dto.Views);
        var saveLoc = MapPath(baseDir, dto.SaveLocation);

        return new RenderOptions(
            BaseOptions: options,
            Format: format,
            Views: views,
            SaveLocation: saveLoc
        );
    }

    private static SnapshotOptions MapSnapshotOptions(ConfigDto dto, string baseDir, BaseOptions options)
    {
        var snapshotManager = MapSnapshotManager(dto.SnapshotManager ?? "local");
        return new SnapshotOptions(
            BaseOptions: options,
            SnapshotManager: snapshotManager,
            GitUrl: dto.GitUrl,
            SnapshotDir: dto.SnapshotDir ?? ".archlens",
            SnapshotFile: dto.SnapshotFile ?? "snapshot"
        );
    }

    private static string GetFullRootPath(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("Path is required.", nameof(root));

        if (!Path.IsPathFullyQualified(root))
            root = Path.Join(baseDir, root);

        var dir = Directory.Exists(root)
            ? new DirectoryInfo(root)
            : new FileInfo(root).Directory!;

        return dir.FullName;
    }

    private static string NormalizeExtension(string ext)
    {
        ext = ext.Trim();
        return ext.StartsWith('.') ? ext : "." + ext;
    }

    private static IReadOnlyList<string> DefaultExtensions(Language lang) => lang switch
    {
        Language.CSharp => [".cs"],
        _ => []
    };

    private static string MapProjectRoot(ConfigDto dto)
    {
        if (!string.IsNullOrEmpty(dto.ProjectRoot))
            return dto.ProjectRoot;
        if (!string.IsNullOrEmpty(dto.RootFolder))
            return dto.RootFolder;
        return string.Empty;
    }

    private static string MapName(ConfigDto dto)
    {
        if (!string.IsNullOrEmpty(dto.ProjectName))
            return dto.ProjectName;
        if (!string.IsNullOrEmpty(dto.Name))
            return dto.Name;
        return string.Empty;
    }

    private static Language MapLanguage(string raw)
    {
        var s = raw.Trim().ToLowerInvariant();
        return s switch
        {
            "c#" or "csharp" or "cs" or "c-sharp" or "c sharp" => Language.CSharp,
            "go" or "golang" => Language.Go,
            "kotlin" or "kt" => Language.Kotlin,
            _ => throw new NotSupportedException($"Unsupported language: '{raw}'.")
        };
    }

    private static SnapshotManager MapSnapshotManager(string raw)
    {
        var s = raw.Trim().ToLowerInvariant();
        return s switch
        {
            "git" => SnapshotManager.Git,
            "local" => SnapshotManager.Local,
            _ => throw new NotSupportedException($"Unsupported baseline: '{raw}'.")
        };
    }

    private static RenderFormat MapFormat(string raw)
    {
        var s = raw.Trim().ToLowerInvariant();
        return s switch
        {
            "json" or "application/json" => RenderFormat.Json,
            "puml" or "plantuml" or "plant-uml" => RenderFormat.PlantUML,
            _ => throw new NotSupportedException($"Unsupported format: '{raw}'.")
        };
    }

    private static string MapPath(string baseDir, string relpath)
    {
        return $"{baseDir}/{relpath}";
    }

    private static List<View> MapViews(Dictionary<string, ViewDto> viewDtos)
    {
        return [.. viewDtos.Select(v =>
            new View(
                v.Key,
                [.. v.Value.Packages.Select<PackageDto,Package>(p => new(p.Path, p.Depth ?? 0))],
                v.Value.IgnorePackages
            ))];
    }
}
