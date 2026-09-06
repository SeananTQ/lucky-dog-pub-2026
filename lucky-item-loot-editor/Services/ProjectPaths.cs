using System.IO;

namespace LuckyItemLootEditor;

public static class ProjectPaths
{
    public static string GetEditorSaveDirectory(string projectRoot) =>
        Path.Combine(projectRoot, "lucky-item-loot-editor", "save");

    public static string? TryFindLatestEditorProject()
    {
        var projectRoot = TryFindProjectRoot();
        if (projectRoot is null)
            return null;

        var saveDirectory = GetEditorSaveDirectory(projectRoot);
        if (!Directory.Exists(saveDirectory))
            return null;

        return Directory.EnumerateFiles(saveDirectory, "*.ldloot", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    public static string? TryFindProjectRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var current = new DirectoryInfo(start);
            for (var i = 0; i < 10 && current is not null; i++, current = current.Parent)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "luban-excels", "output-data")) &&
                    Directory.Exists(Path.Combine(current.FullName, "lucky-dog-rise", "Assets")))
                    return current.FullName;
            }
        }

        return null;
    }

    public static string RequireProjectRoot() =>
        TryFindProjectRoot() ?? throw new DirectoryNotFoundException(
            "无法定位项目根目录。请从 lucky-item-loot-editor 目录或其子目录运行工具。");
}
