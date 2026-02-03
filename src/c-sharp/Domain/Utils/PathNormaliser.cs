using System.IO;

namespace Archlens.Domain.Utils;

public static class PathNormaliser
{
    public const string RelativeRoot = "./";

    public static string NormaliseModule(string root, string path) =>
        Normalise(root, path, isDirectory: true);

    public static string NormaliseFile(string root, string path) =>
        Normalise(root, path, isDirectory: false);

    public static string Normalise(string root, string path, bool isDirectory)
    {
        var fullPath = CombinePaths(root, path);

        var relative = Path
            .GetRelativePath(root, fullPath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .TrimEnd('/');

        if (relative is "." or "")
            return RelativeRoot;

        return isDirectory ? $"./{relative}/" : $"./{relative}";
    }

    public static string CombinePaths(string fullRootPath, string relativePath)
    {
        var fullPath = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.GetFullPath(relativePath, fullRootPath);
        return fullPath;
    }

    public static string GetFileOrModuleName(string normalisedPath)
    {
        var trimmed = normalisedPath.TrimEnd('/');
        return Path.GetFileName(trimmed);
    }
}
