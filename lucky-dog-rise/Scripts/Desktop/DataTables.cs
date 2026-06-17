using Godot;
using Luban.SimpleJSON;

namespace LuckyDogRise;

public static class DataTables
{
    private static cfg.Tables _tables = null!;

    public static cfg.Tables Tables
    {
        get
        {
            _tables ??= new cfg.Tables(name =>
            {
                var path = $"res://Scripts/Data/{name}.json";
                using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
                if (file == null)
                    throw new System.IO.FileNotFoundException($"Luban data file not found: {path}");
                return JSONNode.Parse(file.GetAsText());
            });
            return _tables;
        }
    }
}
