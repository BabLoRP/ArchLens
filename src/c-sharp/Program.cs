using Archlens.Application;
using Archlens.Domain.Models.Records;
using Archlens.Infra;
using Archlens.Infra.Factories;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Archlens.CLI;

public class Program
{
    public static async Task Main(string[] args)
    {
        try
        {
            var path = args.Length == 0 ? string.Empty : args[0].Trim();
            var (baseOptions, parserOptions, renderOptions, snapshotOptions) = await GetOptions(path);

            var snapshotManager = SnapsnotManagerFactory.SelectSnapshotManager(snapshotOptions);
            var parsers = DependencyParserFactory.SelectDependencyParser(parserOptions);
            var renderer = RendererFactory.SelectRenderer(renderOptions);

            var updateDepGraphUseCase = new UpdateDependencyGraphUseCase(
                                                        baseOptions,
                                                        parserOptions,
                                                        renderOptions,
                                                        snapshotOptions,
                                                        parsers,
                                                        renderer,
                                                        snapshotManager);

            await updateDepGraphUseCase.RunAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine($"EXCEPTION: {e.Message}\n{e.StackTrace}");
        }

    }

    private async static Task<(BaseOptions, ParserOptions, RenderOptions, SnapshotOptions)> GetOptions(string args)
    {
        var configPath = args.Length > 0 ? args : FindConfigFile("archlens.json");

        var configManager = new ConfigManager(configPath);

        return await configManager.LoadAsync();
    }

    private static string FindConfigFile(string fileName)
    {
        var dir = AppContext.BaseDirectory;

        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate))
                return candidate;

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new FileNotFoundException($"Could not find '{fileName}' starting from '{AppContext.BaseDirectory}'.");
    }
}