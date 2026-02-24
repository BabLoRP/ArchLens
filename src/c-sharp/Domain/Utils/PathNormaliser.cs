using System.IO;

namespace Archlens.Domain.Utils;

public readonly record struct RelativePath
{
    public string Value { get; }

    private RelativePath(string value) => Value = value;

    public static RelativePath File(string projectRoot, string input)
        => new(PathNormaliser.NormaliseFile(projectRoot, input));

    public static RelativePath Directory(string projectRoot, string input)
        => new(PathNormaliser.NormaliseModule(projectRoot, input));

    public string ToAbsolute(string projectRoot)
        => PathNormaliser.GetAbsolutePath(projectRoot, Value);

    public int Length => Value.Length;

    public override string ToString() => Value;
}


public static class PathNormaliser
{
    public const string RelativeRoot = "./";

    public static string NormaliseModule(string root, string path) =>
        Normalise(root, path, isDirectory: true);

    public static string NormaliseFile(string root, string path) =>
        Normalise(root, path, isDirectory: false);

    public static string Normalise(string root, string path, bool isDirectory)
    {
        var fullPath = GetAbsolutePath(root, path);

        var relative = Path
            .GetRelativePath(root, fullPath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .TrimEnd('/');

        if (relative is "." or "")
            return RelativeRoot;

        return isDirectory ? $"./{relative}/" : $"./{relative}";
    }

    public static string GetAbsolutePath(string fullRootPath, string relativePath)
    {
        var fullPath = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.GetFullPath(relativePath, fullRootPath);
        return fullPath;
    }

    public static bool IsDirectory(string fullRootPath, string relPath)
    {
        var abs = GetAbsolutePath(fullRootPath, relPath);
        return Directory.Exists(abs);
    }

    public static string GetFileOrModuleName(string normalisedPath)
    {
        var trimmed = normalisedPath.TrimEnd('/');
        return Path.GetFileName(trimmed);
    }

    public static string GetPathDotSeparated(string path)
    {
        return path.Replace("/", ".").TrimStart('.').TrimEnd('.');
    }
}

public static class PathComparer
{
    public static bool Equals(string path1, string path2)
    {
        var norm1 = path1.TrimEnd('/').ToLower();
        var norm2 = path2.TrimEnd('/').ToLower();
        return string.Equals(norm1, norm2);
    }
}