using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace LuckyDogRise.Tools;

public sealed class DogSkinAssetCatalog
{
    private const string ShibaRoot = "res://Assets/v1/Shiba";
    private const string EyewearRoot = "res://Assets/v1/Eyewear";

    public IReadOnlyList<string> FolderPaths { get; }
    public IReadOnlyList<string> EyewearFiles { get; }

    public DogSkinAssetCatalog()
    {
        FolderPaths = EnumerateDirectories(ShibaRoot)
            .Select(path => path.Replace("res://Assets/", "").Replace('/', '\\'))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        EyewearFiles = new[] { "" }
            .Concat(EnumeratePngFiles(EyewearRoot))
            .ToArray();
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
            || ResourceLoader.Exists($"{EyewearRoot}/{fileName}");
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
