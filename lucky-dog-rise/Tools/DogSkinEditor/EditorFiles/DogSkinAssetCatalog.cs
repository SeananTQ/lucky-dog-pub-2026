using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace LuckyDogRise.Tools;

public sealed class DogSkinAssetCatalog
{
    private readonly HashSet<string> _duplicateEyewearNames;

    public IReadOnlyList<string> FolderPaths { get; }
    public IReadOnlyList<string> EyewearFiles { get; }

    public DogSkinAssetCatalog()
    {
        var versionRoots = EnumerateDirectories("res://Assets")
            .Where(path => System.Text.RegularExpressions.Regex.IsMatch(path.Split('/')[^1], @"^v\d+$"))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        FolderPaths = versionRoots.Select(root => root + "/Shiba").SelectMany(EnumerateDirectories)
            .Select(path => path.Replace("res://Assets/", "").Replace('/', '\\'))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        EyewearFiles = new[] { "" }
            .Concat(versionRoots.SelectMany(root =>
            {
                var resourceRoot = root + "/Eyewear";
                var absoluteRoot = ProjectSettings.GlobalizePath(resourceRoot);
                return Directory.Exists(absoluteRoot)
                    ? Directory.EnumerateFiles(absoluteRoot, "*.png", SearchOption.AllDirectories)
                        .Select(path => (resourceRoot.Replace("res://Assets/", "") + "/"
                            + Path.GetRelativePath(absoluteRoot, path)).Replace('/', '\\'))
                    : Enumerable.Empty<string>();
            }).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        _duplicateEyewearNames = EyewearFiles.Where(path => !string.IsNullOrEmpty(path))
            .GroupBy(path => Path.GetFileName(path.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> GetFiles(string folderPath, string prefix = "")
    {
        var resourcePath = "res://Assets/" + (folderPath ?? "").Replace('\\', '/').Trim('/');
        return EnumeratePngFiles(resourcePath)
            .Where(file => string.IsNullOrEmpty(prefix)
                || file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public bool ResourceExists(string folderPath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(fileName))
            return false;

        var path = $"res://Assets/{folderPath.Replace('\\', '/').Trim('/')}/{fileName}";
        return ResourceLoader.Exists(path);
    }

    public bool EyewearExists(string fileName)
    {
        return string.IsNullOrEmpty(fileName)
            || ResourceLoader.Exists("res://Assets/" + NormalizeEyewearPath(fileName).Replace('\\', '/'));
    }

    public static string NormalizeEyewearPath(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        value = value.Replace('/', '\\');
        return value.Contains('\\') ? value : $"v1\\Eyewear\\{value}";
    }

    public string DisplayEyewear(string value)
    {
        if (string.IsNullOrEmpty(value)) return "（无眼镜）";
        var relativePath = NormalizeEyewearPath(value);
        var fileName = Path.GetFileName(relativePath.Replace('\\', '/'));
        return _duplicateEyewearNames.Contains(fileName)
            ? relativePath[..^Path.GetExtension(relativePath).Length]
            : Path.GetFileNameWithoutExtension(fileName);
    }

    private static IEnumerable<string> EnumerateDirectories(string resourcePath)
    {
        var absolute = ProjectSettings.GlobalizePath(resourcePath);
        return Directory.Exists(absolute)
            ? Directory.EnumerateDirectories(absolute).Select(path => resourcePath + "/" + Path.GetFileName(path))
            : Enumerable.Empty<string>();
    }

    private static IEnumerable<string> EnumeratePngFiles(string resourcePath)
    {
        var absolute = ProjectSettings.GlobalizePath(resourcePath);
        return Directory.Exists(absolute)
            ? Directory.EnumerateFiles(absolute, "*.png", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name))!
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            : Enumerable.Empty<string>();
    }
}
