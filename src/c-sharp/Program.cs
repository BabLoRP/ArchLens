using System;
using System.IO;
using System.Threading.Tasks;
using Archlens.Application;
using Archlens.Domain.Models.Records;
using Archlens.Infra;
using Archlens.Infra.Factories;

namespace Archlens.CLI;

public class Program
{
    public static async Task Main(string[] args)
    {
        var path = args.Length == 0 ? "../" : args[0].Trim();

        var root = Directory.Exists(path)
            ? new DirectoryInfo(path)
            : new FileInfo(path).Directory!;

        var format = args.Length < 2 ? "puml" : args[1].Trim();
        var diff = args.Length >= 3 && args[2].Trim() == "diff";

        await CLI(root.FullName, format, diff);
    }

    public static string CLISync(string configPath, string format = "puml", bool diff = false)
    {
        return CLI(configPath, format, diff).GetAwaiter().GetResult();
    }

    public static async Task<string> CLI(string configPath, string format = "puml", bool diff = false)
    {
        try
        {
            var resolvedPath = configPath.Length > 0 ? configPath : FindConfigFile("archlens.json");
            var loadConfigUseCase = new LoadConfigUseCase();
            var configManager = await loadConfigUseCase.RunAsync(resolvedPath, diff, format);

            var snapshotManager = SnapshotManagerFactory.SelectSnapshotManager(configManager.GetSnapshotOptions());
            var parsers = DependencyParserFactory.SelectDependencyParser(configManager.GetParserOptions());
            var renderer = RendererFactory.SelectRenderer(configManager.GetRenderOptions());

            var useCase = new UpdateGraphUseCase(configManager, parsers, renderer, snapshotManager, diff);
            await useCase.RunAsync();

            return "";
        }
        catch (Exception e)
        {
            Console.WriteLine($"EXCEPTION: {e.Message}\n{e.StackTrace}");
            return $"EXCEPTION: {e.Message}\n{e.StackTrace}";
        }
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
